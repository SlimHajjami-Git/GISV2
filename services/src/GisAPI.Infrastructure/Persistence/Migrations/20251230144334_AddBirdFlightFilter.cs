using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBirdFlightFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_employees_DriverId",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_gps_devices_DeviceId",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_vehicles_VehicleId",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_employees_DriverId",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_geofences_GeofenceId",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_gps_devices_DeviceId",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_vehicles_VehicleId",
                table: "vehicle_stops");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "vehicle_stops",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Longitude",
                table: "vehicle_stops",
                newName: "longitude");

            migrationBuilder.RenameColumn(
                name: "Latitude",
                table: "vehicle_stops",
                newName: "latitude");

            migrationBuilder.RenameColumn(
                name: "Address",
                table: "vehicle_stops",
                newName: "address");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "vehicle_stops",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "VehicleId",
                table: "vehicle_stops",
                newName: "vehicle_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "vehicle_stops",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "StopType",
                table: "vehicle_stops",
                newName: "stop_type");

            migrationBuilder.RenameColumn(
                name: "StartTime",
                table: "vehicle_stops",
                newName: "start_time");

            migrationBuilder.RenameColumn(
                name: "StartMileage",
                table: "vehicle_stops",
                newName: "start_mileage");

            migrationBuilder.RenameColumn(
                name: "IsAuthorized",
                table: "vehicle_stops",
                newName: "is_authorized");

            migrationBuilder.RenameColumn(
                name: "InsideGeofence",
                table: "vehicle_stops",
                newName: "inside_geofence");

            migrationBuilder.RenameColumn(
                name: "IgnitionOff",
                table: "vehicle_stops",
                newName: "ignition_off");

            migrationBuilder.RenameColumn(
                name: "GeofenceId",
                table: "vehicle_stops",
                newName: "geofence_id");

            migrationBuilder.RenameColumn(
                name: "FuelLevelStart",
                table: "vehicle_stops",
                newName: "fuel_level_start");

            migrationBuilder.RenameColumn(
                name: "FuelLevelEnd",
                table: "vehicle_stops",
                newName: "fuel_level_end");

            migrationBuilder.RenameColumn(
                name: "EndTime",
                table: "vehicle_stops",
                newName: "end_time");

            migrationBuilder.RenameColumn(
                name: "EndMileage",
                table: "vehicle_stops",
                newName: "end_mileage");

            migrationBuilder.RenameColumn(
                name: "DurationSeconds",
                table: "vehicle_stops",
                newName: "duration_seconds");

            migrationBuilder.RenameColumn(
                name: "DriverId",
                table: "vehicle_stops",
                newName: "driver_id");

            migrationBuilder.RenameColumn(
                name: "DeviceId",
                table: "vehicle_stops",
                newName: "device_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "vehicle_stops",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "vehicle_stops",
                newName: "company_id");

            migrationBuilder.RenameIndex(
                name: "IX_vehicle_stops_GeofenceId",
                table: "vehicle_stops",
                newName: "IX_vehicle_stops_geofence_id");

            migrationBuilder.RenameIndex(
                name: "IX_vehicle_stops_DriverId",
                table: "vehicle_stops",
                newName: "IX_vehicle_stops_driver_id");

            migrationBuilder.RenameIndex(
                name: "IX_vehicle_stops_DeviceId",
                table: "vehicle_stops",
                newName: "IX_vehicle_stops_device_id");

            migrationBuilder.RenameColumn(
                name: "Rpm",
                table: "gps_positions",
                newName: "rpm");

            migrationBuilder.RenameColumn(
                name: "TemperatureC",
                table: "gps_positions",
                newName: "temperature_c");

            migrationBuilder.RenameColumn(
                name: "SendFlag",
                table: "gps_positions",
                newName: "send_flag");

            migrationBuilder.RenameColumn(
                name: "ProtocolVersion",
                table: "gps_positions",
                newName: "protocol_version");

            migrationBuilder.RenameColumn(
                name: "OdometerKm",
                table: "gps_positions",
                newName: "odometer_km");

            migrationBuilder.RenameColumn(
                name: "MemsZ",
                table: "gps_positions",
                newName: "mems_z");

            migrationBuilder.RenameColumn(
                name: "MemsY",
                table: "gps_positions",
                newName: "mems_y");

            migrationBuilder.RenameColumn(
                name: "MemsX",
                table: "gps_positions",
                newName: "mems_x");

            migrationBuilder.RenameColumn(
                name: "FuelRateLPer100Km",
                table: "gps_positions",
                newName: "fuel_rate_l_per_100km");

            migrationBuilder.RenameColumn(
                name: "Rpm",
                table: "fuel_records",
                newName: "rpm");

            migrationBuilder.RenameColumn(
                name: "Longitude",
                table: "fuel_records",
                newName: "longitude");

            migrationBuilder.RenameColumn(
                name: "Latitude",
                table: "fuel_records",
                newName: "latitude");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "fuel_records",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "VehicleId",
                table: "fuel_records",
                newName: "vehicle_id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "fuel_records",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "TankCapacityLiters",
                table: "fuel_records",
                newName: "tank_capacity_liters");

            migrationBuilder.RenameColumn(
                name: "SpeedKph",
                table: "fuel_records",
                newName: "speed_kph");

            migrationBuilder.RenameColumn(
                name: "RefuelStation",
                table: "fuel_records",
                newName: "refuel_station");

            migrationBuilder.RenameColumn(
                name: "RefuelCost",
                table: "fuel_records",
                newName: "refuel_cost");

            migrationBuilder.RenameColumn(
                name: "RefuelAmount",
                table: "fuel_records",
                newName: "refuel_amount");

            migrationBuilder.RenameColumn(
                name: "RecordedAt",
                table: "fuel_records",
                newName: "recorded_at");

            migrationBuilder.RenameColumn(
                name: "OdometerKm",
                table: "fuel_records",
                newName: "odometer_km");

            migrationBuilder.RenameColumn(
                name: "IsAnomaly",
                table: "fuel_records",
                newName: "is_anomaly");

            migrationBuilder.RenameColumn(
                name: "IgnitionOn",
                table: "fuel_records",
                newName: "ignition_on");

            migrationBuilder.RenameColumn(
                name: "FuelPercent",
                table: "fuel_records",
                newName: "fuel_percent");

            migrationBuilder.RenameColumn(
                name: "FuelLiters",
                table: "fuel_records",
                newName: "fuel_liters");

            migrationBuilder.RenameColumn(
                name: "FuelChange",
                table: "fuel_records",
                newName: "fuel_change");

            migrationBuilder.RenameColumn(
                name: "EventType",
                table: "fuel_records",
                newName: "event_type");

            migrationBuilder.RenameColumn(
                name: "DriverId",
                table: "fuel_records",
                newName: "driver_id");

            migrationBuilder.RenameColumn(
                name: "DeviceId",
                table: "fuel_records",
                newName: "device_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "fuel_records",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "ConsumptionRateLPer100Km",
                table: "fuel_records",
                newName: "consumption_rate_l_per_100km");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "fuel_records",
                newName: "company_id");

            migrationBuilder.RenameColumn(
                name: "AverageConsumptionLPer100Km",
                table: "fuel_records",
                newName: "average_consumption_l_per_100km");

            migrationBuilder.RenameColumn(
                name: "AnomalyReason",
                table: "fuel_records",
                newName: "anomaly_reason");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_records_DriverId",
                table: "fuel_records",
                newName: "IX_fuel_records_driver_id");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_records_DeviceId",
                table: "fuel_records",
                newName: "IX_fuel_records_device_id");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "vehicle_stops",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "vehicle_stops",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "stop_type",
                table: "vehicle_stops",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "bird_flight_reason",
                table: "gps_positions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "implicit_speed_kph",
                table: "gps_positions",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_bird_flight",
                table: "gps_positions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "refuel_station",
                table: "fuel_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "event_type",
                table: "fuel_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "anomaly_reason",
                table: "fuel_records",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_stops_company_id",
                table: "vehicle_stops",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_records_company_id",
                table: "fuel_records",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_employees_driver_id",
                table: "fuel_records",
                column: "driver_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_gps_devices_device_id",
                table: "fuel_records",
                column: "device_id",
                principalTable: "gps_devices",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_vehicles_vehicle_id",
                table: "fuel_records",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_employees_driver_id",
                table: "vehicle_stops",
                column: "driver_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_geofences_geofence_id",
                table: "vehicle_stops",
                column: "geofence_id",
                principalTable: "geofences",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_gps_devices_device_id",
                table: "vehicle_stops",
                column: "device_id",
                principalTable: "gps_devices",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_vehicles_vehicle_id",
                table: "vehicle_stops",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_employees_driver_id",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_gps_devices_device_id",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_vehicles_vehicle_id",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_employees_driver_id",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_geofences_geofence_id",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_gps_devices_device_id",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_vehicles_vehicle_id",
                table: "vehicle_stops");

            migrationBuilder.DropIndex(
                name: "IX_vehicle_stops_company_id",
                table: "vehicle_stops");

            migrationBuilder.DropIndex(
                name: "IX_fuel_records_company_id",
                table: "fuel_records");

            migrationBuilder.DropColumn(
                name: "bird_flight_reason",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "implicit_speed_kph",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "is_bird_flight",
                table: "gps_positions");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "vehicle_stops",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "longitude",
                table: "vehicle_stops",
                newName: "Longitude");

            migrationBuilder.RenameColumn(
                name: "latitude",
                table: "vehicle_stops",
                newName: "Latitude");

            migrationBuilder.RenameColumn(
                name: "address",
                table: "vehicle_stops",
                newName: "Address");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "vehicle_stops",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "vehicle_id",
                table: "vehicle_stops",
                newName: "VehicleId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "vehicle_stops",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "stop_type",
                table: "vehicle_stops",
                newName: "StopType");

            migrationBuilder.RenameColumn(
                name: "start_time",
                table: "vehicle_stops",
                newName: "StartTime");

            migrationBuilder.RenameColumn(
                name: "start_mileage",
                table: "vehicle_stops",
                newName: "StartMileage");

            migrationBuilder.RenameColumn(
                name: "is_authorized",
                table: "vehicle_stops",
                newName: "IsAuthorized");

            migrationBuilder.RenameColumn(
                name: "inside_geofence",
                table: "vehicle_stops",
                newName: "InsideGeofence");

            migrationBuilder.RenameColumn(
                name: "ignition_off",
                table: "vehicle_stops",
                newName: "IgnitionOff");

            migrationBuilder.RenameColumn(
                name: "geofence_id",
                table: "vehicle_stops",
                newName: "GeofenceId");

            migrationBuilder.RenameColumn(
                name: "fuel_level_start",
                table: "vehicle_stops",
                newName: "FuelLevelStart");

            migrationBuilder.RenameColumn(
                name: "fuel_level_end",
                table: "vehicle_stops",
                newName: "FuelLevelEnd");

            migrationBuilder.RenameColumn(
                name: "end_time",
                table: "vehicle_stops",
                newName: "EndTime");

            migrationBuilder.RenameColumn(
                name: "end_mileage",
                table: "vehicle_stops",
                newName: "EndMileage");

            migrationBuilder.RenameColumn(
                name: "duration_seconds",
                table: "vehicle_stops",
                newName: "DurationSeconds");

            migrationBuilder.RenameColumn(
                name: "driver_id",
                table: "vehicle_stops",
                newName: "DriverId");

            migrationBuilder.RenameColumn(
                name: "device_id",
                table: "vehicle_stops",
                newName: "DeviceId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "vehicle_stops",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "vehicle_stops",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_vehicle_stops_geofence_id",
                table: "vehicle_stops",
                newName: "IX_vehicle_stops_GeofenceId");

            migrationBuilder.RenameIndex(
                name: "IX_vehicle_stops_driver_id",
                table: "vehicle_stops",
                newName: "IX_vehicle_stops_DriverId");

            migrationBuilder.RenameIndex(
                name: "IX_vehicle_stops_device_id",
                table: "vehicle_stops",
                newName: "IX_vehicle_stops_DeviceId");

            migrationBuilder.RenameColumn(
                name: "rpm",
                table: "gps_positions",
                newName: "Rpm");

            migrationBuilder.RenameColumn(
                name: "temperature_c",
                table: "gps_positions",
                newName: "TemperatureC");

            migrationBuilder.RenameColumn(
                name: "send_flag",
                table: "gps_positions",
                newName: "SendFlag");

            migrationBuilder.RenameColumn(
                name: "protocol_version",
                table: "gps_positions",
                newName: "ProtocolVersion");

            migrationBuilder.RenameColumn(
                name: "odometer_km",
                table: "gps_positions",
                newName: "OdometerKm");

            migrationBuilder.RenameColumn(
                name: "mems_z",
                table: "gps_positions",
                newName: "MemsZ");

            migrationBuilder.RenameColumn(
                name: "mems_y",
                table: "gps_positions",
                newName: "MemsY");

            migrationBuilder.RenameColumn(
                name: "mems_x",
                table: "gps_positions",
                newName: "MemsX");

            migrationBuilder.RenameColumn(
                name: "fuel_rate_l_per_100km",
                table: "gps_positions",
                newName: "FuelRateLPer100Km");

            migrationBuilder.RenameColumn(
                name: "rpm",
                table: "fuel_records",
                newName: "Rpm");

            migrationBuilder.RenameColumn(
                name: "longitude",
                table: "fuel_records",
                newName: "Longitude");

            migrationBuilder.RenameColumn(
                name: "latitude",
                table: "fuel_records",
                newName: "Latitude");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "fuel_records",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "vehicle_id",
                table: "fuel_records",
                newName: "VehicleId");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "fuel_records",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "tank_capacity_liters",
                table: "fuel_records",
                newName: "TankCapacityLiters");

            migrationBuilder.RenameColumn(
                name: "speed_kph",
                table: "fuel_records",
                newName: "SpeedKph");

            migrationBuilder.RenameColumn(
                name: "refuel_station",
                table: "fuel_records",
                newName: "RefuelStation");

            migrationBuilder.RenameColumn(
                name: "refuel_cost",
                table: "fuel_records",
                newName: "RefuelCost");

            migrationBuilder.RenameColumn(
                name: "refuel_amount",
                table: "fuel_records",
                newName: "RefuelAmount");

            migrationBuilder.RenameColumn(
                name: "recorded_at",
                table: "fuel_records",
                newName: "RecordedAt");

            migrationBuilder.RenameColumn(
                name: "odometer_km",
                table: "fuel_records",
                newName: "OdometerKm");

            migrationBuilder.RenameColumn(
                name: "is_anomaly",
                table: "fuel_records",
                newName: "IsAnomaly");

            migrationBuilder.RenameColumn(
                name: "ignition_on",
                table: "fuel_records",
                newName: "IgnitionOn");

            migrationBuilder.RenameColumn(
                name: "fuel_percent",
                table: "fuel_records",
                newName: "FuelPercent");

            migrationBuilder.RenameColumn(
                name: "fuel_liters",
                table: "fuel_records",
                newName: "FuelLiters");

            migrationBuilder.RenameColumn(
                name: "fuel_change",
                table: "fuel_records",
                newName: "FuelChange");

            migrationBuilder.RenameColumn(
                name: "event_type",
                table: "fuel_records",
                newName: "EventType");

            migrationBuilder.RenameColumn(
                name: "driver_id",
                table: "fuel_records",
                newName: "DriverId");

            migrationBuilder.RenameColumn(
                name: "device_id",
                table: "fuel_records",
                newName: "DeviceId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "fuel_records",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "consumption_rate_l_per_100km",
                table: "fuel_records",
                newName: "ConsumptionRateLPer100Km");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "fuel_records",
                newName: "CompanyId");

            migrationBuilder.RenameColumn(
                name: "average_consumption_l_per_100km",
                table: "fuel_records",
                newName: "AverageConsumptionLPer100Km");

            migrationBuilder.RenameColumn(
                name: "anomaly_reason",
                table: "fuel_records",
                newName: "AnomalyReason");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_records_driver_id",
                table: "fuel_records",
                newName: "IX_fuel_records_DriverId");

            migrationBuilder.RenameIndex(
                name: "IX_fuel_records_device_id",
                table: "fuel_records",
                newName: "IX_fuel_records_DeviceId");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "vehicle_stops",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "vehicle_stops",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StopType",
                table: "vehicle_stops",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "RefuelStation",
                table: "fuel_records",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EventType",
                table: "fuel_records",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "AnomalyReason",
                table: "fuel_records",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_employees_DriverId",
                table: "fuel_records",
                column: "DriverId",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_gps_devices_DeviceId",
                table: "fuel_records",
                column: "DeviceId",
                principalTable: "gps_devices",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_vehicles_VehicleId",
                table: "fuel_records",
                column: "VehicleId",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_employees_DriverId",
                table: "vehicle_stops",
                column: "DriverId",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_geofences_GeofenceId",
                table: "vehicle_stops",
                column: "GeofenceId",
                principalTable: "geofences",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_gps_devices_DeviceId",
                table: "vehicle_stops",
                column: "DeviceId",
                principalTable: "gps_devices",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_vehicles_VehicleId",
                table: "vehicle_stops",
                column: "VehicleId",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
