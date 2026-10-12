using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avizoro.Server.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IssueReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstRelease",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRelease",
                table: "Issues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProjectId_FirstRelease",
                table: "Issues",
                columns: new[] { "ProjectId", "FirstRelease" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Issues_ProjectId_FirstRelease",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "FirstRelease",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "LastRelease",
                table: "Issues");
        }
    }
}
