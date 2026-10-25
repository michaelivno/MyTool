using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

/// <summary>All screens are derived from the data here; nothing is stored per view.</summary>
public sealed class ViewsService(CockpitDbContext db, SettingsService settings, InboxService inbox)
{
    public async Task<TodayView> TodayAsync(DateOnly today, CancellationToken ct = default)
    {
        var rows = await OpenTaskRows()
            .Where(t => (t.Due != null && t.Due <= today) || t.Status == TaskItemStatus.Doing)
            .OrderBy(t => t.Due).ThenBy(t => t.SortOrder).ThenBy(t => t.CreatedAt)
            .ToListAsync(ct);

        return new TodayView
        {
            Today = today,
            Tasks = rows.Select(t => Row(t, t.Due < today ? "overdue" : t.Due == today ? "today" : "doing")).ToList(),
            InboxCount = await inbox.CountUnprocessedAsync(ct),
            DecideCount = await db.Projects.CountAsync(p => !p.Area!.Archived && p.Status == ProjectStatus.Parked && p.RevisitAt != null && p.RevisitAt <= today, ct),
        };
    }

    public async Task<WeekView> WeekAsync(DateOnly today, CancellationToken ct = default)
    {
        var to = today.AddDays(6);
        var rows = await OpenTaskRows()
            .Where(t => t.Due != null && t.Due <= to)
            .OrderBy(t => t.Due).ThenBy(t => t.SortOrder)
            .ToListAsync(ct);
        var active = await db.Projects.AsNoTracking().Include(p => p.Area)
            .Where(p => !p.Area!.Archived && p.Status == ProjectStatus.Active)
            .OrderBy(p => p.SortOrder).ToListAsync(ct);

        return new WeekView
        {
            From = today, To = to,
            Tasks = rows.Select(t => Row(t, t.Due < today ? "overdue" : t.Due == today ? "today" : "week")).ToList(),
            ActiveProjects = await CardsAsync(active, today, ct),
        };
    }

    public async Task<BoardView> BoardAsync(DateOnly today, Guid? areaId, CancellationToken ct = default)
    {
        var q = db.Projects.AsNoTracking().Include(p => p.Area).Where(p => !p.Area!.Archived);
        if (areaId is { } a) q = q.Where(p => p.AreaId == a);
        var projects = await q.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(ct);
        return new BoardView
        {
            WipLimit = await settings.GetWipLimitAsync(ct),
            ActiveCount = await db.Projects.CountAsync(p => !p.Area!.Archived && p.Status == ProjectStatus.Active, ct),
            Cards = await CardsAsync(projects, today, ct),
        };
    }

    public async Task<DecideView> DecideAsync(DateOnly today, CancellationToken ct = default)
    {
        var projects = await db.Projects.AsNoTracking().Include(p => p.Area)
            .Where(p => !p.Area!.Archived && p.Status == ProjectStatus.Parked && p.RevisitAt != null && p.RevisitAt <= today)
            .OrderBy(p => p.RevisitAt).ThenBy(p => p.Name).ToListAsync(ct);
        return new DecideView { Today = today, Projects = await CardsAsync(projects, today, ct) };
    }

    public async Task<AreasOverview> AreasAsync(DateOnly today, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var areas = await db.Areas.AsNoTracking().Where(a => !a.Archived).OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct);
        var projects = await db.Projects.AsNoTracking().Where(p => !p.Area!.Archived).ToListAsync(ct);
        var tasks = await db.Tasks.AsNoTracking().Where(t => !t.Area!.Archived)
            .Select(t => new { t.AreaId, t.ProjectId, t.Status, t.Due, t.UpdatedAt }).ToListAsync(ct);

        var cards = new List<AreaCardDto>();
        foreach (var a in areas)
        {
            var ap = projects.Where(p => p.AreaId == a.Id).ToList();
            var at = tasks.Where(t => t.AreaId == a.Id).ToList();
            var open = at.Where(t => t.Status.IsOpen()).ToList();
            var card = new AreaCardDto
            {
                Area = a.ToDto(),
                ActiveProjects = ap.Count(p => p.Status == ProjectStatus.Active),
                ParkedProjects = ap.Count(p => p.Status == ProjectStatus.Parked),
                OpenTasks = open.Count,
                OverdueTasks = open.Count(t => t.Due != null && t.Due < today),
                LastTouched = new[] { a.UpdatedAt }.Concat(ap.Select(p => p.UpdatedAt)).Concat(at.Select(t => t.UpdatedAt)).Max(),
            };
            var withDeadline = ap.Where(p => !p.Status.IsClosed() && p.Deadline != null).OrderBy(p => p.Deadline).FirstOrDefault();
            if (withDeadline is not null)
            {
                card.NearestDeadline = withDeadline.Deadline;
                card.NearestDeadlineProject = withDeadline.Name;
            }

            foreach (var p in ap.Where(p => p.Status == ProjectStatus.Active))
            {
                if (!open.Any(t => t.ProjectId == p.Id))
                    card.Flags.Add(new HealthFlagDto { Code = HealthFlagDto.NoNextAction, ProjectId = p.Id, Message = $"“{p.Name}” is active but has no next action." });
                if (p.Deadline is { } d && d < today)
                    card.Flags.Add(new HealthFlagDto { Code = HealthFlagDto.DeadlinePassed, ProjectId = p.Id, Message = $"“{p.Name}” deadline passed ({d:yyyy-MM-dd})." });
                else if (p.Deadline is { } d2 && d2 <= today.AddDays(7))
                    card.Flags.Add(new HealthFlagDto { Code = HealthFlagDto.DeadlineSoon, ProjectId = p.Id, Message = $"“{p.Name}” is due {d2:yyyy-MM-dd}." });
            }
            var revisitDue = ap.Count(p => ProjectRules.IsDueForDecision(p.Status, p.RevisitAt, today));
            if (revisitDue > 0)
                card.Flags.Add(new HealthFlagDto { Code = HealthFlagDto.RevisitDue, Message = $"{revisitDue} parked project(s) waiting for a decision." });
            if (card.OverdueTasks > 0)
                card.Flags.Add(new HealthFlagDto { Code = HealthFlagDto.Overdue, Message = $"{card.OverdueTasks} overdue task(s)." });
            cards.Add(card);
        }

        return new AreasOverview
        {
            Today = today,
            WipLimit = s.WipLimit,
            ActiveCount = projects.Count(p => p.Status == ProjectStatus.Active),
            InboxCount = await inbox.CountUnprocessedAsync(ct),
            StaleInboxCount = await inbox.CountStaleAsync(s.InboxStaleDays, ct),
            Areas = cards,
        };
    }

    public async Task<NavCounts> NavCountsAsync(DateOnly today, CancellationToken ct = default) => new()
    {
        Inbox = await inbox.CountUnprocessedAsync(ct),
        Decide = await db.Projects.CountAsync(p => !p.Area!.Archived && p.Status == ProjectStatus.Parked && p.RevisitAt != null && p.RevisitAt <= today, ct),
        Today = await OpenTaskRows().CountAsync(t => (t.Due != null && t.Due <= today) || t.Status == TaskItemStatus.Doing, ct),
        ActiveProjects = await db.Projects.CountAsync(p => !p.Area!.Archived && p.Status == ProjectStatus.Active, ct),
        WipLimit = await settings.GetWipLimitAsync(ct),
    };

    public async Task<List<ProjectCardDto>> CardsAsync(List<Project> projects, DateOnly today, CancellationToken ct = default)
    {
        var ids = projects.Select(p => p.Id).ToList();
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => t.ProjectId != null && ids.Contains(t.ProjectId.Value))
            .Select(t => new { t.ProjectId, t.Title, t.Status, t.Due, t.SortOrder, t.UpdatedAt })
            .ToListAsync(ct);

        return projects.Select(p =>
        {
            var mine = tasks.Where(t => t.ProjectId == p.Id).ToList();
            var open = mine.Where(t => t.Status.IsOpen()).ToList();
            var next = open.OrderByDescending(t => t.Status == TaskItemStatus.Doing)
                .ThenBy(t => t.Due ?? DateOnly.MaxValue).ThenBy(t => t.SortOrder).FirstOrDefault();
            return new ProjectCardDto
            {
                Project = p.ToDto(),
                AreaName = p.Area?.Name ?? "",
                AreaColor = p.Area?.Color ?? "",
                OpenTaskCount = open.Count,
                OverdueTaskCount = open.Count(t => t.Due != null && t.Due < today),
                NextTaskTitle = next?.Title,
                LastTouched = mine.Select(t => t.UpdatedAt).Append(p.UpdatedAt).Max(),
            };
        }).ToList();
    }

    private IQueryable<TaskItem> OpenTaskRows() => db.Tasks.AsNoTracking()
        .Include(t => t.Area).Include(t => t.Project)
        .Where(t => !t.Area!.Archived && (t.Status == TaskItemStatus.Todo || t.Status == TaskItemStatus.Doing));

    private static TaskRowDto Row(TaskItem t, string bucket) => new()
    {
        Task = t.ToDto(),
        AreaId = t.AreaId,
        AreaName = t.Area?.Name ?? "",
        AreaColor = t.Area?.Color ?? "",
        ProjectName = t.Project?.Name,
        Bucket = bucket,
    };
}
