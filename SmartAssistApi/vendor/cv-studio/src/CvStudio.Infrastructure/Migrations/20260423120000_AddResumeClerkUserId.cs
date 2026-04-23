using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CvStudio.Infrastructure.Persistence;

#nullable disable

namespace CvStudio.Infrastructure.Migrations;

[DbContext(typeof(CvStudioDbContext))]
[Migration("20260423120000_AddResumeClerkUserId")]
public partial class AddResumeClerkUserId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "clerk_user_id",
            table: "resumes",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "cv-studio-standalone");

        migrationBuilder.CreateIndex(
            name: "IX_resumes_clerk_user_id_updated_at_utc",
            table: "resumes",
            columns: new[] { "clerk_user_id", "updated_at_utc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_resumes_clerk_user_id_updated_at_utc",
            table: "resumes");

        migrationBuilder.DropColumn(
            name: "clerk_user_id",
            table: "resumes");
    }
}
