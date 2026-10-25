using System.Text.Json.Nodes;

namespace Cockpit.Shared;

public sealed class AreaDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public AreaKind Kind { get; set; } = AreaKind.Other;
    public string Color { get; set; } = "#6b7280";
    public ReviewCadence ReviewCadence { get; set; } = ReviewCadence.Weekly;
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    /// <summary>Custom field values keyed by FieldDefinition.Key (area-level custom data).</summary>
    public JsonObject? Properties { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class ProjectDto
{
    public Guid Id { get; set; }
    public Guid AreaId { get; set; }
    public string Name { get; set; } = "";
    public ProjectStatus Status { get; set; } = ProjectStatus.Parked;
    public string? Goal { get; set; }
    public string? NextMilestone { get; set; }
    public DateOnly? Deadline { get; set; }
    /// <summary>Required while Status == Parked ("parked means decided").</summary>
    public DateOnly? RevisitAt { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    /// <summary>Markdown.</summary>
    public string? Notes { get; set; }
    public JsonObject? Properties { get; set; }
    public int SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class TaskDto
{
    public Guid Id { get; set; }
    /// <summary>Always set. Equals the project's area when ProjectId is set.</summary>
    public Guid AreaId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Title { get; set; } = "";
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;
    public DateOnly? Due { get; set; }
    public Effort? Effort { get; set; }
    public List<string> Tags { get; set; } = new();
    public int SortOrder { get; set; }
    public JsonObject? Properties { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class InboxItemDto
{
    public Guid Id { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public InboxConversion? ConvertedTo { get; set; }
    /// <summary>Id of the task/project the item became (null for note/discarded).</summary>
    public Guid? ConvertedId { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class FieldDefinitionDto
{
    public Guid Id { get; set; }
    public Guid AreaId { get; set; }
    public FieldTarget AppliesTo { get; set; } = FieldTarget.Project;
    public string Name { get; set; } = "";
    /// <summary>Stable key used inside Properties JSON. Derived from Name on create; never changes afterwards.</summary>
    public string Key { get; set; } = "";
    public FieldType Type { get; set; } = FieldType.Text;
    public List<string> Options { get; set; } = new();
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class ReviewDecisionDto
{
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    /// <summary>keep_active | park | done | drop | activate | skip</summary>
    public string Decision { get; set; } = "";
    public string? Note { get; set; }
    public DateTime At { get; set; }
}

public sealed class ReviewDto
{
    public Guid Id { get; set; }
    public ReviewStatus Status { get; set; } = ReviewStatus.InProgress;
    /// <summary>0 = inbox, 1 = projects, 2 = decide, 3 = choose active, 4 = notes/finish.</summary>
    public int Step { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Notes { get; set; }
    public JsonObject? Snapshot { get; set; }
    public List<ReviewDecisionDto> Decisions { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class SettingsDto
{
    public int WipLimit { get; set; } = 3;
    public int InboxStaleDays { get; set; } = 7;
    /// <summary>Hour (0-23, server local time) at which the daily SQLite backup runs.</summary>
    public int BackupHour { get; set; } = 3;
    public int BackupRetentionDays { get; set; } = 30;
}

/// <summary>Lossless, re-importable snapshot of the whole database. Includes soft-deleted rows.</summary>
public sealed class ExportDocument
{
    public string App { get; set; } = "cockpit";
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; }
    public Dictionary<string, string> Settings { get; set; } = new();
    public List<AreaDto> Areas { get; set; } = new();
    public List<FieldDefinitionDto> FieldDefinitions { get; set; } = new();
    public List<ProjectDto> Projects { get; set; } = new();
    public List<TaskDto> Tasks { get; set; } = new();
    public List<InboxItemDto> InboxItems { get; set; } = new();
    public List<ReviewDto> Reviews { get; set; } = new();
}

public enum ImportMode { Replace, Merge }

public sealed class ImportResult
{
    public ImportMode Mode { get; set; }
    public int Areas { get; set; }
    public int FieldDefinitions { get; set; }
    public int Projects { get; set; }
    public int Tasks { get; set; }
    public int InboxItems { get; set; }
    public int Reviews { get; set; }
}

// ---- Backups / auth ----

public sealed class BackupFileDto
{
    public string FileName { get; set; } = "";
    public long Bytes { get; set; }
    public DateTime CreatedUtc { get; set; }
}

public sealed class BackupStatusDto
{
    public string Directory { get; set; } = "";
    public DateTime? LastRunUtc { get; set; }
    public List<BackupFileDto> Backups { get; set; } = new();
}

public sealed class AuthStatusDto
{
    public bool Required { get; set; }
    public bool Authenticated { get; set; }
}

public sealed class LoginRequest
{
    public string Password { get; set; } = "";
}
