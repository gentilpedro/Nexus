using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Nexus.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContentLengthLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-flight check.
            //
            // These ALTER COLUMNs narrow `text` to `varchar(n)`. PostgreSQL does NOT truncate to
            // fit — it aborts with "value too long for type character varying(n)". Because Nexus
            // applies migrations on startup (APPLY_MIGRATIONS_ON_STARTUP), that abort means the
            // application fails to boot, and the operator sees only a generic Postgres error with
            // no indication of which table or how many rows are at fault.
            //
            // This raises a specific, actionable message instead. It deliberately does NOT
            // truncate anything: silently destroying a user's document to make a deploy succeed is
            // the worse outcome. If this fires, shorten or archive the offending rows and redeploy.
            migrationBuilder.Sql(@"
DO $$
DECLARE
    oversized_desc bigint;
    oversized_html bigint;
    oversized_grid bigint;
BEGIN
    SELECT COUNT(*) INTO oversized_desc FROM ""WorkItems"" WHERE length(""Description"") > 10000;
    SELECT COUNT(*) INTO oversized_html FROM ""DocPages""  WHERE length(""ContentHtml"") > 1000000;
    SELECT COUNT(*) INTO oversized_grid FROM ""DocPages""  WHERE length(""GridDataJson"") > 500000;

    IF oversized_desc > 0 OR oversized_html > 0 OR oversized_grid > 0 THEN
        RAISE EXCEPTION
            'Migration AddContentLengthLimits aborted: % WorkItems.Description, % DocPages.ContentHtml and % DocPages.GridDataJson row(s) exceed the new limits. Shorten or archive them, then redeploy. No data was modified.',
            oversized_desc, oversized_html, oversized_grid;
    END IF;
END $$;");

            // Locking note: each ALTER COLUMN below takes an ACCESS EXCLUSIVE lock on its table and
            // scans it to verify the new constraint. Sub-second at current volumes; on a large
            // table it blocks reads and writes for the duration, so prefer a low-traffic window.
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "WorkItems",
                type: "character varying(10000)",
                maxLength: 10000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GridDataJson",
                table: "DocPages",
                type: "character varying(500000)",
                maxLength: 500000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentHtml",
                table: "DocPages",
                type: "character varying(1000000)",
                maxLength: 1000000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "WorkItems",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10000)",
                oldMaxLength: 10000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "GridDataJson",
                table: "DocPages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500000)",
                oldMaxLength: 500000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentHtml",
                table: "DocPages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000000)",
                oldMaxLength: 1000000,
                oldNullable: true);
        }
    }
}
