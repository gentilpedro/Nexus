using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastWorkItemNumber",
                table: "Workspaces",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "WorkItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Number the existing tasks 1..n per workspace in creation order, then point each
            // workspace's counter at its highest number so new tasks continue the sequence.
            migrationBuilder.Sql("""
                UPDATE "WorkItems" w SET "Number" = n.rn
                FROM (
                    SELECT wi."Id", row_number() OVER (PARTITION BY s."WorkspaceId" ORDER BY wi."CreatedAtUtc", wi."Id") AS rn
                    FROM "WorkItems" wi
                    JOIN "TaskLists" l ON l."Id" = wi."TaskListId"
                    JOIN "Spaces" s ON s."Id" = l."SpaceId"
                ) n
                WHERE w."Id" = n."Id";

                UPDATE "Workspaces" ws SET "LastWorkItemNumber" = m.max_number
                FROM (
                    SELECT s."WorkspaceId", max(wi."Number") AS max_number
                    FROM "WorkItems" wi
                    JOIN "TaskLists" l ON l."Id" = wi."TaskListId"
                    JOIN "Spaces" s ON s."Id" = l."SpaceId"
                    GROUP BY s."WorkspaceId"
                ) m
                WHERE ws."Id" = m."WorkspaceId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastWorkItemNumber",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "WorkItems");
        }
    }
}
