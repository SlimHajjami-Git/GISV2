using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierServicesAndOptimizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add PostalCode to Suppliers
            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                table: "suppliers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // 2. Alter Rating from integer to decimal(3,1) for 0.0-5.0 range
            migrationBuilder.Sql(@"
                ALTER TABLE suppliers 
                ALTER COLUMN ""Rating"" TYPE decimal(3,1) 
                USING COALESCE(""Rating"", 0)::decimal(3,1);
            ");

            // 3. Add SupplierId to MaintenanceRecords for garage linkage
            migrationBuilder.AddColumn<int>(
                name: "supplier_id",
                table: "maintenance_records",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_records_supplier_id",
                table: "maintenance_records",
                column: "supplier_id");

            migrationBuilder.AddForeignKey(
                name: "FK_maintenance_records_suppliers_supplier_id",
                table: "maintenance_records",
                column: "supplier_id",
                principalTable: "suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 4. Create SupplierServices table for N:N relationship
            migrationBuilder.CreateTable(
                name: "supplier_services",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SupplierId = table.Column<int>(type: "integer", nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_services", x => x.Id);
                    table.ForeignKey(
                        name: "FK_supplier_services_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 5. Create indexes and unique constraint
            migrationBuilder.CreateIndex(
                name: "IX_supplier_services_SupplierId",
                table: "supplier_services",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_services_SupplierId_ServiceCode",
                table: "supplier_services",
                columns: new[] { "SupplierId", "ServiceCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supplier_services");

            migrationBuilder.DropForeignKey(
                name: "FK_maintenance_records_suppliers_supplier_id",
                table: "maintenance_records");

            migrationBuilder.DropIndex(
                name: "IX_maintenance_records_supplier_id",
                table: "maintenance_records");

            migrationBuilder.DropColumn(
                name: "supplier_id",
                table: "maintenance_records");

            migrationBuilder.Sql(@"
                ALTER TABLE suppliers 
                ALTER COLUMN ""Rating"" TYPE integer 
                USING ""Rating""::integer;
            ");

            migrationBuilder.DropColumn(
                name: "postal_code",
                table: "suppliers");
        }
    }
}
