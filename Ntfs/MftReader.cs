using System.Text;
using MftParser.Models;

namespace MftParser.Ntfs;

public sealed class ReadProgress {
    public long BytesDone { get; set; }
    public long BytesTotal { get; set; }
    public long Records { get; set; }
    public int Stage { get; set; }
}

public sealed class MftResult {
    public RecordSource Source { get; set; } = null!;
    public MftEntry[] Entries { get; set; } = Array.Empty<MftEntry>();
    public List<MftEntry> AdsRows { get; set; } = new();
    public long RecordCount { get; set; }
    public long Found { get; set; }
    public int Deleted { get; set; }
    public int Orphans { get; set; }
    public int Torn { get; set; }
    public TimeSpan Elapsed { get; set; }
}

public static class MftReader {
    public const int StageRead = 0;
    public const int StagePaths = 1;
    public const int StageFlags = 2;

    const long RootIndex = 5;
    const int FirstUserRecord = 27;

    static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase) {
        "exe", "dll", "jar", "bat", "cmd", "ps1", "vbs", "js", "scr", "com", "msi", "sys", "py", "ahk",
    };

    static readonly HashSet<string> QuietStreams = new(StringComparer.OrdinalIgnoreCase) {
        "Zone.Identifier", "SmartScreen", "encryptable", "Win32App_1", "$DSC", "favicon", "{4c8cc155-6c1e-11d1-8e41-00c04fb9386d}",
    };

    static readonly string[] NoiseFolders = {
        @"\Windows\WinSxS", @"\Windows\servicing", @"\Windows\SoftwareDistribution", @"\Windows\assembly",
        @"\Windows\Microsoft.NET\assembly", @"\Windows\System32\DriverStore", @"\Windows\Installer",
        @"\Windows\SystemApps", @"\Windows\InfusedApps", @"\Windows\System32\catroot",
        @"\Windows\System32\CatRoot2", @"\Windows\System32\wbem\Repository", @"\Windows\Fonts",
        @"\ProgramData\Microsoft\Windows Defender\Definition Updates", @"\ProgramData\Microsoft\Windows Defender\Scans",
        @"\ProgramData\Packages", @"\Program Files\WindowsApps", @"\System Volume Information", @"\$Extend",
    };

    public static MftResult Read(RecordSource source, IProgress<ReadProgress>? progress, CancellationToken token) {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var entries = new MftEntry[source.RecordCount];
        var extensions = new List<ParsedRecord>();
        var report = new ReadProgress { BytesTotal = source.TotalBytes };
        var torn = 0;
        long found = 0;
        var lastReport = 0L;

        foreach (var chunk in source.ReadAll(token)) {
            for (var i = 0; i < chunk.Count; i++) {
                var index = chunk.FirstIndex + i;
                var at = i * source.RecordSize;
                if (chunk.Buffer[at] == 0 && Bytes.U32(chunk.Buffer, at) == 0) continue;

                var parsed = RecordParser.Parse(chunk.Buffer, at, source.RecordSize, index);
                if (parsed == null) {
                    if (Bytes.U32(chunk.Buffer, at) == RecordParser.FileSignature) torn++;
                    continue;
                }

				if (parsed.BaseIndex >= 0) {
                    extensions.Add(parsed);
                } else {
                    entries[index] = parsed.Entry;
                    found++;
                }
            }

            report.BytesDone = (chunk.FirstIndex + chunk.Count) * source.RecordSize;
            report.Records = found;
            if (progress != null && watch.ElapsedMilliseconds - lastReport > 80) {
                lastReport = watch.ElapsedMilliseconds;
                progress.Report(Snapshot(report, StageRead));
            }
        }

        Merge(entries, extensions);

        progress?.Report(Snapshot(report, StagePaths));
        var orphans = ResolvePaths(entries, ".", token);

        progress?.Report(Snapshot(report, StageFlags));
        var deleted = 0;
        foreach (var entry in entries) {
            if (entry == null) continue;
            if (!entry.InUse) deleted++;
            Classify(entry, ".");
        }

        var adsRows = new List<MftEntry>();
        foreach (var entry in entries) {
            if (entry?.Streams == null) continue;
            foreach (var stream in entry.Streams) adsRows.Add(AdsRow(entry, stream));
        }
        NumberLines(entries, adsRows);
        MftEntry.SourceFile = source.SourcePath;

        return new MftResult {
            Source = source,
            Entries = entries,
            AdsRows = adsRows,
            RecordCount = source.RecordCount,
            Found = found,
            Deleted = deleted,
            Orphans = orphans,
            Torn = torn,
            Elapsed = watch.Elapsed,
        };
    }

    static void NumberLines(MftEntry[] entries, List<MftEntry> adsRows) {
        var streams = adsRows.ToLookup(r => r.Owner);
        long line = 0;
        void Number(MftEntry entry) {
            entry.Line = ++line;
            foreach (var row in streams[entry]) row.Line = ++line;
        }
        foreach (var entry in entries) if (entry is { InUse: true }) Number(entry);
        foreach (var entry in entries) if (entry is { InUse: false }) Number(entry);
    }

    static string Unknown(long index, ushort sequence) =>
        $".\\PathUnknown\\Directory with ID 0x{index:X8}-{sequence:X8}";

    static ReadProgress Snapshot(ReadProgress p, int stage) => new() {
        BytesDone = p.BytesDone,
        BytesTotal = p.BytesTotal,
        Records = p.Records,
        Stage = stage,
    };

    static void Merge(MftEntry[] entries, List<ParsedRecord> extensions) {
        foreach (var extra in extensions) {
            if (extra.BaseIndex >= entries.Length) continue;
            var owner = entries[extra.BaseIndex];
            if (owner == null) continue;

            var part = extra.Entry;
            if (owner.Name.Length == 0 && part.Name.Length > 0) {
                owner.Name = part.Name;
                owner.ParentIndex = part.ParentIndex;
                owner.ParentSequence = part.ParentSequence;
                owner.FnCreated = part.FnCreated;
                owner.FnModified = part.FnModified;
                owner.FnChanged = part.FnChanged;
                owner.FnAccessed = part.FnAccessed;
            }
            owner.ShortName ??= part.ShortName;
            if (part.Extra != null) {
                var merged = owner.Extra ??= new EntryExtra();
                merged.ObjectId ??= part.Extra.ObjectId;
                merged.ReparseTarget ??= part.Extra.ReparseTarget;
                merged.LoggedUtility ??= part.Extra.LoggedUtility;
            }

            if (part.HasData) {
                owner.HasData = true;
                owner.Runs += part.Runs;
                if (part.Size > 0 && owner.Size == 0) {
                    owner.Size = part.Size;
                    owner.Allocated = part.Allocated;
                    owner.Resident = part.Resident;
                }
            }

            if (part.Streams != null)
                (owner.Streams ??= new List<DataStream>()).AddRange(part.Streams);
        }
    }

    static int ResolvePaths(MftEntry[] entries, string root, CancellationToken token) {
        var paths = new string?[entries.Length];
        var broken = new bool[entries.Length];
        var chain = new List<long>(64);
        var orphans = 0;

        string DirectoryPath(long index, out bool orphan) {
            chain.Clear();
            var current = index;
            string start;
            orphan = false;

            while (true) {
                if (current == RootIndex) { start = root; break; }
                if (paths[current] != null) { start = paths[current]!; orphan = broken[current]; break; }
                if (chain.Count > 255) { start = Unknown(current, 0); orphan = true; break; }

                chain.Add(current);
                var entry = entries[current];
                var parent = entry.ParentIndex;
                if (!IsValidParent(entries, entry)) { start = Unknown(parent, entry.ParentSequence); orphan = true; break; }
                current = parent;
            }

            var path = start;
            for (var i = chain.Count - 1; i >= 0; i--) {
                path = path + "\\" + entries[chain[i]].Name;
                paths[chain[i]] = path;
                broken[chain[i]] = orphan;
            }
            return path;
        }

        for (long i = 0; i < entries.Length; i++) {
            var entry = entries[i];
            if (entry == null) continue;
            if ((i & 0xFFFF) == 0) token.ThrowIfCancellationRequested();

            if (i == RootIndex) {
                entry.Folder = root;
                continue;
            }

            if (!IsValidParent(entries, entry)) {
                entry.Folder = Unknown(entry.ParentIndex, entry.ParentSequence);
                entry.Flags |= EntryFlags.Orphan;
                orphans++;
                continue;
            }

            if (entry.ParentIndex == RootIndex) {
                entry.Folder = root;
                continue;
            }

            entry.Folder = DirectoryPath(entry.ParentIndex, out var orphan);
            if (orphan) {
                entry.Flags |= EntryFlags.Orphan;
                orphans++;
            }
        }
        return orphans;
    }

    static bool IsValidParent(MftEntry[] entries, MftEntry entry) {
        var parent = entry.ParentIndex;
        if (parent < 0 || parent >= entries.Length) return false;
        var owner = entries[parent];
        if (owner == null || !owner.IsDirectory) return false;
        if (owner.Sequence == entry.ParentSequence) return true;
        return !owner.InUse && owner.Sequence == (ushort)(entry.ParentSequence + 1);
    }

    static void Classify(MftEntry entry, string root) {
        var dot = entry.Name.LastIndexOf('.');
        if (!entry.IsDirectory && dot > 0 && dot < entry.Name.Length - 1)
            entry.Extension = entry.Name.Substring(dot + 1).ToLowerInvariant();

        if (FileTime.IsValid(entry.SiCreated) && FileTime.IsValid(entry.FnCreated) && entry.SiCreated < entry.FnCreated - 10_000_000)
            entry.Flags |= EntryFlags.SiBeforeFn;
        if (FileTime.HasZeroFraction(entry.SiCreated) && FileTime.IsValid(entry.FnCreated) && !FileTime.HasZeroFraction(entry.FnCreated))
            entry.Flags |= EntryFlags.ZeroFraction;

        if ((entry.Attributes & 0x6) != 0) entry.Flags |= EntryFlags.Hidden;
        if (ExecutableExtensions.Contains(entry.Extension)) entry.Flags |= EntryFlags.Executable;

        if (entry.Streams != null) {
            foreach (var stream in entry.Streams) {
                if (stream.Name.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase)) {
                    entry.Flags |= EntryFlags.Downloaded;
                    entry.ZoneId = ZoneOf(stream.Content);
                } else if (!QuietStreams.Contains(stream.Name)) {
                    entry.Flags |= EntryFlags.Ads;
                }
            }
        }

        if (entry.Index < FirstUserRecord || IsNoiseFolder(entry.Folder, root))
            entry.Flags |= EntryFlags.Noise;
    }

    static MftEntry AdsRow(MftEntry owner, DataStream stream) {
        var row = owner.AdsRow(stream);
        row.Flags &= ~(EntryFlags.Ads | EntryFlags.Downloaded | EntryFlags.Executable);
        row.ZoneId = -1;
        if (stream.Name.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase)) {
            row.Flags |= EntryFlags.Downloaded;
            row.ZoneId = owner.ZoneId;
        } else if (!QuietStreams.Contains(stream.Name)) {
            row.Flags |= EntryFlags.Ads;
        }
        if (ExecutableExtensions.Contains(row.Extension)) row.Flags |= EntryFlags.Executable;
        return row;
    }

    static bool IsNoiseFolder(string folder, string root) {
        if (folder.Length <= root.Length) return false;
        foreach (var noise in NoiseFolders) {
            if (folder.Length < root.Length + noise.Length) continue;
            if (string.Compare(folder, root.Length, noise, 0, noise.Length, StringComparison.OrdinalIgnoreCase) != 0) continue;
            if (folder.Length == root.Length + noise.Length || folder[root.Length + noise.Length] == '\\') return true;
        }
        return false;
    }

    public static int ZoneOf(byte[]? content) {
        if (content == null) return -2;
        foreach (var line in Text(content).Split('\n')) {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(trimmed.Substring(7), out var zone))
                return zone;
        }
        return -2;
    }

    public static string Text(byte[] content) {
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        return Encoding.UTF8.GetString(content);
    }
}
