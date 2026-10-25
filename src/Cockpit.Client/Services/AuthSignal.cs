namespace Cockpit.Client.Services;

/// <summary>Lets the API client tell the layout that the session is locked (401) without a circular dependency.</summary>
public sealed class AuthSignal
{
    public event Action? Locked;
    public void RaiseLocked() => Locked?.Invoke();
}
