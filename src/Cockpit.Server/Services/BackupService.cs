using System.Globalization;
using Cockpit.Shared;
using Microsoft.Data.Sqlite;

namespace Cockpit.Server.Services;

public sealed record BackupOptions(string ConnectionString, string Directory);

/// <summary>
/// Daily SQLite backup using SQLite's online backup API (safe while the app writes, WAL included).
/// Runs inside the app, so there is no second container or cron job. Hour and retention come from Settings.
/// Each backup is a single self-contained .db file in <see cref="BackupOptions.Directory"/>.
/// </summary>
public sealed class BackupService(BackupOptions options, IServiceScopeFactory scopes, ILogger<BackupService> log) : BackgroundService
{
    private const string Prefix = "cockpit-";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public DateTime? LastRunUtc { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try { await RunIfDueAsync(ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Daily backup failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(10), ct); } catch (OperationCanceledException) { }
        }
    }

    /// <summary>Backs up once per local calendar day, at or after the configured hour, then prunes.</summary>
    public async Task<bool> RunIfDueAsync(CancellationToken ct = default)
    {
        var (hour, retentionDays) = await ReadSettingsAsync(ct);
        var now = DateTime.Now;
        if (now.Hour < hour) return false;
        var latest = List().FirstOrDefault();
        if (latest is not null && DateOnly.FromDateTime(latest.CreatedUtc.ToLocalTime()) == DateOnly.FromDateTime(now)) return false;
        await BackupNowAsync(ct);
        Prune(retentionDays);
        return true;
    }

    public async Task<BackupFileDto> BackupNowAsync(CancellationToken ct = default)
    {
        await Gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(options.Directory);
            var stamp = DateTime.UtcNow;
            var finalPath = Path.Combine(options.Directory, $"{Prefix}{stamp:yyyyMMdd-HHmmss}.db");
            var tmpPath = finalPath + ".tmp";
            if (File.Exists(tmpPath)) File.Delete(tmpPath);

            var dstConn = new SqliteConnectionStringBuilder { DataSource = tmpPath, Pooling = false }.ToString();
            using (var src = new SqliteConnection(options.ConnectionString))
            using (var dst = new SqliteConnection(dstConn))
            {
                await src.OpenAsync(ct);
                await dst.OpenAsync(ct);
                src.BackupDatabase(dst);
                // The copy inherits WAL mode from the source; switch it to a rollback journal so the backup is one file.
                using var pragma = dst.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=DELETE;";
                await pragma.ExecuteNonQueryAsync(ct);
            }
            File.Move(tmpPath, finalPath, overwrite: true);
            LastRunUtc = stamp;
            var info = ToInfo(new FileInfo(finalPath))!;
            log.LogInformation("Backup written: {File} ({Bytes} bytes)", info.FileName, info.Bytes);
            return info;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Newest first.</summary>
    public List<BackupFileDto> List()
    {
        if (!Directory.Exists(options.Directory)) return new();
        return new DirectoryInfo(options.Directory).GetFiles($"{Prefix}*.db")
            .Select(ToInfo).Where(i => i is not null).Select(i => i!)
            .OrderByDescending(i => i.CreatedUtc).ToList();
    }

    public BackupStatusDto Status() => new() { Directory = options.Directory, LastRunUtc = LastRunUtc ?? List().FirstOrDefault()?.CreatedUtc, Backups = List() };

    /// <summary>Deletes backups older than the retention window, always keeping the newest three.</summary>
    public int Prune(int retentionDays)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, retentionDays));
        var removed = 0;
        foreach (var old in List().Skip(3).Where(b => b.CreatedUtc < cutoff))
        {
            try { File.Delete(Path.Combine(options.Directory, old.FileName)); removed++; }
            catch (IOException ex) { log.LogWarning(ex, "Could not delete old backup {File}", old.FileName); }
        }
        return removed;
    }

    private static BackupFileDto? ToInfo(FileInfo f)
    {
        var core = Path.GetFileNameWithoutExtension(f.Name);
        if (!core.StartsWith(Prefix)) return null;
        var created = DateTime.TryParseExact(core[Prefix.Length..], "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt : f.LastWriteTimeUtc;
        return new BackupFileDto { FileName = f.Name, Bytes = f.Length, CreatedUtc = created };
    }

    private async Task<(int Hour, int RetentionDays)> ReadSettingsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var s = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(ct);
        return (s.BackupHour, s.BackupRetentionDays);
    }
}
