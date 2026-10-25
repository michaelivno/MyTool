using Cockpit.Server.Domain;
using Cockpit.Shared;
using Cockpit.Tests.Support;

namespace Cockpit.Tests;

public class ParkedRevisitTests
{
    [Fact]
    public void ValidateParked_requires_a_date_only_for_parked()
    {
        Assert.NotNull(ProjectRules.ValidateParked(ProjectStatus.Parked, null));
        Assert.Equal(ApiError.ParkedRequiresRevisit, ProjectRules.ValidateParked(ProjectStatus.Parked, null)!.Code);
        Assert.Null(ProjectRules.ValidateParked(ProjectStatus.Parked, new DateOnly(2026, 12, 1)));
        Assert.Null(ProjectRules.ValidateParked(ProjectStatus.Active, null));
        Assert.Null(ProjectRules.ValidateParked(ProjectStatus.Done, null));
    }

    [Theory]
    [InlineData("2026-10-24", true)]   // yesterday → due
    [InlineData("2026-10-25", true)]   // today → due
    [InlineData("2026-10-26", false)]  // tomorrow → not yet
    public void IsDueForDecision_when_revisit_date_reached(string revisit, bool expected)
    {
        var today = new DateOnly(2026, 10, 25);
        Assert.Equal(expected, ProjectRules.IsDueForDecision(ProjectStatus.Parked, DateOnly.Parse(revisit), today));
        Assert.False(ProjectRules.IsDueForDecision(ProjectStatus.Active, DateOnly.Parse(revisit), today));
        Assert.False(ProjectRules.IsDueForDecision(ProjectStatus.Parked, null, today));
    }

    [Fact]
    public async Task Creating_a_parked_project_without_revisit_is_rejected_with_400()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();

        var r = await h.Projects.CreateAsync(new ProjectDto { AreaId = area.Id, Name = "P", Status = ProjectStatus.Parked });

        Assert.False(r.Ok);
        Assert.Equal(400, r.StatusCode);
        Assert.Equal(ApiError.ParkedRequiresRevisit, r.Error!.Code);
        Assert.Empty(await h.Projects.ListAsync(null, null));
    }

    [Fact]
    public async Task Parking_an_active_project_without_revisit_is_rejected_and_it_stays_active()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var p = await h.AddProjectAsync(area.Id, "P");

        p.Status = ProjectStatus.Parked;
        p.RevisitAt = null;
        var r = await h.Projects.UpdateAsync(p.Id, p);

        Assert.Equal(400, r.StatusCode);
        Assert.Equal(ProjectStatus.Active, (await h.Projects.GetAsync(p.Id))!.Status);
    }

    [Fact]
    public async Task Decide_lists_parked_projects_whose_revisit_passed_oldest_first()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "October", ProjectStatus.Parked, new DateOnly(2026, 10, 1));
        await h.AddProjectAsync(area.Id, "September", ProjectStatus.Parked, new DateOnly(2026, 9, 1));
        await h.AddProjectAsync(area.Id, "Today", ProjectStatus.Parked, new DateOnly(2026, 10, 25));
        await h.AddProjectAsync(area.Id, "December", ProjectStatus.Parked, new DateOnly(2026, 12, 1));
        await h.AddProjectAsync(area.Id, "Active one");

        var decide = await h.Views.DecideAsync(new DateOnly(2026, 10, 25));

        Assert.Equal(new[] { "September", "October", "Today" }, decide.Projects.Select(c => c.Project.Name).ToArray());
    }

    [Fact]
    public async Task Decide_is_empty_when_all_revisit_dates_are_in_the_future()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "Later", ProjectStatus.Parked, new DateOnly(2026, 12, 1));

        Assert.Empty((await h.Views.DecideAsync(new DateOnly(2026, 10, 25))).Projects);
        Assert.Single((await h.Views.DecideAsync(new DateOnly(2026, 12, 1))).Projects);
    }

    [Fact]
    public async Task Inbox_item_converted_to_project_starts_parked_with_a_revisit_date()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var item = (await h.Inbox.AddAsync("Build a shed")).Value!;

        var r = await h.Inbox.ProcessAsync(item.Id, new ProcessInboxRequest { ConvertTo = InboxConversion.Project, AreaId = area.Id });

        Assert.True(r.Ok, r.Error?.Message);
        var project = await h.Projects.GetAsync(r.Value!.CreatedId!.Value);
        Assert.Equal(ProjectStatus.Parked, project!.Status);
        Assert.Equal(h.Clock.Today.AddDays(30), project.RevisitAt);
        Assert.Equal(InboxConversion.Project, r.Value.Item.ConvertedTo);
    }

    [Fact]
    public async Task Closing_a_project_stamps_completed_at_and_reopening_clears_it()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var p = await h.AddProjectAsync(area.Id, "P");

        p.Status = ProjectStatus.Done;
        var done = (await h.Projects.UpdateAsync(p.Id, p)).Value!;
        Assert.Equal(h.Clock.UtcNow, done.CompletedAt);

        done.Status = ProjectStatus.Parked;
        done.RevisitAt = new DateOnly(2026, 11, 1);
        var reopened = (await h.Projects.UpdateAsync(p.Id, done)).Value!;
        Assert.Null(reopened.CompletedAt);
    }
}
