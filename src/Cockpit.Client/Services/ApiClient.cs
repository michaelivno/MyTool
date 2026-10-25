using System.Net.Http.Json;
using System.Text.Json;
using Cockpit.Shared;

namespace Cockpit.Client.Services;

/// <summary>Typed wrapper over the Cockpit API. Throws ApiException with the server's ApiError on failure.</summary>
public sealed class ApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = CockpitJson.Options;

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    private static string T => $"today={Today:yyyy-MM-dd}";

    // ---- settings ----
    public Task<SettingsDto> GetSettings() => Get<SettingsDto>("api/settings");
    public Task<SettingsDto> SaveSettings(SettingsDto s) => Put<SettingsDto>("api/settings", s);

    // ---- areas ----
    public Task<List<AreaDto>> GetAreas(bool includeArchived = false) => Get<List<AreaDto>>($"api/areas?includeArchived={includeArchived}");
    public Task<AreaDto> GetArea(Guid id) => Get<AreaDto>($"api/areas/{id}");
    public Task<AreaDto> CreateArea(AreaDto a) => Post<AreaDto>("api/areas", a);
    public Task<AreaDto> SaveArea(AreaDto a) => Put<AreaDto>($"api/areas/{a.Id}", a);
    public Task DeleteArea(Guid id) => Delete($"api/areas/{id}");
    public Task ReorderAreas(IEnumerable<Guid> ids) => PostNoContent("api/areas/reorder", new ReorderRequest { Ids = ids.ToList() });

    // ---- fields ----
    public Task<List<FieldDefinitionDto>> GetFields(Guid? areaId = null, FieldTarget? appliesTo = null, bool includeArchived = false)
        => Get<List<FieldDefinitionDto>>($"api/fields?includeArchived={includeArchived}{(areaId is { } a ? $"&areaId={a}" : "")}{(appliesTo is { } t ? $"&appliesTo={t}" : "")}");
    public Task<FieldDefinitionDto> CreateField(FieldDefinitionDto f) => Post<FieldDefinitionDto>("api/fields", f);
    public Task<FieldDefinitionDto> SaveField(FieldDefinitionDto f) => Put<FieldDefinitionDto>($"api/fields/{f.Id}", f);
    public Task DeleteField(Guid id) => Delete($"api/fields/{id}");

    // ---- projects ----
    public Task<List<ProjectDto>> GetProjects(Guid? areaId = null, ProjectStatus? status = null)
        => Get<List<ProjectDto>>($"api/projects?{(areaId is { } a ? $"areaId={a}&" : "")}{(status is { } s ? $"status={s}" : "")}");
    public Task<ProjectDto> GetProject(Guid id) => Get<ProjectDto>($"api/projects/{id}");
    public Task<ProjectDto> CreateProject(ProjectDto p) => Post<ProjectDto>("api/projects", p);
    public Task<ProjectDto> SaveProject(ProjectDto p) => Put<ProjectDto>($"api/projects/{p.Id}", p);
    public Task DeleteProject(Guid id) => Delete($"api/projects/{id}");
    public Task ReorderProjects(IEnumerable<Guid> ids) => PostNoContent("api/projects/reorder", new ReorderRequest { Ids = ids.ToList() });

    // ---- tasks ----
    public Task<List<TaskDto>> GetTasks(Guid? areaId = null, Guid? projectId = null, bool includeClosed = false)
        => Get<List<TaskDto>>($"api/tasks?includeClosed={includeClosed}{(areaId is { } a ? $"&areaId={a}" : "")}{(projectId is { } p ? $"&projectId={p}" : "")}");
    public Task<TaskDto> CreateTask(TaskDto t) => Post<TaskDto>("api/tasks", t);
    public Task<TaskDto> SaveTask(TaskDto t) => Put<TaskDto>($"api/tasks/{t.Id}", t);
    public Task DeleteTask(Guid id) => Delete($"api/tasks/{id}");
    public Task ReorderTasks(IEnumerable<Guid> ids) => PostNoContent("api/tasks/reorder", new ReorderRequest { Ids = ids.ToList() });

    // ---- inbox ----
    public Task<List<InboxItemDto>> GetInbox(bool includeProcessed = false) => Get<List<InboxItemDto>>($"api/inbox?includeProcessed={includeProcessed}");
    public Task<InboxItemDto> Capture(string text) => Post<InboxItemDto>("api/inbox", new InboxItemDto { Text = text });
    public Task<InboxItemDto> SaveInboxText(InboxItemDto i) => Put<InboxItemDto>($"api/inbox/{i.Id}", i);
    public Task<ProcessInboxResult> ProcessInbox(Guid id, ProcessInboxRequest r) => Post<ProcessInboxResult>($"api/inbox/{id}/process", r);
    public Task<InboxItemDto> ReopenInbox(Guid id) => Post<InboxItemDto>($"api/inbox/{id}/reopen", new { });
    public Task DeleteInbox(Guid id) => Delete($"api/inbox/{id}");

    // ---- views ----
    public Task<TodayView> GetToday() => Get<TodayView>($"api/views/today?{T}");
    public Task<WeekView> GetWeek() => Get<WeekView>($"api/views/week?{T}");
    public Task<BoardView> GetBoard(Guid? areaId = null) => Get<BoardView>($"api/views/board?{T}{(areaId is { } a ? $"&areaId={a}" : "")}");
    public Task<DecideView> GetDecide() => Get<DecideView>($"api/views/decide?{T}");
    public Task<AreasOverview> GetAreasOverview() => Get<AreasOverview>($"api/views/areas?{T}");
    public Task<NavCounts> GetNavCounts() => Get<NavCounts>($"api/views/nav?{T}");

    // ---- weekly review ----
    public async Task<ReviewDto?> GetCurrentReview()
    {
        using var res = await http.GetAsync("api/reviews/current");
        if (res.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await Read<ReviewDto>(res);
    }
    public Task<List<ReviewDto>> GetReviews(int limit = 10) => Get<List<ReviewDto>>($"api/reviews?limit={limit}");
    public Task<ReviewContext> GetReviewContext() => Get<ReviewContext>($"api/reviews/context?{T}");
    public Task<ReviewDto> StartReview() => Post<ReviewDto>($"api/reviews?{T}", new { });
    public Task<ReviewDto> ReviewStep(Guid id, int step) => Post<ReviewDto>($"api/reviews/{id}/step", new ReviewStepRequest { Step = step });
    public Task<ReviewDto> ReviewDecision(Guid id, ReviewDecisionRequest r) => Post<ReviewDto>($"api/reviews/{id}/decisions", r);
    public Task<ReviewDto> FinishReview(Guid id, string? notes) => Post<ReviewDto>($"api/reviews/{id}/finish?{T}", new ReviewFinishRequest { Notes = notes });
    public Task<ReviewDto> AbandonReview(Guid id) => Post<ReviewDto>($"api/reviews/{id}/abandon", new { });

    // ---- export / import ----
    public Task<ImportResult> Import(ExportDocument doc, ImportMode mode) => Post<ImportResult>($"api/import/json?mode={mode}", doc);

    // ---- plumbing ----
    private async Task<T> Get<T>(string url)
    {
        using var res = await http.GetAsync(url);
        return await Read<T>(res);
    }

    private async Task<T> Post<T>(string url, object body)
    {
        using var res = await http.PostAsJsonAsync(url, body, Json);
        return await Read<T>(res);
    }

    private async Task PostNoContent(string url, object body)
    {
        using var res = await http.PostAsJsonAsync(url, body, Json);
        await EnsureOk(res);
    }

    private async Task<T> Put<T>(string url, object body)
    {
        using var res = await http.PutAsJsonAsync(url, body, Json);
        return await Read<T>(res);
    }

    private async Task Delete(string url)
    {
        using var res = await http.DeleteAsync(url);
        await EnsureOk(res);
    }

    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        await EnsureOk(res);
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task EnsureOk(HttpResponseMessage res)
    {
        if (res.IsSuccessStatusCode) return;
        ApiError? error = null;
        try { error = await res.Content.ReadFromJsonAsync<ApiError>(Json); } catch { /* not our JSON */ }
        error ??= new ApiError { Code = ((int)res.StatusCode).ToString(), Message = $"Request failed ({(int)res.StatusCode} {res.ReasonPhrase})." };
        throw new ApiException(res.StatusCode, error);
    }
}
