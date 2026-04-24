using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CvStudio.Infrastructure.Persistence;

#nullable disable

namespace CvStudio.Infrastructure.Migrations;

[DbContext(typeof(CvStudioDbContext))]
[Migration("20260424000001_AddResumeApplicationFields")]
public partial class AddResumeApplicationFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "linked_job_application_id",
            table: "resumes",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "target_company",
            table: "resumes",
            type: "character varying(300)",
            maxLength: 300,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "target_role",
            table: "resumes",
            type: "character varying(300)",
            maxLength: 300,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "notes",
            table: "resumes",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_resumes_linked_job_application_id",
            table: "resumes",
            column: "linked_job_application_id")
            .Annotation("Npgsql:IndexNullsDistinct", false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_resumes_linked_job_application_id",
            table: "resumes");

        migrationBuilder.DropColumn(name: "linked_job_application_id", table: "resumes");
        migrationBuilder.DropColumn(name: "target_company", table: "resumes");
        migrationBuilder.DropColumn(name: "target_role", table: "resumes");
        migrationBuilder.DropColumn(name: "notes", table: "resumes");
    }
}
