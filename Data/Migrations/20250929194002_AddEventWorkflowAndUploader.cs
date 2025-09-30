using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventLauscherApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventWorkflowAndUploader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "Events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "Events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "UploadUserId",
                table: "Events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Events_ReviewedByUserId",
                table: "Events",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_UploadUserId",
                table: "Events",
                column: "UploadUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_AspNetUsers_ReviewedByUserId",
                table: "Events",
                column: "ReviewedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_AspNetUsers_UploadUserId",
                table: "Events",
                column: "UploadUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_AspNetUsers_ReviewedByUserId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_AspNetUsers_UploadUserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_ReviewedByUserId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_UploadUserId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "UploadUserId",
                table: "Events");
        }
    }
}
