using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

public sealed class ProjectService(CockpitDbContext db, SettingsService settings, IClock clock)
{
    public async Task<List<ProjectDto>> ListAsync(Guid? areaId, ProjectStatus? status, CancellationToken ct = default)
    {
        var q = db.Projects.AsNoTracking();
        if (areaId is { } a) q = q.Where(p => p.AreaId == a);
        if (status is { } s) q = q.Where(p => p.Status == s);
        return (await q.OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync(ct)).Select(p => p.ToDto()).ToList();
    }

    public async Task<ProjectDto?> GetAsync(Guid id, CancellationToken ct = default)
        => (await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct))?.ToDto();

    public async Task<Result<ProjectDto>> CreateAsync(ProjectDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<ProjectDto>.Invalid("Project name is required.");
        if (!await db.Areas.AnyAsync(a => a.Id == dto.AreaId, ct)) return Result<ProjectDto>.Invalid("Area does not exist.");

        var p = new Project { Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id, Status = ProjectStatus.Parked };
        p.ApplyFields(dto);
        var rule = await ApplyStatusAsync(p, dto.Status, dto.RevisitAt, ct);
        if (rule is not null) return rule;
        if (dto.SortOrder == 0)
            p.SortOrder = (await db.Projects.Where(x => x.Status == p.Status).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;

        db.Projects.Add(p);
        await db.SaveChangesAsync(ct);
        return Result<ProjectDto>.Success(p.ToDto(), 201);
    }

    public async Task<Result<ProjectDto>> UpdateAsync(Guid id, ProjectDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<ProjectDto>.Invalid("Project name is required.");
        var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Result<ProjectDto>.NotFound("Project");
        if (p.AreaId != dto.AreaId && !await db.Areas.AnyAsync(a => a.Id == dto.AreaId, ct))
            return Result<ProjectDto>.Invalid("Area does not exist.");

        var areaChanged = p.AreaId != dto.AreaId;
        p.ApplyFields(dto);
        var rule = await ApplyStatusAsync(p, dto.Status, dto.RevisitAt, ct);
        if (rule is not null) return rule;

        if (areaChanged)
        {
            // Tasks always carry their project's area (keeps "group by area" a plain query).
            await db.Tasks.Where(t => t.ProjectId == p.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.AreaId, p.AreaId), ct);
        }

        await db.SaveChangesAsync(ct);
        return Result<ProjectDto>.Success(p.ToDto());
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return false;
        var now = clock.UtcNow;
        p.DeletedAt = now;
        await db.Tasks.Where(t => t.ProjectId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, now), ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var projects = await db.Projects.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        for (var i = 0; i < ids.Count; i++)
        {
            var p = projects.FirstOrDefault(x => x.Id == ids[i]);
            if (p is not null) p.SortOrder = i + 1;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Applies a status change with the two project rules:
    /// parked ⇒ revisit date required; activating ⇒ WIP limit not exceeded.
    /// Returns a failed Result when a rule blocks the change, otherwise null.
    /// </summary>
    private async Task<Result<ProjectDto>?> ApplyStatusAsync(Project p, ProjectStatus newStatus, DateOnly? revisitAt, CancellationToken ct)
    {
        if (ProjectRules.ValidateParked(newStatus, revisitAt) is { } parkedError)
            return Result<ProjectDto>.Fail(parkedError, 400);

        if (ProjectRules.IsActivation(p.Status, newStatus))
        {
            var limit = await settings.GetWipLimitAsync(ct);
            var active = await db.Projects.Include(x => x.Area)
                .Where(x => x.Status == ProjectStatus.Active && x.Id != p.Id)
                .OrderBy(x => x.SortOrder).ToListAsync(ct);
            if (ProjectRules.ExceedsWip(active.Count, limit))
                return Result<ProjectDto>.Fail(ProjectRules.WipError(limit, active.Select(a => a.ToSummary())), 409);
        }

        p.Status = newStatus;
        p.RevisitAt = revisitAt;
        p.CompletedAt = newStatus.IsClosed() ? (p.CompletedAt ?? clock.UtcNow) : null;
        return null;
    }
}
