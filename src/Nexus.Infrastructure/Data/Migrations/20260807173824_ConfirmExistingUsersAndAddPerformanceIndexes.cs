using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfirmExistingUsersAndAddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sign-in now requires a confirmed e-mail (SignIn.RequireConfirmedAccount = true in
            // InfrastructureServiceCollectionExtensions). Every account created before this point
            // was registered while confirmation was disabled, so none of them ever received a
            // confirmation e-mail and all have EmailConfirmed = false. Without this backfill,
            // enabling the setting locks out the entire existing user base on the next deploy.
            //
            // Scoped to rows that exist when the migration runs, which is exactly the pre-existing
            // population — accounts created afterwards go through the normal confirmation flow.
            migrationBuilder.Sql(@"UPDATE ""AspNetUsers"" SET ""EmailConfirmed"" = TRUE WHERE ""EmailConfirmed"" = FALSE;");

            // Plain CREATE INDEX takes an ACCESS EXCLUSIVE lock on the table for the duration of
            // the build. Both tables are small at current scale so this is sub-second; if either
            // grows large, rebuild these as CREATE INDEX CONCURRENTLY outside a migration
            // (concurrent builds cannot run inside the transaction EF wraps a migration in).
            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_DueDateUtc",
                table: "WorkItems",
                column: "DueDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_CreatedAtUtc",
                table: "Notifications",
                column: "CreatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItems_DueDateUtc",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_CreatedAtUtc",
                table: "Notifications");
        }
    }
}
