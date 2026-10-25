using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

public sealed class InboxService(CockpitDbContext db, TaskService tasks, ProjectService projects, IClock clock)
{
    public async Task<List<InboxItemDto>> ListAsync(bool includeProcessed, CancellationToken ct = default)
    {
        var q = db.InboxItems.AsNoTracking();
        if (!includeProcessed) q = q.Where(i => i.ProcessedAt == null);
        return (await q.OrderBy(i => i.CreatedAt).ToListAsync(ct)).Select(i => i.ToDto()).ToList();
    }

    public async Task<Result<InboxItemDto>> AddAsync(string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) return Result<InboxItemDto>.Invalid("Nothing to capture.");
        var i = new InboxItem { Text = text };
        db.InboxItems.Add(i);
        await db.SaveChangesAsync(ct);
        return Result<InboxItemDto>.Success(i.ToDto(), 201);
    }

    public async Task<Result<InboxItemDto>> UpdateTextAsync(Guid id, string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) return Result<InboxItemDto>.Invalid("Text is required.");
        var i = await db.InboxItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Result<InboxItemDto>.NotFound("Inbox item");
        i.Text = text;
        await db.SaveChangesAsync(ct);
        return Result<InboxItemDto>.Success(i.ToDto());
    }

    public async Task<Result<ProcessInboxResult>> ProcessAsync(Guid id, ProcessInboxRequest req, CancellationToken ct = default)
    {
        var i = await db.InboxItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Result<ProcessInboxResult>.NotFound("Inbox item");
        if (i.ProcessedAt is not null) return Result<ProcessInboxResult>.Invalid("Item was already processed.");

        var title = Mapping.NullIfBlank(req.Title) ?? i.Text;
        Guid? createdId = null;

        switch (req.ConvertTo)
        {
            case InboxConversion.Task:
            {
                if (req.ProjectId is null && req.AreaId is null) return Result<ProcessInboxResult>.Invalid("Pick a project or an area for the task.");
                var r = await tasks.CreateAsync(new TaskDto { Title = title, ProjectId = req.ProjectId, AreaId = req.AreaId ?? Guid.Empty, Due = req.Due }, ct);
                if (!r.Ok) return r.Cast<ProcessInboxResult>();
                createdId = r.Value!.Id;
                break;
            }
            case InboxConversion.Project:
            {
                if (req.AreaId is null) return Result<ProcessInboxResult>.Invalid("Pick an area for the project.");
                // New projects start parked with a default revisit date — activating is a deliberate step (WIP rule).
                var r = await projects.CreateAsync(new ProjectDto
                {
                    Name = title, AreaId = req.AreaId.Value, Status = ProjectStatus.Parked,
                    RevisitAt = req.Due ?? clock.Today.AddDays(30),
                }, ct);
                if (!r.Ok) return r.Cast<ProcessInboxResult>();
                createdId = r.Value!.Id;
                break;
            }
            case InboxConversion.Note:
            {
                if (req.ProjectId is null) return Result<ProcessInboxResult>.Invalid("Pick a project for the note.");
                var p = await db.Projects.FirstOrDefaultAsync(x => x.Id == req.ProjectId, ct);
                if (p is null) return Result<ProcessInboxResult>.Invalid("Project does not exist.");
                var stamp = clock.Today.ToString("yyyy-MM-dd");
                p.Notes = string.IsNullOrWhiteSpace(p.Notes) ? $"- {stamp}: {i.Text}" : $"{p.Notes.TrimEnd()}\n- {stamp}: {i.Text}";
                createdId = p.Id;
                break;
            }
            case InboxConversion.Discarded:
                break;
            default:
                return Result<ProcessInboxResult>.Invalid("Unknown conversion.");
        }

        i.ProcessedAt = clock.UtcNow;
        i.ConvertedTo = req.ConvertTo;
        i.ConvertedId = createdId;
        await db.SaveChangesAsync(ct);
        return Result<ProcessInboxResult>.Success(new ProcessInboxResult { Item = i.ToDto(), CreatedId = createdId });
    }

    /// <summary>Puts a processed item back into the inbox (does not undo what it was converted into).</summary>
    public async Task<Result<InboxItemDto>> ReopenAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.InboxItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return Result<InboxItemDto>.NotFound("Inbox item");
        i.ProcessedAt = null;
        i.ConvertedTo = null;
        i.ConvertedId = null;
        await db.SaveChangesAsync(ct);
        return Result<InboxItemDto>.Success(i.ToDto());
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.InboxItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (i is null) return false;
        i.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public Task<int> CountUnprocessedAsync(CancellationToken ct = default)
        => db.InboxItems.CountAsync(i => i.ProcessedAt == null, ct);

    public Task<int> CountStaleAsync(int staleDays, CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow.AddDays(-staleDays);
        return db.InboxItems.CountAsync(i => i.ProcessedAt == null && i.CreatedAt < cutoff, ct);
    }
}
