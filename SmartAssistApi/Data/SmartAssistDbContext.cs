using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data.Entities;

namespace SmartAssistApi.Data;

public sealed class SmartAssistDbContext(DbContextOptions<SmartAssistDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();

    public DbSet<ChatNoteEntity> ChatNotes => Set<ChatNoteEntity>();

    public DbSet<JobApplicationEntity> JobApplications => Set<JobApplicationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUserEntity>(e =>
        {
            e.ToTable("app_users");
            e.HasKey(x => x.ClerkUserId);
        });

        modelBuilder.Entity<ChatNoteEntity>(e =>
        {
            e.ToTable("chat_notes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClerkUserId, x.UpdatedAt });
            e.Property(x => x.Tags).HasColumnType("text[]");
        });

        modelBuilder.Entity<JobApplicationEntity>(e =>
        {
            e.ToTable("job_applications");
            e.HasKey(x => new { x.ClerkUserId, x.ApplicationId });
            e.HasIndex(x => new { x.ClerkUserId, x.UpdatedAt });
        });
    }
}
