using Cockpit.Shared;

namespace Cockpit.Server.Data;

public abstract class EntityBase
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Soft delete marker. Nothing is hard-deleted from the UI.</summary>
    public DateTime? DeletedAt { get; set; }
}

public sealed class Area : EntityBase
{
    public string Name { get; set; } = "";
    public AreaKind Kind { get; set; } = AreaKind.Other;
    public string Color { get; set; } = "#6b7280";
    public ReviewCadence ReviewCadence { get; set; } = ReviewCadence.Weekly;
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    /// <summary>JSON object with custom field values (schema-less; never needs a migration).</summary>
    public string PropertiesJson { get; set; } = "{}";

    public ICollection<Project> Projects { get; set; } = new List<Project>();
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
    public ICollection<FieldDefinition> FieldDefinitions { get; set; } = new List<FieldDefinition>();
}

public sealed class Project : EntityBase
{
    public Guid AreaId { get; set; }
    public Area? Area { get; set; }
    public string Name { get; set; } = "";
    public ProjectStatus Status { get; set; } = ProjectStatus.Parked;
    public string? Goal { get; set; }
    public string? NextMilestone { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateOnly? RevisitAt { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;
    public string? Notes { get; set; }
    public string PropertiesJson { get; set; } = "{}";
    public int SortOrder { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}

public sealed class TaskItem : EntityBase
{
    public Guid AreaId { get; set; }
    public Area? Area { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Title { get; set; } = "";
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Todo;
    public DateOnly? Due { get; set; }
    public Effort? Effort { get; set; }
    /// <summary>EF Core maps this primitive collection to a JSON text column.</summary>
    public List<string> Tags { get; set; } = new();
    public int SortOrder { get; set; }
    public string PropertiesJson { get; set; } = "{}";
    public DateTime? CompletedAt { get; set; }
}

public sealed class InboxItem : EntityBase
{
    public string Text { get; set; } = "";
    public DateTime? ProcessedAt { get; set; }
    public InboxConversion? ConvertedTo { get; set; }
    public Guid? ConvertedId { get; set; }
}

public sealed class Review : EntityBase
{
    public ReviewStatus Status { get; set; } = ReviewStatus.InProgress;
    public int Step { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Notes { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public string DecisionsJson { get; set; } = "[]";
}

public sealed class FieldDefinition : EntityBase
{
    public Guid AreaId { get; set; }
    public Area? Area { get; set; }
    public FieldTarget AppliesTo { get; set; } = FieldTarget.Project;
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public FieldType Type { get; set; } = FieldType.Text;
    public List<string> Options { get; set; } = new();
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
}

/// <summary>Key/value app settings (WIP limit etc.). New keys never need a migration.</summary>
public sealed class Setting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
