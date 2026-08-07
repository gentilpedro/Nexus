using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeDueDateIndexPartial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItems_DueDateUtc",
                table: "WorkItems");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_DueDateUtc",
                table: "WorkItems",
                column: "DueDateUtc",
                filter: "\"DueDateUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItems_DueDateUtc",
                table: "WorkItems");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_DueDateUtc",
                table: "WorkItems",
                column: "DueDateUtc");
        }
    }
}
