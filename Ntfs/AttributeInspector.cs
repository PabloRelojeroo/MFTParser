using System.Globalization;
using System.Text;
using MftParser.Models;

namespace MftParser.Ntfs;

public sealed class AttributeInfo {
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Stream { get; set; } = "";
    public string Form { get; set; } = "";
    public string Size { get; set; } = "";
    public string Id { get; set; } = "";
    public string Offset { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsChild { get; set; }
}

public static class AttributeInspector {
    public static List<AttributeInfo> Inspect(byte[] raw, Func<bool, string> form) {
        var list = new List<AttributeInfo>();
        if (raw.Length < 0x38 || Bytes.U32(raw, 0) != RecordParser.FileSignature) return list;

        var record = (byte[])raw.Clone();
        Fixup.Apply(record, 0, record.Length);

        var used = Math.Min((int)Bytes.U32(record, 0x18), record.Length);
        var offset = (int)Bytes.U16(record, 0x14);
        while (offset + 16 <= used) {
            var type = Bytes.U32(record, offset);
            if (type == 0xFFFFFFFF) break;
            var length = (int)Bytes.U32(record, offset + 4);
            if (length < 16 || offset + length > used) break;

            var nonResident = record[offset + 8] != 0;
            int nameLength = record[offset + 9];
            var nameOffset = Bytes.U16(record, offset + 10);
            var name = nameLength == 0 || offset + nameOffset + nameLength * 2 > used
                ? ""
                : Encoding.Unicode.GetString(record, offset + nameOffset, nameLength * 2);

            long size;
            var valueOffset = 0;
            var valueLength = 0;
            if (nonResident) {
                size = Bytes.I64(record, offset + 48);
            } else {
                valueLength = (int)Bytes.U32(record, offset + 16);
                valueOffset = offset + Bytes.U16(record, offset + 20);
                size = valueLength;
                if (valueOffset + valueLength > offset + length) valueLength = 0;
            }

            list.Add(new AttributeInfo {
                Type = AttributeNames.Hex(type),
                Name = AttributeNames.Type(type),
                Stream = name,
                Form = form(!nonResident),
                Size = size.ToString(CultureInfo.InvariantCulture),
                Id = Bytes.U16(record, offset + 14).ToString(CultureInfo.InvariantCulture),
                Offset = "0x" + offset.ToString("X3"),
                Detail = !nonResident && valueLength > 0 ? Describe(type, record, valueOffset, valueLength) : RunsOf(record, offset, length, nonResident),
            });

            if (type == 0x20 && !nonResident && valueLength > 0) list.AddRange(ListEntries(record, valueOffset, valueLength));
            offset += length;
        }
        return list;
    }

    static string RunsOf(byte[] b, int at, int length, bool nonResident) {
        if (!nonResident) return "";
        var runs = DataRun.Decode(b, at + Bytes.U16(b, at + 32), at + length);
        var vcn = Bytes.I64(b, at + 16);
        return $"VCN {vcn} · {runs.Count} run{(runs.Count == 1 ? "" : "s")}";
    }

    static string Describe(uint type, byte[] b, int at, int length) {
        switch (type) {
            case 0x10 when length >= 36:
                var si = AttributeNames.Of(Bytes.U32(b, at + 32));
                if (length < 72) return si;
                return $"{si} · SecurityId {Bytes.U32(b, at + 52)} · USN {Bytes.I64(b, at + 64)}";
            case 0x30 when length >= 66:
                int nameLength = b[at + 64];
                if (66 + nameLength * 2 > length) return "";
                var name = Encoding.Unicode.GetString(b, at + 66, nameLength * 2);
                var parent = Bytes.U64(b, at);
                var space = b[at + 65] switch { 0 => "Posix", 2 => "Dos", 3 => "DosWindows", _ => "Windows" };
                return $"{name} · {space} · parent {parent & 0x0000FFFFFFFFFFFF}/{parent >> 48}";
            case 0x40 when length >= 16:
                var guid = new byte[16];
                Buffer.BlockCopy(b, at, guid, 0, 16);
                return new Guid(guid).ToString();
            case 0x50 when length >= 4:
                return $"Revision {b[at]} · Control 0x{Bytes.U16(b, at + 2):X4}";
            case 0x90 when length >= 16:
                return "Index " + Bytes.U32(b, at).ToString("X2");
            case 0xC0 when length >= 4:
                return "Tag 0x" + Bytes.U32(b, at).ToString("X8");
            default:
                return "";
        }
    }

    static IEnumerable<AttributeInfo> ListEntries(byte[] b, int at, int length) {
        var end = at + length;
        while (at + 26 <= end) {
            var type = Bytes.U32(b, at);
            int entryLength = Bytes.U16(b, at + 4);
            if (entryLength < 26 || at + entryLength > end) yield break;

            int nameLength = b[at + 6];
            int nameOffset = b[at + 7];
            var name = nameLength == 0 || at + nameOffset + nameLength * 2 > end ? "" : Encoding.Unicode.GetString(b, at + nameOffset, nameLength * 2);
            var reference = Bytes.U64(b, at + 16);

            yield return new AttributeInfo {
                Type = AttributeNames.Hex(type),
                Name = AttributeNames.Type(type),
                Stream = name,
                Id = Bytes.U16(b, at + 24).ToString(CultureInfo.InvariantCulture),
                Detail = $"-> {reference & 0x0000FFFFFFFFFFFF}/{reference >> 48} · VCN {Bytes.I64(b, at + 8)}",
                IsChild = true,
            };
            at += entryLength;
        }
    }
}
