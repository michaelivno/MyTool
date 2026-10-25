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

// ---- Weekly review ----

public sealed class ReviewStepRequest
{
    public int Step { get; set; }
}

public sealed class ReviewFinishRequest
{
    public string? Notes { get; set; }
}

public sealed class ReviewDecisionRequest
{
    public Guid ProjectId { get; set; }
    /// <summary>keep_active | park | done | drop | activate | skip</summary>
    public string Decision { get; set; } = "";
    public string? Note { get; set; }
}

/// <summary>Everything the wizard screens need, derived live from the data.</summary>
public sealed class ReviewContext
{
    public DateOnly Today { get; set; }
    public int WipLimit { get; set; }
    public int ActiveCount { get; set; }
    public int InboxCount { get; set; }
    public int StaleInboxCount { get; set; }
    public int StaleDays { get; set; }
    public List<ProjectCardDto> Active { get; set; } = new();
    public List<ProjectCardDto> Decide { get; set; } = new();
    /// <summary>All open (active + parked) projects: the pool for "next week's active".</summary>
    public List<ProjectCardDto> Candidates { get; set; } = new();
}
