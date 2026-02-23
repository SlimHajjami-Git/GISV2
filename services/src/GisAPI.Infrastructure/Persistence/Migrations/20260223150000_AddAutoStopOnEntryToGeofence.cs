using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

public partial class AddAutoStopOnEntryToGeofence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE geofences ADD COLUMN IF NOT EXISTS auto_stop_on_entry boolean NOT NULL DEFAULT false;
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            ALTER TABLE geofences DROP COLUMN IF EXISTS auto_stop_on_entry;
        ");
    }
}
