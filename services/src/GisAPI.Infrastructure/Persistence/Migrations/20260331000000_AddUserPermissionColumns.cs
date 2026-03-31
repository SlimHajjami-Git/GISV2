using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add employee/HR fields
            migrationBuilder.AddColumn<string>(
                name: "permit_type",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "permit_expiry",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cin",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "date_of_birth",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "employee_role",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "hire_date",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            // Add permission columns
            migrationBuilder.AddColumn<string>(
                name: "access_level",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "user");

            migrationBuilder.AddColumn<bool>(
                name: "can_monitoring",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "can_vehicles",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "can_drivers",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "can_reports",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_geofences",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_maintenance",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_costs",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_documents",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_accidents",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_users",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_settings",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_suppliers",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "can_fleet_management",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Populate permissions from Role.Permissions JSONB for existing users
            migrationBuilder.Sql(@"
                UPDATE users u SET
                    access_level = CASE WHEN r.is_company_admin OR r.is_system_role THEN 'admin' ELSE 'user' END,
                    can_monitoring = COALESCE((r.permissions->>'monitoring')::boolean, true),
                    can_vehicles = COALESCE((r.permissions->>'vehicles')::boolean, true),
                    can_drivers = COALESCE((r.permissions->>'employees')::boolean, true),
                    can_reports = COALESCE((r.permissions->>'reports')::boolean, false),
                    can_geofences = COALESCE((r.permissions->>'geofences')::boolean, false),
                    can_maintenance = COALESCE((r.permissions->>'maintenance')::boolean, false),
                    can_costs = COALESCE((r.permissions->>'costs')::boolean, false),
                    can_documents = COALESCE((r.permissions->>'documents')::boolean, false),
                    can_accidents = COALESCE((r.permissions->>'accidents')::boolean, false),
                    can_users = COALESCE((r.permissions->>'users')::boolean, false),
                    can_settings = COALESCE((r.permissions->>'settings')::boolean, false),
                    can_suppliers = COALESCE((r.permissions->>'suppliers')::boolean, false),
                    can_fleet_management = COALESCE((r.permissions->>'fleet_management')::boolean, false)
                FROM roles r
                WHERE u.role_id = r.id;

                -- Admin/system roles get all permissions
                UPDATE users u SET
                    access_level = 'admin',
                    can_monitoring = true, can_vehicles = true, can_drivers = true,
                    can_reports = true, can_geofences = true, can_maintenance = true,
                    can_costs = true, can_documents = true, can_accidents = true,
                    can_users = true, can_settings = true, can_suppliers = true,
                    can_fleet_management = true
                FROM roles r
                WHERE u.role_id = r.id AND (r.is_company_admin = true OR r.is_system_role = true);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "permit_type", table: "users");
            migrationBuilder.DropColumn(name: "permit_expiry", table: "users");
            migrationBuilder.DropColumn(name: "cin", table: "users");
            migrationBuilder.DropColumn(name: "date_of_birth", table: "users");
            migrationBuilder.DropColumn(name: "employee_role", table: "users");
            migrationBuilder.DropColumn(name: "hire_date", table: "users");
            migrationBuilder.DropColumn(name: "access_level", table: "users");
            migrationBuilder.DropColumn(name: "can_monitoring", table: "users");
            migrationBuilder.DropColumn(name: "can_vehicles", table: "users");
            migrationBuilder.DropColumn(name: "can_drivers", table: "users");
            migrationBuilder.DropColumn(name: "can_reports", table: "users");
            migrationBuilder.DropColumn(name: "can_geofences", table: "users");
            migrationBuilder.DropColumn(name: "can_maintenance", table: "users");
            migrationBuilder.DropColumn(name: "can_costs", table: "users");
            migrationBuilder.DropColumn(name: "can_documents", table: "users");
            migrationBuilder.DropColumn(name: "can_accidents", table: "users");
            migrationBuilder.DropColumn(name: "can_users", table: "users");
            migrationBuilder.DropColumn(name: "can_settings", table: "users");
            migrationBuilder.DropColumn(name: "can_suppliers", table: "users");
            migrationBuilder.DropColumn(name: "can_fleet_management", table: "users");
        }
    }
}
