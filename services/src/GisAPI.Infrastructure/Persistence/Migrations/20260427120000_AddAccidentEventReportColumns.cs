using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Calypso 6 (P9) — accident report enrichment. Adds two columns to
/// <c>accident_events</c> so the workflow can capture:
///
/// <list type="bullet">
///   <item><description><c>pdf_report_url</c> — the auto-generated or
///     manually-uploaded PDF report path (served via the static
///     <c>/uploads</c> endpoint registered in <c>Program.cs</c>).</description></item>
///   <item><description><c>damages_json</c> — free-form damages capture
///     filled by the admin after confirmation: description, severity,
///     estimated cost, claim number, internal notes, manual tow date.
///     Stored as JSONB to avoid a column explosion for evolving fields.</description></item>
/// </list>
///
/// Status now also accepts the value <c>"awaiting_details"</c> — there is
/// no DB-level constraint on the column so no schema change is needed for
/// that, the application layer is the single source of truth.
///
/// Idempotent (<c>ADD COLUMN IF NOT EXISTS</c>) so prod is safe to roll
/// over — existing rows simply have NULL for both new columns.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260427120000_AddAccidentEventReportColumns")]
public partial class AddAccidentEventReportColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE accident_events
    ADD COLUMN IF NOT EXISTS pdf_report_url  VARCHAR(500) NULL,
    ADD COLUMN IF NOT EXISTS damages_json    JSONB NULL;
");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE accident_events
    DROP COLUMN IF EXISTS damages_json,
    DROP COLUMN IF EXISTS pdf_report_url;
");
    }
}
