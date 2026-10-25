using Cockpit.Server.Api;
using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Server.Services;
using System.Threading.RateLimiting;
using Cockpit.Shared;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The SQLite file *is* the data. COCKPIT_DB points at it (default: ./data/cockpit.db next to the app).
var dbPath = builder.Configuration["COCKPIT_DB"];
if (string.IsNullOrWhiteSpace(dbPath))
    dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "cockpit.db");
dbPath = Path.GetFullPath(dbPath);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath, ForeignKeys = true }.ToString();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddDbContext<CockpitDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AreaService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<InboxService>();
builder.Services.AddScoped<FieldDefinitionService>();
builder.Services.AddScoped<ViewsService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<MarkdownExportService>();
builder.Services.AddScoped<Seeder>();
builder.Services.ConfigureHttpJsonOptions(o => CockpitJson.Configure(o.SerializerOptions));

// Daily backups next to the database (COCKPIT_BACKUP_DIR overrides).
var backupDir = builder.Configuration["COCKPIT_BACKUP_DIR"];
if (string.IsNullOrWhiteSpace(backupDir)) backupDir = Path.Combine(Path.GetDirectoryName(dbPath)!, "backups");
backupDir = Path.GetFullPath(backupDir);
builder.Services.AddSingleton(new BackupOptions(connectionString, backupDir));
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BackupService>());

// Optional single-user login: set COCKPIT_PASSWORD to require it (use behind HTTPS when exposed publicly).
var password = builder.Configuration["COCKPIT_PASSWORD"];
var auth = new AuthOptions(!string.IsNullOrEmpty(password), password);
builder.Services.AddSingleton(auth);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "cockpit.auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    o.ExpireTimeSpan = TimeSpan.FromDays(90);
    o.SlidingExpiration = true;
    // An API, not a website: answer 401/403 instead of redirecting to a login page.
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.LoginRateLimitPolicy, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Migrate on startup (single-user app; migrations are checked in), switch to WAL for durability + concurrency.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CockpitDbContext>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

    var seedRequested = args.Contains("--seed");
    var seedOnEmpty = string.Equals(builder.Configuration["COCKPIT_SEED_ON_EMPTY"], "true", StringComparison.OrdinalIgnoreCase);
    if (seedRequested || seedOnEmpty)
    {
        var seeded = await scope.ServiceProvider.GetRequiredService<Seeder>().SeedIfEmptyAsync();
        app.Logger.LogInformation(seeded ? "Seeded initial areas and projects into {Db}" : "Database {Db} already has data; seed skipped", dbPath);
    }
    if (seedRequested) return;
}

app.Logger.LogInformation("Cockpit database: {Db}; backups: {Backups}; login: {Login}", dbPath, backupDir, auth.Enabled ? "required" : "off");

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Client assets come from the static web assets manifest: fingerprinted framework files with immutable caching,
// precompressed (.br/.gz) variants, and plain aliases such as _framework/blazor.webassembly.js that index.html uses.
app.MapStaticAssets();
app.MapCockpitApi(requireAuth: auth.Enabled);
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
