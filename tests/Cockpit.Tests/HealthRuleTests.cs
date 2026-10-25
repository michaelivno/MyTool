using Cockpit.Shared;
using Cockpit.Tests.Support;

namespace Cockpit.Tests;

/// <summary>"Every active project needs a next action" and "inbox hygiene".</summary>
public class HealthRuleTests
{
    [Fact]
    public async Task Active_project_with_no_open_task_is_flagged_in_area_health()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync("Home");
        var withTask = await h.AddProjectAsync(area.Id, "Has next action");
        var without = await h.AddProjectAsync(area.Id, "Stuck");
        var parked = await h.AddProjectAsync(area.Id, "Parked", ProjectStatus.Parked, new DateOnly(2027, 1, 1));
        await h.AddTaskAsync(area.Id, "Do it", withTask.Id);
        await h.AddTaskAsync(area.Id, "Done already", without.Id, status: TaskItemStatus.Done);

        var overview = await h.Views.AreasAsync(h.Clock.Today);
        var card = Assert.Single(overview.Areas);

        var flag = Assert.Single(card.Flags, f => f.Code == HealthFlagDto.NoNextAction);
        Assert.Equal(without.Id, flag.ProjectId);
        Assert.Equal(2, card.ActiveProjects);
        Assert.Equal(1, card.ParkedProjects);
        Assert.Equal(1, card.OpenTasks);
        _ = parked;
    }

    [Fact]
    public async Task Today_buckets_overdue_due_today_and_doing()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var p = await h.AddProjectAsync(area.Id, "P");
        await h.AddTaskAsync(area.Id, "late", p.Id, due: new DateOnly(2026, 10, 20));
        await h.AddTaskAsync(area.Id, "today", p.Id, due: new DateOnly(2026, 10, 25));
        await h.AddTaskAsync(area.Id, "in progress", p.Id, status: TaskItemStatus.Doing);
        await h.AddTaskAsync(area.Id, "future", p.Id, due: new DateOnly(2026, 11, 5));
        await h.AddTaskAsync(area.Id, "no date", p.Id);
        await h.AddTaskAsync(area.Id, "done late", p.Id, due: new DateOnly(2026, 10, 1), status: TaskItemStatus.Done);

        var today = await h.Views.TodayAsync(new DateOnly(2026, 10, 25));

        var byTitle = today.Tasks.ToDictionary(t => t.Task.Title, t => t.Bucket);
        Assert.Equal(new Dictionary<string, string> { ["late"] = "overdue", ["today"] = "today", ["in progress"] = "doing" }, byTitle);
        Assert.Equal("P", today.Tasks[0].ProjectName);
    }

    [Fact]
    public async Task Stale_inbox_counts_unprocessed_items_older_than_the_threshold()
    {
        await using var h = await TestHost.CreateAsync();
        var old = (await h.Inbox.AddAsync("old idea")).Value!;
        h.Clock.Advance(TimeSpan.FromDays(8));
        await h.Inbox.AddAsync("fresh idea");
        var processedOld = (await h.Inbox.AddAsync("processed")).Value!;
        await h.Inbox.ProcessAsync(processedOld.Id, new ProcessInboxRequest { ConvertTo = InboxConversion.Discarded });

        Assert.Equal(2, await h.Inbox.CountUnprocessedAsync());
        Assert.Equal(1, await h.Inbox.CountStaleAsync(7));
        Assert.Equal(0, await h.Inbox.CountStaleAsync(30));
        _ = old;
    }

    [Fact]
    public async Task Task_on_a_project_always_carries_the_projects_area()
    {
        await using var h = await TestHost.CreateAsync();
        var a1 = await h.AddAreaAsync("A1");
        var a2 = await h.AddAreaAsync("A2");
        var p = await h.AddProjectAsync(a1.Id, "P");

        var t = await h.AddTaskAsync(a2.Id, "mis-filed", p.Id); // client sent the wrong area
        Assert.Equal(a1.Id, t.AreaId);

        p.AreaId = a2.Id; // move project → tasks follow
        Assert.True((await h.Projects.UpdateAsync(p.Id, p)).Ok);
        Assert.Equal(a2.Id, (await h.Tasks.GetAsync(t.Id))!.AreaId);
    }
}
