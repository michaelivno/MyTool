using Cockpit.Server.Data;
using Cockpit.Server.Services;
using Cockpit.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Cockpit.Tests.Support;

/// <summary>Real services on a real SQLite database (in-memory, migrated), no HTTP. One instance per test.</summary>
public sealed class TestHost : IAsyncDisposable
{
    private readonly SqliteConnection _conn;
    private readonly DbContextOptions<CockpitDbContext> _options;

    public FakeClock Clock { get; }
    public CockpitDbContext Db { get; }
    public SettingsService Settings { get; }
    public AreaService Areas { get; }
    public ProjectService Projects { get; }
    public TaskService Tasks { get; }
    public InboxService Inbox { get; }
    public FieldDefinitionService Fields { get; }
    public ViewsService Views { get; }
    public ExportService Export { get; }
    public ExportService Import => Export;
    public Seeder Seeder { get; }
    public ReviewService Reviews { get; }

    private TestHost(SqliteConnection conn, DbContextOptions<CockpitDbContext> options, FakeClock clock)
    {
        _conn = conn;
        _options = options;
        Clock = clock;
        Db = new CockpitDbContext(options, clock);
        Settings = new SettingsService(Db);
        Areas = new AreaService(Db, clock);
        Projects = new ProjectService(Db, Settings, clock);
        Tasks = new TaskService(Db, clock);
        Inbox = new InboxService(Db, Tasks, Projects, clock);
        Fields = new FieldDefinitionService(Db, clock);
        Views = new ViewsService(Db, Settings, Inbox);
        Export = new ExportService(Db, Settings, clock);
        Seeder = new Seeder(Db, Settings);
        Reviews = new ReviewService(Db, Settings, Inbox, Views, clock);
    }

    public static async Task<TestHost> CreateAsync()
    {
        var conn = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await conn.OpenAsync();
        var options = new DbContextOptionsBuilder<CockpitDbContext>().UseSqlite(conn).Options;
        var clock = new FakeClock();
        var host = new TestHost(conn, options, clock);
        await host.Db.Database.MigrateAsync(); // runs the checked-in migrations, so a forgotten migration fails tests
        return host;
    }

    /// <summary>A second context on the same connection, with an empty change tracker.</summary>
    public CockpitDbContext FreshContext() => new(_options, Clock);

    public async Task<AreaDto> AddAreaAsync(string name = "Area", AreaKind kind = AreaKind.Other)
    {
        var r = await Areas.CreateAsync(new AreaDto { Name = name, Kind = kind });
        Assert.True(r.Ok, r.Error?.Message);
        return r.Value!;
    }

    public async Task<ProjectDto> AddProjectAsync(Guid areaId, string name, ProjectStatus status = ProjectStatus.Active, DateOnly? revisitAt = null)
    {
        var r = await Projects.CreateAsync(new ProjectDto { AreaId = areaId, Name = name, Status = status, RevisitAt = revisitAt });
        Assert.True(r.Ok, r.Error?.Message);
        return r.Value!;
    }

    public async Task<TaskDto> AddTaskAsync(Guid areaId, string title, Guid? projectId = null, DateOnly? due = null, TaskItemStatus status = TaskItemStatus.Todo)
    {
        var r = await Tasks.CreateAsync(new TaskDto { AreaId = areaId, ProjectId = projectId, Title = title, Due = due, Status = status });
        Assert.True(r.Ok, r.Error?.Message);
        return r.Value!;
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _conn.DisposeAsync();
    }
}
