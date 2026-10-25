using System.Text.Json;
using System.Text.Json.Nodes;
using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

public sealed class ReviewService(CockpitDbContext db, SettingsService settings, InboxService inbox, ViewsService views, IClock clock)
{
    public async Task<ReviewDto?> GetCurrentAsync(CancellationToken ct = default)
        => (await db.Reviews.AsNoTracking().Where(r => r.Status == ReviewStatus.InProgress).OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync(ct))?.ToDto();

    public async Task<ReviewDto?> GetAsync(Guid id, CancellationToken ct = default)
        => (await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct))?.ToDto();

    public async Task<List<ReviewDto>> ListAsync(int limit, CancellationToken ct = default)
        => (await db.Reviews.AsNoTracking().Where(r => r.Status != ReviewStatus.InProgress).OrderByDescending(r => r.StartedAt).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct))
            .Select(r => r.ToDto()).ToList();

    /// <summary>Resumes the in-progress review if there is one, otherwise starts a new one with a snapshot of the current state.</summary>
    public async Task<ReviewDto> StartOrResumeAsync(DateOnly today, CancellationToken ct = default)
    {
        var current = await db.Reviews.FirstOrDefaultAsync(r => r.Status == ReviewStatus.InProgress, ct);
        if (current is not null) return current.ToDto();

        var review = new Review
        {
            Status = ReviewStatus.InProgress,
            Step = ReviewWizard.FirstStep,
            StartedAt = clock.UtcNow,
            SnapshotJson = new JsonObject { ["start"] = await SnapshotAsync(today, ct) }.ToJsonString(),
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
        return review.ToDto();
    }

    public async Task<Result<ReviewDto>> MoveToStepAsync(Guid id, int step, CancellationToken ct = default)
    {
        var r = await db.Reviews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Result<ReviewDto>.NotFound("Review");
        if (ReviewWizard.ValidateMove(r.Status, r.Step, step) is { } err) return Result<ReviewDto>.Fail(err, 400);
        r.Step = step;
        await db.SaveChangesAsync(ct);
        return Result<ReviewDto>.Success(r.ToDto());
    }

    public async Task<Result<ReviewDto>> AddDecisionAsync(Guid id, ReviewDecisionRequest req, CancellationToken ct = default)
    {
        var r = await db.Reviews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Result<ReviewDto>.NotFound("Review");
        if (ReviewWizard.ValidateDecision(r.Status, req.Decision) is { } err) return Result<ReviewDto>.Fail(err, 400);
        var project = await db.Projects.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProjectId, ct);
        if (project is null) return Result<ReviewDto>.Invalid("Project does not exist.");

        var decisions = Mapping.ParseDecisions(r.DecisionsJson);
        decisions.RemoveAll(d => d.ProjectId == req.ProjectId); // latest decision per project wins
        decisions.Add(new ReviewDecisionDto { ProjectId = project.Id, ProjectName = project.Name, Decision = req.Decision, Note = Mapping.NullIfBlank(req.Note), At = clock.UtcNow });
        r.DecisionsJson = JsonSerializer.Serialize(decisions, CockpitJson.Options);
        await db.SaveChangesAsync(ct);
        return Result<ReviewDto>.Success(r.ToDto());
    }

    public async Task<Result<ReviewDto>> FinishAsync(Guid id, string? notes, DateOnly today, CancellationToken ct = default)
    {
        var r = await db.Reviews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Result<ReviewDto>.NotFound("Review");
        var activeCount = await db.Projects.CountAsync(p => !p.Area!.Archived && p.Status == ProjectStatus.Active, ct);
        var limit = await settings.GetWipLimitAsync(ct);
        if (ReviewWizard.ValidateFinish(r.Status, r.Step, activeCount, limit) is { } err)
            return Result<ReviewDto>.Fail(err, err.Code == ApiError.WipLimitExceeded ? 409 : 400);

        var snapshot = Mapping.ParseObject(r.SnapshotJson) ?? new JsonObject();
        snapshot["end"] = await SnapshotAsync(today, ct);
        r.SnapshotJson = snapshot.ToJsonString();
        r.Notes = Mapping.NullIfBlank(notes);
        r.Status = ReviewStatus.Finished;
        r.FinishedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<ReviewDto>.Success(r.ToDto());
    }

    public async Task<Result<ReviewDto>> AbandonAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.Reviews.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Result<ReviewDto>.NotFound("Review");
        if (ReviewWizard.ValidateAbandon(r.Status) is { } err) return Result<ReviewDto>.Fail(err, 400);
        r.Status = ReviewStatus.Abandoned;
        r.FinishedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Result<ReviewDto>.Success(r.ToDto());
    }

    public async Task<ReviewContext> ContextAsync(DateOnly today, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var open = await db.Projects.AsNoTracking().Include(p => p.Area)
            .Where(p => !p.Area!.Archived && (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Parked))
            .OrderBy(p => p.Status).ThenBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(ct);
        var cards = await views.CardsAsync(open, today, ct);
        return new ReviewContext
        {
            Today = today,
            WipLimit = s.WipLimit,
            ActiveCount = cards.Count(c => c.Project.Status == ProjectStatus.Active),
            InboxCount = await inbox.CountUnprocessedAsync(ct),
            StaleInboxCount = await inbox.CountStaleAsync(s.InboxStaleDays, ct),
            StaleDays = s.InboxStaleDays,
            Active = cards.Where(c => c.Project.Status == ProjectStatus.Active).ToList(),
            Decide = cards.Where(c => ProjectRules.IsDueForDecision(c.Project.Status, c.Project.RevisitAt, today)).OrderBy(c => c.Project.RevisitAt).ToList(),
            Candidates = cards,
        };
    }

    /// <summary>What was active / parked / overdue at this moment — stored on the review for later reading.</summary>
    private async Task<JsonObject> SnapshotAsync(DateOnly today, CancellationToken ct)
    {
        var s = await settings.GetAsync(ct);
        var projects = await db.Projects.AsNoTracking().Include(p => p.Area).Where(p => !p.Area!.Archived).ToListAsync(ct);
        var openTasks = await db.Tasks.AsNoTracking().Where(t => !t.Area!.Archived && (t.Status == TaskItemStatus.Todo || t.Status == TaskItemStatus.Doing))
            .Select(t => new { t.Due }).ToListAsync(ct);

        static JsonArray Names(IEnumerable<Project> ps) => new(ps.Select(p => (JsonNode?)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["area"] = p.Area?.Name }).ToArray());

        return new JsonObject
        {
            ["at"] = clock.UtcNow,
            ["today"] = today.ToString("yyyy-MM-dd"),
            ["wipLimit"] = s.WipLimit,
            ["active"] = Names(projects.Where(p => p.Status == ProjectStatus.Active)),
            ["parked"] = Names(projects.Where(p => p.Status == ProjectStatus.Parked)),
            ["decide"] = Names(projects.Where(p => ProjectRules.IsDueForDecision(p.Status, p.RevisitAt, today))),
            ["openTasks"] = openTasks.Count,
            ["overdueTasks"] = openTasks.Count(t => t.Due != null && t.Due < today),
            ["inboxUnprocessed"] = await inbox.CountUnprocessedAsync(ct),
            ["inboxStale"] = await inbox.CountStaleAsync(s.InboxStaleDays, ct),
        };
    }
}
