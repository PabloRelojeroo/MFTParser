using System.Windows;
using System.Windows.Media;

namespace MftParser.Ui;

public static class Theme {
    static readonly Dictionary<string, string> Dark = new() {
        ["Bg"] = "#0F1115",
        ["Panel"] = "#151820",
        ["Panel2"] = "#1B1F29",
        ["Hover"] = "#1D2230",
        ["Selected"] = "#243049",
        ["Line"] = "#232733",
        ["GridLine"] = "#1C2029",
        ["Text"] = "#E6E8EE",
        ["Muted"] = "#8A90A2",
        ["Accent"] = "#7C9CFF",
        ["OnAccent"] = "#0F1115",
        ["Red"] = "#FF5C6C",
        ["Orange"] = "#FFB454",
        ["Green"] = "#5FD39A",
        ["Violet"] = "#C792EA",
        ["Track"] = "#2A2F3B",
        ["Thumb"] = "#2E3442",
    };

    static readonly Dictionary<string, string> Pink = new() {
        ["Bg"] = "#FFFFFF",
        ["Panel"] = "#FCE8EF",
        ["Panel2"] = "#FFF5F8",
        ["Hover"] = "#FADDE7",
        ["Selected"] = "#F8C9D9",
        ["Line"] = "#F1CFDB",
        ["GridLine"] = "#F7E3EA",
        ["Text"] = "#2B2228",
        ["Muted"] = "#8E6F7C",
        ["Accent"] = "#D63F7A",
        ["OnAccent"] = "#FFFFFF",
        ["Red"] = "#D3264A",
        ["Orange"] = "#B86B00",
        ["Green"] = "#1E9A5F",
        ["Violet"] = "#8E44C9",
        ["Track"] = "#F1CFDB",
        ["Thumb"] = "#E9B3C6",
    };

    static readonly string[] Tinted = { "Red", "Orange", "Accent", "Violet", "Green", "Muted" };

    public static bool IsPink { get; private set; }

    public static void Toggle() => Apply(!IsPink);

    public static void Apply(bool pink) {
        IsPink = pink;
        var resources = Application.Current.Resources;
        foreach (var pair in pink ? Pink : Dark) {
            var color = (Color)ColorConverter.ConvertFromString(pair.Value);
            resources[pair.Key] = Frozen(color);
            if (Array.IndexOf(Tinted, pair.Key) >= 0) {
                resources[pair.Key + "Soft"] = Frozen(Color.FromArgb(pink ? (byte)0x26 : (byte)0x33, color.R, color.G, color.B));
                resources[pair.Key + "Faint"] = Frozen(Color.FromArgb(pink ? (byte)0x14 : (byte)0x1F, color.R, color.G, color.B));
            }
        }
    }

    static SolidColorBrush Frozen(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
