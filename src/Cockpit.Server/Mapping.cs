using System.Text.Json;
using System.Text.Json.Nodes;
using Cockpit.Server.Data;
using Cockpit.Shared;

namespace Cockpit.Server;

public static class Mapping
{
    public static JsonObject? ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try { return JsonNode.Parse(json) as JsonObject; } catch (JsonException) { return null; }
    }

    public static string ToJson(JsonObject? o) => o is null || o.Count == 0 ? "{}" : o.ToJsonString();

    public static List<ReviewDecisionDto> ParseDecisions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<ReviewDecisionDto>>(json, CockpitJson.Options) ?? new(); }
        catch (JsonException) { return new(); }
    }

    // ---- Area ----
    public static AreaDto ToDto(this Area a) => new()
    {
        Id = a.Id, Name = a.Name, Kind = a.Kind, Color = a.Color, ReviewCadence = a.ReviewCadence,
        SortOrder = a.SortOrder, Archived = a.Archived, Properties = ParseObject(a.PropertiesJson),
        CreatedAt = a.CreatedAt, UpdatedAt = a.UpdatedAt, DeletedAt = a.DeletedAt,
    };

    public static void Apply(this Area a, AreaDto d)
    {
        a.Name = d.Name.Trim();
        a.Kind = d.Kind;
        a.Color = string.IsNullOrWhiteSpace(d.Color) ? "#6b7280" : d.Color.Trim();
        a.ReviewCadence = d.ReviewCadence;
        a.SortOrder = d.SortOrder;
        a.Archived = d.Archived;
        a.PropertiesJson = ToJson(d.Properties);
    }

    // ---- Project ----
    public static ProjectDto ToDto(this Project p) => new()
    {
        Id = p.Id, AreaId = p.AreaId, Name = p.Name, Status = p.Status, Goal = p.Goal, NextMilestone = p.NextMilestone,
        Deadline = p.Deadline, RevisitAt = p.RevisitAt, Priority = p.Priority, Notes = p.Notes,
        Properties = ParseObject(p.PropertiesJson), SortOrder = p.SortOrder, CompletedAt = p.CompletedAt,
        CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt, DeletedAt = p.DeletedAt,
    };

    /// <summary>Copies editable fields. Status/RevisitAt/CompletedAt are handled by ProjectService (rules).</summary>
    public static void ApplyFields(this Project p, ProjectDto d)
    {
        p.AreaId = d.AreaId;
        p.Name = d.Name.Trim();
        p.Goal = NullIfBlank(d.Goal);
        p.NextMilestone = NullIfBlank(d.NextMilestone);
        p.Deadline = d.Deadline;
        p.Priority = d.Priority;
        p.Notes = NullIfBlank(d.Notes);
        p.PropertiesJson = ToJson(d.Properties);
        p.SortOrder = d.SortOrder;
    }

    public static ProjectSummaryDto ToSummary(this Project p) => new()
    {
        Id = p.Id, Name = p.Name, Status = p.Status, AreaId = p.AreaId,
        AreaName = p.Area?.Name ?? "", AreaColor = p.Area?.Color ?? "",
    };

    // ---- Task ----
    public static TaskDto ToDto(this TaskItem t) => new()
    {
        Id = t.Id, AreaId = t.AreaId, ProjectId = t.ProjectId, Title = t.Title, Status = t.Status, Due = t.Due,
        Effort = t.Effort, Tags = t.Tags.ToList(), SortOrder = t.SortOrder, Properties = ParseObject(t.PropertiesJson),
        CompletedAt = t.CompletedAt, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt, DeletedAt = t.DeletedAt,
    };

    public static void ApplyFields(this TaskItem t, TaskDto d)
    {
        t.Title = d.Title.Trim();
        t.Due = d.Due;
        t.Effort = d.Effort;
        t.Tags = d.Tags.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        t.SortOrder = d.SortOrder;
        t.PropertiesJson = ToJson(d.Properties);
    }

    // ---- Inbox ----
    public static InboxItemDto ToDto(this InboxItem i) => new()
    {
        Id = i.Id, Text = i.Text, CreatedAt = i.CreatedAt, ProcessedAt = i.ProcessedAt,
        ConvertedTo = i.ConvertedTo, ConvertedId = i.ConvertedId, DeletedAt = i.DeletedAt,
    };

    // ---- FieldDefinition ----
    public static FieldDefinitionDto ToDto(this FieldDefinition f) => new()
    {
        Id = f.Id, AreaId = f.AreaId, AppliesTo = f.AppliesTo, Name = f.Name, Key = f.Key, Type = f.Type,
        Options = f.Options.ToList(), SortOrder = f.SortOrder, Archived = f.Archived,
        CreatedAt = f.CreatedAt, UpdatedAt = f.UpdatedAt, DeletedAt = f.DeletedAt,
    };

    public static void ApplyFields(this FieldDefinition f, FieldDefinitionDto d)
    {
        f.Name = d.Name.Trim();
        f.Type = d.Type;
        f.Options = d.Options.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();
        f.SortOrder = d.SortOrder;
        f.Archived = d.Archived;
    }

    // ---- Review ----
    public static ReviewDto ToDto(this Review r) => new()
    {
        Id = r.Id, Status = r.Status, Step = r.Step, StartedAt = r.StartedAt, FinishedAt = r.FinishedAt, Notes = r.Notes,
        Snapshot = ParseObject(r.SnapshotJson), Decisions = ParseDecisions(r.DecisionsJson),
        UpdatedAt = r.UpdatedAt, DeletedAt = r.DeletedAt,
    };

    public static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>"Next milestone" → "next_milestone". Used for custom-field keys and markdown file names.</summary>
    public static string Slug(string s, char sep = '_')
    {
        var chars = s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : sep).ToArray();
        var slug = new string(chars);
        while (slug.Contains($"{sep}{sep}")) slug = slug.Replace($"{sep}{sep}", $"{sep}");
        slug = slug.Trim(sep);
        return slug.Length == 0 ? "item" : slug;
    }
}
