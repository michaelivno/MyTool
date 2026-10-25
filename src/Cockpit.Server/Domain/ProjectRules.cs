using Cockpit.Shared;

namespace Cockpit.Server.Domain;

/// <summary>
/// Pure rules for projects. Kept free of EF so they are trivially unit-testable:
///  - WIP limit: at most N active projects (N from settings, default 3).
///  - Parked means decided: a parked project must carry a revisit date.
/// </summary>
public static class ProjectRules
{
    public const int DefaultWipLimit = 3;

    public static ApiError? ValidateParked(ProjectStatus status, DateOnly? revisitAt)
        => status == ProjectStatus.Parked && revisitAt is null
            ? new ApiError { Code = ApiError.ParkedRequiresRevisit, Message = "A parked project needs a revisit date — parked means decided." }
            : null;

    /// <summary>True when switching <paramref name="from"/> → <paramref name="to"/> adds a new active project.</summary>
    public static bool IsActivation(ProjectStatus from, ProjectStatus to)
        => to == ProjectStatus.Active && from != ProjectStatus.Active;

    public static bool ExceedsWip(int activeExcludingSelf, int limit) => activeExcludingSelf >= Math.Max(0, limit);

    public static ApiError WipError(int limit, IEnumerable<ProjectSummaryDto> active) => new()
    {
        Code = ApiError.WipLimitExceeded,
        Message = $"WIP limit reached: {limit} active project(s) already. Park one first.",
        Limit = limit,
        ActiveProjects = active.ToList(),
    };

    /// <summary>Parked project whose revisit date has passed → shows up in "Decide".</summary>
    public static bool IsDueForDecision(ProjectStatus status, DateOnly? revisitAt, DateOnly today)
        => status == ProjectStatus.Parked && revisitAt is { } d && d <= today;
}
