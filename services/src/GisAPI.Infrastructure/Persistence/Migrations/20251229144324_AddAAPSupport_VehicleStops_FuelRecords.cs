using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAAPSupport_VehicleStops_FuelRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_gps_positions_recorded_at",
                table: "gps_positions",
                newName: "ix_gps_positions_time");

            migrationBuilder.AddColumn<decimal>(
                name: "FuelRateLPer100Km",
                table: "gps_positions",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "MemsX",
                table: "gps_positions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "MemsY",
                table: "gps_positions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "MemsZ",
                table: "gps_positions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OdometerKm",
                table: "gps_positions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "ProtocolVersion",
                table: "gps_positions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "Rpm",
                table: "gps_positions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "SendFlag",
                table: "gps_positions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "TemperatureC",
                table: "gps_positions",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "fuel_records",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<int>(type: "integer", nullable: true),
                    DeviceId = table.Column<int>(type: "integer", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FuelPercent = table.Column<short>(type: "smallint", nullable: false),
                    FuelLiters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TankCapacityLiters = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ConsumptionRateLPer100Km = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    AverageConsumptionLPer100Km = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    OdometerKm = table.Column<long>(type: "bigint", nullable: true),
                    SpeedKph = table.Column<double>(type: "double precision", nullable: true),
                    Rpm = table.Column<short>(type: "smallint", nullable: true),
                    IgnitionOn = table.Column<bool>(type: "boolean", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    FuelChange = table.Column<short>(type: "smallint", nullable: true),
                    RefuelAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    RefuelCost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    RefuelStation = table.Column<string>(type: "text", nullable: true),
                    IsAnomaly = table.Column<bool>(type: "boolean", nullable: false),
                    AnomalyReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fuel_records_employees_DriverId",
                        column: x => x.DriverId,
                        principalTable: "employees",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_fuel_records_gps_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "gps_devices",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_fuel_records_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_stops",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<int>(type: "integer", nullable: true),
                    DeviceId = table.Column<int>(type: "integer", nullable: true),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    StopType = table.Column<string>(type: "text", nullable: false),
                    IgnitionOff = table.Column<bool>(type: "boolean", nullable: false),
                    IsAuthorized = table.Column<bool>(type: "boolean", nullable: false),
                    StartMileage = table.Column<int>(type: "integer", nullable: true),
                    EndMileage = table.Column<int>(type: "integer", nullable: true),
                    FuelLevelStart = table.Column<int>(type: "integer", nullable: true),
                    FuelLevelEnd = table.Column<int>(type: "integer", nullable: true),
                    GeofenceId = table.Column<int>(type: "integer", nullable: true),
                    InsideGeofence = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_stops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicle_stops_employees_DriverId",
                        column: x => x.DriverId,
                        principalTable: "employees",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_vehicle_stops_geofences_GeofenceId",
                        column: x => x.GeofenceId,
                        principalTable: "geofences",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_vehicle_stops_gps_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "gps_devices",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_vehicle_stops_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_gps_positions_device_time",
                table: "gps_positions",
                columns: new[] { "device_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_records_DeviceId",
                table: "fuel_records",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_records_DriverId",
                table: "fuel_records",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_records_vehicle_time",
                table: "fuel_records",
                columns: new[] { "VehicleId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_stops_DeviceId",
                table: "vehicle_stops",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_stops_DriverId",
                table: "vehicle_stops",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_stops_GeofenceId",
                table: "vehicle_stops",
                column: "GeofenceId");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_stops_vehicle_time",
                table: "vehicle_stops",
                columns: new[] { "VehicleId", "StartTime" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fuel_records");

            migrationBuilder.DropTable(
                name: "vehicle_stops");

            migrationBuilder.DropIndex(
                name: "ix_gps_positions_device_time",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "FuelRateLPer100Km",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "MemsX",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "MemsY",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "MemsZ",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "OdometerKm",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "ProtocolVersion",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "Rpm",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "SendFlag",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "TemperatureC",
                table: "gps_positions");

            migrationBuilder.RenameIndex(
                name: "ix_gps_positions_time",
                table: "gps_positions",
                newName: "IX_gps_positions_recorded_at");
        }
    }
}
