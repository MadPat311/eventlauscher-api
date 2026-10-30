using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventLauscherApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSourceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourcePlatform",
                table: "Events",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "Events",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourcePlatform",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "SourceUrl",
                table: "Events");
        }
    }
}
