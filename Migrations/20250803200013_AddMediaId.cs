using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventLauscherApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MediaId",
                table: "Events",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_MediaId",
                table: "Events",
                column: "MediaId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_MediaFiles_MediaId",
                table: "Events",
                column: "MediaId",
                principalTable: "MediaFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_MediaFiles_MediaId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_MediaId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "MediaId",
                table: "Events");
        }
    }
}
