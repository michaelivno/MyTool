using Cockpit.Shared;

namespace Cockpit.Server.Domain;

/// <summary>
/// The weekly review is a five-step guided flow. Pure rules, no EF:
///   0 inbox → 1 walk active projects → 2 decide → 3 choose next week's active (≤ WIP) → 4 notes & finish.
/// Forward one step at a time, back to any earlier step, finish only from the last step with WIP respected.
/// </summary>
public static class ReviewWizard
{
    public const int FirstStep = 0;
    public const int LastStep = 4;
    public static readonly string[] StepNames = { "inbox", "projects", "decide", "choose_active", "finish" };
    public static readonly HashSet<string> Decisions = new(StringComparer.Ordinal) { "keep_active", "park", "done", "drop", "activate", "skip" };

    public static ApiError? ValidateMove(ReviewStatus status, int from, int to)
    {
        if (status != ReviewStatus.InProgress) return Error("This review is already closed.");
        if (to < FirstStep || to > LastStep) return Error($"Step must be between {FirstStep} and {LastStep}.");
        if (to > from + 1) return Error("Finish the current step before skipping ahead.");
        return null;
    }

    public static ApiError? ValidateFinish(ReviewStatus status, int step, int activeCount, int wipLimit)
    {
        if (status != ReviewStatus.InProgress) return Error("This review is already closed.");
        if (step != LastStep) return Error("Walk through all steps before finishing.");
        if (activeCount > wipLimit)
            return new ApiError { Code = ApiError.WipLimitExceeded, Limit = wipLimit, Message = $"{activeCount} projects are active but the limit is {wipLimit}. Park some in step 4 first." };
        return null;
    }

    public static ApiError? ValidateAbandon(ReviewStatus status)
        => status == ReviewStatus.InProgress ? null : Error("This review is already closed.");

    public static ApiError? ValidateDecision(ReviewStatus status, string decision)
    {
        if (status != ReviewStatus.InProgress) return Error("This review is already closed.");
        if (!Decisions.Contains(decision)) return Error($"Unknown decision '{decision}'.");
        return null;
    }

    private static ApiError Error(string message) => new() { Code = ApiError.Validation, Message = message };
}
