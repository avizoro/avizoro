using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avizoro.Server.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class IssueGrouping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Fingerprint",
                table: "Events",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Issues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    Fingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Culprit = table.Column<string>(type: "TEXT", nullable: true),
                    EventType = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: true),
                    EventCount = table.Column<long>(type: "INTEGER", nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastEventId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Issues", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_ProjectId_Fingerprint",
                table: "Events",
                columns: new[] { "ProjectId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProjectId_Fingerprint",
                table: "Issues",
                columns: new[] { "ProjectId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProjectId_LastSeen",
                table: "Issues",
                columns: new[] { "ProjectId", "LastSeen" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Issues");

            migrationBuilder.DropIndex(
                name: "IX_Events_ProjectId_Fingerprint",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Fingerprint",
                table: "Events");
        }
    }
}
