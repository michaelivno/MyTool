using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

public sealed class AreaService(CockpitDbContext db, IClock clock)
{
    public async Task<List<AreaDto>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var q = db.Areas.AsNoTracking();
        if (!includeArchived) q = q.Where(a => !a.Archived);
        return (await q.OrderBy(a => a.SortOrder).ThenBy(a => a.Name).ToListAsync(ct)).Select(a => a.ToDto()).ToList();
    }

    public async Task<AreaDto?> GetAsync(Guid id, CancellationToken ct = default)
        => (await db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct))?.ToDto();

    public async Task<Result<AreaDto>> CreateAsync(AreaDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<AreaDto>.Invalid("Area name is required.");
        var a = new Area { Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id };
        a.Apply(dto);
        if (dto.SortOrder == 0)
            a.SortOrder = (await db.Areas.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
        db.Areas.Add(a);
        await db.SaveChangesAsync(ct);
        return Result<AreaDto>.Success(a.ToDto(), 201);
    }

    public async Task<Result<AreaDto>> UpdateAsync(Guid id, AreaDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<AreaDto>.Invalid("Area name is required.");
        var a = await db.Areas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Result<AreaDto>.NotFound("Area");
        a.Apply(dto);
        await db.SaveChangesAsync(ct);
        return Result<AreaDto>.Success(a.ToDto());
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var a = await db.Areas.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return false;
        var now = clock.UtcNow;
        a.DeletedAt = now;
        a.Archived = true;
        // Soft delete cascades: an area's projects, tasks and field definitions go with it (all recoverable in the DB file).
        await db.Projects.Where(p => p.AreaId == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.DeletedAt, now), ct);
        await db.Tasks.Where(t => t.AreaId == id).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, now), ct);
        await db.FieldDefinitions.Where(f => f.AreaId == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.DeletedAt, now), ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReorderAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        var areas = await db.Areas.Where(a => ids.Contains(a.Id)).ToListAsync(ct);
        for (var i = 0; i < ids.Count; i++)
        {
            var a = areas.FirstOrDefault(x => x.Id == ids[i]);
            if (a is not null) a.SortOrder = i + 1;
        }
        await db.SaveChangesAsync(ct);
    }
}
