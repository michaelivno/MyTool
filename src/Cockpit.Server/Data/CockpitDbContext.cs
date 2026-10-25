using Cockpit.Server.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cockpit.Server.Data;

public sealed class CockpitDbContext(DbContextOptions<CockpitDbContext> options, IClock clock) : DbContext(options)
{
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<InboxItem> InboxItems => Set<InboxItem>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<FieldDefinition> FieldDefinitions => Set<FieldDefinition>();
    public DbSet<Setting> Settings => Set<Setting>();

    /// <summary>Set by the importer so CreatedAt/UpdatedAt from the export document survive untouched.</summary>
    public bool PreserveTimestamps { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // SQLite stores DateTime as text without a kind; always write UTC and read back as UTC.
        b.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Enums are stored as readable strings so the .db file stays inspectable with sqlite3.
        b.Entity<Area>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Color).HasMaxLength(16);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.ReviewCadence).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => new { x.Archived, x.SortOrder });
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<Project>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Area).WithMany(a => a.Projects).HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AreaId, x.Status });
            e.HasIndex(x => new { x.Status, x.RevisitAt });
            e.HasIndex(x => x.Deadline);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<TaskItem>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Effort).HasConversion<string>().HasMaxLength(8);
            e.HasOne(x => x.Area).WithMany(a => a.Tasks).HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Project).WithMany(p => p.Tasks).HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AreaId, x.Status });
            e.HasIndex(x => new { x.ProjectId, x.Status });
            e.HasIndex(x => new { x.Status, x.Due });
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<InboxItem>(e =>
        {
            e.Property(x => x.ConvertedTo).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.ProcessedAt);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<Review>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.HasIndex(x => x.StartedAt);
            e.HasIndex(x => x.Status);
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<FieldDefinition>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.AppliesTo).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32);
            e.HasOne(x => x.Area).WithMany(a => a.FieldDefinitions).HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AreaId, x.AppliesTo, x.Key }).IsUnique();
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        foreach (var entry in ChangeTracker.Entries<EntityBase>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.Id == Guid.Empty) entry.Entity.Id = Guid.NewGuid();
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                if (entry.Entity.UpdatedAt == default) entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified && !PreserveTimestamps)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(x => x.CreatedAt).IsModified = false;
            }
        }
        return base.SaveChangesAsync(ct);
    }
}

internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
