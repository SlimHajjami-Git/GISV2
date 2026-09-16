using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.AccidentEvents.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Tests.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Recette GPA, DEF-045 : la phase affichée dans la liste des sinistres était déduite de
/// champs que le formulaire de déclaration pré-remplit (n° de sinistre, montant estimé).
/// Un sinistre déclaré à l'instant apparaissait en « Assurance » alors qu'aucune expertise,
/// aucun devis ni aucun dépôt n'avait été saisi.
/// </summary>
public class AccidentListPhaseTests
{
    private const int CompanyId = 7;
    private const int VehicleId = 49;
    private static readonly DateTime Accident = new(2026, 9, 10, 8, 30, 0, DateTimeKind.Utc);

    private static TestGisDbContext CreerContexte()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(TestDataBuilder.CreateVehicle(id: VehicleId, companyId: CompanyId));
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static async Task<AccidentEventListItemDto> LigneDeLaListeAsync(TestGisDbContext context, int id)
    {
        var handler = new ListAccidentEventsQueryHandler(
            context, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object);
        var result = await handler.Handle(new ListAccidentEventsQuery(), CancellationToken.None);
        return result.Items.Single(i => i.Id == id);
    }

    private static AccidentEvent SinistreConfirme(int id) => new()
    {
        Id = id,
        CompanyId = CompanyId,
        VehicleId = VehicleId,
        DeviceUid = string.Empty,
        IncidentAt = Accident,
        Confidence = 100,
        Origin = "manual",
        Status = "confirmed",
    };

    [Fact]
    public async Task Un_sinistre_declare_avec_numero_et_cout_estime_reste_en_phase_confirme()
    {
        using var context = CreerContexte();
        var creation = new CreateManualAccidentCommandHandler(
            context,
            TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: 1).Object,
            NullLogger<CreateManualAccidentCommandHandler>.Instance);

        // Reproduction de la fiche : estimatedCost 1500.00 et claimNumber « QA-SIN-001 ».
        var id = await creation.Handle(new CreateManualAccidentCommand(
            VehicleId, Accident, null, null, null, null, "Choc arrière", "minor",
            1500.00m, "QA-SIN-001", null), CancellationToken.None);
        context.ChangeTracker.Clear();

        var ligne = await LigneDeLaListeAsync(context, id);

        ligne.CurrentPhase.Should().Be("confirmed");
        // Les références saisies à la déclaration restent consultables.
        ligne.ClaimNumber.Should().Be("QA-SIN-001");
        ligne.ExpertEstimatedAmount.Should().Be(1500.00m);
    }

    public static TheoryData<string, Action<AccidentEvent>> PhasesInstruites => new()
    {
        { "expertise", e => e.ExpertVisitedAt = Accident.AddDays(3) },
        { "expertise", e => e.ExpertName = "QA Expert" },
        { "expertise", e => e.ExpertCompany = "Cabinet QA" },
        { "expertise", e => e.ExpertAssessment = "Pare-choc arrière enfoncé" },
        { "quote", e => e.MechanicQuoteAt = Accident.AddDays(4) },
        { "quote", e => e.MechanicQuotedAmount = 1380m },
        { "repair", e => e.RepairCompletedAt = Accident.AddDays(8) },
        { "repair", e => e.ActualRepairCost = 1400m },
        { "claim", e => e.ClaimSubmittedAt = Accident.AddDays(6) },
        { "claim", e => e.ClaimStatus = "pending" },
        { "claim", e => e.ClaimStatus = "partial" },
        { "claim", e => e.ClaimApprovedAmount = 900m },
        { "closed", e => e.ClaimStatus = "approved" },
        { "closed", e => e.ClaimStatus = "rejected" },
        { "closed", e => e.ClaimStatus = "closed" },
    };

    [Theory]
    [MemberData(nameof(PhasesInstruites))]
    public async Task La_phase_suit_les_saisies_propres_a_chaque_phase(string phaseAttendue, Action<AccidentEvent> saisie)
    {
        using var context = CreerContexte();
        var ev = SinistreConfirme(1);
        // Pré-remplissage de la déclaration, présent sur tous les cas : il ne doit rien décider.
        ev.ClaimNumber = "QA-SIN-001";
        ev.ExpertEstimatedAmount = 1500m;
        saisie(ev);
        context.AccidentEvents.Add(ev);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var ligne = await LigneDeLaListeAsync(context, 1);

        ligne.CurrentPhase.Should().Be(phaseAttendue);
    }

    public static TheoryData<string, Action<AccidentEvent>> ChampsPartagesAvecLaDeclaration => new()
    {
        { "montant d'expertise seul", e => e.ExpertEstimatedAmount = 1500m },
        { "n° de sinistre seul", e => e.ClaimNumber = "QA-SIN-001" },
    };

    /// <summary>
    /// Comportement voulu : ces deux champs sont aussi écrits par la déclaration, rien ne dit
    /// qui les a saisis. Une expertise réduite au montant, ou un suivi assurance réduit au n°,
    /// laisse donc le dossier en « confirmed » tant que les montants ne sont pas séparés.
    /// </summary>
    [Theory]
    [MemberData(nameof(ChampsPartagesAvecLaDeclaration))]
    public async Task Un_champ_que_la_declaration_ecrit_aussi_ne_fait_pas_avancer_la_phase(string cas, Action<AccidentEvent> saisie)
    {
        using var context = CreerContexte();
        var ev = SinistreConfirme(1);
        saisie(ev);
        context.AccidentEvents.Add(ev);
        context.SaveChanges();
        context.ChangeTracker.Clear();

        var ligne = await LigneDeLaListeAsync(context, 1);

        ligne.CurrentPhase.Should().Be("confirmed", cas);
    }
}
