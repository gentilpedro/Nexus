using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarFeedToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CalendarFeedCreatedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalendarFeedTokenHash",
                table: "AspNetUsers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalendarFeedTokenProtected",
                table: "AspNetUsers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_CalendarFeedTokenHash",
                table: "AspNetUsers",
                column: "CalendarFeedTokenHash",
                unique: true,
                filter: "\"CalendarFeedTokenHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_CalendarFeedTokenHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CalendarFeedCreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CalendarFeedTokenHash",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CalendarFeedTokenProtected",
                table: "AspNetUsers");
        }
    }
}
