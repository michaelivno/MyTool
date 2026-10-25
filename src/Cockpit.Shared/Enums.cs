namespace Cockpit.Shared;

// All enums serialize as snake_case strings (see CockpitJson), so the JSON export
// reads naturally ("active", "parked") and adding a value never needs a migration.

public enum AreaKind { Work, Business, Startup, Home, Finance, Health, Other }

public enum ReviewCadence { Weekly, Monthly }

public enum ProjectStatus { Active, Parked, Done, Dropped }

public enum TaskItemStatus { Todo, Doing, Done, Dropped }

public enum Effort { S, M, L }

public enum Priority { Low, Normal, High }

public enum InboxConversion { Task, Project, Note, Discarded }

public enum FieldType { Text, Number, Date, Select, Checkbox }

public enum FieldTarget { Project, Task }

public enum ReviewStatus { InProgress, Finished, Abandoned }

public static class EnumExtensions
{
    public static bool IsOpen(this TaskItemStatus s) => s is TaskItemStatus.Todo or TaskItemStatus.Doing;
    public static bool IsClosed(this ProjectStatus s) => s is ProjectStatus.Done or ProjectStatus.Dropped;
}
