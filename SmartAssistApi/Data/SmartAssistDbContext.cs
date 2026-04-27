using Microsoft.EntityFrameworkCore;
using SmartAssistApi.Data.Entities;

namespace SmartAssistApi.Data;

public sealed class SmartAssistDbContext(DbContextOptions<SmartAssistDbContext> options) : DbContext(options)
{
    public DbSet<AppUserEntity> AppUsers => Set<AppUserEntity>();

    public DbSet<ChatNoteEntity> ChatNotes => Set<ChatNoteEntity>();

    public DbSet<JobApplicationEntity> JobApplications => Set<JobApplicationEntity>();

    public DbSet<CareerProfileEntity> CareerProfiles => Set<CareerProfileEntity>();

    public DbSet<ChatSessionEntity> ChatSessions => Set<ChatSessionEntity>();

    public DbSet<ChatTranscriptEntity> ChatTranscripts => Set<ChatTranscriptEntity>();

    public DbSet<LearningMemoryEntity> LearningMemories => Set<LearningMemoryEntity>();

    public DbSet<TokenUsageGlobalDailyEntity> TokenUsageGlobalDaily => Set<TokenUsageGlobalDailyEntity>();

    public DbSet<TokenUsageDailyUserEntity> TokenUsageDailyUsers => Set<TokenUsageDailyUserEntity>();

    public DbSet<TokenUsageDailyUserModelEntity> TokenUsageDailyUserModels => Set<TokenUsageDailyUserModelEntity>();

    public DbSet<TokenUsageDailyUserToolEntity> TokenUsageDailyUserTools => Set<TokenUsageDailyUserToolEntity>();

    public DbSet<TokenUsageRegisteredUserEntity> TokenUsageRegisteredUsers => Set<TokenUsageRegisteredUserEntity>();

    public DbSet<UserUsageDailyEntity> UserUsageDaily => Set<UserUsageDailyEntity>();

    public DbSet<UserPlanEntity> UserPlans => Set<UserPlanEntity>();

    public DbSet<CvPdfExportEntity> CvPdfExports => Set<CvPdfExportEntity>();

    public DbSet<CvUserCategoryEntity> CvUserCategories => Set<CvUserCategoryEntity>();

    public DbSet<CvResumeCategoryAssignmentEntity> CvResumeCategoryAssignments => Set<CvResumeCategoryAssignmentEntity>();

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

        modelBuilder.Entity<CareerProfileEntity>(e =>
        {
            e.ToTable("career_profiles");
            e.HasKey(x => x.ClerkUserId);
            e.HasIndex(x => x.UpdatedAt);
        });

        modelBuilder.Entity<ChatSessionEntity>(e =>
        {
            e.ToTable("chat_sessions");
            e.HasKey(x => new { x.ClerkUserId, x.SessionId });
            e.HasIndex(x => new { x.ClerkUserId, x.DisplayOrder });
        });

        modelBuilder.Entity<ChatTranscriptEntity>(e =>
        {
            e.ToTable("chat_transcripts");
            e.HasKey(x => new { x.ClerkUserId, x.SessionId });
        });

        modelBuilder.Entity<LearningMemoryEntity>(e =>
        {
            e.ToTable("learning_memories");
            e.HasKey(x => x.ClerkUserId);
        });

        modelBuilder.Entity<TokenUsageGlobalDailyEntity>(e =>
        {
            e.ToTable("token_usage_global_daily");
            e.HasKey(x => x.UsageDate);
        });

        modelBuilder.Entity<TokenUsageDailyUserEntity>(e =>
        {
            e.ToTable("token_usage_daily_user");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate });
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
        });

        modelBuilder.Entity<TokenUsageDailyUserModelEntity>(e =>
        {
            e.ToTable("token_usage_daily_user_model");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate, x.ModelKey });
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
        });

        modelBuilder.Entity<TokenUsageDailyUserToolEntity>(e =>
        {
            e.ToTable("token_usage_daily_user_tool");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate, x.Tool });
            e.Property(x => x.CostUsd).HasPrecision(18, 6);
        });

        modelBuilder.Entity<TokenUsageRegisteredUserEntity>(e =>
        {
            e.ToTable("token_usage_registered_users");
            e.HasKey(x => x.ClerkUserId);
        });

        modelBuilder.Entity<UserUsageDailyEntity>(e =>
        {
            e.ToTable("user_usage_daily");
            e.HasKey(x => new { x.ClerkUserId, x.UsageDate });
        });

        modelBuilder.Entity<UserPlanEntity>(e =>
        {
            e.ToTable("user_plan");
            e.HasKey(x => x.ClerkUserId);
        });

        modelBuilder.Entity<CvPdfExportEntity>(e =>
        {
            e.ToTable("cv_pdf_exports");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClerkUserId, x.CreatedAt });
        });

        modelBuilder.Entity<CvUserCategoryEntity>(e =>
        {
            e.ToTable("cv_user_categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.ClerkUserId).HasColumnName("clerk_user_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.HasIndex(x => x.ClerkUserId);
        });

        modelBuilder.Entity<CvResumeCategoryAssignmentEntity>(e =>
        {
            e.ToTable("cv_resume_category_assignments");
            e.HasKey(x => x.ResumeId);
            e.Property(x => x.ResumeId).HasColumnName("resume_id");
            e.Property(x => x.ClerkUserId).HasColumnName("clerk_user_id").HasMaxLength(128).IsRequired();
            e.Property(x => x.CategoryId).HasColumnName("category_id");
            e.HasIndex(x => x.ClerkUserId);
        });
    }
}
