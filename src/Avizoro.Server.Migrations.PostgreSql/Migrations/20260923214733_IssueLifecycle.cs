using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avizoro.Server.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class IssueLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRegression",
                table: "Issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegressedAt",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAt",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedInRelease",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Issues",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unresolved");

            migrationBuilder.CreateIndex(
                name: "IX_Issues_ProjectId_Status",
                table: "Issues",
                columns: new[] { "ProjectId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Issues_ProjectId_Status",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "IsRegression",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "RegressedAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "ResolvedInRelease",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Issues");
        }
    }
}
