using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatDocReferenceAndAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReferencedDocPageId",
                table: "ChatMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatMessageAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessageAttachments_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ReferencedDocPageId",
                table: "ChatMessages",
                column: "ReferencedDocPageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageAttachments_ChatMessageId",
                table: "ChatMessageAttachments",
                column: "ChatMessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_DocPages_ReferencedDocPageId",
                table: "ChatMessages",
                column: "ReferencedDocPageId",
                principalTable: "DocPages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_DocPages_ReferencedDocPageId",
                table: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatMessageAttachments");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ReferencedDocPageId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ReferencedDocPageId",
                table: "ChatMessages");
        }
    }
}
