using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IntelligenceKit.Server.Migrations.PostgreSql.Migrations
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
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastActivity",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Manufacturer",
                table: "Sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OperatingSystem",
                table: "Sessions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TimelineEntries",
                table: "Sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TimelineEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<string>(type: "text", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DataJson = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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
