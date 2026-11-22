using System.Text;
using MftParser.Models;

namespace MftParser.Ntfs;

public sealed class UnnamedData {
    public List<DataRun> Runs { get; set; } = new();
    public long RealSize { get; set; }
    public bool HasAttributeList { get; set; }
}

public sealed class ParsedRecord {
    public MftEntry Entry { get; set; } = null!;
    public long BaseIndex { get; set; } = -1;
    public int NameRank { get; set; } = int.MaxValue;
}

static class RecordParser {
    public const uint FileSignature = 0x454C4946;

    const uint StandardInformation = 0x10;
    const uint AttributeList = 0x20;
    const uint FileName = 0x30;
    const uint ObjectId = 0x40;
    const uint Data = 0x80;
    const uint ReparsePoint = 0xC0;
    const uint LoggedUtility = 0x100;
    const uint End = 0xFFFFFFFF;

    const int MaxKeptContent = 4096;

    public static ParsedRecord? Parse(byte[] b, int at, int recordSize, long index) {
        if (Bytes.U32(b, at) != FileSignature) return null;
        if (!Fixup.Apply(b, at, recordSize)) return null;

        var flags = Bytes.U16(b, at + 0x16);
        var used = (int)Bytes.U32(b, at + 0x18);
        if (used > recordSize || used < 0x30) used = recordSize;

        var baseRef = Bytes.U64(b, at + 0x20);
        var entry = new MftEntry {
            Index = index,
            Sequence = Bytes.U16(b, at + 0x10),
            InUse = (flags & 0x01) != 0,
            IsDirectory = (flags & 0x02) != 0,
            Lsn = Bytes.I64(b, at + 0x08),
            LinkCount = Bytes.U16(b, at + 0x12),
        };
        var parsed = new ParsedRecord {
            Entry = entry,
            BaseIndex = baseRef == 0 ? -1 : (long)(baseRef & 0x0000FFFFFFFFFFFF),
        };

        var offset = at + Bytes.U16(b, at + 0x14);
        var limit = at + used;
        while (offset + 16 <= limit) {
            var type = Bytes.U32(b, offset);
            if (type == End) break;

            var length = (int)Bytes.U32(b, offset + 4);
            if (length < 16 || offset + length > limit) break;

            var nonResident = b[offset + 8] != 0;
            int nameLength = b[offset + 9];
            var nameOffset = Bytes.U16(b, offset + 10);
            var name = nameLength == 0 ? "" : Encoding.Unicode.GetString(b, offset + nameOffset, nameLength * 2);

            if (!nonResident) {
                var valueLength = (int)Bytes.U32(b, offset + 16);
                var valueOffset = offset + Bytes.U16(b, offset + 20);
                if (valueOffset + valueLength <= offset + length) {
                    switch (type) {
                        case StandardInformation: ReadStandardInformation(b, valueOffset, valueLength, entry); break;
                        case FileName: ReadFileName(b, valueOffset, valueLength, parsed); break;
                        case Data: ReadResidentData(b, valueOffset, valueLength, name, entry); break;
                        case ObjectId when valueLength >= 16: Extra(entry).ObjectId = new Guid(Slice(b, valueOffset, 16)); break;
                        case ReparsePoint: Extra(entry).ReparseTarget = ReadReparse(b, valueOffset, valueLength); break;
                    }
                }
            } else if (type == Data) {
                ReadNonResidentData(b, offset, length, name, entry);
            }

            if (type == LoggedUtility) Extra(entry).LoggedUtility = name.Length > 0 ? name : AttributeNames.Type(type);

            offset += length;
        }

        return parsed;
      }

    public static UnnamedData? FindUnnamedData(byte[] b, int recordSize) {
        var used = Math.Min((int)Bytes.U32(b, 0x18), recordSize);
        var offset = (int)Bytes.U16(b, 0x14);
        var hasList = false;
        while (offset + 16 <= used) {
            var type = Bytes.U32(b, offset);
            if (type == End) break;
            var length = (int)Bytes.U32(b, offset + 4);
            if (length < 16 || offset + length > used) break;

            if (type == AttributeList) hasList = true;
            if (type == Data && b[offset + 8] != 0 && b[offset + 9] == 0) {
                var runsOffset = offset + Bytes.U16(b, offset + 32);
                return new UnnamedData {
                    Runs = DataRun.Decode(b, runsOffset, offset + length),
                    RealSize = Bytes.I64(b, offset + 48),
                    HasAttributeList = hasList,
                };
            }
            offset += length;
        }
        return null;
    }

    static void ReadStandardInformation(byte[] b, int at, int length, MftEntry entry) {
        if (length < 36) return;
        entry.SiCreated = Bytes.I64(b, at);
        entry.SiModified = Bytes.I64(b, at + 8);
        entry.SiChanged = Bytes.I64(b, at + 16);
        entry.SiAccessed = Bytes.I64(b, at + 24);
        entry.Attributes = Bytes.U32(b, at + 32);
        if (length < 72) return;
        entry.SecurityId = Bytes.U32(b, at + 52);
        entry.Usn = Bytes.I64(b, at + 64);
    }

    static void ReadFileName(byte[] b, int at, int length, ParsedRecord parsed) {
        if (length < 66) return;
        int nameLength = b[at + 64];
        int space = b[at + 65];
        if (66 + nameLength * 2 > length) return;

        var entry = parsed.Entry;
        var name = Encoding.Unicode.GetString(b, at + 66, nameLength * 2);

        if (space == 2) {
            entry.ShortName ??= name;
            if (parsed.NameRank <= 2) return;
        }

        var rank = space switch { 1 or 3 => 0, 0 => 1, _ => 2 };
        if (rank >= parsed.NameRank) return;

        parsed.NameRank = rank;
        var parent = Bytes.U64(b, at);
        entry.ParentIndex = (long)(parent & 0x0000FFFFFFFFFFFF);
        entry.ParentSequence = (ushort)(parent >> 48);
        entry.FnCreated = Bytes.I64(b, at + 8);
        entry.FnModified = Bytes.I64(b, at + 16);
        entry.FnChanged = Bytes.I64(b, at + 24);
        entry.FnAccessed = Bytes.I64(b, at + 32);
        entry.Name = name;
        entry.NameType = (byte)space;
        if (space == 3) entry.ShortName = null;
    }

    static EntryExtra Extra(MftEntry entry) => entry.Extra ??= new EntryExtra();

    static byte[] Slice(byte[] b, int at, int count) {
        var result = new byte[count];
        Buffer.BlockCopy(b, at, result, 0, count);
        return result;
    }

    static string ReadReparse(byte[] b, int at, int length) {
        if (length < 8) return "";
        var tag = Bytes.U32(b, at);
        var header = tag switch { 0xA0000003 => 16, 0xA000000C => 20, _ => 0 };
        if (header == 0 || length < header) return "Tag 0x" + tag.ToString("X8");

        var pathBuffer = at + header;
        int substituteOffset = Bytes.U16(b, at + 8);
        int substituteLength = Bytes.U16(b, at + 10);
        int printOffset = Bytes.U16(b, at + 12);
        int printLength = Bytes.U16(b, at + 14);

        string Read(int offset, int count) =>
            count > 0 && pathBuffer + offset + count <= at + length ? Encoding.Unicode.GetString(b, pathBuffer + offset, count) : "";

        var print = Read(printOffset, printLength);
        return print.Length > 0 ? print : Read(substituteOffset, substituteLength);
    }

    static void ReadResidentData(byte[] b, int at, int length, string name, MftEntry entry) {
        if (name.Length == 0) {
            entry.HasData = true;
            entry.Resident = true;
            entry.Size = length;
            entry.Allocated = 0;
            return;
        }

        var stream = new DataStream { Name = name, Size = length, Resident = true };
        if (length <= MaxKeptContent) {
            stream.Content = new byte[length];
            Buffer.BlockCopy(b, at, stream.Content, 0, length);
        }
        (entry.Streams ??= new List<DataStream>(1)).Add(stream);
    }

    static void ReadNonResidentData(byte[] b, int at, int length, string name, MftEntry entry) {
        var startVcn = Bytes.I64(b, at + 16);
        var runsOffset = at + Bytes.U16(b, at + 32);
        var runs = runsOffset < at + length ? DataRun.Decode(b, runsOffset, at + length).Count : 0;

        if (name.Length == 0) {
            entry.HasData = true;
            entry.Runs += runs;
            if (startVcn == 0) {
                entry.Allocated = Bytes.I64(b, at + 40);
                entry.Size = Bytes.I64(b, at + 48);
            }
            return;
        }

        if (startVcn != 0) return;
        (entry.Streams ??= new List<DataStream>(1)).Add(new DataStream {
            Name = name,
            Size = Bytes.I64(b, at + 48),
            Resident = false,
        });
    }
}
