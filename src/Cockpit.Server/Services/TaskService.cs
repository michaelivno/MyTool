using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

public sealed class TaskService(CockpitDbContext db, IClock clock)
{
    public async Task<List<TaskDto>> ListAsync(Guid? areaId, Guid? projectId, bool includeClosed, CancellationToken ct = default)
    {
        var q = db.Tasks.AsNoTracking();
        if (areaId is { } a) q = q.Where(t => t.AreaId == a);
        if (projectId is { } p) q = q.Where(t => t.ProjectId == p);
        if (!includeClosed) q = q.Where(t => t.Status == TaskItemStatus.Todo || t.Status == TaskItemStatus.Doing);
        return (await q.OrderBy(t => t.SortOrder).ThenBy(t => t.Due).ThenBy(t => t.CreatedAt).ToListAsync(ct)).Select(t => t.ToDto()).ToList();
    }

    public async Task<TaskDto?> GetAsync(Guid id, CancellationToken ct = default)
        => (await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct))?.ToDto();

    public async Task<Result<TaskDto>> CreateAsync(TaskDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return Result<TaskDto>.Invalid("Task title is required.");
        var t = new TaskItem { Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id };
        var owner = await ResolveOwnerAsync(dto.ProjectId, dto.AreaId, ct);
        if (owner.Error is not null) return Result<TaskDto>.Invalid(owner.Error);
        t.ProjectId = owner.ProjectId;
        t.AreaId = owner.AreaId;
        t.ApplyFields(dto);
        ApplyStatus(t, dto.Status);
        if (dto.SortOrder == 0)
            t.SortOrder = (await db.Tasks.Where(x => x.AreaId == t.AreaId).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
        db.Tasks.Add(t);
        await db.SaveChangesAsync(ct);
        return Result<TaskDto>.Success(t.ToDto(), 201);
    }

    public async Task<Result<TaskDto>> UpdateAsync(Guid id, TaskDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Title)) return Result<TaskDto>.Invalid("Task title is required.");
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Result<TaskDto>.NotFound("Task");
        if (t.ProjectId != dto.ProjectId || t.AreaId != dto.AreaId)
        {
            var owner = await ResolveOwnerAsync(dto.ProjectId, dto.AreaId, ct);
            if (owner.Error is not null) return Result<TaskDto>.Invalid(owner.Error);
            t.ProjectId = owner.ProjectId;
            t.AreaId = owner.AreaId;
        }
        t.ApplyFields(dto);
        ApplyStatus(t, dto.Status);
        await db.SaveChangesAsync(ct);
        return Result<TaskDto>.Success(t.ToDto());
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.Tasks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return false;
        t.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var tasks = await db.Tasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
        for (var i = 0; i < ids.Count; i++)
        {
            var t = tasks.FirstOrDefault(x => x.Id == ids[i]);
            if (t is not null) t.SortOrder = i + 1;
        }
        await db.SaveChangesAsync(ct);
    }

    private void ApplyStatus(TaskItem t, TaskItemStatus status)
    {
        t.Status = status;
        t.CompletedAt = status is TaskItemStatus.Done or TaskItemStatus.Dropped ? (t.CompletedAt ?? clock.UtcNow) : null;
    }

    /// <summary>A task belongs to a project (area derived) or directly to an area.</summary>
    private async Task<(Guid? ProjectId, Guid AreaId, string? Error)> ResolveOwnerAsync(Guid? projectId, Guid areaId, CancellationToken ct)
    {
        if (projectId is { } pid)
        {
            var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct);
            if (project is null) return (null, Guid.Empty, "Project does not exist.");
            return (pid, project.AreaId, null);
        }
        if (!await db.Areas.AnyAsync(a => a.Id == areaId, ct)) return (null, Guid.Empty, "Area does not exist.");
        return (null, areaId, null);
    }
}
