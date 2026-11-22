using System.Windows;
using System.Windows.Media;
using MftParser.Localization;

namespace MftParser.Models;

public sealed class Category {
    public Category(string key, Func<string> label, Func<MftEntry, bool>? match, string? dot = null, bool sub = false, bool header = false) {
        Key = key;
        Label = label;
        Match = match ?? (_ => true);
        Dot = dot;
        Sub = sub;
        Header = header;
    }

    public string Key { get; }
    public Func<string> Label { get; }
    public Func<MftEntry, bool> Match { get; }
    public string? Dot { get; }
    public bool Sub { get; }
    public bool Header { get; }

    public static readonly Category[] All = {
        new("views", () => Strings.Views, null, header: true),
        new("all", () => Strings.CatAll, null),
        new("deleted", () => Strings.CatDeleted, e => !e.InUse, "Red"),
        new("timestomp", () => Strings.CatTimestomp, e => e.IsTimestomped, "Orange"),
        new("ads", () => Strings.CatAds, e => e.Has(EntryFlags.Ads), "Accent"),
        new("downloaded", () => Strings.CatDownloaded, e => e.IsDownloaded, "Violet"),
        new("hidden", () => Strings.CatHidden, e => e.IsHidden, "Muted"),
        new("orphans", () => Strings.CatOrphans, e => e.IsOrphan, "Muted"),

        new("ext", () => Strings.Extensions, null, header: true),
        new("x-exe", () => ".exe", e => e.Extension == "exe", sub: true),
        new("x-jar", () => ".jar", e => e.Extension == "jar", sub: true),
        new("x-dll", () => ".dll", e => e.Extension == "dll", sub: true),
        new("x-script", () => Strings.CatScripts, e => e.Extension is "bat" or "cmd" or "ps1" or "vbs" or "js" or "ahk" or "py", sub: true),
        new("x-archive", () => Strings.CatArchives, e => e.Extension is "zip" or "rar" or "7z" or "tar" or "gz", sub: true),
        new("x-pf", () => Strings.CatPrefetch, e => e.Extension == "pf", sub: true),

        new("dirs", () => Strings.Folders, null, header: true),
        new("d-mc", () => ".minecraft", e => Contains(e.Folder, @"\.minecraft"), sub: true),
        new("d-downloads", () => "Downloads", e => Contains(e.Folder, @"\Downloads"), sub: true),
        new("d-desktop", () => "Desktop", e => Contains(e.Folder, @"\Desktop"), sub: true),
        new("d-temp", () => "Temp", e => Contains(e.Folder, @"\Temp"), sub: true),
        new("d-recycle", () => Strings.CatRecycle, e => Contains(e.Folder, @"\$Recycle.Bin"), sub: true),
    };

    static bool Contains(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
}

public sealed class SideItem {
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Count { get; set; } = "";
    public bool IsHeader { get; set; }
    public Brush? Dot { get; set; }
    public Visibility DotVisibility => Dot == null ? Visibility.Collapsed : Visibility.Visible;
    public Thickness Indent { get; set; }
}
