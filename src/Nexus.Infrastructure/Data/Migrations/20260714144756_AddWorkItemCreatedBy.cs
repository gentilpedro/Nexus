using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "WorkItems",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_CreatedByUserId",
                table: "WorkItems",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_AspNetUsers_CreatedByUserId",
                table: "WorkItems",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_AspNetUsers_CreatedByUserId",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_CreatedByUserId",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "WorkItems");
        }
    }
}
