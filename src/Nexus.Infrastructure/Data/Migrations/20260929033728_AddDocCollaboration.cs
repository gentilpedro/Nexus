using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDocCollaboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeltaJson",
                table: "DocPages",
                type: "character varying(4000000)",
                maxLength: 4000000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "HtmlRevision",
                table: "DocPages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "Revision",
                table: "DocPages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "DocOperations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DocPageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientSeq = table.Column<long>(type: "bigint", nullable: false),
                    ChangeJson = table.Column<string>(type: "character varying(524288)", maxLength: 524288, nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocOperations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocOperations_DocPages_DocPageId",
                        column: x => x.DocPageId,
                        principalTable: "DocPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocOperations_DocPageId_ClientId_ClientSeq",
                table: "DocOperations",
                columns: new[] { "DocPageId", "ClientId", "ClientSeq" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocOperations_DocPageId_Revision",
                table: "DocOperations",
                columns: new[] { "DocPageId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocOperations_UserId",
                table: "DocOperations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocOperations");

            migrationBuilder.DropColumn(
                name: "DeltaJson",
                table: "DocPages");

            migrationBuilder.DropColumn(
                name: "HtmlRevision",
                table: "DocPages");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "DocPages");
        }
    }
}
