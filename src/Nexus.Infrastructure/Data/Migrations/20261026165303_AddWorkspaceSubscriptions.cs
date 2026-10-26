using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspaceSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkspaceSubscriptions",
                columns: table => new
                {
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plan = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Seats = table.Column<int>(type: "integer", nullable: false),
                    CurrentPeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentPeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceSubscriptions", x => x.WorkspaceId);
                    table.ForeignKey(
                        name: "FK_WorkspaceSubscriptions_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Every workspace that already exists starts on Starter (Plan 0, Status Active 0),
            // with its current member count as seats. Without this, the plan model ships with
            // no rows and every reader falls back to the "missing subscription" path forever.
            migrationBuilder.Sql("""
                INSERT INTO "WorkspaceSubscriptions"
                    ("WorkspaceId", "Plan", "Status", "Seats", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT w."Id", 0, 0,
                       GREATEST(1, (SELECT COUNT(*) FROM "WorkspaceMembers" m WHERE m."WorkspaceId" = w."Id")),
                       now(), now()
                FROM "Workspaces" w;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceSubscriptions");
        }
    }
}
