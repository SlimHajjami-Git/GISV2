using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GisAPI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Calypso 8 — ajoute la colonne <c>insurance_expiry</c> sur la table
/// <c>accident_event_third_parties</c>.
///
/// <para><b>Pourquoi</b> : le client (cf. PDF Calypso 8 page 3) demande de
/// pouvoir tracker la date d expiration de la police d assurance du tiers
/// implique dans un accident, en plus du nom, de la compagnie et du numero
/// de police deja existants. C est utile pour les sinistres avec
/// contre-partie : si la police du tiers est expiree, il faut le savoir
/// avant de soumettre le dossier.</para>
///
/// <para>Idempotent : <c>ADD COLUMN IF NOT EXISTS</c> pour qu un re-run
/// en prod ne casse pas si la colonne a ete hot-patchee via kubectl exec.</para>
/// </summary>
[DbContext(typeof(GisDbContext))]
[Migration("20260507150000_AddAccidentThirdPartyInsuranceExpiry")]
public partial class AddAccidentThirdPartyInsuranceExpiry : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE accident_event_third_parties
    ADD COLUMN IF NOT EXISTS insurance_expiry TIMESTAMP WITH TIME ZONE NULL;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
ALTER TABLE accident_event_third_parties
    DROP COLUMN IF EXISTS insurance_expiry;
");
    }
}
