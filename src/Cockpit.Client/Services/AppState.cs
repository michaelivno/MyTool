using Cockpit.Shared;

namespace Cockpit.Client.Services;

/// <summary>Small client cache: areas, settings and nav badge counts. Pages call Refresh* after mutations.</summary>
public sealed class AppState(ApiClient api)
{
    public List<AreaDto> Areas { get; private set; } = new();
    public List<FieldDefinitionDto> Fields { get; private set; } = new();
    public SettingsDto Settings { get; private set; } = new();
    public NavCounts Nav { get; private set; } = new();
    public bool Loaded { get; private set; }

    public event Action? Changed;

    public AreaDto? Area(Guid id) => Areas.FirstOrDefault(a => a.Id == id);
    public string AreaName(Guid id) => Area(id)?.Name ?? "?";
    public string AreaColor(Guid id) => Area(id)?.Color ?? "#6b7280";
    public List<FieldDefinitionDto> FieldsFor(Guid areaId, FieldTarget target)
        => Fields.Where(f => f.AreaId == areaId && f.AppliesTo == target && !f.Archived).OrderBy(f => f.SortOrder).ThenBy(f => f.Name).ToList();

    public async Task EnsureLoadedAsync()
    {
        if (Loaded) return;
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var areas = api.GetAreas();
        var settings = api.GetSettings();
        var nav = api.GetNavCounts();
        var fields = api.GetFields();
        await Task.WhenAll(areas, settings, nav, fields);
        Areas = areas.Result;
        Settings = settings.Result;
        Nav = nav.Result;
        Fields = fields.Result;
        Loaded = true;
        Changed?.Invoke();
    }

    public async Task RefreshAreasAsync()
    {
        Areas = await api.GetAreas();
        Changed?.Invoke();
    }

    public async Task RefreshFieldsAsync()
    {
        Fields = await api.GetFields();
        Changed?.Invoke();
    }

    public async Task RefreshNavAsync()
    {
        try
        {
            Nav = await api.GetNavCounts();
            Changed?.Invoke();
        }
        catch { /* badges are best-effort */ }
    }

    public async Task RefreshSettingsAsync()
    {
        Settings = await api.GetSettings();
        Changed?.Invoke();
    }
}
