using MftParser.Localization;

namespace MftParser.Models;

[Flags]
public enum EntryFlags {
    None = 0,
    SiBeforeFn = 1 << 0,
    ZeroFraction = 1 << 1,
    Ads = 1 << 2,
    Downloaded = 1 << 3,
    Hidden = 1 << 4,
    Executable = 1 << 5,
    Orphan = 1 << 6,
    Noise = 1 << 7,
    Timestomp = SiBeforeFn | ZeroFraction,
}

public sealed class DataStream {
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public bool Resident { get; set; }
    public byte[]? Content { get; set; }
}

public sealed class EntryExtra {
    public Guid? ObjectId { get; set; }
    public string? ReparseTarget { get; set; }
    public string? LoggedUtility { get; set; }
}

public sealed class MftEntry {
    public long Index { get; set; }
    public ushort Sequence { get; set; }
    public bool InUse { get; set; }
    public bool IsDirectory { get; set; }
    public string Name { get; set; } = "";
    public string? ShortName { get; set; }
    public byte NameType { get; set; } = 1;
    public long ParentIndex { get; set; } = -1;
    public ushort ParentSequence { get; set; }
    public ushort LinkCount { get; set; }
    public long Lsn { get; set; }

    public long SiCreated { get; set; }
    public long SiModified { get; set; }
    public long SiChanged { get; set; }
    public long SiAccessed { get; set; }
    public long FnCreated { get; set; }
    public long FnModified { get; set; }
    public long FnChanged { get; set; }
    public long FnAccessed { get; set; }

    public uint Attributes { get; set; }
    public uint SecurityId { get; set; }
    public long Usn { get; set; }
    public long Size { get; set; }
    public long Allocated { get; set; }
    public bool Resident { get; set; }
    public bool HasData { get; set; }
    public int Runs { get; set; }
    public List<DataStream>? Streams { get; set; }
    public EntryExtra? Extra { get; set; }

    public bool IsAds { get; set; }
    public MftEntry? Owner { get; set; }
    public DataStream? Stream { get; set; }

    public string Folder { get; set; } = "";
    public string Extension { get; set; } = "";
    public EntryFlags Flags { get; set; }
    public int ZoneId { get; set; } = -1;

    public static string SourceFile { get; set; } = "";

    public long Line { get; set; }
    public bool Tagged { get; set; }

    public MftEntry Base => Owner ?? this;
    public string FullPath => Folder.Length == 0 ? Name : Folder + "\\" + Name;
    public string SourceFileText => SourceFile;
    public string ParseErrors => "";
    public string ResidentDataDecoded => "";

    public string ExtensionText {
        get {
            if (IsDirectory && !IsAds) return "";
            var dot = Name.LastIndexOf('.');
            if (dot < 0 || dot == Name.Length - 1) return "";
            var extension = Name.Substring(dot);
            return extension.IndexOf(':') >= 0 ? "" : extension;
        }
    }
    public bool HasAds => !IsAds && Streams is { Count: > 0 };
    public bool SiLessFn => FileTime.IsValid(SiCreated) && FileTime.IsValid(FnCreated) && SiCreated < FnCreated;
    public bool USecZeros => FileTime.HasZeroFraction(SiCreated) || FileTime.HasZeroFraction(SiModified);
    public bool Copied => FileTime.IsValid(SiCreated) && FileTime.IsValid(SiModified) && SiModified < SiCreated;
    public string SiFlags => AttributeNames.Flags(Attributes);
    public string NameTypeText => NameType switch { 0 => "Posix", 2 => "Dos", 3 => "DosWindows", _ => "Windows" };
    public string ReparseTarget => Base.Extra?.ReparseTarget ?? "";
    public string ObjectIdText => Base.Extra?.ObjectId?.ToString() ?? "";
    public string LoggedUtilStream => Base.Extra?.LoggedUtility ?? "";
    public string ZoneIdContents => ZoneText(Stream);

    public string Created10 => FileTime.Short(SiCreated);
    public string Created30 => FnCreated == SiCreated ? "" : FileTime.Short(FnCreated);
    public string Modified10 => FileTime.Short(SiModified);
    public string Modified30 => FnModified == SiModified ? "" : FileTime.Short(FnModified);
    public string Changed10 => FileTime.Short(SiChanged);
    public string Changed30 => FnChanged == SiChanged ? "" : FileTime.Short(FnChanged);
    public string Accessed10 => FileTime.Short(SiAccessed);
    public string Accessed30 => FnAccessed == SiAccessed ? "" : FileTime.Short(FnAccessed);

    public bool IsTimestomped => (Flags & EntryFlags.Timestomp) != 0;
    public bool IsDownloaded => (Flags & EntryFlags.Downloaded) != 0;
    public bool IsHidden => (Flags & EntryFlags.Hidden) != 0;
    public bool IsOrphan => (Flags & EntryFlags.Orphan) != 0;

    public bool Has(EntryFlags flag) => (Flags & flag) != 0;

    public MftEntry AdsRow(DataStream stream) {
        var row = (MftEntry)MemberwiseClone();
        row.IsAds = true;
        row.Owner = this;
        row.Stream = stream;
        row.Streams = null;
        row.Name = Name + ":" + stream.Name;
        row.Size = stream.Size;
        row.Allocated = 0;
        row.Resident = stream.Resident;
        var dot = stream.Name.LastIndexOf('.');
        row.Extension = dot >= 0 && dot < stream.Name.Length - 1 ? stream.Name.Substring(dot + 1).ToLowerInvariant() : "";
        return row;
    }

    static string ZoneText(DataStream? stream) {
        if (stream?.Content == null || !stream.Name.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase)) return "";
        return Ntfs.MftReader.Text(stream.Content).Replace("\r", "").Replace("\0", "").TrimEnd();
    }
}

public static class AttributeNames {
    static readonly (uint Bit, string Name)[] Bits = {
        (0x1, "ReadOnly"), (0x2, "Hidden"), (0x4, "System"), (0x8, "VolumeLabel"), (0x10, "Directory"),
        (0x20, "Archive"), (0x40, "Device"), (0x80, "Normal"), (0x100, "Temporary"), (0x200, "SparseFile"),
        (0x400, "ReparsePoint"), (0x800, "Compressed"), (0x1000, "Offline"), (0x2000, "NotContentIndexed"),
        (0x4000, "Encrypted"), (0x10000000, "IsDirectory"), (0x20000000, "IsIndexView"),
    };

    public static string Of(uint value) {
        if (value == 0) return "";
        var names = new List<string>();
        foreach (var (bit, name) in Bits) if ((value & bit) != 0) names.Add(name);
        return string.Join(", ", names);
    }

    public static string Flags(uint value) {
        if ((value & 0xFFFF) == 0) return "None";
        var names = new List<string>();
        foreach (var (bit, name) in Bits) if (bit <= 0x4000 && (value & bit) != 0) names.Add(name);
        return string.Join("|", names);
    }

    public static string Type(uint type) => type switch {
        0x10 => "$STANDARD_INFORMATION",
        0x20 => "$ATTRIBUTE_LIST",
        0x30 => "$FILE_NAME",
        0x40 => "$OBJECT_ID",
        0x50 => "$SECURITY_DESCRIPTOR",
        0x60 => "$VOLUME_NAME",
        0x70 => "$VOLUME_INFORMATION",
        0x80 => "$DATA",
        0x90 => "$INDEX_ROOT",
        0xA0 => "$INDEX_ALLOCATION",
        0xB0 => "$BITMAP",
        0xC0 => "$REPARSE_POINT",
        0xD0 => "$EA_INFORMATION",
        0xE0 => "$EA",
        0xF0 => "$PROPERTY_SET",
        0x100 => "$LOGGED_UTILITY_STREAM",
        _ => "?",
    };

    public static string Hex(uint type) => "0x" + type.ToString("X2");
}
