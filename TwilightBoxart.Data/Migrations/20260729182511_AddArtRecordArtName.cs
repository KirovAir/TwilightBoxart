using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TwilightBoxart.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArtRecordArtName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArtName",
                table: "ArtRecord",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtName",
                table: "ArtRecord");
        }
    }
}
