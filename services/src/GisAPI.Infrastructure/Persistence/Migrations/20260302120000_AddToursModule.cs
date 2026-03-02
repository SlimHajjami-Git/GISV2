using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace GisAPI.Infrastructure.Persistence.Migrations;

public partial class AddToursModule : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── Tours table ──
        migrationBuilder.CreateTable(
            name: "tours",
            columns: table => new
            {
                id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                company_id = table.Column<int>(nullable: false),
                name = table.Column<string>(maxLength: 200, nullable: false),
                description = table.Column<string>(maxLength: 500, nullable: true),
                vehicle_id = table.Column<int>(nullable: false),
                driver_id = table.Column<int>(nullable: true),
                status = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "planned"),
                scheduled_start_time = table.Column<DateTime>(nullable: false),
                scheduled_end_time = table.Column<DateTime>(nullable: true),
                actual_start_time = table.Column<DateTime>(nullable: true),
                actual_end_time = table.Column<DateTime>(nullable: true),
                estimated_distance_km = table.Column<decimal>(precision: 10, scale: 2, nullable: false, defaultValue: 0m),
                estimated_duration_minutes = table.Column<int>(nullable: false, defaultValue: 0),
                estimated_fuel_liters = table.Column<decimal>(precision: 10, scale: 2, nullable: true),
                estimated_route_polyline = table.Column<string>(nullable: true),
                actual_distance_km = table.Column<decimal>(precision: 10, scale: 2, nullable: true),
                actual_duration_minutes = table.Column<int>(nullable: true),
                actual_fuel_liters = table.Column<decimal>(precision: 10, scale: 2, nullable: true),
                actual_route_polyline = table.Column<string>(nullable: true),
                total_pause_minutes = table.Column<int>(nullable: false, defaultValue: 0),
                recurrence = table.Column<string>(maxLength: 100, nullable: false, defaultValue: "none"),
                notes = table.Column<string>(maxLength: 1000, nullable: true),
                created_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tours", x => x.id);
                table.ForeignKey("FK_tours_societes_company_id", x => x.company_id, "societes", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_tours_vehicles_vehicle_id", x => x.vehicle_id, "vehicles", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_tours_users_driver_id", x => x.driver_id, "users", "id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex("IX_tours_company_id", "tours", "company_id");
        migrationBuilder.CreateIndex("IX_tours_vehicle_id", "tours", "vehicle_id");
        migrationBuilder.CreateIndex("IX_tours_driver_id", "tours", "driver_id");
        migrationBuilder.CreateIndex("IX_tours_status", "tours", "status");
        migrationBuilder.CreateIndex("IX_tours_scheduled_start_time", "tours", "scheduled_start_time");

        // ── Tour Waypoints table ──
        migrationBuilder.CreateTable(
            name: "tour_waypoints",
            columns: table => new
            {
                id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                tour_id = table.Column<int>(nullable: false),
                sequence_order = table.Column<int>(nullable: false),
                name = table.Column<string>(maxLength: 200, nullable: true),
                address = table.Column<string>(maxLength: 500, nullable: true),
                latitude = table.Column<double>(nullable: false),
                longitude = table.Column<double>(nullable: false),
                type = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "waypoint"),
                geofence_id = table.Column<int>(nullable: true),
                estimated_leg_minutes = table.Column<int>(nullable: false, defaultValue: 0),
                deadline_margin_minutes = table.Column<int>(nullable: false, defaultValue: 60),
                estimated_arrival_time = table.Column<DateTime>(nullable: true),
                actual_arrival_time = table.Column<DateTime>(nullable: true),
                planned_pause_minutes = table.Column<int>(nullable: false, defaultValue: 0),
                actual_pause_minutes = table.Column<int>(nullable: true),
                is_completed = table.Column<bool>(nullable: false, defaultValue: false),
                waypoint_status = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "pending"),
                created_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tour_waypoints", x => x.id);
                table.ForeignKey("FK_tour_waypoints_tours_tour_id", x => x.tour_id, "tours", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_tour_waypoints_geofences_geofence_id", x => x.geofence_id, "geofences", "id", onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex("IX_tour_waypoints_tour_id", "tour_waypoints", "tour_id");
        migrationBuilder.CreateIndex("IX_tour_waypoints_geofence_id", "tour_waypoints", "geofence_id");

        // ── Tour Pauses table ──
        migrationBuilder.CreateTable(
            name: "tour_pauses",
            columns: table => new
            {
                id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                tour_id = table.Column<int>(nullable: false),
                start_time = table.Column<DateTime>(nullable: false),
                end_time = table.Column<DateTime>(nullable: true),
                duration_minutes = table.Column<int>(nullable: true),
                latitude = table.Column<double>(nullable: true),
                longitude = table.Column<double>(nullable: true),
                reason = table.Column<string>(maxLength: 50, nullable: false, defaultValue: "break"),
                notes = table.Column<string>(maxLength: 500, nullable: true),
                created_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tour_pauses", x => x.id);
                table.ForeignKey("FK_tour_pauses_tours_tour_id", x => x.tour_id, "tours", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_tour_pauses_tour_id", "tour_pauses", "tour_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("tour_pauses");
        migrationBuilder.DropTable("tour_waypoints");
        migrationBuilder.DropTable("tours");
    }
}
