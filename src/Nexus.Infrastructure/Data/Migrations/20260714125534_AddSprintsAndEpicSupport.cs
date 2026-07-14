using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSprintsAndEpicSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SprintId",
                table: "WorkItems",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Sprints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskListId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Goal = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    StartDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sprints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sprints_TaskLists_TaskListId",
                        column: x => x.TaskListId,
                        principalTable: "TaskLists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SprintBurndownSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SprintId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SnapshotDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalCount = table.Column<int>(type: "int", nullable: false),
                    RemainingCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SprintBurndownSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SprintBurndownSnapshots_Sprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_SprintId",
                table: "WorkItems",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_SprintBurndownSnapshots_SprintId_SnapshotDateUtc",
                table: "SprintBurndownSnapshots",
                columns: new[] { "SprintId", "SnapshotDateUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sprints_TaskListId",
                table: "Sprints",
                column: "TaskListId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_Sprints_SprintId",
                table: "WorkItems",
                column: "SprintId",
                principalTable: "Sprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_Sprints_SprintId",
                table: "WorkItems");

            migrationBuilder.DropTable(
                name: "SprintBurndownSnapshots");

            migrationBuilder.DropTable(
                name: "Sprints");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_SprintId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "SprintId",
                table: "WorkItems");
        }
    }
}
