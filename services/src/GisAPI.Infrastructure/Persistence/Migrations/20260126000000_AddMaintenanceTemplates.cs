using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMaintenanceTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_templates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "medium"),
                    IntervalKm = table.Column<int>(type: "integer", nullable: true),
                    IntervalMonths = table.Column<int>(type: "integer", nullable: true),
                    EstimatedCost = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_templates", x => x.Id);
                    table.CheckConstraint("CK_maintenance_templates_interval", "\"IntervalKm\" IS NOT NULL OR \"IntervalMonths\" IS NOT NULL");
                });

            migrationBuilder.CreateTable(
                name: "vehicle_maintenance_schedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    LastDoneDate = table.Column<DateTime>(type: "date", nullable: true),
                    LastDoneKm = table.Column<int>(type: "integer", nullable: true),
                    NextDueDate = table.Column<DateTime>(type: "date", nullable: true),
                    NextDueKm = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "upcoming"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicle_maintenance_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_vehicle_maintenance_schedules_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vehicle_maintenance_schedules_maintenance_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "maintenance_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_logs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    ScheduleId = table.Column<int>(type: "integer", nullable: true),
                    CostId = table.Column<int>(type: "integer", nullable: true),
                    DoneDate = table.Column<DateTime>(type: "date", nullable: false),
                    DoneKm = table.Column<int>(type: "integer", nullable: false),
                    ActualCost = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    SupplierId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_logs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_maintenance_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "maintenance_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_vehicle_maintenance_schedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "vehicle_maintenance_schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_vehicle_costs_CostId",
                        column: x => x.CostId,
                        principalTable: "vehicle_costs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_maintenance_logs_suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Indexes
            migrationBuilder.CreateIndex(
                name: "IX_maintenance_templates_CompanyId",
                table: "maintenance_templates",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_VehicleId",
                table: "vehicle_maintenance_schedules",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_TemplateId",
                table: "vehicle_maintenance_schedules",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_Status",
                table: "vehicle_maintenance_schedules",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_vehicle_maintenance_schedules_VehicleId_TemplateId",
                table: "vehicle_maintenance_schedules",
                columns: new[] { "VehicleId", "TemplateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_logs_VehicleId",
                table: "maintenance_logs",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_logs_TemplateId",
                table: "maintenance_logs",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "maintenance_logs");
            migrationBuilder.DropTable(name: "vehicle_maintenance_schedules");
            migrationBuilder.DropTable(name: "maintenance_templates");
        }
    }
}


