using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EventLauscherApi.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: true),
                    Data = table.Column<byte[]>(type: "bytea", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadUserId = table.Column<int>(type: "integer", nullable: false),
                    SourcePlattform = table.Column<string>(type: "text", nullable: false),
                    SourceURL = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaFiles");
        }
    }
}
