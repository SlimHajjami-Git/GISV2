using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    public partial class AddFuelEntries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fuel_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
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
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fuel_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_fuel_entries_fuel_types_fuel_type_id",
                        column: x => x.fuel_type_id,
                        principalTable: "fuel_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fuel_entries_societes_company_id",
                        column: x => x.company_id,
                        principalTable: "societes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "fuel_entries");
        }
    }
}
