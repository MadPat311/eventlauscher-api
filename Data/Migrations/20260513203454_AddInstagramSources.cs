using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EventLauscherApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instagram_sources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WebhookInboxEntryId = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    InstagramEntryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MediaId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FirstCommentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastCommentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MentionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MentionCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "PendingFetch"),
                    RawValueJson = table.Column<string>(type: "text", nullable: true),
                    Username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CaptionOrText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Permalink = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    MediaType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    MediaUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawApiJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastMentionAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instagram_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_instagram_sources_instagram_webhook_inbox_entries_WebhookIn~",
                        column: x => x.WebhookInboxEntryId,
                        principalTable: "instagram_webhook_inbox_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instagram_sources_MediaId",
                table: "instagram_sources",
                column: "MediaId");

            migrationBuilder.CreateIndex(
                name: "IX_instagram_sources_SourceKey",
                table: "instagram_sources",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instagram_sources_Status",
                table: "instagram_sources",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_instagram_sources_WebhookInboxEntryId",
                table: "instagram_sources",
                column: "WebhookInboxEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instagram_sources");
        }
    }
}
