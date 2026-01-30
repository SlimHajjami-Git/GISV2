using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAccidentClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accident_claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CompanyId = table.Column<int>(type: "integer", nullable: false),
                    VehicleId = table.Column<int>(type: "integer", nullable: false),
                    DriverId = table.Column<int>(type: "integer", nullable: true),
                    AccidentDate = table.Column<DateTime>(type: "date", nullable: false),
                    AccidentTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    Location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    WeatherConditions = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RoadConditions = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EstimatedDamage = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    ApprovedAmount = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "draft"),
                    ThirdPartyInvolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    PoliceReportNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MileageAtAccident = table.Column<int>(type: "integer", nullable: true),
                    DamagedZones = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Witnesses = table.Column<string>(type: "text", nullable: true),
                    AdditionalNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accident_claims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accident_claims_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_accident_claims_users_DriverId",
                        column: x => x.DriverId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_accident_claims_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "accident_claim_third_parties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VehiclePlate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    VehicleModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InsuranceCompany = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    InsuranceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accident_claim_third_parties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accident_claim_third_parties_accident_claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "accident_claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "accident_claim_documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClaimId = table.Column<int>(type: "integer", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    FileUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FileSize = table.Column<int>(type: "integer", nullable: true),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accident_claim_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_accident_claim_documents_accident_claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "accident_claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Indexes
            migrationBuilder.CreateIndex(
                name: "IX_accident_claims_ClaimNumber",
                table: "accident_claims",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_accident_claims_CompanyId",
                table: "accident_claims",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_accident_claims_VehicleId",
                table: "accident_claims",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_accident_claims_Status",
                table: "accident_claims",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_accident_claims_AccidentDate",
                table: "accident_claims",
                column: "AccidentDate");

            migrationBuilder.CreateIndex(
                name: "IX_accident_claim_third_parties_ClaimId",
                table: "accident_claim_third_parties",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_accident_claim_documents_ClaimId",
                table: "accident_claim_documents",
                column: "ClaimId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "accident_claim_documents");
            migrationBuilder.DropTable(name: "accident_claim_third_parties");
            migrationBuilder.DropTable(name: "accident_claims");
        }
    }
}


