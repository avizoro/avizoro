using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avizoro.Server.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class SessionTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeviceModel",
                table: "Sessions",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivity",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Sessions",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                table: "Sessions",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TimelineEntries",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TimelineEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TimelineEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_SessionId",
                table: "Events",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_BatchId",
                table: "TimelineEntries",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_ProjectId_ReceivedAt",
                table: "TimelineEntries",
                columns: new[] { "ProjectId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TimelineEntries_SessionId_Sequence",
                table: "TimelineEntries",
                columns: new[] { "SessionId", "Sequence" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TimelineEntries");

            migrationBuilder.DropIndex(
                name: "IX_Events_SessionId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DeviceModel",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LastActivity",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Manufacturer",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "OperatingSystem",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "TimelineEntries",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "Events");
        }
    }
}
