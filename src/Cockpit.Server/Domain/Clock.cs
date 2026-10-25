namespace Cockpit.Server.Domain;

public interface IClock
{
    DateTime UtcNow { get; }
    /// <summary>Calendar date on the server; views accept an explicit <c>today</c> from the client instead.</summary>
    DateOnly Today { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
}
