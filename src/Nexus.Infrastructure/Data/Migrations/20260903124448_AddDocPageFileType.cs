using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocPageFileType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FileContentType",
                table: "DocPages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "DocPages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "DocPages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileStoragePath",
                table: "DocPages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FileContentType",
                table: "DocPages");

            migrationBuilder.DropColumn(
                name: "FileName",
                table: "DocPages");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "DocPages");

            migrationBuilder.DropColumn(
                name: "FileStoragePath",
                table: "DocPages");
        }
    }
}
