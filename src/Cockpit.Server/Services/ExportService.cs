using System.Text.Json;
using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

/// <summary>Lossless JSON export/import. Includes soft-deleted rows and preserves ids and timestamps.</summary>
public sealed class ExportService(CockpitDbContext db, SettingsService settings, IClock clock)
{
    public const int CurrentVersion = 1;

    public async Task<ExportDocument> ExportAsync(CancellationToken ct = default) => new()
    {
        Version = CurrentVersion,
        ExportedAt = clock.UtcNow,
        Settings = await settings.GetRawAsync(ct),
        Areas = (await db.Areas.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToDto()).ToList(),
        FieldDefinitions = (await db.FieldDefinitions.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToDto()).ToList(),
        Projects = (await db.Projects.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToDto()).ToList(),
        Tasks = (await db.Tasks.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToDto()).ToList(),
        InboxItems = (await db.InboxItems.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToDto()).ToList(),
        Reviews = (await db.Reviews.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.StartedAt).ThenBy(x => x.Id).ToListAsync(ct)).Select(x => x.ToDto()).ToList(),
    };

    public static byte[] Serialize(ExportDocument doc) => JsonSerializer.SerializeToUtf8Bytes(doc, CockpitJson.Indented());

    public async Task<Result<ImportResult>> ImportAsync(ExportDocument doc, ImportMode mode, CancellationToken ct = default)
    {
        if (!string.Equals(doc.App, "cockpit", StringComparison.OrdinalIgnoreCase))
            return Result<ImportResult>.Invalid("This file is not a Cockpit export.");
        if (doc.Version > CurrentVersion)
            return Result<ImportResult>.Invalid($"Export version {doc.Version} is newer than this app supports ({CurrentVersion}).");

        // Referential integrity against the document itself (merge also accepts ids already in the DB).
        var areaIds = doc.Areas.Select(a => a.Id).ToHashSet();
        var projectIds = doc.Projects.Select(p => p.Id).ToHashSet();
        if (mode == ImportMode.Merge)
        {
            areaIds.UnionWith(await db.Areas.IgnoreQueryFilters().Select(a => a.Id).ToListAsync(ct));
            projectIds.UnionWith(await db.Projects.IgnoreQueryFilters().Select(p => p.Id).ToListAsync(ct));
        }
        foreach (var p in doc.Projects)
            if (!areaIds.Contains(p.AreaId)) return Result<ImportResult>.Invalid($"Project “{p.Name}” references an unknown area.");
        foreach (var f in doc.FieldDefinitions)
            if (!areaIds.Contains(f.AreaId)) return Result<ImportResult>.Invalid($"Field “{f.Name}” references an unknown area.");
        foreach (var t in doc.Tasks)
        {
            if (!areaIds.Contains(t.AreaId)) return Result<ImportResult>.Invalid($"Task “{t.Title}” references an unknown area.");
            if (t.ProjectId is { } pid && !projectIds.Contains(pid)) return Result<ImportResult>.Invalid($"Task “{t.Title}” references an unknown project.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.PreserveTimestamps = true;
        try
        {
            if (mode == ImportMode.Replace)
            {
                await db.Tasks.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
                await db.Projects.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
                await db.FieldDefinitions.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
                await db.Areas.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
                await db.InboxItems.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
                await db.Reviews.IgnoreQueryFilters().ExecuteDeleteAsync(ct);
                await db.Settings.ExecuteDeleteAsync(ct);
                db.ChangeTracker.Clear();
            }

            foreach (var d in doc.Areas)
            {
                var e = mode == ImportMode.Merge ? await db.Areas.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.Id, ct) : null;
                if (e is null) { e = new Area { Id = d.Id }; db.Areas.Add(e); }
                e.Apply(d);
                Stamp(e, d.CreatedAt, d.UpdatedAt, d.DeletedAt);
            }
            await db.SaveChangesAsync(ct);

            foreach (var d in doc.FieldDefinitions)
            {
                var e = mode == ImportMode.Merge ? await db.FieldDefinitions.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.Id, ct) : null;
                if (e is null) { e = new FieldDefinition { Id = d.Id }; db.FieldDefinitions.Add(e); }
                e.AreaId = d.AreaId; e.AppliesTo = d.AppliesTo; e.Key = d.Key;
                e.ApplyFields(d);
                Stamp(e, d.CreatedAt, d.UpdatedAt, d.DeletedAt);
            }
            await db.SaveChangesAsync(ct);

            foreach (var d in doc.Projects)
            {
                var e = mode == ImportMode.Merge ? await db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.Id, ct) : null;
                if (e is null) { e = new Project { Id = d.Id }; db.Projects.Add(e); }
                e.ApplyFields(d);
                e.Status = d.Status; e.RevisitAt = d.RevisitAt; e.CompletedAt = d.CompletedAt;
                Stamp(e, d.CreatedAt, d.UpdatedAt, d.DeletedAt);
            }
            await db.SaveChangesAsync(ct);

            foreach (var d in doc.Tasks)
            {
                var e = mode == ImportMode.Merge ? await db.Tasks.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.Id, ct) : null;
                if (e is null) { e = new TaskItem { Id = d.Id }; db.Tasks.Add(e); }
                e.AreaId = d.AreaId; e.ProjectId = d.ProjectId;
                e.ApplyFields(d);
                e.Status = d.Status; e.CompletedAt = d.CompletedAt;
                Stamp(e, d.CreatedAt, d.UpdatedAt, d.DeletedAt);
            }
            await db.SaveChangesAsync(ct);

            foreach (var d in doc.InboxItems)
            {
                var e = mode == ImportMode.Merge ? await db.InboxItems.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.Id, ct) : null;
                if (e is null) { e = new InboxItem { Id = d.Id }; db.InboxItems.Add(e); }
                e.Text = d.Text; e.ProcessedAt = d.ProcessedAt; e.ConvertedTo = d.ConvertedTo; e.ConvertedId = d.ConvertedId;
                Stamp(e, d.CreatedAt, d.CreatedAt, d.DeletedAt);
            }
            await db.SaveChangesAsync(ct);

            foreach (var d in doc.Reviews)
            {
                var e = mode == ImportMode.Merge ? await db.Reviews.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == d.Id, ct) : null;
                if (e is null) { e = new Review { Id = d.Id }; db.Reviews.Add(e); }
                e.Status = d.Status; e.Step = d.Step; e.StartedAt = d.StartedAt; e.FinishedAt = d.FinishedAt; e.Notes = d.Notes;
                e.SnapshotJson = Mapping.ToJson(d.Snapshot);
                e.DecisionsJson = JsonSerializer.Serialize(d.Decisions, CockpitJson.Options);
                Stamp(e, d.StartedAt, d.UpdatedAt, d.DeletedAt);
            }
            await db.SaveChangesAsync(ct);

            if (doc.Settings.Count > 0) await settings.SetRawAsync(doc.Settings, ct);

            await tx.CommitAsync(ct);
        }
        finally
        {
            db.PreserveTimestamps = false;
        }

        return Result<ImportResult>.Success(new ImportResult
        {
            Mode = mode, Areas = doc.Areas.Count, FieldDefinitions = doc.FieldDefinitions.Count, Projects = doc.Projects.Count,
            Tasks = doc.Tasks.Count, InboxItems = doc.InboxItems.Count, Reviews = doc.Reviews.Count,
        });
    }

    private void Stamp(EntityBase e, DateTime createdAt, DateTime updatedAt, DateTime? deletedAt)
    {
        e.CreatedAt = createdAt == default ? clock.UtcNow : createdAt;
        e.UpdatedAt = updatedAt == default ? e.CreatedAt : updatedAt;
        e.DeletedAt = deletedAt;
    }
}
