using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CvStudio.Infrastructure.Persistence;

#nullable disable

namespace CvStudio.Infrastructure.Migrations;

[DbContext(typeof(CvStudioDbContext))]
[Migration("20260303220000_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "resumes",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                current_content_json = table.Column<string>(type: "jsonb", nullable: false),
                updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_resumes", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "resume_versions",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                resume_id = table.Column<Guid>(type: "uuid", nullable: false),
                version_number = table.Column<int>(type: "integer", nullable: false),
                label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                content_json = table.Column<string>(type: "jsonb", nullable: false),
                created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_resume_versions", x => x.id);
                table.ForeignKey(
                    name: "FK_resume_versions_resumes_resume_id",
                    column: x => x.resume_id,
                    principalTable: "resumes",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_resume_versions_resume_id",
            table: "resume_versions",
            column: "resume_id");

        migrationBuilder.CreateIndex(
            name: "IX_resume_versions_resume_id_version_number",
            table: "resume_versions",
            columns: new[] { "resume_id", "version_number" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "resume_versions");
        migrationBuilder.DropTable(name: "resumes");
    }
}

