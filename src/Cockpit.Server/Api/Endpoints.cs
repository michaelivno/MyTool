using Cockpit.Server.Domain;
using Cockpit.Server.Services;
using Cockpit.Shared;
using Microsoft.AspNetCore.Mvc;

namespace Cockpit.Server.Api;

public static class Endpoints
{
    public static IEndpointRouteBuilder MapCockpitApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/health", () => Results.Ok(new { status = "ok", app = "cockpit" }));

        // ---- Settings ----
        api.MapGet("/settings", async (SettingsService s, CancellationToken ct) => Results.Ok(await s.GetAsync(ct)));
        api.MapPut("/settings", async (SettingsDto dto, SettingsService s, CancellationToken ct) => Send(await s.UpdateAsync(dto, ct)));

        // ---- Areas ----
        api.MapGet("/areas", async (bool? includeArchived, AreaService s, CancellationToken ct) => Results.Ok(await s.ListAsync(includeArchived ?? false, ct)));
        api.MapGet("/areas/{id:guid}", async (Guid id, AreaService s, CancellationToken ct) => await s.GetAsync(id, ct) is { } a ? Results.Ok(a) : NotFound("Area"));
        api.MapPost("/areas", async (AreaDto dto, AreaService s, CancellationToken ct) => Send(await s.CreateAsync(dto, ct)));
        api.MapPut("/areas/{id:guid}", async (Guid id, AreaDto dto, AreaService s, CancellationToken ct) => Send(await s.UpdateAsync(id, dto, ct)));
        api.MapDelete("/areas/{id:guid}", async (Guid id, AreaService s, CancellationToken ct) => await s.SoftDeleteAsync(id, ct) ? Results.NoContent() : NotFound("Area"));
        api.MapPost("/areas/reorder", async (ReorderRequest r, AreaService s, CancellationToken ct) => { await s.ReorderAsync(r.Ids, ct); return Results.NoContent(); });

        // ---- Field definitions (custom fields per area) ----
        api.MapGet("/fields", async (Guid? areaId, string? appliesTo, bool? includeArchived, FieldDefinitionService s, CancellationToken ct)
            => TryEnum<FieldTarget>(appliesTo, out var target)
                ? Results.Ok(await s.ListAsync(areaId, target, includeArchived ?? false, ct))
                : BadQuery("appliesTo"));
        api.MapPost("/fields", async (FieldDefinitionDto dto, FieldDefinitionService s, CancellationToken ct) => Send(await s.CreateAsync(dto, ct)));
        api.MapPut("/fields/{id:guid}", async (Guid id, FieldDefinitionDto dto, FieldDefinitionService s, CancellationToken ct) => Send(await s.UpdateAsync(id, dto, ct)));
        api.MapDelete("/fields/{id:guid}", async (Guid id, FieldDefinitionService s, CancellationToken ct) => await s.SoftDeleteAsync(id, ct) ? Results.NoContent() : NotFound("Field"));

        // ---- Projects ----
        api.MapGet("/projects", async (Guid? areaId, string? status, ProjectService s, CancellationToken ct)
            => TryEnum<ProjectStatus>(status, out var st) ? Results.Ok(await s.ListAsync(areaId, st, ct)) : BadQuery("status"));
        api.MapGet("/projects/{id:guid}", async (Guid id, ProjectService s, CancellationToken ct) => await s.GetAsync(id, ct) is { } p ? Results.Ok(p) : NotFound("Project"));
        api.MapPost("/projects", async (ProjectDto dto, ProjectService s, CancellationToken ct) => Send(await s.CreateAsync(dto, ct)));
        api.MapPut("/projects/{id:guid}", async (Guid id, ProjectDto dto, ProjectService s, CancellationToken ct) => Send(await s.UpdateAsync(id, dto, ct)));
        api.MapDelete("/projects/{id:guid}", async (Guid id, ProjectService s, CancellationToken ct) => await s.SoftDeleteAsync(id, ct) ? Results.NoContent() : NotFound("Project"));
        api.MapPost("/projects/reorder", async (ReorderRequest r, ProjectService s, CancellationToken ct) => { await s.ReorderAsync(r.Ids, ct); return Results.NoContent(); });

        // ---- Tasks ----
        api.MapGet("/tasks", async (Guid? areaId, Guid? projectId, bool? includeClosed, TaskService s, CancellationToken ct)
            => Results.Ok(await s.ListAsync(areaId, projectId, includeClosed ?? false, ct)));
        api.MapGet("/tasks/{id:guid}", async (Guid id, TaskService s, CancellationToken ct) => await s.GetAsync(id, ct) is { } t ? Results.Ok(t) : NotFound("Task"));
        api.MapPost("/tasks", async (TaskDto dto, TaskService s, CancellationToken ct) => Send(await s.CreateAsync(dto, ct)));
        api.MapPut("/tasks/{id:guid}", async (Guid id, TaskDto dto, TaskService s, CancellationToken ct) => Send(await s.UpdateAsync(id, dto, ct)));
        api.MapDelete("/tasks/{id:guid}", async (Guid id, TaskService s, CancellationToken ct) => await s.SoftDeleteAsync(id, ct) ? Results.NoContent() : NotFound("Task"));
        api.MapPost("/tasks/reorder", async (ReorderRequest r, TaskService s, CancellationToken ct) => { await s.ReorderAsync(r.Ids, ct); return Results.NoContent(); });

        // ---- Inbox ----
        api.MapGet("/inbox", async (bool? includeProcessed, InboxService s, CancellationToken ct) => Results.Ok(await s.ListAsync(includeProcessed ?? false, ct)));
        api.MapPost("/inbox", async (InboxItemDto dto, InboxService s, CancellationToken ct) => Send(await s.AddAsync(dto.Text, ct)));
        api.MapPut("/inbox/{id:guid}", async (Guid id, InboxItemDto dto, InboxService s, CancellationToken ct) => Send(await s.UpdateTextAsync(id, dto.Text, ct)));
        api.MapPost("/inbox/{id:guid}/process", async (Guid id, ProcessInboxRequest req, InboxService s, CancellationToken ct) => Send(await s.ProcessAsync(id, req, ct)));
        api.MapPost("/inbox/{id:guid}/reopen", async (Guid id, InboxService s, CancellationToken ct) => Send(await s.ReopenAsync(id, ct)));
        api.MapDelete("/inbox/{id:guid}", async (Guid id, InboxService s, CancellationToken ct) => await s.SoftDeleteAsync(id, ct) ? Results.NoContent() : NotFound("Inbox item"));

        // ---- Derived views (client passes its local calendar date as ?today=yyyy-MM-dd) ----
        api.MapGet("/views/today", async (DateOnly? today, ViewsService s, IClock clock, CancellationToken ct) => Results.Ok(await s.TodayAsync(today ?? clock.Today, ct)));
        api.MapGet("/views/week", async (DateOnly? today, ViewsService s, IClock clock, CancellationToken ct) => Results.Ok(await s.WeekAsync(today ?? clock.Today, ct)));
        api.MapGet("/views/board", async (DateOnly? today, Guid? areaId, ViewsService s, IClock clock, CancellationToken ct) => Results.Ok(await s.BoardAsync(today ?? clock.Today, areaId, ct)));
        api.MapGet("/views/decide", async (DateOnly? today, ViewsService s, IClock clock, CancellationToken ct) => Results.Ok(await s.DecideAsync(today ?? clock.Today, ct)));
        api.MapGet("/views/areas", async (DateOnly? today, ViewsService s, IClock clock, CancellationToken ct) => Results.Ok(await s.AreasAsync(today ?? clock.Today, ct)));
        api.MapGet("/views/nav", async (DateOnly? today, ViewsService s, IClock clock, CancellationToken ct) => Results.Ok(await s.NavCountsAsync(today ?? clock.Today, ct)));

        // ---- Weekly review ----
        api.MapGet("/reviews", async (int? limit, ReviewService s, CancellationToken ct) => Results.Ok(await s.ListAsync(limit ?? 20, ct)));
        api.MapGet("/reviews/current", async (ReviewService s, CancellationToken ct) => await s.GetCurrentAsync(ct) is { } r ? Results.Ok(r) : Results.NoContent());
        api.MapGet("/reviews/context", async (DateOnly? today, ReviewService s, IClock clock, CancellationToken ct) => Results.Ok(await s.ContextAsync(today ?? clock.Today, ct)));
        api.MapGet("/reviews/{id:guid}", async (Guid id, ReviewService s, CancellationToken ct) => await s.GetAsync(id, ct) is { } r ? Results.Ok(r) : NotFound("Review"));
        api.MapPost("/reviews", async (DateOnly? today, ReviewService s, IClock clock, CancellationToken ct) => Results.Ok(await s.StartOrResumeAsync(today ?? clock.Today, ct)));
        api.MapPost("/reviews/{id:guid}/step", async (Guid id, ReviewStepRequest req, ReviewService s, CancellationToken ct) => Send(await s.MoveToStepAsync(id, req.Step, ct)));
        api.MapPost("/reviews/{id:guid}/decisions", async (Guid id, ReviewDecisionRequest req, ReviewService s, CancellationToken ct) => Send(await s.AddDecisionAsync(id, req, ct)));
        api.MapPost("/reviews/{id:guid}/finish", async (Guid id, ReviewFinishRequest req, DateOnly? today, ReviewService s, IClock clock, CancellationToken ct) => Send(await s.FinishAsync(id, req.Notes, today ?? clock.Today, ct)));
        api.MapPost("/reviews/{id:guid}/abandon", async (Guid id, ReviewService s, CancellationToken ct) => Send(await s.AbandonAsync(id, ct)));

        // ---- Export / import ----
        api.MapGet("/export/json", async (ExportService s, IClock clock, CancellationToken ct) =>
        {
            var doc = await s.ExportAsync(ct);
            var name = $"cockpit-export-{clock.UtcNow:yyyyMMdd-HHmm}.json";
            return Results.File(ExportService.Serialize(doc), "application/json", name);
        });
        api.MapGet("/export/document", async (ExportService s, CancellationToken ct) => Results.Ok(await s.ExportAsync(ct)));
        api.MapGet("/export/markdown", async (MarkdownExportService s, IClock clock, CancellationToken ct) =>
            Results.File(await s.BuildZipAsync(ct), "application/zip", $"cockpit-vault-{clock.UtcNow:yyyyMMdd-HHmm}.zip"));
        api.MapPost("/import/json", async (ExportDocument doc, string? mode, ExportService s, CancellationToken ct)
            => TryEnum<ImportMode>(mode, out var m) ? Send(await s.ImportAsync(doc, m ?? ImportMode.Merge, ct)) : BadQuery("mode"));

        return app;
    }

    private static IResult Send<T>(Result<T> r) => r.Ok
        ? Results.Json(r.Value, statusCode: r.StatusCode)
        : Results.Json(r.Error, statusCode: r.StatusCode);

    /// <summary>Query-string enums: case-insensitive, snake_case tolerated ("in_progress" → InProgress). Empty = null.</summary>
    private static bool TryEnum<T>(string? raw, out T? value) where T : struct, Enum
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!Enum.TryParse<T>(raw.Replace("_", ""), ignoreCase: true, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static IResult BadQuery(string name) =>
        Results.Json(new ApiError { Code = ApiError.Validation, Message = $"Invalid value for query parameter '{name}'." }, statusCode: 400);

    private static IResult NotFound(string what) =>
        Results.Json(new ApiError { Code = ApiError.NotFound, Message = $"{what} not found." }, statusCode: 404);
}
