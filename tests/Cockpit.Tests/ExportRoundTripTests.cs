using System.Text.Json;
using System.Text.Json.Nodes;
using Cockpit.Shared;
using Cockpit.Tests.Support;

namespace Cockpit.Tests;

public class ExportRoundTripTests
{
    private static async Task PopulateAsync(TestHost h)
    {
        Assert.True((await h.Settings.UpdateAsync(new SettingsDto { WipLimit = 4, InboxStaleDays = 10 })).Ok);

        var home = (await h.Areas.CreateAsync(new AreaDto
        {
            Name = "Home", Kind = AreaKind.Home, Color = "#10b981",
            Properties = new JsonObject { ["address"] = "Holon", ["floor"] = 3 },
        })).Value!;
        var work = await h.AddAreaAsync("Day job", AreaKind.Work);

        var field = (await h.Fields.CreateAsync(new FieldDefinitionDto
        {
            AreaId = home.Id, AppliesTo = FieldTarget.Project, Name = "Budget (₪)", Type = FieldType.Number,
        })).Value!;
        Assert.Equal("budget", field.Key);
        await h.Fields.CreateAsync(new FieldDefinitionDto { AreaId = home.Id, AppliesTo = FieldTarget.Task, Name = "Where", Type = FieldType.Select, Options = ["garden", "kitchen"] });

        var decking = (await h.Projects.CreateAsync(new ProjectDto
        {
            AreaId = home.Id, Name = "Garden decking", Status = ProjectStatus.Active, Goal = "Deck by spring",
            NextMilestone = "Order wood", Deadline = new DateOnly(2027, 3, 1), Priority = Priority.High,
            Notes = "- 2026-10-01: measured\n- בדוק מחירים", Properties = new JsonObject { ["budget"] = 12000 },
        })).Value!;
        var parked = await h.AddProjectAsync(home.Id, "Apartment search", ProjectStatus.Parked, new DateOnly(2026, 12, 1));
        var done = await h.AddProjectAsync(work.Id, "Old thing");
        done.Status = ProjectStatus.Done;
        Assert.True((await h.Projects.UpdateAsync(done.Id, done)).Ok);
        var deleted = await h.AddProjectAsync(work.Id, "Deleted one", ProjectStatus.Parked, new DateOnly(2027, 1, 1));
        Assert.True(await h.Projects.SoftDeleteAsync(deleted.Id));

        await h.Tasks.CreateAsync(new TaskDto
        {
            AreaId = home.Id, ProjectId = decking.Id, Title = "Buy posts", Due = new DateOnly(2026, 10, 30), Effort = Effort.M,
            Tags = ["shopping", "garden"], Properties = new JsonObject { ["where"] = "garden" },
        });
        var doneTask = await h.AddTaskAsync(home.Id, "Measure", decking.Id);
        doneTask.Status = TaskItemStatus.Done;
        Assert.True((await h.Tasks.UpdateAsync(doneTask.Id, doneTask)).Ok);
        await h.AddTaskAsync(work.Id, "Loose task on area");

        var inbox1 = (await h.Inbox.AddAsync("Call plumber")).Value!;
        await h.Inbox.AddAsync("Random idea");
        await h.Inbox.ProcessAsync(inbox1.Id, new ProcessInboxRequest { ConvertTo = InboxConversion.Task, ProjectId = decking.Id });
        _ = parked;
    }

    private static string Canonical(ExportDocument doc)
    {
        doc.ExportedAt = default;
        return JsonSerializer.Serialize(doc, CockpitJson.Indented());
    }

    [Fact]
    public async Task Export_then_import_replace_into_an_empty_database_reproduces_the_same_export()
    {
        await using var source = await TestHost.CreateAsync();
        await PopulateAsync(source);
        var exported = await source.Export.ExportAsync();
        var bytes = ExportService_Serialize(exported);

        // Simulate the file round trip: bytes on disk → parsed back.
        var parsed = JsonSerializer.Deserialize<ExportDocument>(bytes, CockpitJson.Options)!;

        await using var target = await TestHost.CreateAsync();
        var result = await target.Import.ImportAsync(parsed, ImportMode.Replace);
        Assert.True(result.Ok, result.Error?.Message);

        var reExported = await target.Export.ExportAsync();
        Assert.Equal(Canonical(exported), Canonical(reExported));

        // Soft-deleted rows travelled too, and are still hidden from normal queries.
        Assert.Contains(reExported.Projects, p => p.DeletedAt != null);
        Assert.DoesNotContain(await target.Projects.ListAsync(null, null), p => p.Name == "Deleted one");
        Assert.Equal(4, (await target.Settings.GetAsync()).WipLimit);
    }

    [Fact]
    public async Task Import_replace_wipes_existing_data_first()
    {
        await using var source = await TestHost.CreateAsync();
        await PopulateAsync(source);
        var doc = await source.Export.ExportAsync();

        await using var target = await TestHost.CreateAsync();
        var junkArea = await target.AddAreaAsync("Junk");
        await target.AddProjectAsync(junkArea.Id, "Junk project");

        Assert.True((await target.Import.ImportAsync(doc, ImportMode.Replace)).Ok);

        Assert.DoesNotContain(await target.Areas.ListAsync(true), a => a.Name == "Junk");
        Assert.Equal(doc.Areas.Count, (await target.Areas.ListAsync(true)).Count);
    }

    [Fact]
    public async Task Import_merge_upserts_by_id_and_keeps_unrelated_rows()
    {
        await using var h = await TestHost.CreateAsync();
        await PopulateAsync(h);
        var doc = await h.Export.ExportAsync();
        var extraArea = await h.AddAreaAsync("Kept");

        var project = doc.Projects.First(p => p.Name == "Garden decking");
        project.Name = "Garden decking v2";
        project.Properties!["budget"] = 15000;
        doc.Tasks.Add(new TaskDto { Id = Guid.NewGuid(), AreaId = project.AreaId, ProjectId = project.Id, Title = "New from merge" });

        var r = await h.Import.ImportAsync(doc, ImportMode.Merge);
        Assert.True(r.Ok, r.Error?.Message);

        var after = await h.Projects.GetAsync(project.Id);
        Assert.Equal("Garden decking v2", after!.Name);
        Assert.Equal(15000, after.Properties!["budget"]!.GetValue<int>());
        Assert.Contains(await h.Tasks.ListAsync(null, project.Id, true), t => t.Title == "New from merge");
        Assert.NotNull(await h.Areas.GetAsync(extraArea.Id));
    }

    [Fact]
    public async Task Import_rejects_files_that_are_not_cockpit_exports_or_are_inconsistent()
    {
        await using var h = await TestHost.CreateAsync();

        var foreign = await h.Import.ImportAsync(new ExportDocument { App = "other-app" }, ImportMode.Replace);
        Assert.Equal(400, foreign.StatusCode);

        var newer = await h.Import.ImportAsync(new ExportDocument { Version = 99 }, ImportMode.Replace);
        Assert.Equal(400, newer.StatusCode);

        var orphanTask = new ExportDocument();
        orphanTask.Areas.Add(new AreaDto { Id = Guid.NewGuid(), Name = "A" });
        orphanTask.Tasks.Add(new TaskDto { Id = Guid.NewGuid(), AreaId = orphanTask.Areas[0].Id, ProjectId = Guid.NewGuid(), Title = "orphan" });
        var orphan = await h.Import.ImportAsync(orphanTask, ImportMode.Replace);
        Assert.Equal(400, orphan.StatusCode);
        Assert.Contains("unknown project", orphan.Error!.Message);
        Assert.Empty(await h.Areas.ListAsync(true)); // nothing partially applied
    }

    [Fact]
    public async Task Timestamps_round_trip_as_utc()
    {
        await using var h = await TestHost.CreateAsync();
        var area = await h.AddAreaAsync();
        var fromDb = await h.Areas.GetAsync(area.Id);
        Assert.Equal(DateTimeKind.Utc, fromDb!.CreatedAt.Kind);
        Assert.Equal(h.Clock.UtcNow, fromDb.CreatedAt);
    }

    private static byte[] ExportService_Serialize(ExportDocument doc) => Cockpit.Server.Services.ExportService.Serialize(doc);
}
