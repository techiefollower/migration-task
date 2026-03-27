using Microsoft.EntityFrameworkCore;
using RepoMigration.Core.Entities;

namespace RepoMigration.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<RepoMigrationRecord> RepoMigrations => Set<RepoMigrationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RepoMigrationRecord>(entity =>
        {
            entity.ToTable("RepoMigration");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RepoName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.SourceUrl).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.TargetUrl).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.TargetRepoVisibility).HasMaxLength(32).IsRequired();
            entity.Property(e => e.AdoPipeline).HasMaxLength(512);
            entity.Property(e => e.ServiceConnectionId).HasMaxLength(128);
            entity.Property(e => e.Logs).HasMaxLength(16000);
            entity.Property(e => e.Status).HasConversion<int>();
        });
    }
}
