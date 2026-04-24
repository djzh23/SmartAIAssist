using Microsoft.EntityFrameworkCore;
using CvStudio.Application.Repositories;
using CvStudio.Domain.Entities;

namespace CvStudio.Infrastructure.Persistence;

public sealed class CvStudioDbContext : DbContext, IApplicationDbContext
{
    public CvStudioDbContext(DbContextOptions<CvStudioDbContext> options)
        : base(options)
    {
    }

    public DbSet<Resume> Resumes => Set<Resume>();
    public DbSet<Snapshot> ResumeVersions => Set<Snapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Resume>(entity =>
        {
            entity.ToTable("resumes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ClerkUserId).HasColumnName("clerk_user_id").HasMaxLength(128).IsRequired();
            entity.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
            entity.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(80);
            entity.Property(x => x.CurrentContentJson).HasColumnName("current_content_json").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            entity.Property(x => x.LinkedJobApplicationId).HasColumnName("linked_job_application_id").HasMaxLength(80);
            entity.Property(x => x.TargetCompany).HasColumnName("target_company").HasMaxLength(300);
            entity.Property(x => x.TargetRole).HasColumnName("target_role").HasMaxLength(300);
            entity.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(2000);

            entity.HasIndex(x => new { x.ClerkUserId, x.UpdatedAtUtc });

            entity.HasIndex(x => x.LinkedJobApplicationId)
                .HasDatabaseName("IX_resumes_linked_job_application_id");

            entity.HasMany(x => x.Versions)
                .WithOne(x => x.Resume)
                .HasForeignKey(x => x.ResumeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Snapshot>(entity =>
        {
            entity.ToTable("resume_versions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.ResumeId).HasColumnName("resume_id").IsRequired();
            entity.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
            entity.Property(x => x.Label).HasColumnName("label").HasMaxLength(120);
            entity.Property(x => x.ContentJson).HasColumnName("content_json").HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

            entity.HasIndex(x => new { x.ResumeId, x.VersionNumber }).IsUnique();
            entity.HasIndex(x => x.ResumeId);
        });
    }
}

