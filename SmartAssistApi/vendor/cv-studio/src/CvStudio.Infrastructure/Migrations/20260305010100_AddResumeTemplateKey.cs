using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CvStudio.Infrastructure.Persistence;

#nullable disable

namespace CvStudio.Infrastructure.Migrations;

[DbContext(typeof(CvStudioDbContext))]
[Migration("20260304003000_AddResumeTemplateKey")]
public partial class AddResumeTemplateKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "template_key",
            table: "resumes",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "template_key",
            table: "resumes");
    }
}

