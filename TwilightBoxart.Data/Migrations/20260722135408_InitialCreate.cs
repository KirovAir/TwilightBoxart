using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwilightBoxart.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtRecord",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ConsoleType = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Serial = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    CanonicalName = table.Column<string>(type: "TEXT", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    RegionId = table.Column<string>(type: "TEXT", nullable: true),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: true, collation: "NOCASE"),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", nullable: true),
                    MissUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CacheEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CacheKey = table.Column<string>(type: "TEXT", nullable: false, collation: "NOCASE"),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    LastAccessUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HitCount = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceSha256 = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CacheEntry", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtRecord_ConsoleType_Key",
                table: "ArtRecord",
                columns: new[] { "ConsoleType", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntry_CacheKey",
                table: "CacheEntry",
                column: "CacheKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntry_Kind_CreatedDate",
                table: "CacheEntry",
                columns: new[] { "Kind", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntry_Kind_LastAccessUtc",
                table: "CacheEntry",
                columns: new[] { "Kind", "LastAccessUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CacheEntry_Kind_SizeBytes",
                table: "CacheEntry",
                columns: new[] { "Kind", "SizeBytes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtRecord");

            migrationBuilder.DropTable(
                name: "CacheEntry");
        }
    }
}
