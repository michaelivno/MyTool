using Cockpit.Server.Domain;
using Cockpit.Shared;
using Cockpit.Tests.Support;

namespace Cockpit.Tests;

public class ReviewWizardTests
{
    // ---- pure state machine ----

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(1, 2, true)]
    [InlineData(3, 4, true)]
    [InlineData(2, 0, true)]   // back to any earlier step
    [InlineData(4, 3, true)]
    [InlineData(0, 2, false)]  // no skipping ahead
    [InlineData(1, 4, false)]
    [InlineData(4, 5, false)]  // out of range
    [InlineData(0, -1, false)]
    public void Moves_go_forward_one_step_or_back_any_number(int from, int to, bool allowed)
        => Assert.Equal(allowed, ReviewWizard.ValidateMove(ReviewStatus.InProgress, from, to) is null);

    [Theory]
    [InlineData(ReviewStatus.Finished)]
    [InlineData(ReviewStatus.Abandoned)]
    public void Closed_reviews_accept_no_moves_decisions_or_finish(ReviewStatus status)
    {
        Assert.NotNull(ReviewWizard.ValidateMove(status, 0, 1));
        Assert.NotNull(ReviewWizard.ValidateDecision(status, "park"));
        Assert.NotNull(ReviewWizard.ValidateFinish(status, ReviewWizard.LastStep, 0, 3));
        Assert.NotNull(ReviewWizard.ValidateAbandon(status));
    }

    [Theory]
    [InlineData(4, 3, 3, true)]
    [InlineData(4, 0, 3, true)]
    [InlineData(4, 4, 3, false)]  // over WIP
    [InlineData(3, 2, 3, false)]  // not on the last step
    [InlineData(0, 0, 3, false)]
    public void Finish_requires_last_step_and_wip_respected(int step, int active, int limit, bool allowed)
        => Assert.Equal(allowed, ReviewWizard.ValidateFinish(ReviewStatus.InProgress, step, active, limit) is null);

    [Fact]
    public void Over_wip_finish_reports_the_wip_error_code()
        => Assert.Equal(ApiError.WipLimitExceeded, ReviewWizard.ValidateFinish(ReviewStatus.InProgress, 4, 5, 3)!.Code);

    [Theory]
    [InlineData("keep_active", true)]
    [InlineData("park", true)]
    [InlineData("done", true)]
    [InlineData("drop", true)]
    [InlineData("activate", true)]
    [InlineData("skip", true)]
    [InlineData("maybe", false)]
    [InlineData("", false)]
    public void Only_known_decisions_are_accepted(string decision, bool allowed)
        => Assert.Equal(allowed, ReviewWizard.ValidateDecision(ReviewStatus.InProgress, decision) is null);

    // ---- service on a real database ----

    [Fact]
    public async Task Starting_a_review_snapshots_the_current_state_and_starting_again_resumes_it()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "Active one");
        await h.AddProjectAsync(area.Id, "Overdue parked", ProjectStatus.Parked, new DateOnly(2026, 9, 1));
        await h.AddTaskAsync(area.Id, "late task", due: new DateOnly(2026, 10, 1));
        await h.Inbox.AddAsync("unprocessed");

        var first = await h.Reviews.StartOrResumeAsync(h.Clock.Today);
        var again = await h.Reviews.StartOrResumeAsync(h.Clock.Today);

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(ReviewStatus.InProgress, first.Status);
        Assert.Equal(0, first.Step);
        var start = first.Snapshot!["start"]!;
        Assert.Single(start["active"]!.AsArray());
        Assert.Single(start["decide"]!.AsArray());
        Assert.Equal(1, (int)start["overdueTasks"]!);
        Assert.Equal(1, (int)start["inboxUnprocessed"]!);
        Assert.Equal(first.Id, (await h.Reviews.GetCurrentAsync())!.Id);
    }

    [Fact]
    public async Task Steps_persist_and_skipping_ahead_is_refused()
    {
        await using var h = await TestHost.CreateAsync();
        var r = await h.Reviews.StartOrResumeAsync(h.Clock.Today);

        Assert.Equal(400, (await h.Reviews.MoveToStepAsync(r.Id, 2)).StatusCode);
        Assert.True((await h.Reviews.MoveToStepAsync(r.Id, 1)).Ok);
        Assert.True((await h.Reviews.MoveToStepAsync(r.Id, 2)).Ok);
        Assert.True((await h.Reviews.MoveToStepAsync(r.Id, 0)).Ok);
        Assert.Equal(0, (await h.Reviews.GetCurrentAsync())!.Step);
    }

    [Fact]
    public async Task Decisions_are_recorded_latest_per_project()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var p = await h.AddProjectAsync(area.Id, "P");
        var r = await h.Reviews.StartOrResumeAsync(h.Clock.Today);

        Assert.True((await h.Reviews.AddDecisionAsync(r.Id, new ReviewDecisionRequest { ProjectId = p.Id, Decision = "park", Note = "no time" })).Ok);
        var updated = (await h.Reviews.AddDecisionAsync(r.Id, new ReviewDecisionRequest { ProjectId = p.Id, Decision = "keep_active" })).Value!;

        var d = Assert.Single(updated.Decisions);
        Assert.Equal("keep_active", d.Decision);
        Assert.Equal("P", d.ProjectName);
        Assert.Equal(400, (await h.Reviews.AddDecisionAsync(r.Id, new ReviewDecisionRequest { ProjectId = p.Id, Decision = "nonsense" })).StatusCode);
        Assert.Equal(400, (await h.Reviews.AddDecisionAsync(r.Id, new ReviewDecisionRequest { ProjectId = Guid.NewGuid(), Decision = "park" })).StatusCode);
    }

    [Fact]
    public async Task Finish_only_from_the_last_step_writes_notes_end_snapshot_and_closes_the_review()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "P");
        var r = await h.Reviews.StartOrResumeAsync(h.Clock.Today);

        Assert.Equal(400, (await h.Reviews.FinishAsync(r.Id, "too early", h.Clock.Today)).StatusCode);
        for (var s = 1; s <= ReviewWizard.LastStep; s++) Assert.True((await h.Reviews.MoveToStepAsync(r.Id, s)).Ok);

        h.Clock.Advance(TimeSpan.FromMinutes(20));
        var finished = (await h.Reviews.FinishAsync(r.Id, "Good week.", h.Clock.Today)).Value!;

        Assert.Equal(ReviewStatus.Finished, finished.Status);
        Assert.Equal("Good week.", finished.Notes);
        Assert.Equal(h.Clock.UtcNow, finished.FinishedAt);
        Assert.NotNull(finished.Snapshot!["start"]);
        Assert.NotNull(finished.Snapshot!["end"]);
        Assert.Null(await h.Reviews.GetCurrentAsync());
        Assert.Single(await h.Reviews.ListAsync(10));
        Assert.Equal(400, (await h.Reviews.MoveToStepAsync(r.Id, 3)).StatusCode); // closed
    }

    [Fact]
    public async Task Finish_is_refused_with_409_while_more_projects_are_active_than_the_limit()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        Assert.True((await h.Settings.UpdateAsync(new SettingsDto { WipLimit = 3 })).Ok);
        for (var i = 0; i < 3; i++) await h.AddProjectAsync(area.Id, $"P{i}");
        Assert.True((await h.Settings.UpdateAsync(new SettingsDto { WipLimit = 2 })).Ok); // limit lowered after the fact

        var r = await h.Reviews.StartOrResumeAsync(h.Clock.Today);
        for (var s = 1; s <= ReviewWizard.LastStep; s++) Assert.True((await h.Reviews.MoveToStepAsync(r.Id, s)).Ok);

        var result = await h.Reviews.FinishAsync(r.Id, null, h.Clock.Today);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(ApiError.WipLimitExceeded, result.Error!.Code);
        Assert.NotNull(await h.Reviews.GetCurrentAsync()); // still open
    }

    [Fact]
    public async Task Abandon_closes_the_review_and_a_new_one_can_start()
    {
        await using var h = await TestHost.CreateAsync();
        var r = await h.Reviews.StartOrResumeAsync(h.Clock.Today);
        Assert.True((await h.Reviews.AbandonAsync(r.Id)).Ok);
        Assert.Equal(400, (await h.Reviews.AbandonAsync(r.Id)).StatusCode);
        var next = await h.Reviews.StartOrResumeAsync(h.Clock.Today);
        Assert.NotEqual(r.Id, next.Id);
    }

    [Fact]
    public async Task Context_exposes_stale_inbox_active_decide_and_candidates()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "Active");
        await h.AddProjectAsync(area.Id, "Due", ProjectStatus.Parked, new DateOnly(2026, 10, 1));
        await h.AddProjectAsync(area.Id, "Later", ProjectStatus.Parked, new DateOnly(2027, 1, 1));
        var done = await h.AddProjectAsync(area.Id, "Done");
        done.Status = ProjectStatus.Done;
        Assert.True((await h.Projects.UpdateAsync(done.Id, done)).Ok);
        await h.Inbox.AddAsync("old");
        h.Clock.Advance(TimeSpan.FromDays(8));

        var ctx = await h.Reviews.ContextAsync(h.Clock.Today);

        Assert.Equal(1, ctx.StaleInboxCount);
        Assert.Equal(7, ctx.StaleDays);
        Assert.Single(ctx.Active);
        Assert.Single(ctx.Decide);
        Assert.Equal(3, ctx.Candidates.Count); // active + 2 parked, not the done one
        Assert.Equal(1, ctx.ActiveCount);
    }
}
