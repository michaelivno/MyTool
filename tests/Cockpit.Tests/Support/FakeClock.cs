using Cockpit.Server.Domain;

namespace Cockpit.Tests.Support;

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 25, 9, 0, 0, DateTimeKind.Utc);
    public DateOnly Today { get; set; } = new(2026, 10, 25);

    public void Advance(TimeSpan by)
    {
        UtcNow = UtcNow.Add(by);
        Today = DateOnly.FromDateTime(UtcNow);
    }
}
