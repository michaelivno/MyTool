using Cockpit.Server.Data;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

/// <summary>
/// First-run data from the plan. Only runs on an empty database.
/// Statuses and revisit dates are starting points — finalize them in the UI.
/// </summary>
public sealed class Seeder(CockpitDbContext db, SettingsService settings)
{
    // TODO(Michael): confirm which three start active, and add Day job projects.
    private static readonly HashSet<string> InitiallyActive = new(StringComparer.OrdinalIgnoreCase)
    {
        "AI discovery scanner MVP", "Vektralog", "Holon apartment search",
    };

    private static readonly DateOnly DefaultRevisit = new(2026, 12, 1);

    private static readonly (string Name, AreaKind Kind, string Color, string[] Projects)[] Plan =
    {
        ("Day job", AreaKind.Work, "#3b82f6", Array.Empty<string>()),
        ("IvnoWorks", AreaKind.Business, "#8b5cf6", new[] { "Vektralog", "Probity", "Last Pixel", "ivnoworks.com", "M&M's Skins" }),
        ("Startup", AreaKind.Startup, "#f59e0b", new[] { "AI discovery scanner MVP" }),
        ("Marketing side work", AreaKind.Business, "#ec4899", new[] { "Yallabama ads", "PPC clients" }),
        ("Home", AreaKind.Home, "#10b981", new[] { "Garden decking", "Holon apartment search", "Home tasks" }),
        ("Household finance", AreaKind.Finance, "#14b8a6", new[] { "House economic plan" }),
        ("Health", AreaKind.Health, "#ef4444", new[] { "Gym" }),
    };

    public async Task<bool> SeedIfEmptyAsync(CancellationToken ct = default)
    {
        if (await db.Areas.IgnoreQueryFilters().AnyAsync(ct)) return false;

        var areaOrder = 0;
        var projectOrder = 0;
        foreach (var (name, kind, color, projects) in Plan)
        {
            var area = new Area { Name = name, Kind = kind, Color = color, SortOrder = ++areaOrder, ReviewCadence = kind == AreaKind.Finance ? ReviewCadence.Monthly : ReviewCadence.Weekly };
            db.Areas.Add(area);
            foreach (var pn in projects)
            {
                var active = InitiallyActive.Contains(pn);
                db.Projects.Add(new Project
                {
                    Area = area,
                    Name = pn,
                    Status = active ? ProjectStatus.Active : ProjectStatus.Parked,
                    RevisitAt = active ? null : DefaultRevisit,
                    SortOrder = ++projectOrder,
                });
            }
        }
        await db.SaveChangesAsync(ct);
        await settings.SetRawAsync(new Dictionary<string, string> { [SettingsService.WipLimitKey] = "3", [SettingsService.InboxStaleDaysKey] = "7" }, ct);
        return true;
    }
}
