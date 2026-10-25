using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

public sealed class FieldDefinitionService(CockpitDbContext db, IClock clock)
{
    public async Task<List<FieldDefinitionDto>> ListAsync(Guid? areaId, FieldTarget? appliesTo, bool includeArchived, CancellationToken ct = default)
    {
        var q = db.FieldDefinitions.AsNoTracking();
        if (areaId is { } a) q = q.Where(f => f.AreaId == a);
        if (appliesTo is { } t) q = q.Where(f => f.AppliesTo == t);
        if (!includeArchived) q = q.Where(f => !f.Archived);
        return (await q.OrderBy(f => f.AreaId).ThenBy(f => f.SortOrder).ThenBy(f => f.Name).ToListAsync(ct)).Select(f => f.ToDto()).ToList();
    }

    public async Task<Result<FieldDefinitionDto>> CreateAsync(FieldDefinitionDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<FieldDefinitionDto>.Invalid("Field name is required.");
        if (!await db.Areas.AnyAsync(a => a.Id == dto.AreaId, ct)) return Result<FieldDefinitionDto>.Invalid("Area does not exist.");
        if (dto.Type == FieldType.Select && dto.Options.Count == 0) return Result<FieldDefinitionDto>.Invalid("A select field needs at least one option.");

        var baseKey = string.IsNullOrWhiteSpace(dto.Key) ? Mapping.Slug(dto.Name) : Mapping.Slug(dto.Key);
        var taken = await db.FieldDefinitions.IgnoreQueryFilters()
            .Where(f => f.AreaId == dto.AreaId && f.AppliesTo == dto.AppliesTo).Select(f => f.Key).ToListAsync(ct);
        var key = baseKey;
        for (var n = 2; taken.Contains(key); n++) key = $"{baseKey}_{n}";

        var f = new FieldDefinition { Id = dto.Id == Guid.Empty ? Guid.NewGuid() : dto.Id, AreaId = dto.AreaId, AppliesTo = dto.AppliesTo, Key = key };
        f.ApplyFields(dto);
        if (dto.SortOrder == 0)
            f.SortOrder = (await db.FieldDefinitions.Where(x => x.AreaId == f.AreaId && x.AppliesTo == f.AppliesTo).MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1;
        db.FieldDefinitions.Add(f);
        await db.SaveChangesAsync(ct);
        return Result<FieldDefinitionDto>.Success(f.ToDto(), 201);
    }

    public async Task<Result<FieldDefinitionDto>> UpdateAsync(Guid id, FieldDefinitionDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return Result<FieldDefinitionDto>.Invalid("Field name is required.");
        var f = await db.FieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return Result<FieldDefinitionDto>.NotFound("Field");
        if (dto.Type == FieldType.Select && dto.Options.Count == 0) return Result<FieldDefinitionDto>.Invalid("A select field needs at least one option.");
        f.ApplyFields(dto); // Key, AreaId and AppliesTo are immutable: values already stored under the key must stay readable.
        await db.SaveChangesAsync(ct);
        return Result<FieldDefinitionDto>.Success(f.ToDto());
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var f = await db.FieldDefinitions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (f is null) return false;
        f.DeletedAt = clock.UtcNow;
        f.Archived = true;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
