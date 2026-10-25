using System.Net;
using System.Net.Http.Json;
using Cockpit.Shared;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cockpit.Tests;

/// <summary>End-to-end over HTTP: JSON shape, status codes and query binding as the Blazor client sees them.</summary>
public sealed class ApiTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cockpit-test-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = default!;
    private HttpClient _client = default!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("COCKPIT_DB", _dbPath));
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public async Task Health_endpoint_answers()
    {
        var res = await _client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Enums_are_snake_case_strings_and_query_filters_are_case_insensitive()
    {
        var area = await PostAsync<AreaDto>("/api/areas", new AreaDto { Name = "A", Kind = AreaKind.Startup });
        var raw = await _client.GetStringAsync($"/api/areas/{area.Id}");
        Assert.Contains("\"kind\":\"startup\"", raw);

        await PostAsync<ProjectDto>("/api/projects", new ProjectDto { AreaId = area.Id, Name = "P1", Status = ProjectStatus.Parked, RevisitAt = new DateOnly(2027, 1, 1) });
        await PostAsync<ProjectDto>("/api/projects", new ProjectDto { AreaId = area.Id, Name = "P2", Status = ProjectStatus.Active });

        Assert.Single((await _client.GetFromJsonAsync<List<ProjectDto>>("/api/projects?status=parked", CockpitJson.Options))!);
        Assert.Single((await _client.GetFromJsonAsync<List<ProjectDto>>("/api/projects?status=ACTIVE", CockpitJson.Options))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/projects?status=bogus")).StatusCode);
    }

    [Fact]
    public async Task Wip_violation_is_a_409_with_machine_readable_body()
    {
        var area = await PostAsync<AreaDto>("/api/areas", new AreaDto { Name = "A" });
        for (var i = 0; i < 3; i++)
            await PostAsync<ProjectDto>("/api/projects", new ProjectDto { AreaId = area.Id, Name = $"P{i}", Status = ProjectStatus.Active });

        var res = await _client.PostAsJsonAsync("/api/projects", new ProjectDto { AreaId = area.Id, Name = "P4", Status = ProjectStatus.Active }, CockpitJson.Options);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        var error = await res.Content.ReadFromJsonAsync<ApiError>(CockpitJson.Options);
        Assert.Equal(ApiError.WipLimitExceeded, error!.Code);
        Assert.Equal(3, error.ActiveProjects!.Count);
    }

    [Fact]
    public async Task Parked_without_revisit_is_a_400()
    {
        var area = await PostAsync<AreaDto>("/api/areas", new AreaDto { Name = "A" });
        var res = await _client.PostAsJsonAsync("/api/projects", new ProjectDto { AreaId = area.Id, Name = "P", Status = ProjectStatus.Parked }, CockpitJson.Options);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(ApiError.ParkedRequiresRevisit, (await res.Content.ReadFromJsonAsync<ApiError>(CockpitJson.Options))!.Code);
    }

    [Fact]
    public async Task Export_is_a_json_download_and_import_round_trips_over_http()
    {
        var area = await PostAsync<AreaDto>("/api/areas", new AreaDto { Name = "Home", Kind = AreaKind.Home });
        var project = await PostAsync<ProjectDto>("/api/projects", new ProjectDto { AreaId = area.Id, Name = "Deck", Status = ProjectStatus.Active });
        await PostAsync<TaskDto>("/api/tasks", new TaskDto { AreaId = area.Id, ProjectId = project.Id, Title = "Buy wood", Tags = ["shop"] });

        var res = await _client.GetAsync("/api/export/json");
        Assert.Equal("application/json", res.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("cockpit-export-", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var doc = await res.Content.ReadFromJsonAsync<ExportDocument>(CockpitJson.Options);
        Assert.Single(doc!.Tasks);

        var import = await _client.PostAsJsonAsync("/api/import/json?mode=replace", doc, CockpitJson.Options);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
        var again = await _client.GetFromJsonAsync<ExportDocument>("/api/export/document", CockpitJson.Options);
        Assert.Equal(doc.Tasks[0].Id, again!.Tasks[0].Id);
        Assert.Equal(doc.Projects[0].CreatedAt, again.Projects[0].CreatedAt);
    }

    [Fact]
    public async Task Markdown_export_is_a_zip_download()
    {
        var area = await PostAsync<AreaDto>("/api/areas", new AreaDto { Name = "Home", Kind = AreaKind.Home });
        await PostAsync<ProjectDto>("/api/projects", new ProjectDto { AreaId = area.Id, Name = "Deck", Status = ProjectStatus.Active });

        var res = await _client.GetAsync("/api/export/markdown");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/zip", res.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".zip", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        using var zip = new System.IO.Compression.ZipArchive(await res.Content.ReadAsStreamAsync());
        Assert.Contains(zip.Entries, e => e.FullName == "Projects/Home/Deck.md");
    }

    [Fact]
    public async Task Spa_fallback_serves_index_html_for_client_routes()
    {
        var res = await _client.GetAsync("/board");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("text/html", res.Content.Headers.ContentType!.ToString());
    }

    private async Task<T> PostAsync<T>(string url, object body)
    {
        var res = await _client.PostAsJsonAsync(url, body, CockpitJson.Options);
        Assert.True(res.IsSuccessStatusCode, $"{url} → {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>(CockpitJson.Options))!;
    }
}
