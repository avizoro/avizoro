using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avizoro.Server.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class RichContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BreadcrumbsJson",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeviceRuntimeJson",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "Events",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Level",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Message",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Release",
                table: "Events",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TagsJson",
                table: "Events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserId",
                table: "Events",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BreadcrumbsJson",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DeviceRuntimeJson",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Level",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Message",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Release",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TagsJson",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Events");
        }
    }
}
