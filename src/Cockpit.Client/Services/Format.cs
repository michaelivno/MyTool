using System.Text.RegularExpressions;
using Cockpit.Shared;

namespace Cockpit.Client.Services;

public static partial class Format
{
    /// <summary>"NextMilestone" / "in_progress" → "Next milestone" / "In progress".</summary>
    public static string Label(string raw)
    {
        var s = raw.Replace('_', ' ');
        s = PascalBoundary().Replace(s, "$1 $2").ToLowerInvariant().Trim();
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    }

    public static string Label<T>(T value) where T : struct, Enum => Label(value.ToString());

    public static string Date(DateOnly? d)
    {
        if (d is null) return "";
        var today = ApiClient.Today;
        var diff = d.Value.DayNumber - today.DayNumber;
        return diff switch
        {
            0 => "Today",
            1 => "Tomorrow",
            -1 => "Yesterday",
            > 1 and < 7 => d.Value.ToString("ddd"),
            _ => d.Value.Year == today.Year ? d.Value.ToString("d MMM") : d.Value.ToString("d MMM yyyy"),
        };
    }

    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc.ToUniversalTime();
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return utc.ToLocalTime().ToString("d MMM yyyy");
    }

    public static string DueClass(DateOnly? due, bool open = true)
    {
        if (due is null || !open) return "";
        var diff = due.Value.DayNumber - ApiClient.Today.DayNumber;
        return diff < 0 ? "due-overdue" : diff == 0 ? "due-today" : diff <= 3 ? "due-soon" : "";
    }

    public static string EffortLabel(Effort? e) => e?.ToString() ?? "";

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundary();
}
