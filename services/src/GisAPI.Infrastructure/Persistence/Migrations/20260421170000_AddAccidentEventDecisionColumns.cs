using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the decision-workflow columns to <c>accident_events</c>:
///
/// <list type="bullet">
///   <item><description><c>status</c> — <c>'pending'</c> by default, becomes
///     <c>'confirmed'</c> or <c>'dismissed'</c> once a company admin clicks
///     through the accident decision modal.</description></item>
///   <item><description><c>decided_by_user_id</c> / <c>decided_at</c> — audit
///     trail of who took the decision and when.</description></item>
///   <item><description><c>tow_detected_at</c> — stamped by
///     <c>AccidentTowMonitoringService</c> when it detects 3 consecutive
///     post-accident frames at &gt; 5 km/h on a confirmed event.</description></item>
/// </list>
///
/// Idempotent (<c>ADD COLUMN IF NOT EXISTS</c>) so production can be rolled
/// over without ad-hoc SQL fixes — the table was already bootstrapped by
/// <c>20260421160000_AddAccidentEventsTable</c> and any prod row will keep
/// its default <c>'pending'</c> status.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260421170000_AddAccidentEventDecisionColumns")]
public partial class AddAccidentEventDecisionColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE accident_events
    ADD COLUMN IF NOT EXISTS status              VARCHAR(30) NOT NULL DEFAULT 'pending',
    ADD COLUMN IF NOT EXISTS decided_by_user_id  INTEGER NULL,
    ADD COLUMN IF NOT EXISTS decided_at          TIMESTAMP WITH TIME ZONE NULL,
    ADD COLUMN IF NOT EXISTS tow_detected_at     TIMESTAMP WITH TIME ZONE NULL;
");

        migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS ix_accident_events_status
    ON accident_events (status);
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_accident_events_status;");
        migrationBuilder.Sql(@"
ALTER TABLE accident_events
    DROP COLUMN IF EXISTS tow_detected_at,
    DROP COLUMN IF EXISTS decided_at,
    DROP COLUMN IF EXISTS decided_by_user_id,
    DROP COLUMN IF EXISTS status;
");
    }
}
