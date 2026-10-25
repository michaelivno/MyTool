using Cockpit.Shared;

namespace Cockpit.Client.Services;

/// <summary>Small client cache: areas, settings and nav badge counts. Pages call Refresh* after mutations.</summary>
public sealed class AppState(ApiClient api)
{
    public List<AreaDto> Areas { get; private set; } = new();
    public SettingsDto Settings { get; private set; } = new();
    public NavCounts Nav { get; private set; } = new();
    public bool Loaded { get; private set; }

    public event Action? Changed;

    public AreaDto? Area(Guid id) => Areas.FirstOrDefault(a => a.Id == id);
    public string AreaName(Guid id) => Area(id)?.Name ?? "?";
    public string AreaColor(Guid id) => Area(id)?.Color ?? "#6b7280";

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
        await Task.WhenAll(areas, settings, nav);
        Areas = areas.Result;
        Settings = settings.Result;
        Nav = nav.Result;
        Loaded = true;
        Changed?.Invoke();
    }

    public async Task RefreshAreasAsync()
    {
        Areas = await api.GetAreas();
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
