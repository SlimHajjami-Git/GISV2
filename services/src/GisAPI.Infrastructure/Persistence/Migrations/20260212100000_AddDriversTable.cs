using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace GisAPI.Infrastructure.Persistence.Migrations;

public partial class AddDriversTable : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "drivers",
            columns: table => new
            {
                id = table.Column<int>(nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                company_id = table.Column<int>(nullable: false),
                user_id = table.Column<int>(nullable: false),
                permit_number = table.Column<string>(maxLength: 50, nullable: true),
                permit_type = table.Column<string>(maxLength: 10, nullable: true),
                permit_expiry = table.Column<DateTime>(nullable: true),
                cin = table.Column<string>(maxLength: 20, nullable: true),
                date_of_birth = table.Column<DateTime>(nullable: true),
                hire_date = table.Column<DateTime>(nullable: true),
                assigned_vehicle_id = table.Column<int>(nullable: true),
                status = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "active"),
                created_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()"),
                updated_at = table.Column<DateTime>(nullable: false, defaultValueSql: "NOW()")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_drivers", x => x.id);
                table.ForeignKey("FK_drivers_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_drivers_vehicles_assigned_vehicle_id", x => x.assigned_vehicle_id, "vehicles", "id", onDelete: ReferentialAction.SetNull);
                table.ForeignKey("FK_drivers_societes_company_id", x => x.company_id, "societes", "id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_drivers_company_id_user_id", "drivers", new[] { "company_id", "user_id" }, unique: true);
        migrationBuilder.CreateIndex("IX_drivers_assigned_vehicle_id", "drivers", "assigned_vehicle_id");

        // Migrate existing driver data from users table to drivers table
        migrationBuilder.Sql(@"
            INSERT INTO drivers (company_id, user_id, permit_number, permit_type, permit_expiry, cin, date_of_birth, hire_date, status, created_at, updated_at)
            SELECT company_id, id, permit_number, permit_type, permit_expiry, ""CIN"", date_of_birth, hire_date, status, created_at, updated_at
            FROM users
            WHERE employee_role = 'driver'
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("drivers");
    }
}
