using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusTransitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StatusTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToStatusId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatusTransitions_TaskStatusDefinitions_FromStatusId",
                        column: x => x.FromStatusId,
                        principalTable: "TaskStatusDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StatusTransitions_TaskStatusDefinitions_ToStatusId",
                        column: x => x.ToStatusId,
                        principalTable: "TaskStatusDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StatusTransitions_FromStatusId_ToStatusId",
                table: "StatusTransitions",
                columns: new[] { "FromStatusId", "ToStatusId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StatusTransitions_ToStatusId",
                table: "StatusTransitions",
                column: "ToStatusId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StatusTransitions");
        }
    }
}
