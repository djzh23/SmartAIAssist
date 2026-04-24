using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using CvStudio.Infrastructure.Persistence;

#nullable disable

namespace CvStudio.Infrastructure.Migrations;

[DbContext(typeof(CvStudioDbContext))]
partial class CvStudioDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder
            .HasAnnotation("ProductVersion", "9.0.0")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("CvStudio.Domain.Entities.Resume", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<string>("ClerkUserId")
                    .IsRequired()
                    .HasMaxLength(128)
                    .HasColumnType("character varying(128)")
                    .HasColumnName("clerk_user_id");

                b.Property<string>("CurrentContentJson")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("current_content_json");

                b.Property<string>("Title")
                    .IsRequired()
                    .HasMaxLength(160)
                    .HasColumnType("character varying(160)")
                    .HasColumnName("title");

                b.Property<string>("TemplateKey")
                    .HasMaxLength(80)
                    .HasColumnType("character varying(80)")
                    .HasColumnName("template_key");

                b.Property<DateTime>("UpdatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("updated_at_utc");

                b.Property<string>("LinkedJobApplicationId")
                    .HasMaxLength(80)
                    .HasColumnType("character varying(80)")
                    .HasColumnName("linked_job_application_id");

                b.Property<string>("TargetCompany")
                    .HasMaxLength(300)
                    .HasColumnType("character varying(300)")
                    .HasColumnName("target_company");

                b.Property<string>("TargetRole")
                    .HasMaxLength(300)
                    .HasColumnType("character varying(300)")
                    .HasColumnName("target_role");

                b.Property<string>("Notes")
                    .HasMaxLength(2000)
                    .HasColumnType("character varying(2000)")
                    .HasColumnName("notes");

                b.HasKey("Id");

                b.HasIndex("ClerkUserId", "UpdatedAtUtc")
                    .HasDatabaseName("IX_resumes_clerk_user_id_updated_at_utc");

                b.HasIndex("LinkedJobApplicationId")
                    .HasDatabaseName("IX_resumes_linked_job_application_id");

                b.ToTable("resumes", (string)null);
            });

        modelBuilder.Entity("CvStudio.Domain.Entities.Snapshot", b =>
            {
                b.Property<Guid>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("uuid")
                    .HasColumnName("id");

                b.Property<string>("ContentJson")
                    .IsRequired()
                    .HasColumnType("jsonb")
                    .HasColumnName("content_json");

                b.Property<DateTime>("CreatedAtUtc")
                    .HasColumnType("timestamp with time zone")
                    .HasColumnName("created_at_utc");

                b.Property<string>("Label")
                    .HasMaxLength(120)
                    .HasColumnType("character varying(120)")
                    .HasColumnName("label");

                b.Property<Guid>("ResumeId")
                    .HasColumnType("uuid")
                    .HasColumnName("resume_id");

                b.Property<int>("VersionNumber")
                    .HasColumnType("integer")
                    .HasColumnName("version_number");

                b.HasKey("Id");

                b.HasIndex("ResumeId")
                    .HasDatabaseName("IX_resume_versions_resume_id");

                b.HasIndex("ResumeId", "VersionNumber")
                    .IsUnique()
                    .HasDatabaseName("IX_resume_versions_resume_id_version_number");

                b.ToTable("resume_versions", (string)null);

                b.HasOne("CvStudio.Domain.Entities.Resume", "Resume")
                    .WithMany("Versions")
                    .HasForeignKey("ResumeId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();

                b.Navigation("Resume");
            });

        modelBuilder.Entity("CvStudio.Domain.Entities.Resume", b =>
            {
                b.Navigation("Versions");
            });
#pragma warning restore 612, 618
    }
}


