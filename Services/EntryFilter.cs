using MftParser.Models;

namespace MftParser.Services;

public enum DateField {
    Any,
    Created,
    Modified,
    Changed,
    Accessed,
    FnCreated,
}

public sealed class FilterState {
    public string Category { get; set; } = "all";
    public string Query { get; set; } = "";
    public long From { get; set; }
    public long To { get; set; }
    public DateField Field { get; set; }
    public bool HideNoise { get; set; } = true;
    public bool ShowDirectories { get; set; }
    public bool ShowAdsRows { get; set; } = true;
    public string SortKey { get; set; } = "Line";
    public bool Descending { get; set; }
    public Dictionary<string, string> ColumnFilters { get; set; } = new();

    public FilterState Copy() {
        var copy = (FilterState)MemberwiseClone();
        copy.ColumnFilters = new Dictionary<string, string>(ColumnFilters);
        return copy;
    }
}

public sealed class FilterResult {
    public List<MftEntry> Rows { get; set; } = new();
    public int[] Counts { get; set; } = Array.Empty<int>();
    public int Timestomped { get; set; }
}

public static class EntryFilter {
    public static FilterResult Apply(MftEntry[] entries, List<MftEntry> adsRows, FilterState state, CancellationToken token) {
        var categories = Category.All;
        var counts = new int[categories.Length];
        var selected = Array.FindIndex(categories, c => c.Key == state.Category);
        if (selected < 0) selected = 1;
        var match = categories[selected].Match;

        var query = state.Query.Trim();
        long recordQuery = -1;
        if (query.StartsWith("#") && long.TryParse(query.Substring(1), out var number)) recordQuery = number;
        var pathQuery = query.IndexOf('\\') >= 0;

        var rows = new List<MftEntry>();
        var timestomped = 0;
        var columns = state.ColumnFilters.Where(f => f.Value.Length > 0).ToArray();

        void Consider(MftEntry e) {
            if (e.IsDirectory && !e.IsAds && !state.ShowDirectories) return;
            if (state.HideNoise && e.Has(EntryFlags.Noise)) return;
            if (!InRange(e, state)) return;

            if (recordQuery >= 0) {
                if (e.Index != recordQuery) return;
            } else if (query.Length > 0) {
                var hit = pathQuery
                    ? e.FullPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    : e.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                      || e.Folder.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) return;
            }

            foreach (var column in columns)
                if (!Matches(column.Key, column.Value, e)) return;

            if (e.IsTimestomped && !e.IsAds) timestomped++;
            for (var c = 0; c < categories.Length; c++)
                if (!categories[c].Header && categories[c].Match(e)) counts[c]++;

            if (match(e)) rows.Add(e);
        }

        for (var i = 0; i < entries.Length; i++) {
            if ((i & 0x3FFF) == 0) token.ThrowIfCancellationRequested();
            if (entries[i] != null) Consider(entries[i]);
        }

        if (state.ShowAdsRows)
            foreach (var row in adsRows) Consider(row);  

        token.ThrowIfCancellationRequested();
        Sort(rows, state.SortKey, state.Descending);
        return new FilterResult { Rows = rows, Counts = counts, Timestomped = timestomped };
    }

    static bool InRange(MftEntry e, FilterState s) {
        if (s.From == 0 && s.To == 0) return true;
        var to = s.To == 0 ? long.MaxValue : s.To;
        bool In(long t) => t >= s.From && t <= to;

        return s.Field switch {
            DateField.Created => In(e.SiCreated),
            DateField.Modified => In(e.SiModified),
            DateField.Changed => In(e.SiChanged),
            DateField.Accessed => In(e.SiAccessed),
            DateField.FnCreated => In(e.FnCreated),
            _ => In(e.SiCreated) || In(e.SiModified) || In(e.SiChanged),
        };
    }

    public static string ColumnKind(string key) => key switch {
        "Line" or "Index" or "Sequence" or "ParentIndex" or "ParentSequence" or "Size" or "LinkCount" or "Usn" or "Lsn" or "SecurityId" => "number",
        "SiCreated" or "FnCreated" or "SiModified" or "FnModified" or "SiChanged" or "FnChanged" or "SiAccessed" or "FnAccessed" => "date",
        "Tagged" or "InUse" or "IsDirectory" or "HasAds" or "IsAds" or "SiLessFn" or "USecZeros" or "Copied" => "bool",
        _ => "text",
    };

    static bool Matches(string key, string value, MftEntry e) {
        var text = ColumnText(key, e);
        return ColumnKind(key) is "number" or "bool"
            ? text.Equals(value, StringComparison.OrdinalIgnoreCase)
            : text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string ColumnText(string key, MftEntry e) => key switch {
        "Line" => e.Line.ToString(),
        "Tagged" => e.Tagged.ToString(),
        "SourceFile" => MftEntry.SourceFile,
        "Index" => e.Index.ToString(),
        "Sequence" => e.Sequence.ToString(),
        "InUse" => e.InUse.ToString(),
        "ParentIndex" => e.ParentIndex.ToString(),
        "ParentSequence" => e.ParentSequence.ToString(),
        "Folder" => e.Folder,
        "Name" => e.Name,
        "Extension" => e.ExtensionText,
        "Size" => e.Size.ToString(),
        "LinkCount" => e.LinkCount.ToString(),
        "ReparseTarget" => e.ReparseTarget,
        "IsDirectory" => e.IsDirectory.ToString(),
        "HasAds" => e.HasAds.ToString(),
        "IsAds" => e.IsAds.ToString(),
        "SiLessFn" => e.SiLessFn.ToString(),
        "USecZeros" => e.USecZeros.ToString(),
        "Copied" => e.Copied.ToString(),
        "SiFlags" => e.SiFlags,
        "NameType" => e.NameTypeText,
        "SiCreated" => e.Created10,
        "FnCreated" => e.Created30,
        "SiModified" => e.Modified10,
        "FnModified" => e.Modified30,
        "SiChanged" => e.Changed10,
        "FnChanged" => e.Changed30,
        "SiAccessed" => e.Accessed10,
        "FnAccessed" => e.Accessed30,
        "Usn" => e.Usn.ToString(),
        "Lsn" => e.Lsn.ToString(),
        "SecurityId" => e.SecurityId.ToString(),
        "ObjectId" => e.ObjectIdText,
        "LoggedUtilStream" => e.LoggedUtilStream,
        "ZoneIdContents" => e.ZoneIdContents,
        _ => "",
    };

    static Comparison<MftEntry> Text(Func<MftEntry, string> key) =>
        (a, b) => string.Compare(key(a), key(b), StringComparison.OrdinalIgnoreCase);

    static Comparison<MftEntry> Number(Func<MftEntry, long> key) => (a, b) => key(a).CompareTo(key(b));

    static Comparison<MftEntry> Flag(Func<MftEntry, bool> key) => (a, b) => key(a).CompareTo(key(b));

    public static void Sort(List<MftEntry> rows, string key, bool descending) {
        var compare = key switch {
            "Line" => Number(e => e.Line),
            "Tagged" => Flag(e => e.Tagged),
            "Index" => Number(e => e.Index),
            "Sequence" => Number(e => e.Sequence),
            "InUse" => Flag(e => e.InUse),
            "ParentIndex" => Number(e => e.ParentIndex),
            "ParentSequence" => Number(e => e.ParentSequence),
            "Folder" => Text(e => e.Folder),
            "Name" => Text(e => e.Name),
            "Extension" => Text(e => e.Extension),
            "Size" => Number(e => e.Size),
            "LinkCount" => Number(e => e.LinkCount),
            "ReparseTarget" => Text(e => e.ReparseTarget),
            "IsDirectory" => Flag(e => e.IsDirectory),
            "HasAds" => Flag(e => e.HasAds),
            "IsAds" => Flag(e => e.IsAds),
            "SiLessFn" => Flag(e => e.SiLessFn),
            "USecZeros" => Flag(e => e.USecZeros),
            "Copied" => Flag(e => e.Copied),
            "SiFlags" => Text(e => e.SiFlags),
            "NameType" => Number(e => e.NameType),
            "FnCreated" => Number(e => e.FnCreated),
            "SiModified" => Number(e => e.SiModified),
            "FnModified" => Number(e => e.FnModified),
            "SiChanged" => Number(e => e.SiChanged),
            "FnChanged" => Number(e => e.FnChanged),
            "SiAccessed" => Number(e => e.SiAccessed),
            "FnAccessed" => Number(e => e.FnAccessed),
            "Usn" => Number(e => e.Usn),
            "Lsn" => Number(e => e.Lsn),
            "SecurityId" => Number(e => e.SecurityId),
            "ObjectId" => Text(e => e.ObjectIdText),
            "LoggedUtilStream" => Text(e => e.LoggedUtilStream),
            "ZoneIdContents" => Text(e => e.ZoneIdContents),
            "SiCreated" => Number(e => e.SiCreated),
            _ => Number(e => e.Line),
        };

        rows.Sort(descending
            ? (a, b) => { var r = compare(b, a); return r != 0 ? r : Tie(b, a); }
            : (a, b) => { var r = compare(a, b); return r != 0 ? r : Tie(a, b); });
    }

    static int Tie(MftEntry a, MftEntry b) => a.Line.CompareTo(b.Line);

    public static bool StartsDescending(string key) =>
        key is "SiCreated" or "FnCreated" or "SiModified" or "FnModified" or "SiChanged" or "FnChanged"
            or "SiAccessed" or "FnAccessed" or "Size" or "Usn" or "Lsn";
}
