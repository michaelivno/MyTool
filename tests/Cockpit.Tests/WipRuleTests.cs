using Cockpit.Server.Domain;
using Cockpit.Shared;
using Cockpit.Tests.Support;

namespace Cockpit.Tests;

public class WipRuleTests
{
    [Theory]
    [InlineData(ProjectStatus.Parked, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Done, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Dropped, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.Active, false)]
    [InlineData(ProjectStatus.Active, ProjectStatus.Parked, false)]
    [InlineData(ProjectStatus.Parked, ProjectStatus.Done, false)]
    public void IsActivation_only_when_entering_active(ProjectStatus from, ProjectStatus to, bool expected)
        => Assert.Equal(expected, ProjectRules.IsActivation(from, to));

    [Theory]
    [InlineData(0, 3, false)]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(5, 3, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    public void ExceedsWip_compares_other_active_projects_to_limit(int activeExcludingSelf, int limit, bool expected)
        => Assert.Equal(expected, ProjectRules.ExceedsWip(activeExcludingSelf, limit));

    [Fact]
    public async Task Fourth_active_project_is_rejected_with_409_listing_the_active_ones()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "A");
        await h.AddProjectAsync(area.Id, "B");
        await h.AddProjectAsync(area.Id, "C");

        var r = await h.Projects.CreateAsync(new ProjectDto { AreaId = area.Id, Name = "D", Status = ProjectStatus.Active });

        Assert.False(r.Ok);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal(ApiError.WipLimitExceeded, r.Error!.Code);
        Assert.Equal(3, r.Error.Limit);
        Assert.Equal(new[] { "A", "B", "C" }, r.Error.ActiveProjects!.Select(p => p.Name).ToArray());
        Assert.All(r.Error.ActiveProjects!, p => Assert.Equal("Area", p.AreaName));
    }

    [Fact]
    public async Task Activating_a_parked_project_at_the_limit_is_rejected()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "A");
        await h.AddProjectAsync(area.Id, "B");
        await h.AddProjectAsync(area.Id, "C");
        var parked = await h.AddProjectAsync(area.Id, "P", ProjectStatus.Parked, new DateOnly(2026, 12, 1));

        parked.Status = ProjectStatus.Active;
        parked.RevisitAt = null;
        var r = await h.Projects.UpdateAsync(parked.Id, parked);

        Assert.False(r.Ok);
        Assert.Equal(409, r.StatusCode);
        Assert.Equal(ApiError.WipLimitExceeded, r.Error!.Code);
        Assert.Equal(ProjectStatus.Parked, (await h.Projects.GetAsync(parked.Id))!.Status); // unchanged
    }

    [Fact]
    public async Task Parking_one_first_makes_room_for_the_new_one()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var a = await h.AddProjectAsync(area.Id, "A");
        await h.AddProjectAsync(area.Id, "B");
        await h.AddProjectAsync(area.Id, "C");
        var parked = await h.AddProjectAsync(area.Id, "P", ProjectStatus.Parked, new DateOnly(2026, 12, 1));

        a.Status = ProjectStatus.Parked;
        a.RevisitAt = new DateOnly(2026, 11, 15);
        Assert.True((await h.Projects.UpdateAsync(a.Id, a)).Ok);

        parked.Status = ProjectStatus.Active;
        parked.RevisitAt = null;
        var r = await h.Projects.UpdateAsync(parked.Id, parked);

        Assert.True(r.Ok, r.Error?.Message);
        Assert.Equal(ProjectStatus.Active, r.Value!.Status);
        Assert.Equal(3, (await h.Projects.ListAsync(null, ProjectStatus.Active)).Count);
    }

    [Fact]
    public async Task Editing_an_already_active_project_does_not_count_itself()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var a = await h.AddProjectAsync(area.Id, "A");
        await h.AddProjectAsync(area.Id, "B");
        await h.AddProjectAsync(area.Id, "C");

        a.Name = "A renamed";
        a.Goal = "ship";
        var r = await h.Projects.UpdateAsync(a.Id, a);

        Assert.True(r.Ok, r.Error?.Message);
        Assert.Equal("A renamed", r.Value!.Name);
    }

    [Fact]
    public async Task Done_and_dropped_projects_do_not_count_toward_the_limit()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var a = await h.AddProjectAsync(area.Id, "A");
        var b = await h.AddProjectAsync(area.Id, "B");
        await h.AddProjectAsync(area.Id, "C");

        a.Status = ProjectStatus.Done;
        Assert.True((await h.Projects.UpdateAsync(a.Id, a)).Ok);
        b.Status = ProjectStatus.Dropped;
        Assert.True((await h.Projects.UpdateAsync(b.Id, b)).Ok);

        Assert.True((await h.Projects.CreateAsync(new ProjectDto { AreaId = area.Id, Name = "D", Status = ProjectStatus.Active })).Ok);
        Assert.True((await h.Projects.CreateAsync(new ProjectDto { AreaId = area.Id, Name = "E", Status = ProjectStatus.Active })).Ok);
        var sixth = await h.Projects.CreateAsync(new ProjectDto { AreaId = area.Id, Name = "F", Status = ProjectStatus.Active });
        Assert.Equal(409, sixth.StatusCode);
    }

    [Fact]
    public async Task Limit_is_configurable_in_settings()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        Assert.True((await h.Settings.UpdateAsync(new SettingsDto { WipLimit = 1 })).Ok);
        await h.AddProjectAsync(area.Id, "A");

        var r = await h.Projects.CreateAsync(new ProjectDto { AreaId = area.Id, Name = "B", Status = ProjectStatus.Active });

        Assert.Equal(409, r.StatusCode);
        Assert.Equal(1, r.Error!.Limit);

        Assert.True((await h.Settings.UpdateAsync(new SettingsDto { WipLimit = 2 })).Ok);
        Assert.True((await h.Projects.CreateAsync(new ProjectDto { AreaId = area.Id, Name = "B", Status = ProjectStatus.Active })).Ok);
    }

    [Fact]
    public async Task Board_view_exposes_limit_and_active_count()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        await h.AddProjectAsync(area.Id, "A");
        await h.AddProjectAsync(area.Id, "B");
        await h.AddProjectAsync(area.Id, "P", ProjectStatus.Parked, new DateOnly(2027, 1, 1));

        var board = await h.Views.BoardAsync(h.Clock.Today, null);

        Assert.Equal(3, board.WipLimit);
        Assert.Equal(2, board.ActiveCount);
        Assert.Equal(3, board.Cards.Count);
    }
}
