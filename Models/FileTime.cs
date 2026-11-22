using System.Globalization;

namespace MftParser.Models;

public static class FileTime {
    static readonly long Max = DateTime.MaxValue.ToFileTimeUtc();

    public static bool Utc { get; set; } = true;

    public static bool IsValid(long value) => value > 0 && value < Max;

    public static DateTime? Local(long value) {
        if (!IsValid(value)) return null;
        var utc = DateTime.FromFileTimeUtc(value);
        return Utc ? utc : utc.ToLocalTime();
    }

    public static long From(DateTime shown) =>
        Utc ? DateTime.SpecifyKind(shown, DateTimeKind.Utc).ToFileTimeUtc() : shown.ToUniversalTime().ToFileTimeUtc();

    public static DateTime Shown(DateTime local) => Utc ? local.ToUniversalTime() : local;

    public static bool HasZeroFraction(long value) => IsValid(value) && value % 10_000_000 == 0;

    public static string Short(long value) =>
        Local(value)?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "";

    public static string Precise(long value) =>
        Local(value)?.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) ?? "-";

    public static string Cell(long value) =>
        Local(value)?.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture) ?? "";
}

public static class Format {
    public static string Size(long bytes) {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576d).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        return (bytes / 1073741824d).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }
}
