namespace Cockpit.Client.Services;

public sealed record Toast(Guid Id, string Message, string Kind, string? ActionLabel, Func<Task>? Action);

public sealed class ToastService
{
    private readonly List<Toast> _toasts = new();
    public IReadOnlyList<Toast> Toasts => _toasts;
    public event Action? Changed;

    public void Info(string message, string? actionLabel = null, Func<Task>? action = null) => Add(message, "info", actionLabel, action);
    public void Success(string message, string? actionLabel = null, Func<Task>? action = null) => Add(message, "success", actionLabel, action);
    public void Error(string message) => Add(message, "error", null, null);

    public void Error(Exception ex) => Error(ex is ApiException api ? api.Error.Message : "Something went wrong. Check the connection and try again.");

    private void Add(string message, string kind, string? actionLabel, Func<Task>? action)
    {
        var t = new Toast(Guid.NewGuid(), message, kind, actionLabel, action);
        _toasts.Add(t);
        Changed?.Invoke();
        _ = DismissLater(t, kind == "error" ? 6000 : 3500);
    }

    public void Dismiss(Guid id)
    {
        _toasts.RemoveAll(t => t.Id == id);
        Changed?.Invoke();
    }

    private async Task DismissLater(Toast t, int ms)
    {
        await Task.Delay(ms);
        Dismiss(t.Id);
    }
}
