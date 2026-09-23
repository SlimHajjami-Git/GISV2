using FluentAssertions;
using GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using MediatR;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// FAIL-OPEN du rapport d'activité journalière « tous véhicules », trouvé par la
/// relecture adversariale du 23/09/2026 (incident HERTZ).
///
/// <c>GetDailyActivityReportsQueryHandler</c> plaçait la portée DANS le
/// <c>if (companyId.HasValue)</c>. Quand le tenant est absent, il ne filtrait donc NI
/// par société NI par utilisateur — et le filtre global de multi-tenance est lui aussi
/// neutralisé dans ce cas (<c>GisDbContext</c> : <c>_tenantService.CompanyId == null</c>
/// ouvre tout). Une requête HTTP portant un jeton sans companyId exploitable
/// (<c>TenantMiddleware</c> n'appelle alors jamais <c>SetTenant</c>) obtenait ainsi
/// l'activité de TOUTES les sociétés de la plateforme.
///
/// Règle retenue : sans tenant, on ne rend quelque chose QUE si le périmètre demandé
/// désigne des véhicules d'UNE SEULE société — ce que le job de fond
/// <c>DailyFleetReportService</c> garantit par construction (il envoie la liste
/// explicite des véhicules d'une société, lot de destinataires par lot de
/// destinataires, puis le récapitulatif hebdomadaire). Tout le reste ne rend RIEN.
///
/// Le cas « avec tenant » (locataire restreint, administrateur sans affectation) est
/// couvert par <see cref="ReportsVehicleScopeTests"/> : il ne change pas ici.
/// </summary>
public class RapportActiviteSansTenantTests
{
    private const int Societe = 1;
    private const int AutreSociete = 2;

    private static readonly DateTime Jour = new(2026, 9, 22, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Deux sociétés, deux véhicules chacune, tous équipés d'un boîtier.</summary>
    private static async Task SeedAsync(TestGisDbContext ctx)
    {
        ctx.Vehicles.AddRange(
            new Vehicle { Id = 1, Name = "Hertz 1", Plate = "111 TU 1", CompanyId = Societe, GpsDeviceId = 11 },
            new Vehicle { Id = 2, Name = "Hertz 2", Plate = "222 TU 2", CompanyId = Societe, GpsDeviceId = 12 },
            new Vehicle { Id = 9, Name = "Autre societe", Plate = "999 TU 9", CompanyId = AutreSociete, GpsDeviceId = 19 },
            new Vehicle { Id = 10, Name = "Autre societe bis", Plate = "101 TU 10", CompanyId = AutreSociete, GpsDeviceId = 20 });

        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    /// <summary>
    /// Tenant VIDE : exactement l'état de <c>CurrentTenantService</c> quand
    /// <c>SetTenant</c> n'a jamais été appelé (jeton sans companyId exploitable), et
    /// aussi celui du job de fond, qui tourne hors requête HTTP.
    /// </summary>
    private static ICurrentTenantService SansTenant()
    {
        var mock = new Mock<ICurrentTenantService>();
        mock.Setup(x => x.CompanyId).Returns((int?)null);
        mock.Setup(x => x.UserId).Returns((int?)null);
        mock.Setup(x => x.UserRoles).Returns(Array.Empty<string>());
        mock.Setup(x => x.IsAuthenticated).Returns(false);
        return mock.Object;
    }

    /// <summary>Le calcul d'UN véhicule est hors sujet ici : seule la LISTE retenue compte.</summary>
    private static IMediator MediateurEcho()
    {
        var m = new Mock<IMediator>();
        m.Setup(x => x.Send(It.IsAny<GetDailyActivityReportQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetDailyActivityReportQuery q, CancellationToken _) => new DailyActivityReportDto { VehicleId = q.VehicleId });
        return m.Object;
    }

    private static async Task<List<int>> VehiculesRendusAsync(int[]? vehicleIds)
    {
        using var ctx = TestDbContextFactory.Create();
        await SeedAsync(ctx);

        var rapports = await new GetDailyActivityReportsQueryHandler(ctx, MediateurEcho(), SansTenant())
            .Handle(new GetDailyActivityReportsQuery(Jour, vehicleIds), CancellationToken.None);

        return rapports.Select(r => r.VehicleId).OrderBy(i => i).ToList();
    }

    [Fact]
    public async Task Sans_tenant_et_sans_perimetre_aucune_donnee()
    {
        // C'est l'appel exploitable : le front n'envoie pas vehicleIds quand aucun
        // véhicule n'est sélectionné. Avant le correctif, il rendait les 4 véhicules
        // des DEUX sociétés.
        (await VehiculesRendusAsync(null)).Should().BeEmpty(
            "une requête HTTP sans tenant ne doit JAMAIS rendre de données");
    }

    [Fact]
    public async Task Sans_tenant_un_perimetre_vide_ne_rend_rien()
    {
        (await VehiculesRendusAsync(Array.Empty<int>())).Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_tenant_un_perimetre_a_cheval_sur_deux_societes_ne_rend_rien()
    {
        // Aucun appelant légitime ne demande des véhicules de deux sociétés à la fois :
        // le job boucle société par société.
        (await VehiculesRendusAsync(new[] { 1, 9 })).Should().BeEmpty();
    }

    [Fact]
    public async Task Sans_tenant_un_perimetre_de_vehicules_inconnus_ne_rend_rien()
    {
        (await VehiculesRendusAsync(new[] { 4242 })).Should().BeEmpty();
    }

    /// <summary>
    /// TEST JUMEAU — le chemin du job de fond reste fonctionnel :
    /// <c>DailyFleetReportService</c> envoie <c>GetDailyActivityReportsQuery(date,
    /// lot.VehicleIds)</c> avec les véhicules d'UNE société, hors contexte HTTP.
    /// </summary>
    [Fact]
    public async Task Le_job_de_fond_avec_le_parc_d_une_seule_societe_est_inchange()
    {
        (await VehiculesRendusAsync(new[] { 1, 2 })).Should().Equal(new[] { 1, 2 },
            "le rapport journalier par e-mail doit continuer à partir");
    }

    /// <summary>
    /// Le job envoie aussi des LOTS partiels (un contenu par périmètre de
    /// destinataires) : un seul véhicule d'une société reste un appel légitime.
    /// </summary>
    [Fact]
    public async Task Le_job_de_fond_avec_un_lot_partiel_est_inchange()
    {
        (await VehiculesRendusAsync(new[] { 2 })).Should().Equal(new[] { 2 });
    }
}
