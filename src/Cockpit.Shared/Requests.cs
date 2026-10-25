namespace Cockpit.Shared;

public sealed class ProcessInboxRequest
{
    public InboxConversion ConvertTo { get; set; }
    /// <summary>Target area (task/project) — required unless ProjectId is given.</summary>
    public Guid? AreaId { get; set; }
    /// <summary>Target project (task → project, note → project).</summary>
    public Guid? ProjectId { get; set; }
    /// <summary>Overrides the item text as task title / project name.</summary>
    public string? Title { get; set; }
    public DateOnly? Due { get; set; }
}

public sealed class ProcessInboxResult
{
    public InboxItemDto Item { get; set; } = new();
    public Guid? CreatedId { get; set; }
}

public sealed class ReorderRequest
{
    /// <summary>Ids in their new order; SortOrder becomes the index.</summary>
    public List<Guid> Ids { get; set; } = new();
}

public sealed class ProjectSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public ProjectStatus Status { get; set; }
    public Guid AreaId { get; set; }
    public string AreaName { get; set; } = "";
    public string AreaColor { get; set; } = "";
}

/// <summary>Standard error body. Code is machine-readable; the UI switches on it.</summary>
public sealed class ApiError
{
    public const string WipLimitExceeded = "wip_limit_exceeded";
    public const string ParkedRequiresRevisit = "parked_requires_revisit";
    public const string Validation = "validation";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";

    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public int? Limit { get; set; }
    public List<ProjectSummaryDto>? ActiveProjects { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}
