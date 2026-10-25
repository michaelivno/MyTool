using System.IO.Compression;
using System.Text.Json.Nodes;
using Cockpit.Server.Services;
using Cockpit.Shared;
using Cockpit.Tests.Support;

namespace Cockpit.Tests;

public class MarkdownExportTests
{
    private static async Task<(TestHost Host, ExportDocument Doc)> ArrangeAsync()
    {
        var h = await TestHost.CreateAsync();
        var home = (await h.Areas.CreateAsync(new AreaDto { Name = "Home", Kind = AreaKind.Home, Color = "#10b981", Properties = new JsonObject { ["address"] = "Holon" } })).Value!;
        var work = await h.AddAreaAsync("Day job", AreaKind.Work);
        var deck = (await h.Projects.CreateAsync(new ProjectDto
        {
            AreaId = home.Id, Name = "Garden decking", Status = ProjectStatus.Active, Goal = "Deck by spring", NextMilestone = "Order wood",
            Deadline = new DateOnly(2027, 3, 1), Priority = Priority.High, Notes = "- measured 4x3\n- בדוק מחירים", Properties = new JsonObject { ["budget"] = 12000, ["status"] = "custom clash" },
        })).Value!;
        await h.AddProjectAsync(home.Id, "Apartment search", ProjectStatus.Parked, new DateOnly(2026, 12, 1));
        await h.AddProjectAsync(work.Id, "Weird/Name: \"quoted\"? #1", ProjectStatus.Parked, new DateOnly(2027, 1, 1));
        await h.AddProjectAsync(work.Id, "Garden decking", ProjectStatus.Parked, new DateOnly(2027, 1, 1)); // same name, other area
        var deleted = await h.AddProjectAsync(work.Id, "Deleted project", ProjectStatus.Parked, new DateOnly(2027, 1, 1));
        Assert.True(await h.Projects.SoftDeleteAsync(deleted.Id));
        await h.Tasks.CreateAsync(new TaskDto { AreaId = home.Id, ProjectId = deck.Id, Title = "Buy posts", Due = new DateOnly(2026, 10, 30), Effort = Effort.M, Tags = ["shopping", "garden"] });
        var done = await h.AddTaskAsync(home.Id, "Measure", deck.Id);
        done.Status = TaskItemStatus.Done;
        Assert.True((await h.Tasks.UpdateAsync(done.Id, done)).Ok);
        await h.AddTaskAsync(home.Id, "Loose home task");
        await h.Inbox.AddAsync("Call plumber");
        var review = await h.Reviews.StartOrResumeAsync(h.Clock.Today);
        await h.Reviews.AddDecisionAsync(review.Id, new ReviewDecisionRequest { ProjectId = deck.Id, Decision = "keep_active", Note = "going well" });
        for (var s = 1; s <= 4; s++) await h.Reviews.MoveToStepAsync(review.Id, s);
        Assert.True((await h.Reviews.FinishAsync(review.Id, "Solid week", h.Clock.Today)).Ok);
        return (h, await h.Export.ExportAsync());
    }

    [Fact]
    public async Task Vault_has_a_note_per_area_and_project_plus_inbox_reviews_and_index()
    {
        var (h, doc) = await ArrangeAsync();
        await using var _ = h;
        var files = MarkdownExportService.Build(doc, h.Clock.UtcNow);
        var paths = files.Select(f => f.Path).ToList();

        Assert.Contains("README.md", paths);
        Assert.Contains("Areas/Home.md", paths);
        Assert.Contains("Areas/Day job.md", paths);
        Assert.Contains("Projects/Home/Garden decking (Home).md", paths);      // duplicate names are disambiguated by area
        Assert.Contains("Projects/Day job/Garden decking (Day job).md", paths);
        Assert.Contains("Projects/Home/Apartment search.md", paths);
        Assert.Contains("Projects/Day job/Weird-Name- -quoted- -1.md", paths); // Obsidian-illegal characters replaced
        Assert.Contains("Inbox.md", paths);
        Assert.Single(paths, p => p.StartsWith("Reviews/") && p.EndsWith(" review.md"));
        Assert.DoesNotContain(paths, p => p.Contains("Deleted project"));
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    [Fact]
    public async Task Project_note_has_frontmatter_wikilink_checklist_custom_fields_and_notes()
    {
        var (h, doc) = await ArrangeAsync();
        await using var _ = h;
        var note = MarkdownExportService.Build(doc, h.Clock.UtcNow).Single(f => f.Path == "Projects/Home/Garden decking (Home).md").Content;

        Assert.StartsWith("---\n", note.Replace("\r\n", "\n"));
        Assert.Contains("type: \"project\"", note);
        Assert.Contains("area: \"[[Home]]\"", note);
        Assert.Contains("status: \"active\"", note);
        Assert.Contains("priority: \"high\"", note);
        Assert.Contains("deadline: 2027-03-01", note);
        Assert.Contains("budget: 12000", note);
        Assert.Contains("status_custom: \"custom clash\"", note); // custom key never shadows a core key
        Assert.Contains("# Garden decking", note);
        Assert.Contains("- [ ] Buy posts (due 2026-10-30) [M] #shopping #garden", note);
        Assert.Contains("- [x] Measure", note);
        Assert.Contains("## Notes", note);
        Assert.Contains("בדוק מחירים", note);
    }

    [Fact]
    public async Task Area_note_links_projects_by_status_and_lists_loose_tasks()
    {
        var (h, doc) = await ArrangeAsync();
        await using var _ = h;
        var note = MarkdownExportService.Build(doc, h.Clock.UtcNow).Single(f => f.Path == "Areas/Home.md").Content;

        Assert.Contains("kind: \"home\"", note);
        Assert.Contains("address: \"Holon\"", note);
        Assert.Contains("## Active", note);
        Assert.Contains("- [[Garden decking (Home)]] — deadline 2027-03-01", note);
        Assert.Contains("## Parked", note);
        Assert.Contains("- [[Apartment search]] — revisit 2026-12-01", note);
        Assert.Contains("- [ ] Loose home task", note);
    }

    [Fact]
    public async Task Review_note_and_inbox_note_are_written()
    {
        var (h, doc) = await ArrangeAsync();
        await using var _ = h;
        var files = MarkdownExportService.Build(doc, h.Clock.UtcNow);
        var review = files.Single(f => f.Path.StartsWith("Reviews/")).Content;
        var inbox = files.Single(f => f.Path == "Inbox.md").Content;

        Assert.Contains("status: \"finished\"", review);
        Assert.Contains("Solid week", review);
        Assert.Contains("- [[Garden decking (Home)]] → keep active — going well", review);
        Assert.Contains("- **start:** 1 active", review);
        Assert.Contains("- [ ] Call plumber", inbox);
    }

    [Fact]
    public async Task Zip_contains_every_note_as_utf8()
    {
        var (h, doc) = await ArrangeAsync();
        await using var _ = h;
        var expected = MarkdownExportService.Build(doc, h.Clock.UtcNow);
        var bytes = await h.Markdown.BuildZipAsync();

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(expected.Count, zip.Entries.Count);
        using var reader = new StreamReader(zip.GetEntry("Projects/Home/Garden decking (Home).md")!.Open());
        Assert.Contains("בדוק מחירים", await reader.ReadToEndAsync());
    }

    [Theory]
    [InlineData("M&M's Skins", "M&M's Skins")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j#k^l[m]n", "a-b-c-d-e-f-g-h-i-j-k-l-m-n")]
    [InlineData("   ", "untitled")]
    [InlineData("...dots...", "dots")]
    public void Safe_names_follow_obsidian_rules(string input, string expected)
        => Assert.Equal(expected, MarkdownExportService.Safe(input));
}
