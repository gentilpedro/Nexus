using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceCheckins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

migrationBuilder.CreateTable(
                name: "WorkspaceCheckins",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeekStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Perception = table.Column<int>(type: "integer", nullable: false),
                    Finished = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    InProgress = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    NextSteps = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Observations = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    AuthorId = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceCheckins", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkspaceCheckins_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkspaceCheckins_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CalendarFeedTokenHash",
                table: "AspNetUsers",
                column: "CalendarFeedTokenHash",
                unique: true,
                filter: "\"CalendarFeedTokenHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceCheckins_AuthorId",
                table: "WorkspaceCheckins",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceCheckins_WorkspaceId_WeekStartUtc",
                table: "WorkspaceCheckins",
                columns: new[] { "WorkspaceId", "WeekStartUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceCheckins");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CalendarFeedTokenHash",
                table: "AspNetUsers");

}
    }
}
