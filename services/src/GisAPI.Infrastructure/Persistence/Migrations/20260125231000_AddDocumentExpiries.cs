using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentExpiries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add expiry date columns to Vehicles
            migrationBuilder.AddColumn<DateTime>(
                name: "insurance_expiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "technical_inspection_expiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "tax_expiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "registration_expiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "transport_permit_expiry",
                table: "vehicles",
                type: "timestamp with time zone",
                nullable: true);

            // Add document fields to VehicleCosts
            migrationBuilder.AddColumn<DateTime>(
                name: "expiry_date",
                table: "vehicle_costs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_number",
                table: "vehicle_costs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_url",
                table: "vehicle_costs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Create indexes for expiry queries
            migrationBuilder.CreateIndex(
                name: "IX_vehicles_insurance_expiry",
                table: "vehicles",
                column: "insurance_expiry");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_technical_inspection_expiry",
                table: "vehicles",
                column: "technical_inspection_expiry");

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_tax_expiry",
                table: "vehicles",
                column: "tax_expiry");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vehicles_insurance_expiry",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_vehicles_technical_inspection_expiry",
                table: "vehicles");

            migrationBuilder.DropIndex(
                name: "IX_vehicles_tax_expiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "insurance_expiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "technical_inspection_expiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "tax_expiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "registration_expiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "transport_permit_expiry",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "expiry_date",
                table: "vehicle_costs");

            migrationBuilder.DropColumn(
                name: "document_number",
                table: "vehicle_costs");

            migrationBuilder.DropColumn(
                name: "document_url",
                table: "vehicle_costs");
        }
    }
}


