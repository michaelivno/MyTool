using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Shared;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Server.Services;

public sealed class SettingsService(CockpitDbContext db)
{
    public const string WipLimitKey = "wip_limit";
    public const string InboxStaleDaysKey = "inbox_stale_days";
    public const string BackupHourKey = "backup_hour";
    public const string BackupRetentionDaysKey = "backup_retention_days";

    public async Task<SettingsDto> GetAsync(CancellationToken ct = default)
        => FromRaw(await GetRawAsync(ct));

    public async Task<Dictionary<string, string>> GetRawAsync(CancellationToken ct = default)
        => await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

    public async Task<int> GetWipLimitAsync(CancellationToken ct = default)
    {
        var raw = await db.Settings.AsNoTracking().Where(s => s.Key == WipLimitKey).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return int.TryParse(raw, out var n) && n >= 1 ? n : ProjectRules.DefaultWipLimit;
    }

    public async Task<Result<SettingsDto>> UpdateAsync(SettingsDto dto, CancellationToken ct = default)
    {
        if (dto.WipLimit is < 1 or > 50) return Result<SettingsDto>.Invalid("WIP limit must be between 1 and 50.");
        if (dto.InboxStaleDays is < 1 or > 365) return Result<SettingsDto>.Invalid("Inbox stale days must be between 1 and 365.");
        if (dto.BackupHour is < 0 or > 23) return Result<SettingsDto>.Invalid("Backup hour must be 0-23.");
        if (dto.BackupRetentionDays is < 1 or > 3650) return Result<SettingsDto>.Invalid("Backup retention must be 1-3650 days.");

        await SetRawAsync(new Dictionary<string, string>
        {
            [WipLimitKey] = dto.WipLimit.ToString(),
            [InboxStaleDaysKey] = dto.InboxStaleDays.ToString(),
            [BackupHourKey] = dto.BackupHour.ToString(),
            [BackupRetentionDaysKey] = dto.BackupRetentionDays.ToString(),
        }, ct);
        return Result<SettingsDto>.Success(await GetAsync(ct));
    }

    public async Task SetRawAsync(Dictionary<string, string> values, CancellationToken ct = default)
    {
        var existing = await db.Settings.ToDictionaryAsync(s => s.Key, ct);
        foreach (var (k, v) in values)
        {
            if (existing.TryGetValue(k, out var row)) row.Value = v;
            else db.Settings.Add(new Setting { Key = k, Value = v });
        }
        await db.SaveChangesAsync(ct);
    }

    public static SettingsDto FromRaw(IReadOnlyDictionary<string, string> raw)
    {
        var d = new SettingsDto();
        if (raw.TryGetValue(WipLimitKey, out var s) && int.TryParse(s, out var n) && n >= 1) d.WipLimit = n;
        if (raw.TryGetValue(InboxStaleDaysKey, out s) && int.TryParse(s, out n) && n >= 1) d.InboxStaleDays = n;
        if (raw.TryGetValue(BackupHourKey, out s) && int.TryParse(s, out n) && n is >= 0 and <= 23) d.BackupHour = n;
        if (raw.TryGetValue(BackupRetentionDaysKey, out s) && int.TryParse(s, out n) && n >= 1) d.BackupRetentionDays = n;
        return d;
    }
}
