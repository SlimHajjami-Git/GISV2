using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFreeMaintenanceBenefits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // vehicle_maintenance_schedules: 5 columns for free maintenance benefits
            migrationBuilder.AddColumn<int>(
                name: "free_uses_total",
                table: "vehicle_maintenance_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "free_uses_remaining",
                table: "vehicle_maintenance_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "free_source",
                table: "vehicle_maintenance_schedules",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "free_expiry_date",
                table: "vehicle_maintenance_schedules",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "free_notes",
                table: "vehicle_maintenance_schedules",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // maintenance_logs: flag to remember a log was a free use
            migrationBuilder.AddColumn<bool>(
                name: "was_free",
                table: "maintenance_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "free_uses_total", table: "vehicle_maintenance_schedules");
            migrationBuilder.DropColumn(name: "free_uses_remaining", table: "vehicle_maintenance_schedules");
            migrationBuilder.DropColumn(name: "free_source", table: "vehicle_maintenance_schedules");
            migrationBuilder.DropColumn(name: "free_expiry_date", table: "vehicle_maintenance_schedules");
            migrationBuilder.DropColumn(name: "free_notes", table: "vehicle_maintenance_schedules");
            migrationBuilder.DropColumn(name: "was_free", table: "maintenance_logs");
        }
    }
}
