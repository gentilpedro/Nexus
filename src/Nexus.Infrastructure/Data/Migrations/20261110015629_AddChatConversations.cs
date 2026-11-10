using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DirectKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatConversations_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChatConversations_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatConversationMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatConversationMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatConversationMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatConversationMembers_ChatConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "ChatConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversationMembers_ConversationId_UserId",
                table: "ChatConversationMembers",
                columns: new[] { "ConversationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversationMembers_UserId",
                table: "ChatConversationMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_CreatedByUserId",
                table: "ChatConversations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_WorkspaceId_DirectKey",
                table: "ChatConversations",
                columns: new[] { "WorkspaceId", "DirectKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatConversations_WorkspaceId_WorkspaceType",
                table: "ChatConversations",
                column: "WorkspaceId",
                unique: true,
                filter: "\"Type\" = 0");

            // Every message so far belongs to its workspace's one shared chat. Create that
            // conversation per workspace first, then point the messages at it; only after that
            // can the column become NOT NULL. Adding it NOT NULL with a Guid.Empty default
            // instead would leave every existing message orphaned behind a foreign key that
            // points nowhere.
            migrationBuilder.Sql("""
                INSERT INTO "ChatConversations" ("Id", "WorkspaceId", "Type", "CreatedAtUtc")
                SELECT gen_random_uuid(), w."Id", 0, w."CreatedAtUtc"
                FROM "Workspaces" w;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "ChatMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ChatMessages" m
                SET "ConversationId" = c."Id"
                FROM "ChatConversations" c
                WHERE c."WorkspaceId" = m."WorkspaceId" AND c."Type" = 0;
                """);

            // Everyone starts with the shared chat marked read as of now, so the unread badge
            // only ever counts messages posted after this deploy — not the whole history.
            migrationBuilder.Sql("""
                INSERT INTO "ChatConversationMembers" ("Id", "ConversationId", "UserId", "JoinedAtUtc", "LastReadAtUtc")
                SELECT gen_random_uuid(), c."Id", wm."UserId", wm."JoinedAtUtc", now()
                FROM "ChatConversations" c
                JOIN "WorkspaceMembers" wm ON wm."WorkspaceId" = c."WorkspaceId"
                WHERE c."Type" = 0;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ConversationId",
                table: "ChatMessages",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_WorkspaceId_CreatedAtUtc",
                table: "ChatMessages");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ConversationId_CreatedAtUtc",
                table: "ChatMessages",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_WorkspaceId",
                table: "ChatMessages",
                column: "WorkspaceId");

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "Notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ConversationId",
                table: "Notifications",
                column: "ConversationId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ChatConversations_ConversationId",
                table: "ChatMessages",
                column: "ConversationId",
                principalTable: "ChatConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_ChatConversations_ConversationId",
                table: "Notifications",
                column: "ConversationId",
                principalTable: "ChatConversations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_ChatConversations_ConversationId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_ChatConversations_ConversationId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "ChatConversationMembers");

            migrationBuilder.DropTable(
                name: "ChatConversations");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ConversationId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ConversationId_CreatedAtUtc",
                table: "ChatMessages");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_WorkspaceId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "ChatMessages");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_WorkspaceId_CreatedAtUtc",
                table: "ChatMessages",
                columns: new[] { "WorkspaceId", "CreatedAtUtc" });
        }
    }
}
