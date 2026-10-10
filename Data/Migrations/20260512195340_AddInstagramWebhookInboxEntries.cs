using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EventLauscherApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramWebhookInboxEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instagram_webhook_inbox_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Received"),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instagram_webhook_inbox_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instagram_webhook_inbox_entries_ReceivedAt",
                table: "instagram_webhook_inbox_entries",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_instagram_webhook_inbox_entries_Status",
                table: "instagram_webhook_inbox_entries",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instagram_webhook_inbox_entries");
        }
    }
}
