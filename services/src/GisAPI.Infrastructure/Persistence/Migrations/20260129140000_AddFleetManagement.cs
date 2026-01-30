using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFleetManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Create departments table
            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
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

            // 2. Create fuel_types table (enum values with company-specific pricing)
            migrationBuilder.CreateTable(
                name: "fuel_types",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_types", x => x.Id);
                });

            // Insert default fuel types
            migrationBuilder.InsertData(
                table: "fuel_types",
                columns: new[] { "Code", "Name", "IsSystem" },
                values: new object[,]
                {
                    { "essence", "Essence", true },
                    { "diesel", "Diesel", true },
                    { "hybride", "Hybride", true },
                    { "electrique", "Électrique", true },
                    { "gpl", "GPL", true }
                });

            // 3. Create fuel_pricing table (company-specific pricing for fuel types)
            migrationBuilder.CreateTable(
                name: "fuel_pricing",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    FuelTypeId = table.Column<int>(type: "integer", nullable: false),
                    PricePerLiter = table.Column<decimal>(type: "numeric(10,3)", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_pricing", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fuel_pricing_societes_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fuel_pricing_fuel_types_FuelTypeId",
                        column: x => x.FuelTypeId,
                        principalTable: "fuel_types",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 4. Add DepartmentId and SpeedLimit to vehicles table
            migrationBuilder.AddColumn<int>(
                name: "DepartmentId",
                table: "vehicles",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SpeedLimit",
                table: "vehicles",
                type: "integer",
                nullable: true,
                defaultValue: 120);

            migrationBuilder.AddColumn<string>(
                name: "FuelType",
                table: "vehicles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                defaultValue: "diesel");

            // 5. Create speed_limit_alerts table
            migrationBuilder.CreateTable(
                name: "speed_limit_alerts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    SpeedLimit = table.Column<int>(type: "integer", nullable: false),
                    ActualSpeed = table.Column<int>(type: "integer", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcknowledgedById = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_speed_limit_alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_speed_limit_alerts_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_speed_limit_alerts_societes_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_speed_limit_alerts_users_AcknowledgedById",
                        column: x => x.AcknowledgedById,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Indexes
            migrationBuilder.CreateIndex(
                name: "IX_departments_CompanyId",
                table: "departments",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_departments_CompanyId_Name",
                table: "departments",
                columns: new[] { "CompanyId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_types_Code",
                table: "fuel_types",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fuel_pricing_CompanyId",
                table: "fuel_pricing",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_pricing_FuelTypeId",
                table: "fuel_pricing",
                column: "FuelTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_fuel_pricing_CompanyId_FuelTypeId_IsActive",
                table: "fuel_pricing",
                columns: new[] { "CompanyId", "FuelTypeId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_DepartmentId",
                table: "vehicles",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_vehicles_departments_DepartmentId",
                table: "vehicles",
                column: "DepartmentId",
                principalTable: "departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_speed_limit_alerts_VehicleId",
                table: "speed_limit_alerts",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_speed_limit_alerts_CompanyId",
                table: "speed_limit_alerts",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_speed_limit_alerts_RecordedAt",
                table: "speed_limit_alerts",
                column: "RecordedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_vehicles_departments_DepartmentId", table: "vehicles");
            migrationBuilder.DropIndex(name: "IX_vehicles_DepartmentId", table: "vehicles");
            migrationBuilder.DropColumn(name: "DepartmentId", table: "vehicles");
            migrationBuilder.DropColumn(name: "SpeedLimit", table: "vehicles");
            migrationBuilder.DropColumn(name: "FuelType", table: "vehicles");

            migrationBuilder.DropTable(name: "speed_limit_alerts");
            migrationBuilder.DropTable(name: "fuel_pricing");
            migrationBuilder.DropTable(name: "fuel_types");
            migrationBuilder.DropTable(name: "departments");
        }
    }
}


