using Cockpit.Server.Api;
using Cockpit.Server.Data;
using Cockpit.Server.Domain;
using Cockpit.Server.Services;
using Cockpit.Shared;
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
builder.Services.AddScoped<Seeder>();
builder.Services.ConfigureHttpJsonOptions(o => CockpitJson.Configure(o.SerializerOptions));

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

app.Logger.LogInformation("Cockpit database: {Db}", dbPath);

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapCockpitApi();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
