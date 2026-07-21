using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Tables du module Devis (admin plateforme) : estimates + estimate_items.
/// Montants calculés à la lecture (non stockés). Migration idempotente.
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260721140000_AddEstimates")]
public partial class AddEstimates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS estimates (
    ""Id"" SERIAL PRIMARY KEY,
    ""Number"" VARCHAR(30) NOT NULL,
    ""CompanyId"" INTEGER NULL REFERENCES societes(id) ON DELETE SET NULL,
    ""ClientName"" VARCHAR(255) NOT NULL,
    ""ClientEmail"" VARCHAR(255) NULL,
    ""ClientPhone"" VARCHAR(50) NULL,
    ""ClientAddress"" TEXT NULL,
    ""Status"" VARCHAR(20) NOT NULL DEFAULT 'draft',
    ""IssueDate"" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT now(),
    ""ValidUntil"" TIMESTAMP WITHOUT TIME ZONE NULL,
    ""DiscountPercent"" NUMERIC(5,2) NOT NULL DEFAULT 0,
    ""TaxPercent"" NUMERIC(5,2) NOT NULL DEFAULT 19,
    ""Notes"" TEXT NULL,
    ""CreatedAt"" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT now(),
    ""UpdatedAt"" TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_estimates_number ON estimates(""Number"");

CREATE TABLE IF NOT EXISTS estimate_items (
    ""Id"" SERIAL PRIMARY KEY,
    ""EstimateId"" INTEGER NOT NULL REFERENCES estimates(""Id"") ON DELETE CASCADE,
    ""Description"" TEXT NOT NULL,
    ""Quantity"" NUMERIC(12,2) NOT NULL DEFAULT 1,
    ""UnitPrice"" NUMERIC(14,3) NOT NULL DEFAULT 0,
    ""SortOrder"" INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS idx_estimate_items_estimate ON estimate_items(""EstimateId"");
");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DROP TABLE IF EXISTS estimate_items; DROP TABLE IF EXISTS estimates;");
    }
}
