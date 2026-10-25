using System.Net;
using System.Net.Http.Json;
using Cockpit.Server.Data;
using Cockpit.Server.Services;
using Cockpit.Shared;
using Cockpit.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cockpit.Tests;

public sealed class AuthTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"cockpit-auth-{Guid.NewGuid():N}.db");
    private WebApplicationFactory<Program> _factory = default!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("COCKPIT_DB", _dbPath);
            b.UseSetting("COCKPIT_PASSWORD", "correct horse");
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" }) if (File.Exists(f)) File.Delete(f);
    }

    [Fact]
    public async Task Api_is_locked_until_the_password_is_given_then_the_cookie_opens_it()
    {
        var client = _factory.CreateClient(); // handles cookies

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        var status = await client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status", CockpitJson.Options);
        Assert.True(status!.Required);
        Assert.False(status.Authenticated);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/areas")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/export/json")).StatusCode);

        var wrong = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Password = "nope" }, CockpitJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/areas")).StatusCode);

        var right = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Password = "correct horse" }, CockpitJson.Options);
        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
        Assert.Contains(right.Headers, h => h.Key == "Set-Cookie" && h.Value.Any(v => v.StartsWith("cockpit.auth=") && v.Contains("httponly")));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/areas")).StatusCode);
        Assert.True((await client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status", CockpitJson.Options))!.Authenticated);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/areas")).StatusCode);
    }

    [Fact]
    public async Task Login_is_rate_limited()
    {
        var client = _factory.CreateClient();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++)
            codes.Add((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Password = "guess" }, CockpitJson.Options)).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        Assert.Equal(HttpStatusCode.Unauthorized, codes[0]);
    }

    [Fact]
    public async Task Static_app_shell_stays_public()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/board");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}

public sealed class NoPasswordTests
{
    [Fact]
    public async Task Without_a_password_the_api_is_open_and_status_says_so()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cockpit-open-{Guid.NewGuid():N}.db");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseSetting("COCKPIT_DB", dbPath));
        var client = factory.CreateClient();
        var status = await client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status", CockpitJson.Options);
        Assert.False(status!.Required);
        Assert.True(status.Authenticated);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/areas")).StatusCode);
    }
}

public sealed class BackupTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"cockpit-bk-{Guid.NewGuid():N}");
    private readonly string _dbPath;
    private readonly string _connString;

    public BackupTests()
    {
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "live.db");
        _connString = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ToString();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private BackupService CreateService(string backupDir)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Cockpit.Server.Domain.IClock, FakeClock>();
        services.AddDbContext<CockpitDbContext>(o => o.UseSqlite(_connString));
        services.AddScoped<SettingsService>();
        var sp = services.BuildServiceProvider();
        using (var scope = sp.CreateScope()) scope.ServiceProvider.GetRequiredService<CockpitDbContext>().Database.Migrate();
        return new BackupService(new BackupOptions(_connString, backupDir), sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackupService>.Instance);
    }

    [Fact]
    public async Task Backup_is_a_consistent_single_file_copy_that_opens_on_its_own()
    {
        var backupDir = Path.Combine(_dir, "backups");
        var svc = CreateService(backupDir);
        await using (var conn = new SqliteConnection(_connString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; INSERT INTO Areas (Id, Name, Kind, Color, ReviewCadence, SortOrder, Archived, PropertiesJson, CreatedAt, UpdatedAt) VALUES ('11111111-1111-1111-1111-111111111111','Home','Home','#fff','Weekly',1,0,'{}','2026-01-01 00:00:00','2026-01-01 00:00:00');";
            await cmd.ExecuteNonQueryAsync();
        }

        var info = await svc.BackupNowAsync();

        var file = Path.Combine(backupDir, info.FileName);
        Assert.True(File.Exists(file));
        Assert.StartsWith("cockpit-", info.FileName);
        Assert.False(File.Exists(file + "-wal"), "backup should not need a WAL side file");
        await using var copy = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await copy.OpenAsync();
        await using var check = copy.CreateCommand();
        check.CommandText = "SELECT Name FROM Areas";
        Assert.Equal("Home", (string?)await check.ExecuteScalarAsync());
        Assert.Single(svc.List());
        Assert.Equal(info.FileName, svc.Status().Backups[0].FileName);
    }

    [Fact]
    public async Task Prune_removes_old_backups_but_keeps_the_newest_three()
    {
        var backupDir = Path.Combine(_dir, "backups");
        Directory.CreateDirectory(backupDir);
        var svc = CreateService(backupDir);
        foreach (var daysAgo in new[] { 1, 2, 3, 40, 50, 60 })
            File.WriteAllText(Path.Combine(backupDir, $"cockpit-{DateTime.UtcNow.AddDays(-daysAgo):yyyyMMdd-HHmmss}.db"), "x");
        File.WriteAllText(Path.Combine(backupDir, "unrelated.db"), "x");

        var removed = svc.Prune(retentionDays: 30);

        Assert.Equal(3, removed);
        Assert.Equal(3, svc.List().Count);
        Assert.True(File.Exists(Path.Combine(backupDir, "unrelated.db")));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RunIfDue_backs_up_once_per_day_after_the_configured_hour()
    {
        var backupDir = Path.Combine(_dir, "backups");
        var svc = CreateService(backupDir);
        // Default backup hour is 3; make it 0 so "now" is always past it.
        var services = new ServiceCollection();
        services.AddSingleton<Cockpit.Server.Domain.IClock, FakeClock>();
        services.AddDbContext<CockpitDbContext>(o => o.UseSqlite(_connString));
        services.AddScoped<SettingsService>();
        using (var scope = services.BuildServiceProvider().CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<SettingsService>().UpdateAsync(new SettingsDto { BackupHour = 0 })).Ok);

        Assert.True(await svc.RunIfDueAsync());
        Assert.False(await svc.RunIfDueAsync()); // already done today
        Assert.Single(svc.List());
    }
}
