namespace Cockpit.Shared;

public sealed class TaskRowDto
{
    public TaskDto Task { get; set; } = new();
    public Guid AreaId { get; set; }
    public string AreaName { get; set; } = "";
    public string AreaColor { get; set; } = "";
    public string? ProjectName { get; set; }
    /// <summary>overdue | today | doing | week</summary>
    public string Bucket { get; set; } = "";
}

public sealed class ProjectCardDto
{
    public ProjectDto Project { get; set; } = new();
    public string AreaName { get; set; } = "";
    public string AreaColor { get; set; } = "";
    public int OpenTaskCount { get; set; }
    public int OverdueTaskCount { get; set; }
    public string? NextTaskTitle { get; set; }
    public DateTime LastTouched { get; set; }
}

public sealed class TodayView
{
    public DateOnly Today { get; set; }
    public List<TaskRowDto> Tasks { get; set; } = new();
    public int InboxCount { get; set; }
    public int DecideCount { get; set; }
}

public sealed class WeekView
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<TaskRowDto> Tasks { get; set; } = new();
    public List<ProjectCardDto> ActiveProjects { get; set; } = new();
}

public sealed class BoardView
{
    public int WipLimit { get; set; }
    public int ActiveCount { get; set; }
    public List<ProjectCardDto> Cards { get; set; } = new();
}

public sealed class DecideView
{
    public DateOnly Today { get; set; }
    public List<ProjectCardDto> Projects { get; set; } = new();
}

public sealed class HealthFlagDto
{
    public const string NoNextAction = "no_next_action";
    public const string Overdue = "overdue";
    public const string RevisitDue = "revisit_due";
    public const string DeadlineSoon = "deadline_soon";
    public const string DeadlinePassed = "deadline_passed";

    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public Guid? ProjectId { get; set; }
}

public sealed class AreaCardDto
{
    public AreaDto Area { get; set; } = new();
    public int ActiveProjects { get; set; }
    public int ParkedProjects { get; set; }
    public int OpenTasks { get; set; }
    public int OverdueTasks { get; set; }
    public DateTime? LastTouched { get; set; }
    public DateOnly? NearestDeadline { get; set; }
    public string? NearestDeadlineProject { get; set; }
    public List<HealthFlagDto> Flags { get; set; } = new();
}

public sealed class AreasOverview
{
    public DateOnly Today { get; set; }
    public int WipLimit { get; set; }
    public int ActiveCount { get; set; }
    public int InboxCount { get; set; }
    public int StaleInboxCount { get; set; }
    public List<AreaCardDto> Areas { get; set; } = new();
}

public sealed class NavCounts
{
    public int Inbox { get; set; }
    public int Decide { get; set; }
    public int Today { get; set; }
    public int ActiveProjects { get; set; }
    public int WipLimit { get; set; }
}
