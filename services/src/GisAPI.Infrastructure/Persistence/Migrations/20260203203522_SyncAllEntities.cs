using System;
using System.Collections.Generic;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SyncAllEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_societes_CompanyId",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_driver_assignments_employees_DriverId",
                table: "driver_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_driver_scores_employees_DriverId",
                table: "driver_scores");

            migrationBuilder.DropForeignKey(
                name: "FK_driving_events_employees_DriverId",
                table: "driving_events");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_employees_driver_id",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_maintenance_records_societes_company_id",
                table: "maintenance_records");

            migrationBuilder.DropForeignKey(
                name: "FK_part_inventory_suppliers_SupplierId",
                table: "part_inventory");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_maintenance_records_MaintenanceRecordId",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_part_inventory_PartId",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_suppliers_SupplierId",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_users_CreatedByUserId",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_vehicles_VehicleId",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_points_of_interest_societes_CompanyId",
                table: "points_of_interest");

            migrationBuilder.DropForeignKey(
                name: "FK_report_schedules_societes_CompanyId",
                table: "report_schedules");

            migrationBuilder.DropForeignKey(
                name: "FK_reports_societes_CompanyId",
                table: "reports");

            migrationBuilder.DropForeignKey(
                name: "FK_reservations_employees_AssignedDriverId",
                table: "reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_societes_subscription_types_subscription_id",
                table: "societes");

            migrationBuilder.DropForeignKey(
                name: "FK_suppliers_societes_CompanyId",
                table: "suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_trips_employees_DriverId",
                table: "trips");

            migrationBuilder.DropForeignKey(
                name: "FK_user_vehicles_users_AssignedByUserId",
                table: "user_vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_vehicles_users_UserId",
                table: "user_vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_vehicles_vehicles_VehicleId",
                table: "user_vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_users_user_settings_user_settings_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_costs_societes_company_id",
                table: "vehicle_costs");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_employees_driver_id",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicles_employees_assigned_driver_id",
                table: "vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicles_employees_assigned_supervisor_id",
                table: "vehicles");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropIndex(
                name: "IX_users_user_settings_id",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_vehicles",
                table: "user_vehicles");

            migrationBuilder.DropIndex(
                name: "IX_societes_subscription_id",
                table: "societes");

            migrationBuilder.DropIndex(
                name: "IX_reports_CompanyId",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_report_schedules_CompanyId",
                table: "report_schedules");

            migrationBuilder.DropIndex(
                name: "IX_geofence_events_geofence_id",
                table: "geofence_events");

            migrationBuilder.DropIndex(
                name: "IX_geofence_events_vehicle_id",
                table: "geofence_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_CompanyId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "assigned_vehicle_ids",
                table: "users");

            migrationBuilder.DropColumn(
                name: "permissions",
                table: "users");

            migrationBuilder.DropColumn(
                name: "roles",
                table: "users");

            migrationBuilder.DropColumn(
                name: "user_settings_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "AccessLevel",
                table: "user_vehicles");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "features",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "price",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "subscription_id",
                table: "societes");

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
                name: "name",
                table: "users",
                newName: "last_name");

            migrationBuilder.RenameIndex(
                name: "IX_users_email",
                table: "users",
                newName: "idx_users_email");

            migrationBuilder.RenameIndex(
                name: "IX_users_company_id",
                table: "users",
                newName: "idx_users_company_id");

            migrationBuilder.RenameColumn(
                name: "AssignedAt",
                table: "user_vehicles",
                newName: "assigned_at");

            migrationBuilder.RenameColumn(
                name: "VehicleId",
                table: "user_vehicles",
                newName: "vehicle_id");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "user_vehicles",
                newName: "user_id");

            migrationBuilder.RenameColumn(
                name: "AssignedByUserId",
                table: "user_vehicles",
                newName: "assigned_by");

            migrationBuilder.RenameIndex(
                name: "IX_user_vehicles_VehicleId",
                table: "user_vehicles",
                newName: "idx_user_vehicles_vehicle_id");

            migrationBuilder.RenameIndex(
                name: "IX_user_vehicles_AssignedByUserId",
                table: "user_vehicles",
                newName: "IX_user_vehicles_assigned_by");

            migrationBuilder.RenameColumn(
                name: "MaxUsers",
                table: "subscription_types",
                newName: "max_users");

            migrationBuilder.RenameColumn(
                name: "MaxGpsDevices",
                table: "subscription_types",
                newName: "max_gps_devices");

            migrationBuilder.RenameColumn(
                name: "MaxGeofences",
                table: "subscription_types",
                newName: "max_geofences");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "subscription_types",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "type",
                table: "subscription_types",
                newName: "target_company_type");

            migrationBuilder.RenameColumn(
                name: "RC",
                table: "societes",
                newName: "rc");

            migrationBuilder.RenameColumn(
                name: "IF",
                table: "societes",
                newName: "if");

            migrationBuilder.RenameColumn(
                name: "TaxId",
                table: "societes",
                newName: "tax_id");

            migrationBuilder.RenameColumn(
                name: "SubscriptionExpiresAt",
                table: "societes",
                newName: "subscription_expires_at");

            migrationBuilder.RenameColumn(
                name: "LogoUrl",
                table: "societes",
                newName: "logo_url");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "societes",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "Website",
                table: "points_of_interest",
                newName: "website");

            migrationBuilder.RenameColumn(
                name: "Phone",
                table: "points_of_interest",
                newName: "phone");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "points_of_interest",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Longitude",
                table: "points_of_interest",
                newName: "longitude");

            migrationBuilder.RenameColumn(
                name: "Latitude",
                table: "points_of_interest",
                newName: "latitude");

            migrationBuilder.RenameColumn(
                name: "Icon",
                table: "points_of_interest",
                newName: "icon");

            migrationBuilder.RenameColumn(
                name: "Email",
                table: "points_of_interest",
                newName: "email");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "points_of_interest",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Color",
                table: "points_of_interest",
                newName: "color");

            migrationBuilder.RenameColumn(
                name: "City",
                table: "points_of_interest",
                newName: "city");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "points_of_interest",
                newName: "category");

            migrationBuilder.RenameColumn(
                name: "Address",
                table: "points_of_interest",
                newName: "address");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "points_of_interest",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "points_of_interest",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "SubCategory",
                table: "points_of_interest",
                newName: "sub_category");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "points_of_interest",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "HasGasoline",
                table: "points_of_interest",
                newName: "has_gasoline");

            migrationBuilder.RenameColumn(
                name: "HasElectricCharging",
                table: "points_of_interest",
                newName: "has_electric_charging");

            migrationBuilder.RenameColumn(
                name: "HasDiesel",
                table: "points_of_interest",
                newName: "has_diesel");

            migrationBuilder.RenameColumn(
                name: "FuelBrand",
                table: "points_of_interest",
                newName: "fuel_brand");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "points_of_interest",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "points_of_interest",
                newName: "company_id");

            migrationBuilder.RenameIndex(
                name: "IX_points_of_interest_CompanyId",
                table: "points_of_interest",
                newName: "IX_points_of_interest_company_id");

            migrationBuilder.RenameColumn(
                name: "Type",
                table: "part_transactions",
                newName: "type");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "part_transactions",
                newName: "quantity");

            migrationBuilder.RenameColumn(
                name: "Notes",
                table: "part_transactions",
                newName: "notes");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "part_transactions",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "VehicleId",
                table: "part_transactions",
                newName: "vehicle_id");

            migrationBuilder.RenameColumn(
                name: "UnitCost",
                table: "part_transactions",
                newName: "unit_cost");

            migrationBuilder.RenameColumn(
                name: "TotalCost",
                table: "part_transactions",
                newName: "total_cost");

            migrationBuilder.RenameColumn(
                name: "SupplierId",
                table: "part_transactions",
                newName: "supplier_id");

            migrationBuilder.RenameColumn(
                name: "ReferenceNumber",
                table: "part_transactions",
                newName: "reference_number");

            migrationBuilder.RenameColumn(
                name: "QuantityBefore",
                table: "part_transactions",
                newName: "quantity_before");

            migrationBuilder.RenameColumn(
                name: "QuantityAfter",
                table: "part_transactions",
                newName: "quantity_after");

            migrationBuilder.RenameColumn(
                name: "PartId",
                table: "part_transactions",
                newName: "part_id");

            migrationBuilder.RenameColumn(
                name: "MaintenanceRecordId",
                table: "part_transactions",
                newName: "maintenance_record_id");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "part_transactions",
                newName: "created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "part_transactions",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "part_transactions",
                newName: "company_id");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_VehicleId",
                table: "part_transactions",
                newName: "IX_part_transactions_vehicle_id");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_SupplierId",
                table: "part_transactions",
                newName: "IX_part_transactions_supplier_id");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_PartId",
                table: "part_transactions",
                newName: "IX_part_transactions_part_id");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_MaintenanceRecordId",
                table: "part_transactions",
                newName: "IX_part_transactions_maintenance_record_id");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_CreatedByUserId",
                table: "part_transactions",
                newName: "IX_part_transactions_created_by_user_id");

            migrationBuilder.RenameColumn(
                name: "Unit",
                table: "part_inventory",
                newName: "unit");

            migrationBuilder.RenameColumn(
                name: "Name",
                table: "part_inventory",
                newName: "name");

            migrationBuilder.RenameColumn(
                name: "Location",
                table: "part_inventory",
                newName: "location");

            migrationBuilder.RenameColumn(
                name: "Description",
                table: "part_inventory",
                newName: "description");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "part_inventory",
                newName: "category");

            migrationBuilder.RenameColumn(
                name: "Brand",
                table: "part_inventory",
                newName: "brand");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "part_inventory",
                newName: "id");

            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "part_inventory",
                newName: "updated_at");

            migrationBuilder.RenameColumn(
                name: "UnitCost",
                table: "part_inventory",
                newName: "unit_cost");

            migrationBuilder.RenameColumn(
                name: "SupplierId",
                table: "part_inventory",
                newName: "supplier_id");

            migrationBuilder.RenameColumn(
                name: "SellingPrice",
                table: "part_inventory",
                newName: "selling_price");

            migrationBuilder.RenameColumn(
                name: "ReorderQuantity",
                table: "part_inventory",
                newName: "reorder_quantity");

            migrationBuilder.RenameColumn(
                name: "QuantityInStock",
                table: "part_inventory",
                newName: "quantity_in_stock");

            migrationBuilder.RenameColumn(
                name: "PartNumber",
                table: "part_inventory",
                newName: "part_number");

            migrationBuilder.RenameColumn(
                name: "MinimumStock",
                table: "part_inventory",
                newName: "minimum_stock");

            migrationBuilder.RenameColumn(
                name: "LastRestockDate",
                table: "part_inventory",
                newName: "last_restock_date");

            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "part_inventory",
                newName: "is_active");

            migrationBuilder.RenameColumn(
                name: "ExpiryDate",
                table: "part_inventory",
                newName: "expiry_date");

            migrationBuilder.RenameColumn(
                name: "CreatedAt",
                table: "part_inventory",
                newName: "created_at");

            migrationBuilder.RenameColumn(
                name: "CompatibleVehicles",
                table: "part_inventory",
                newName: "compatible_vehicles");

            migrationBuilder.RenameColumn(
                name: "CompanyId",
                table: "part_inventory",
                newName: "company_id");

            migrationBuilder.RenameIndex(
                name: "IX_part_inventory_SupplierId",
                table: "part_inventory",
                newName: "IX_part_inventory_supplier_id");

            migrationBuilder.RenameColumn(
                name: "Mat",
                table: "gps_devices",
                newName: "mat");

            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FuelType",
                table: "vehicles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InsuranceExpiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RegistrationExpiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeedLimit",
                table: "vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TaxExpiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TechnicalInspectionExpiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TransportPermitExpiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "fuel_tank_capacity",
                table: "vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "active",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(150)",
                oldMaxLength: 150);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "permit_number",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "role_id",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<DateTime>(
                name: "assigned_at",
                table: "user_vehicles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "user_vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AlterColumn<string>(
                name: "Website",
                table: "suppliers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "general",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "TaxId",
                table: "suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "Rating",
                table: "suppliers",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentTerms",
                table: "suppliers",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "net30",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "suppliers",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactName",
                table: "suppliers",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "suppliers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BankAccount",
                table: "suppliers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "suppliers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SocieteId",
                table: "suppliers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, object>>(
                name: "access_rights",
                table: "subscription_types",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "advanced_reports",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "api_access",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "code",
                table: "subscription_types",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "subscription_types",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "driving_behavior",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "fuel_analysis",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "history_playback",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "history_retention_days",
                table: "subscription_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "module_accidents",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_costs",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_dashboard",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_documents",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_employees",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_fleet_management",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_geofences",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_maintenance",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_monitoring",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_reports",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_settings",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_suppliers",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_users",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "module_vehicles",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "monthly_duration_days",
                table: "subscription_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_price",
                table: "subscription_types",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "quarterly_duration_days",
                table: "subscription_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "quarterly_price",
                table: "subscription_types",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "real_time_alerts",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_costs",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_daily",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_driving_behavior",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_fuel",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_maintenance",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_mileage",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_mileage_period",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_monthly",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_speed",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_speed_infraction",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_stops",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "report_trips",
                table: "subscription_types",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "subscription_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "updated_at",
                table: "subscription_types",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "yearly_duration_days",
                table: "subscription_types",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "yearly_price",
                table: "subscription_types",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "societes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "societes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "societes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "societes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "country",
                table: "societes",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "city",
                table: "societes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "societes",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_cycle",
                table: "societes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "societes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_payment_at",
                table: "societes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "next_payment_amount",
                table: "societes",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settings",
                table: "societes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "subscription_started_at",
                table: "societes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "subscription_status",
                table: "societes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "subscription_type_id",
                table: "societes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SocieteId",
                table: "reports",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SocieteId",
                table: "report_schedules",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "website",
                table: "points_of_interest",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "points_of_interest",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "points_of_interest",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "icon",
                table: "points_of_interest",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "points_of_interest",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "description",
                table: "points_of_interest",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "points_of_interest",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "city",
                table: "points_of_interest",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "points_of_interest",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "points_of_interest",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "sub_category",
                table: "points_of_interest",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "fuel_brand",
                table: "points_of_interest",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SocieteId",
                table: "points_of_interest",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "alert_on_arrival",
                table: "points_of_interest",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "alert_on_departure",
                table: "points_of_interest",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                table: "points_of_interest",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "expected_stay_minutes",
                table: "points_of_interest",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_id",
                table: "points_of_interest",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_visit_at",
                table: "points_of_interest",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notification_cooldown_minutes",
                table: "points_of_interest",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "radius",
                table: "points_of_interest",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<string[]>(
                name: "tags",
                table: "points_of_interest",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "visit_count",
                table: "points_of_interest",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "part_transactions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "reference_number",
                table: "part_transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "unit",
                table: "part_inventory",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "part_inventory",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "location",
                table: "part_inventory",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "category",
                table: "part_inventory",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "general",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "brand",
                table: "part_inventory",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "quantity_in_stock",
                table: "part_inventory",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "part_number",
                table: "part_inventory",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "minimum_stock",
                table: "part_inventory",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "is_active",
                table: "part_inventory",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<string>(
                name: "event_key",
                table: "gps_positions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "mat",
                table: "gps_devices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fuel_sensor_mode",
                table: "gps_devices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "raw_255");

            migrationBuilder.AddColumn<string[]>(
                name: "active_days",
                table: "geofences",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "active_end_time",
                table: "geofences",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "active_start_time",
                table: "geofences",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "group_id",
                table: "geofences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "icon_name",
                table: "geofences",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_stay_duration_minutes",
                table: "geofences",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "notification_cooldown_minutes",
                table: "geofences",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "geofence_events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "device_id",
                table: "geofence_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "duration_inside_seconds",
                table: "geofence_events",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_notified",
                table: "geofence_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "notified_at",
                table: "geofence_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SocieteId",
                table: "audit_logs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_vehicles",
                table: "user_vehicles",
                column: "id");

            migrationBuilder.CreateTable(
                name: "AccidentClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimNumber = table.Column<string>(type: "text", nullable: false),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<int>(type: "integer", nullable: true),
                    AccidentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccidentTime = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    WeatherConditions = table.Column<string>(type: "text", nullable: true),
                    RoadConditions = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    EstimatedDamage = table.Column<decimal>(type: "numeric", nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ThirdPartyInvolved = table.Column<bool>(type: "boolean", nullable: false),
                    PoliceReportNumber = table.Column<string>(type: "text", nullable: true),
                    MileageAtAccident = table.Column<int>(type: "integer", nullable: true),
                    DamagedZones = table.Column<string>(type: "text", nullable: true),
                    Witnesses = table.Column<string>(type: "text", nullable: true),
                    AdditionalNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccidentClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccidentClaims_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_AccidentClaims_users_DriverId",
                        column: x => x.DriverId,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_AccidentClaims_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "brands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LogoUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_departments_societes_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fuel_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "geofence_groups",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    icon_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_geofence_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_geofence_groups_societes_company_id",
                        column: x => x.company_id,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_alert_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    enable_push = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    enable_email = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    enable_sms = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    notify_driver = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notify_supervisor = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notify_fleet_manager = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    additional_emails = table.Column<string[]>(type: "text[]", nullable: false),
                    additional_phones = table.Column<string[]>(type: "text[]", nullable: false),
                    reminder_frequency_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 7),
                    max_reminders = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    quiet_hours_start = table.Column<TimeSpan>(type: "interval", nullable: true),
                    quiet_hours_end = table.Column<TimeSpan>(type: "interval", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_alert_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_templates",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "medium"),
                    interval_km = table.Column<int>(type: "integer", nullable: true),
                    interval_months = table.Column<int>(type: "integer", nullable: true),
                    estimated_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    warning_km = table.Column<int>(type: "integer", nullable: false, defaultValue: 1000),
                    warning_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    critical_km = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    critical_days = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    instructions = table.Column<string>(type: "text", nullable: true),
                    applies_to_vehicle_types = table.Column<string[]>(type: "text[]", nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "wrench"),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    company_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "part_categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_part_categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "poi_visits",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    poi_id = table.Column<int>(type: "integer", nullable: false),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    device_id = table.Column<int>(type: "integer", nullable: true),
                    arrival_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    departure_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    arrival_lat = table.Column<double>(type: "double precision", nullable: false),
                    arrival_lng = table.Column<double>(type: "double precision", nullable: false),
                    departure_lat = table.Column<double>(type: "double precision", nullable: true),
                    departure_lng = table.Column<double>(type: "double precision", nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_notified = table.Column<bool>(type: "boolean", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_poi_visits", x => x.id);
                    table.ForeignKey(
                        name: "FK_poi_visits_points_of_interest_poi_id",
                        column: x => x.poi_id,
                        principalTable: "points_of_interest",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_poi_visits_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repairs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    societe_id = table.Column<int>(type: "integer", nullable: false),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    supplier_id = table.Column<int>(type: "integer", nullable: true),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    repair_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    mileage_at_repair = table.Column<int>(type: "integer", nullable: true),
                    labor_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    parts_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repairs", x => x.id);
                    table.ForeignKey(
                        name: "FK_repairs_societes_societe_id",
                        column: x => x.societe_id,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_repairs_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    societe_id = table.Column<int>(type: "integer", nullable: true),
                    is_company_admin = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_system_role = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    permissions = table.Column<Dictionary<string, object>>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_roles", x => x.id);
                    table.ForeignKey(
                        name: "FK_roles_societes_societe_id",
                        column: x => x.societe_id,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "speed_limit_alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    SpeedLimit = table.Column<int>(type: "integer", nullable: false),
                    ActualSpeed = table.Column<int>(type: "integer", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedById = table.Column<int>(type: "integer", nullable: true),
                    SocieteId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_speed_limit_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_speed_limit_alerts_societes_SocieteId",
                        column: x => x.SocieteId,
                        principalTable: "societes",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_speed_limit_alerts_users_AcknowledgedById",
                        column: x => x.AcknowledgedById,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_speed_limit_alerts_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supplier_services",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_id = table.Column<int>(type: "integer", nullable: false),
                    service_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_services", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_services_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_user_assignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    assigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    unassigned_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_by = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_user_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicle_user_assignments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vehicle_user_assignments_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccidentClaimDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimId = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FileUrl = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<int>(type: "integer", nullable: true),
                    MimeType = table.Column<string>(type: "text", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccidentClaimDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccidentClaimDocuments_AccidentClaims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "AccidentClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccidentClaimThirdParties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    VehiclePlate = table.Column<string>(type: "text", nullable: true),
                    VehicleModel = table.Column<string>(type: "text", nullable: true),
                    InsuranceCompany = table.Column<string>(type: "text", nullable: true),
                    InsuranceNumber = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccidentClaimThirdParties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccidentClaimThirdParties_AccidentClaims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "AccidentClaims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_models",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BrandId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VehicleType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_models", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicle_models_brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fuel_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: true),
                    vehicle_plate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fuel_type_id = table.Column<int>(type: "integer", nullable: false),
                    volume = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    price_per_liter = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    invoice_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    station_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    driver_id = table.Column<int>(type: "integer", nullable: true),
                    odometer_km = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_fuel_entries_fuel_types_fuel_type_id",
                        column: x => x.fuel_type_id,
                        principalTable: "fuel_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fuel_entries_users_driver_id",
                        column: x => x.driver_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_fuel_entries_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "fuel_pricing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FuelTypeId = table.Column<int>(type: "integer", nullable: false),
                    PricePerLiter = table.Column<decimal>(type: "numeric", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_pricing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fuel_pricing_fuel_types_FuelTypeId",
                        column: x => x.FuelTypeId,
                        principalTable: "fuel_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fuel_pricing_societes_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_template_parts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    part_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    part_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "unit"),
                    estimated_unit_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    is_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    preferred_supplier_id = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_template_parts", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_template_parts_maintenance_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "maintenance_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_template_parts_suppliers_preferred_supplier_id",
                        column: x => x.preferred_supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_maintenance_schedules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    last_done_date = table.Column<DateTime>(type: "date", nullable: true),
                    last_done_km = table.Column<int>(type: "integer", nullable: true),
                    next_due_date = table.Column<DateTime>(type: "date", nullable: true),
                    next_due_km = table.Column<int>(type: "integer", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "upcoming"),
                    is_paused = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    paused_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paused_reason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    custom_interval_km = table.Column<int>(type: "integer", nullable: true),
                    custom_interval_months = table.Column<int>(type: "integer", nullable: true),
                    last_notification_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    notification_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_maintenance_schedules", x => x.id);
                    table.ForeignKey(
                        name: "FK_vehicle_maintenance_schedules_maintenance_templates_templat~",
                        column: x => x.template_id,
                        principalTable: "maintenance_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vehicle_maintenance_schedules_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "vehicle_parts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PartNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_parts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicle_parts_part_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "part_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "repair_parts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    repair_id = table.Column<int>(type: "integer", nullable: false),
                    part_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    part_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_parts", x => x.id);
                    table.ForeignKey(
                        name: "FK_repair_parts_repairs_repair_id",
                        column: x => x.repair_id,
                        principalTable: "repairs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_logs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    schedule_id = table.Column<int>(type: "integer", nullable: true),
                    cost_id = table.Column<int>(type: "integer", nullable: true),
                    done_date = table.Column<DateTime>(type: "date", nullable: false),
                    done_km = table.Column<int>(type: "integer", nullable: false),
                    actual_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    supplier_id = table.Column<int>(type: "integer", nullable: true),
                    technician_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    work_order_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    parts_replaced = table.Column<List<ReplacedPart>>(type: "jsonb", nullable: true),
                    labor_hours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    labor_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    parts_cost = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    quality_rating = table.Column<int>(type: "integer", nullable: true),
                    photos = table.Column<string[]>(type: "text[]", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_maintenance_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "maintenance_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_vehicle_costs_cost_id",
                        column: x => x.cost_id,
                        principalTable: "vehicle_costs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_vehicle_maintenance_schedules_schedule_id",
                        column: x => x.schedule_id,
                        principalTable: "vehicle_maintenance_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    schedule_id = table.Column<int>(type: "integer", nullable: false),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    template_id = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    notification_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    trigger_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    current_km = table.Column<int>(type: "integer", nullable: true),
                    km_remaining = table.Column<int>(type: "integer", nullable: true),
                    days_remaining = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    sent_channels = table.Column<string[]>(type: "text[]", nullable: false),
                    acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    acknowledged_by = table.Column<int>(type: "integer", nullable: true),
                    dismissed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_maintenance_notifications_maintenance_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "maintenance_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_notifications_vehicle_maintenance_schedules_sch~",
                        column: x => x.schedule_id,
                        principalTable: "vehicle_maintenance_schedules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_notifications_vehicles_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "part_pricing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PartId = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Supplier = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_part_pricing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_part_pricing_societes_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_part_pricing_vehicle_parts_PartId",
                        column: x => x.PartId,
                        principalTable: "vehicle_parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_DepartmentId",
                table: "vehicles",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "idx_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "idx_users_status",
                table: "users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "idx_user_vehicles_user_id",
                table: "user_vehicles",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_user_vehicle",
                table: "user_vehicles",
                columns: new[] { "user_id", "vehicle_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_Name",
                table: "suppliers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_suppliers_SocieteId",
                table: "suppliers",
                column: "SocieteId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_types_code",
                table: "subscription_types",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_societes_subscription_type_id",
                table: "societes",
                column: "subscription_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_SocieteId",
                table: "reports",
                column: "SocieteId");

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_SocieteId",
                table: "report_schedules",
                column: "SocieteId");

            migrationBuilder.CreateIndex(
                name: "IX_points_of_interest_category",
                table: "points_of_interest",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_points_of_interest_SocieteId",
                table: "points_of_interest",
                column: "SocieteId");

            migrationBuilder.CreateIndex(
                name: "IX_part_transactions_company_id",
                table: "part_transactions",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_part_inventory_company_id",
                table: "part_inventory",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_part_inventory_part_number",
                table: "part_inventory",
                column: "part_number");

            migrationBuilder.CreateIndex(
                name: "ux_gps_positions_event_key",
                table: "gps_positions",
                column: "event_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gps_devices_mat",
                table: "gps_devices",
                column: "mat");

            migrationBuilder.CreateIndex(
                name: "IX_geofences_group_id",
                table: "geofences",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_geofence_events_geofence_id_timestamp",
                table: "geofence_events",
                columns: new[] { "geofence_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_geofence_events_vehicle_id_timestamp",
                table: "geofence_events",
                columns: new[] { "vehicle_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_SocieteId",
                table: "audit_logs",
                column: "SocieteId");

            migrationBuilder.CreateIndex(
                name: "IX_AccidentClaimDocuments_ClaimId",
                table: "AccidentClaimDocuments",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_AccidentClaims_CreatedByUserId",
                table: "AccidentClaims",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AccidentClaims_DriverId",
                table: "AccidentClaims",
                column: "DriverId");

            migrationBuilder.CreateIndex(
                name: "IX_AccidentClaims_VehicleId",
                table: "AccidentClaims",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_AccidentClaimThirdParties_ClaimId",
                table: "AccidentClaimThirdParties",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_departments_CompanyId",
                table: "departments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "ix_fuel_entries_company_date",
                table: "fuel_entries",
                columns: new[] { "company_id", "invoice_date" });

            migrationBuilder.CreateIndex(
                name: "ix_fuel_entries_company_plate",
                table: "fuel_entries",
                columns: new[] { "company_id", "vehicle_plate" });

            migrationBuilder.CreateIndex(
                name: "IX_fuel_entries_driver_id",
                table: "fuel_entries",
                column: "driver_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_entries_fuel_type_id",
                table: "fuel_entries",
                column: "fuel_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_entries_vehicle_id",
                table: "fuel_entries",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_pricing_CompanyId",
                table: "fuel_pricing",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_pricing_FuelTypeId",
                table: "fuel_pricing",
                column: "FuelTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_geofence_groups_company_id",
                table: "geofence_groups",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_maint_alert_settings_company",
                table: "maintenance_alert_settings",
                column: "company_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_logs_cost_id",
                table: "maintenance_logs",
                column: "cost_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_logs_schedule_id",
                table: "maintenance_logs",
                column: "schedule_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_logs_supplier_id",
                table: "maintenance_logs",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_logs_TemplateId",
                table: "maintenance_logs",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_logs_VehicleId",
                table: "maintenance_logs",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "idx_maint_notif_company",
                table: "maintenance_notifications",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "idx_maint_notif_type",
                table: "maintenance_notifications",
                column: "notification_type");

            migrationBuilder.CreateIndex(
                name: "idx_maint_notif_vehicle",
                table: "maintenance_notifications",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "ix_maint_notif_unique_daily",
                table: "maintenance_notifications",
                columns: new[] { "schedule_id", "notification_type", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_notifications_template_id",
                table: "maintenance_notifications",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "idx_template_parts_template_id",
                table: "maintenance_template_parts",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_template_parts_preferred_supplier_id",
                table: "maintenance_template_parts",
                column: "preferred_supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_templates_CompanyId",
                table: "maintenance_templates",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_part_pricing_CompanyId_PartId",
                table: "part_pricing",
                columns: new[] { "CompanyId", "PartId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_part_pricing_PartId",
                table: "part_pricing",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_poi_visits_company_id",
                table: "poi_visits",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_poi_visits_poi_id_arrival_at",
                table: "poi_visits",
                columns: new[] { "poi_id", "arrival_at" });

            migrationBuilder.CreateIndex(
                name: "IX_poi_visits_vehicle_id_arrival_at",
                table: "poi_visits",
                columns: new[] { "vehicle_id", "arrival_at" });

            migrationBuilder.CreateIndex(
                name: "IX_repair_parts_repair_id",
                table: "repair_parts",
                column: "repair_id");

            migrationBuilder.CreateIndex(
                name: "IX_repairs_societe_id",
                table: "repairs",
                column: "societe_id");

            migrationBuilder.CreateIndex(
                name: "IX_repairs_vehicle_id",
                table: "repairs",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "idx_roles_societe_id",
                table: "roles",
                column: "societe_id");

            migrationBuilder.CreateIndex(
                name: "IX_speed_limit_alerts_AcknowledgedById",
                table: "speed_limit_alerts",
                column: "AcknowledgedById");

            migrationBuilder.CreateIndex(
                name: "IX_speed_limit_alerts_SocieteId",
                table: "speed_limit_alerts",
                column: "SocieteId");

            migrationBuilder.CreateIndex(
                name: "IX_speed_limit_alerts_VehicleId",
                table: "speed_limit_alerts",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_services_supplier_id",
                table: "supplier_services",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_Status",
                table: "vehicle_maintenance_schedules",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_TemplateId",
                table: "vehicle_maintenance_schedules",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_VehicleId",
                table: "vehicle_maintenance_schedules",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_VehicleId_TemplateId",
                table: "vehicle_maintenance_schedules",
                columns: new[] { "vehicle_id", "template_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_models_BrandId",
                table: "vehicle_models",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_parts_CategoryId",
                table: "vehicle_parts",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_user_assignments_user_id",
                table: "vehicle_user_assignments",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_user_assignments_vehicle_id",
                table: "vehicle_user_assignments",
                column: "vehicle_id");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_societes_SocieteId",
                table: "audit_logs",
                column: "SocieteId",
                principalTable: "societes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_driver_assignments_users_DriverId",
                table: "driver_assignments",
                column: "DriverId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_driver_scores_users_DriverId",
                table: "driver_scores",
                column: "DriverId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_driving_events_users_DriverId",
                table: "driving_events",
                column: "DriverId",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_users_driver_id",
                table: "fuel_records",
                column: "driver_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_geofences_geofence_groups_group_id",
                table: "geofences",
                column: "group_id",
                principalTable: "geofence_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_part_inventory_suppliers_supplier_id",
                table: "part_inventory",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_maintenance_records_maintenance_record_id",
                table: "part_transactions",
                column: "maintenance_record_id",
                principalTable: "maintenance_records",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_part_inventory_part_id",
                table: "part_transactions",
                column: "part_id",
                principalTable: "part_inventory",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_suppliers_supplier_id",
                table: "part_transactions",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_users_created_by_user_id",
                table: "part_transactions",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_vehicles_vehicle_id",
                table: "part_transactions",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_points_of_interest_societes_SocieteId",
                table: "points_of_interest",
                column: "SocieteId",
                principalTable: "societes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_points_of_interest_societes_company_id",
                table: "points_of_interest",
                column: "company_id",
                principalTable: "societes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_report_schedules_societes_SocieteId",
                table: "report_schedules",
                column: "SocieteId",
                principalTable: "societes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_reports_societes_SocieteId",
                table: "reports",
                column: "SocieteId",
                principalTable: "societes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_users_AssignedDriverId",
                table: "reservations",
                column: "AssignedDriverId",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_societes_subscription_types_subscription_type_id",
                table: "societes",
                column: "subscription_type_id",
                principalTable: "subscription_types",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_suppliers_societes_SocieteId",
                table: "suppliers",
                column: "SocieteId",
                principalTable: "societes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_trips_users_DriverId",
                table: "trips",
                column: "DriverId",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_user_vehicles_users_assigned_by",
                table: "user_vehicles",
                column: "assigned_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_user_vehicles_users_user_id",
                table: "user_vehicles",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_vehicles_vehicles_vehicle_id",
                table: "user_vehicles",
                column: "vehicle_id",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_users_roles_role_id",
                table: "users",
                column: "role_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_users_driver_id",
                table: "vehicle_stops",
                column: "driver_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicles_departments_DepartmentId",
                table: "vehicles",
                column: "DepartmentId",
                principalTable: "departments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicles_users_assigned_driver_id",
                table: "vehicles",
                column: "assigned_driver_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_vehicles_users_assigned_supervisor_id",
                table: "vehicles",
                column: "assigned_supervisor_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_logs_societes_SocieteId",
                table: "audit_logs");

            migrationBuilder.DropForeignKey(
                name: "FK_driver_assignments_users_DriverId",
                table: "driver_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_driver_scores_users_DriverId",
                table: "driver_scores");

            migrationBuilder.DropForeignKey(
                name: "FK_driving_events_users_DriverId",
                table: "driving_events");

            migrationBuilder.DropForeignKey(
                name: "FK_fuel_records_users_driver_id",
                table: "fuel_records");

            migrationBuilder.DropForeignKey(
                name: "FK_geofences_geofence_groups_group_id",
                table: "geofences");

            migrationBuilder.DropForeignKey(
                name: "FK_part_inventory_suppliers_supplier_id",
                table: "part_inventory");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_maintenance_records_maintenance_record_id",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_part_inventory_part_id",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_suppliers_supplier_id",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_users_created_by_user_id",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_part_transactions_vehicles_vehicle_id",
                table: "part_transactions");

            migrationBuilder.DropForeignKey(
                name: "FK_points_of_interest_societes_SocieteId",
                table: "points_of_interest");

            migrationBuilder.DropForeignKey(
                name: "FK_points_of_interest_societes_company_id",
                table: "points_of_interest");

            migrationBuilder.DropForeignKey(
                name: "FK_report_schedules_societes_SocieteId",
                table: "report_schedules");

            migrationBuilder.DropForeignKey(
                name: "FK_reports_societes_SocieteId",
                table: "reports");

            migrationBuilder.DropForeignKey(
                name: "FK_reservations_users_AssignedDriverId",
                table: "reservations");

            migrationBuilder.DropForeignKey(
                name: "FK_societes_subscription_types_subscription_type_id",
                table: "societes");

            migrationBuilder.DropForeignKey(
                name: "FK_suppliers_societes_SocieteId",
                table: "suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_trips_users_DriverId",
                table: "trips");

            migrationBuilder.DropForeignKey(
                name: "FK_user_vehicles_users_assigned_by",
                table: "user_vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_vehicles_users_user_id",
                table: "user_vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_user_vehicles_vehicles_vehicle_id",
                table: "user_vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_users_roles_role_id",
                table: "users");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicle_stops_users_driver_id",
                table: "vehicle_stops");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicles_departments_DepartmentId",
                table: "vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicles_users_assigned_driver_id",
                table: "vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_vehicles_users_assigned_supervisor_id",
                table: "vehicles");

            migrationBuilder.DropTable(
                name: "AccidentClaimDocuments");

            migrationBuilder.DropTable(
                name: "AccidentClaimThirdParties");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "fuel_entries");

            migrationBuilder.DropTable(
                name: "fuel_pricing");

            migrationBuilder.DropTable(
                name: "geofence_groups");

            migrationBuilder.DropTable(
                name: "maintenance_alert_settings");

            migrationBuilder.DropTable(
                name: "maintenance_logs");

            migrationBuilder.DropTable(
                name: "maintenance_notifications");

            migrationBuilder.DropTable(
                name: "maintenance_template_parts");

            migrationBuilder.DropTable(
                name: "part_pricing");

            migrationBuilder.DropTable(
                name: "poi_visits");

            migrationBuilder.DropTable(
                name: "repair_parts");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropTable(
                name: "speed_limit_alerts");

            migrationBuilder.DropTable(
                name: "supplier_services");

            migrationBuilder.DropTable(
                name: "vehicle_models");

            migrationBuilder.DropTable(
                name: "vehicle_user_assignments");

            migrationBuilder.DropTable(
                name: "AccidentClaims");

            migrationBuilder.DropTable(
                name: "fuel_types");

            migrationBuilder.DropTable(
                name: "vehicle_maintenance_schedules");

            migrationBuilder.DropTable(
                name: "vehicle_parts");

            migrationBuilder.DropTable(
                name: "repairs");

            migrationBuilder.DropTable(
                name: "brands");

            migrationBuilder.DropTable(
                name: "maintenance_templates");

            migrationBuilder.DropTable(
                name: "part_categories");

            migrationBuilder.DropIndex(
                name: "IX_vehicles_DepartmentId",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "idx_users_role_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "idx_users_status",
                table: "users");

            migrationBuilder.DropPrimaryKey(
                name: "PK_user_vehicles",
                table: "user_vehicles");

            migrationBuilder.DropIndex(
                name: "idx_user_vehicles_user_id",
                table: "user_vehicles");

            migrationBuilder.DropIndex(
                name: "uq_user_vehicle",
                table: "user_vehicles");

            migrationBuilder.DropIndex(
                name: "IX_suppliers_Name",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "IX_suppliers_SocieteId",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "IX_subscription_types_code",
                table: "subscription_types");

            migrationBuilder.DropIndex(
                name: "IX_societes_subscription_type_id",
                table: "societes");

            migrationBuilder.DropIndex(
                name: "IX_reports_SocieteId",
                table: "reports");

            migrationBuilder.DropIndex(
                name: "IX_report_schedules_SocieteId",
                table: "report_schedules");

            migrationBuilder.DropIndex(
                name: "IX_points_of_interest_category",
                table: "points_of_interest");

            migrationBuilder.DropIndex(
                name: "IX_points_of_interest_SocieteId",
                table: "points_of_interest");

            migrationBuilder.DropIndex(
                name: "IX_part_transactions_company_id",
                table: "part_transactions");

            migrationBuilder.DropIndex(
                name: "IX_part_inventory_company_id",
                table: "part_inventory");

            migrationBuilder.DropIndex(
                name: "IX_part_inventory_part_number",
                table: "part_inventory");

            migrationBuilder.DropIndex(
                name: "ux_gps_positions_event_key",
                table: "gps_positions");

            migrationBuilder.DropIndex(
                name: "IX_gps_devices_mat",
                table: "gps_devices");

            migrationBuilder.DropIndex(
                name: "IX_geofences_group_id",
                table: "geofences");

            migrationBuilder.DropIndex(
                name: "IX_geofence_events_geofence_id_timestamp",
                table: "geofence_events");

            migrationBuilder.DropIndex(
                name: "IX_geofence_events_vehicle_id_timestamp",
                table: "geofence_events");

            migrationBuilder.DropIndex(
                name: "IX_audit_logs_SocieteId",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "FuelType",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "InsuranceExpiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "RegistrationExpiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "SpeedLimit",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "TaxExpiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "TechnicalInspectionExpiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "TransportPermitExpiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "fuel_tank_capacity",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "first_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "permit_number",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "id",
                table: "user_vehicles");

            migrationBuilder.DropColumn(
                name: "SocieteId",
                table: "suppliers");

            migrationBuilder.DropColumn(
                name: "access_rights",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "advanced_reports",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "api_access",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "code",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "description",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "driving_behavior",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "fuel_analysis",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "history_playback",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "history_retention_days",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_accidents",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_costs",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_dashboard",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_documents",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_employees",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_fleet_management",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_geofences",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_maintenance",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_monitoring",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_reports",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_settings",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_suppliers",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_users",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "module_vehicles",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "monthly_duration_days",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "monthly_price",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "quarterly_duration_days",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "quarterly_price",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "real_time_alerts",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_costs",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_daily",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_driving_behavior",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_fuel",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_maintenance",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_mileage",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_mileage_period",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_monthly",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_speed",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_speed_infraction",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_stops",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "report_trips",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "yearly_duration_days",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "yearly_price",
                table: "subscription_types");

            migrationBuilder.DropColumn(
                name: "billing_cycle",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "description",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "last_payment_at",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "next_payment_amount",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "settings",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "subscription_started_at",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "subscription_status",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "subscription_type_id",
                table: "societes");

            migrationBuilder.DropColumn(
                name: "SocieteId",
                table: "reports");

            migrationBuilder.DropColumn(
                name: "SocieteId",
                table: "report_schedules");

            migrationBuilder.DropColumn(
                name: "SocieteId",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "alert_on_arrival",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "alert_on_departure",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "contact_name",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "expected_stay_minutes",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "external_id",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "last_visit_at",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "notification_cooldown_minutes",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "radius",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "tags",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "visit_count",
                table: "points_of_interest");

            migrationBuilder.DropColumn(
                name: "event_key",
                table: "gps_positions");

            migrationBuilder.DropColumn(
                name: "fuel_sensor_mode",
                table: "gps_devices");

            migrationBuilder.DropColumn(
                name: "active_days",
                table: "geofences");

            migrationBuilder.DropColumn(
                name: "active_end_time",
                table: "geofences");

            migrationBuilder.DropColumn(
                name: "active_start_time",
                table: "geofences");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "geofences");

            migrationBuilder.DropColumn(
                name: "icon_name",
                table: "geofences");

            migrationBuilder.DropColumn(
                name: "max_stay_duration_minutes",
                table: "geofences");

            migrationBuilder.DropColumn(
                name: "notification_cooldown_minutes",
                table: "geofences");

            migrationBuilder.DropColumn(
                name: "address",
                table: "geofence_events");

            migrationBuilder.DropColumn(
                name: "device_id",
                table: "geofence_events");

            migrationBuilder.DropColumn(
                name: "duration_inside_seconds",
                table: "geofence_events");

            migrationBuilder.DropColumn(
                name: "is_notified",
                table: "geofence_events");

            migrationBuilder.DropColumn(
                name: "notified_at",
                table: "geofence_events");

            migrationBuilder.DropColumn(
                name: "SocieteId",
                table: "audit_logs");

            migrationBuilder.RenameColumn(
                name: "last_name",
                table: "users",
                newName: "name");

            migrationBuilder.RenameIndex(
                name: "idx_users_email",
                table: "users",
                newName: "IX_users_email");

            migrationBuilder.RenameIndex(
                name: "idx_users_company_id",
                table: "users",
                newName: "IX_users_company_id");

            migrationBuilder.RenameColumn(
                name: "vehicle_id",
                table: "user_vehicles",
                newName: "VehicleId");

            migrationBuilder.RenameColumn(
                name: "user_id",
                table: "user_vehicles",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "assigned_at",
                table: "user_vehicles",
                newName: "AssignedAt");

            migrationBuilder.RenameColumn(
                name: "assigned_by",
                table: "user_vehicles",
                newName: "AssignedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_user_vehicles_assigned_by",
                table: "user_vehicles",
                newName: "IX_user_vehicles_AssignedByUserId");

            migrationBuilder.RenameIndex(
                name: "idx_user_vehicles_vehicle_id",
                table: "user_vehicles",
                newName: "IX_user_vehicles_VehicleId");

            migrationBuilder.RenameColumn(
                name: "max_users",
                table: "subscription_types",
                newName: "MaxUsers");

            migrationBuilder.RenameColumn(
                name: "max_gps_devices",
                table: "subscription_types",
                newName: "MaxGpsDevices");

            migrationBuilder.RenameColumn(
                name: "max_geofences",
                table: "subscription_types",
                newName: "MaxGeofences");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "subscription_types",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "target_company_type",
                table: "subscription_types",
                newName: "type");

            migrationBuilder.RenameColumn(
                name: "rc",
                table: "societes",
                newName: "RC");

            migrationBuilder.RenameColumn(
                name: "if",
                table: "societes",
                newName: "IF");

            migrationBuilder.RenameColumn(
                name: "tax_id",
                table: "societes",
                newName: "TaxId");

            migrationBuilder.RenameColumn(
                name: "subscription_expires_at",
                table: "societes",
                newName: "SubscriptionExpiresAt");

            migrationBuilder.RenameColumn(
                name: "logo_url",
                table: "societes",
                newName: "LogoUrl");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "societes",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "website",
                table: "points_of_interest",
                newName: "Website");

            migrationBuilder.RenameColumn(
                name: "phone",
                table: "points_of_interest",
                newName: "Phone");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "points_of_interest",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "longitude",
                table: "points_of_interest",
                newName: "Longitude");

            migrationBuilder.RenameColumn(
                name: "latitude",
                table: "points_of_interest",
                newName: "Latitude");

            migrationBuilder.RenameColumn(
                name: "icon",
                table: "points_of_interest",
                newName: "Icon");

            migrationBuilder.RenameColumn(
                name: "email",
                table: "points_of_interest",
                newName: "Email");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "points_of_interest",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "points_of_interest",
                newName: "Color");

            migrationBuilder.RenameColumn(
                name: "city",
                table: "points_of_interest",
                newName: "City");

            migrationBuilder.RenameColumn(
                name: "category",
                table: "points_of_interest",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "address",
                table: "points_of_interest",
                newName: "Address");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "points_of_interest",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "points_of_interest",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "sub_category",
                table: "points_of_interest",
                newName: "SubCategory");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "points_of_interest",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "has_gasoline",
                table: "points_of_interest",
                newName: "HasGasoline");

            migrationBuilder.RenameColumn(
                name: "has_electric_charging",
                table: "points_of_interest",
                newName: "HasElectricCharging");

            migrationBuilder.RenameColumn(
                name: "has_diesel",
                table: "points_of_interest",
                newName: "HasDiesel");

            migrationBuilder.RenameColumn(
                name: "fuel_brand",
                table: "points_of_interest",
                newName: "FuelBrand");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "points_of_interest",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "points_of_interest",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_points_of_interest_company_id",
                table: "points_of_interest",
                newName: "IX_points_of_interest_CompanyId");

            migrationBuilder.RenameColumn(
                name: "type",
                table: "part_transactions",
                newName: "Type");

            migrationBuilder.RenameColumn(
                name: "quantity",
                table: "part_transactions",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "notes",
                table: "part_transactions",
                newName: "Notes");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "part_transactions",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "vehicle_id",
                table: "part_transactions",
                newName: "VehicleId");

            migrationBuilder.RenameColumn(
                name: "unit_cost",
                table: "part_transactions",
                newName: "UnitCost");

            migrationBuilder.RenameColumn(
                name: "total_cost",
                table: "part_transactions",
                newName: "TotalCost");

            migrationBuilder.RenameColumn(
                name: "supplier_id",
                table: "part_transactions",
                newName: "SupplierId");

            migrationBuilder.RenameColumn(
                name: "reference_number",
                table: "part_transactions",
                newName: "ReferenceNumber");

            migrationBuilder.RenameColumn(
                name: "quantity_before",
                table: "part_transactions",
                newName: "QuantityBefore");

            migrationBuilder.RenameColumn(
                name: "quantity_after",
                table: "part_transactions",
                newName: "QuantityAfter");

            migrationBuilder.RenameColumn(
                name: "part_id",
                table: "part_transactions",
                newName: "PartId");

            migrationBuilder.RenameColumn(
                name: "maintenance_record_id",
                table: "part_transactions",
                newName: "MaintenanceRecordId");

            migrationBuilder.RenameColumn(
                name: "created_by_user_id",
                table: "part_transactions",
                newName: "CreatedByUserId");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "part_transactions",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "part_transactions",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_vehicle_id",
                table: "part_transactions",
                newName: "IX_part_transactions_VehicleId");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_supplier_id",
                table: "part_transactions",
                newName: "IX_part_transactions_SupplierId");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_part_id",
                table: "part_transactions",
                newName: "IX_part_transactions_PartId");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_maintenance_record_id",
                table: "part_transactions",
                newName: "IX_part_transactions_MaintenanceRecordId");

            migrationBuilder.RenameIndex(
                name: "IX_part_transactions_created_by_user_id",
                table: "part_transactions",
                newName: "IX_part_transactions_CreatedByUserId");

            migrationBuilder.RenameColumn(
                name: "unit",
                table: "part_inventory",
                newName: "Unit");

            migrationBuilder.RenameColumn(
                name: "name",
                table: "part_inventory",
                newName: "Name");

            migrationBuilder.RenameColumn(
                name: "location",
                table: "part_inventory",
                newName: "Location");

            migrationBuilder.RenameColumn(
                name: "description",
                table: "part_inventory",
                newName: "Description");

            migrationBuilder.RenameColumn(
                name: "category",
                table: "part_inventory",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "brand",
                table: "part_inventory",
                newName: "Brand");

            migrationBuilder.RenameColumn(
                name: "id",
                table: "part_inventory",
                newName: "Id");

            migrationBuilder.RenameColumn(
                name: "updated_at",
                table: "part_inventory",
                newName: "UpdatedAt");

            migrationBuilder.RenameColumn(
                name: "unit_cost",
                table: "part_inventory",
                newName: "UnitCost");

            migrationBuilder.RenameColumn(
                name: "supplier_id",
                table: "part_inventory",
                newName: "SupplierId");

            migrationBuilder.RenameColumn(
                name: "selling_price",
                table: "part_inventory",
                newName: "SellingPrice");

            migrationBuilder.RenameColumn(
                name: "reorder_quantity",
                table: "part_inventory",
                newName: "ReorderQuantity");

            migrationBuilder.RenameColumn(
                name: "quantity_in_stock",
                table: "part_inventory",
                newName: "QuantityInStock");

            migrationBuilder.RenameColumn(
                name: "part_number",
                table: "part_inventory",
                newName: "PartNumber");

            migrationBuilder.RenameColumn(
                name: "minimum_stock",
                table: "part_inventory",
                newName: "MinimumStock");

            migrationBuilder.RenameColumn(
                name: "last_restock_date",
                table: "part_inventory",
                newName: "LastRestockDate");

            migrationBuilder.RenameColumn(
                name: "is_active",
                table: "part_inventory",
                newName: "IsActive");

            migrationBuilder.RenameColumn(
                name: "expiry_date",
                table: "part_inventory",
                newName: "ExpiryDate");

            migrationBuilder.RenameColumn(
                name: "created_at",
                table: "part_inventory",
                newName: "CreatedAt");

            migrationBuilder.RenameColumn(
                name: "compatible_vehicles",
                table: "part_inventory",
                newName: "CompatibleVehicles");

            migrationBuilder.RenameColumn(
                name: "company_id",
                table: "part_inventory",
                newName: "CompanyId");

            migrationBuilder.RenameIndex(
                name: "IX_part_inventory_supplier_id",
                table: "part_inventory",
                newName: "IX_part_inventory_SupplierId");

            migrationBuilder.RenameColumn(
                name: "mat",
                table: "gps_devices",
                newName: "Mat");

            migrationBuilder.AlterColumn<DateTime>(
                name: "updated_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AlterColumn<string>(
                name: "status",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "active");

            migrationBuilder.AlterColumn<string>(
                name: "password_hash",
                table: "users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "users",
                type: "character varying(150)",
                maxLength: 150,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AddColumn<int[]>(
                name: "assigned_vehicle_ids",
                table: "users",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "permissions",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "roles",
                table: "users",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<int>(
                name: "user_settings_id",
                table: "users",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "AssignedAt",
                table: "user_vehicles",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW()");

            migrationBuilder.AddColumn<string>(
                name: "AccessLevel",
                table: "user_vehicles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "Website",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "suppliers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "general");

            migrationBuilder.AlterColumn<string>(
                name: "TaxId",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Rating",
                table: "suppliers",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(3,1)",
                oldPrecision: 3,
                oldScale: 1,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PaymentTerms",
                table: "suppliers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "net30");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "suppliers",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "suppliers",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContactName",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BankAccount",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "suppliers",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "subscription_types",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string[]>(
                name: "features",
                table: "subscription_types",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<decimal>(
                name: "price",
                table: "subscription_types",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "type",
                table: "societes",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "phone",
                table: "societes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "name",
                table: "societes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "email",
                table: "societes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "country",
                table: "societes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "city",
                table: "societes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "address",
                table: "societes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "subscription_id",
                table: "societes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Website",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Phone",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "points_of_interest",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Icon",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "points_of_interest",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "City",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "points_of_interest",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SubCategory",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FuelBrand",
                table: "points_of_interest",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "part_transactions",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "part_transactions",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Unit",
                table: "part_inventory",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "part_inventory",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Location",
                table: "part_inventory",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "part_inventory",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValue: "general");

            migrationBuilder.AlterColumn<string>(
                name: "Brand",
                table: "part_inventory",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "QuantityInStock",
                table: "part_inventory",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "PartNumber",
                table: "part_inventory",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<int>(
                name: "MinimumStock",
                table: "part_inventory",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "part_inventory",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

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
                name: "Mat",
                table: "gps_devices",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_user_vehicles",
                table: "user_vehicles",
                columns: new[] { "UserId", "VehicleId" });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    settings = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    hire_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    license_expiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    license_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.id);
                    table.ForeignKey(
                        name: "FK_employees_societes_company_id",
                        column: x => x.company_id,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_users_user_settings_id",
                table: "users",
                column: "user_settings_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_societes_subscription_id",
                table: "societes",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_reports_CompanyId",
                table: "reports",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_report_schedules_CompanyId",
                table: "report_schedules",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_geofence_events_geofence_id",
                table: "geofence_events",
                column: "geofence_id");

            migrationBuilder.CreateIndex(
                name: "IX_geofence_events_vehicle_id",
                table: "geofence_events",
                column: "vehicle_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_CompanyId",
                table: "audit_logs",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_employees_company_id",
                table: "employees",
                column: "company_id");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_logs_societes_CompanyId",
                table: "audit_logs",
                column: "CompanyId",
                principalTable: "societes",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_driver_assignments_employees_DriverId",
                table: "driver_assignments",
                column: "DriverId",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_driver_scores_employees_DriverId",
                table: "driver_scores",
                column: "DriverId",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_driving_events_employees_DriverId",
                table: "driving_events",
                column: "DriverId",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_fuel_records_employees_driver_id",
                table: "fuel_records",
                column: "driver_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_maintenance_records_societes_company_id",
                table: "maintenance_records",
                column: "company_id",
                principalTable: "societes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_part_inventory_suppliers_SupplierId",
                table: "part_inventory",
                column: "SupplierId",
                principalTable: "suppliers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_maintenance_records_MaintenanceRecordId",
                table: "part_transactions",
                column: "MaintenanceRecordId",
                principalTable: "maintenance_records",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_part_inventory_PartId",
                table: "part_transactions",
                column: "PartId",
                principalTable: "part_inventory",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_suppliers_SupplierId",
                table: "part_transactions",
                column: "SupplierId",
                principalTable: "suppliers",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_users_CreatedByUserId",
                table: "part_transactions",
                column: "CreatedByUserId",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_part_transactions_vehicles_VehicleId",
                table: "part_transactions",
                column: "VehicleId",
                principalTable: "vehicles",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_points_of_interest_societes_CompanyId",
                table: "points_of_interest",
                column: "CompanyId",
                principalTable: "societes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_report_schedules_societes_CompanyId",
                table: "report_schedules",
                column: "CompanyId",
                principalTable: "societes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_reports_societes_CompanyId",
                table: "reports",
                column: "CompanyId",
                principalTable: "societes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_reservations_employees_AssignedDriverId",
                table: "reservations",
                column: "AssignedDriverId",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_societes_subscription_types_subscription_id",
                table: "societes",
                column: "subscription_id",
                principalTable: "subscription_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_suppliers_societes_CompanyId",
                table: "suppliers",
                column: "CompanyId",
                principalTable: "societes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_trips_employees_DriverId",
                table: "trips",
                column: "DriverId",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_user_vehicles_users_AssignedByUserId",
                table: "user_vehicles",
                column: "AssignedByUserId",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_user_vehicles_users_UserId",
                table: "user_vehicles",
                column: "UserId",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_user_vehicles_vehicles_VehicleId",
                table: "user_vehicles",
                column: "VehicleId",
                principalTable: "vehicles",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_users_user_settings_user_settings_id",
                table: "users",
                column: "user_settings_id",
                principalTable: "user_settings",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_costs_societes_company_id",
                table: "vehicle_costs",
                column: "company_id",
                principalTable: "societes",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_vehicle_stops_employees_driver_id",
                table: "vehicle_stops",
                column: "driver_id",
                principalTable: "employees",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicles_employees_assigned_driver_id",
                table: "vehicles",
                column: "assigned_driver_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_vehicles_employees_assigned_supervisor_id",
                table: "vehicles",
                column: "assigned_supervisor_id",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
