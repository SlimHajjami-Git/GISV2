using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Recette GPA, DEF-021 (annexe) : un sinistre dont le véhicule a été supprimé
/// (accident_events.vehicle_id sans clé étrangère) répondait 500 dès qu'un montant
/// approuvé ou un coût de réparation créait la dépense liée — vehicle_costs exige
/// un véhicule existant. Comme pour un accident sans véhicule, la phase et son
/// montant s'enregistrent ; seule la dépense liée est omise.
///
/// <para>Contrat complété le 18/09/2026 : la phase ne se contente plus d'omettre, elle
/// AVERTIT l'appelant (<c>PhaseSyncResult.Synced == false</c>) — ce fichier le vérifie,
/// sans quoi il documentait un contrat périmé.</para>
/// </summary>
public class PhaseCostDeletedVehicleTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private const int VehiculeSupprime = 50;
    private const int VehiculeExistant = 49;
    private static readonly DateTime DepotSinistre = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext CreerContexte(int vehiculeDuSinistre)
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(TestDataBuilder.CreateVehicle(id: VehiculeExistant, companyId: CompanyId));
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = vehiculeDuSinistre,
            DeviceUid = string.Empty,
            IncidentAt = DepotSinistre.AddDays(-3),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ClaimNumber = "QA-SIN-001",
            ClaimStatus = "pending",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static ICurrentTenantService Tenant() => TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object;

    [Fact]
    public async Task MontantApprouve_VehiculeSupprime_LaPhaseSEnregistreSansDepense()
    {
        using var context = CreerContexte(VehiculeSupprime);
        var handler = new RegisterClaimCommandHandler(context, Tenant(), NullLogger<RegisterClaimCommandHandler>.Instance);

        var result = await handler.Handle(
            new RegisterClaimCommand(AccidentId, "QA-SIN-001", DepotSinistre, 900m, "approved", null),
            CancellationToken.None);

        result.Synced.Should().BeFalse("l'écran doit pouvoir dire que rien n'a été reporté");
        context.ChangeTracker.Clear();
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
        var ev = await context.AccidentEvents.AsNoTracking().SingleAsync(e => e.Id == AccidentId);
        ev.ClaimStatus.Should().Be("approved");
        ev.ClaimApprovedAmount.Should().Be(900m);
    }

    [Fact]
    public async Task SansMontant_VehiculeSupprime_LaPhaseSEnregistre()
    {
        using var context = CreerContexte(VehiculeSupprime);
        var handler = new RegisterClaimCommandHandler(context, Tenant(), NullLogger<RegisterClaimCommandHandler>.Instance);

        await handler.Handle(
            new RegisterClaimCommand(AccidentId, "QA-SIN-001", DepotSinistre, null, "rejected", null),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        (await context.AccidentEvents.AsNoTracking().SingleAsync(e => e.Id == AccidentId)).ClaimStatus.Should().Be("rejected");
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CoutDeReparation_VehiculeSupprime_LeCoutEstGardeSansDepense()
    {
        using var context = CreerContexte(VehiculeSupprime);
        var handler = new RegisterRepairCommandHandler(context, Tenant(), NullLogger<RegisterRepairCommandHandler>.Instance);

        var result = await handler.Handle(
            new RegisterRepairCommand(AccidentId, null, DepotSinistre, 1200m),
            CancellationToken.None);

        result.Synced.Should().BeFalse("l'écran doit pouvoir dire que rien n'a été reporté");
        context.ChangeTracker.Clear();
        (await context.VehicleCosts.CountAsync()).Should().Be(0);
        (await context.AccidentEvents.AsNoTracking().SingleAsync(e => e.Id == AccidentId)).ActualRepairCost.Should().Be(1200m);
    }

    [Fact]
    public async Task MontantApprouve_VehiculeExistant_LaDepenseDeRemboursementEstCreee()
    {
        using var context = CreerContexte(VehiculeExistant);
        var handler = new RegisterClaimCommandHandler(context, Tenant(), NullLogger<RegisterClaimCommandHandler>.Instance);

        await handler.Handle(
            new RegisterClaimCommand(AccidentId, "QA-SIN-001", DepotSinistre, 900m, "approved", null),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var cout = await context.VehicleCosts.AsNoTracking().SingleAsync();
        cout.VehicleId.Should().Be(VehiculeExistant);
        cout.Type.Should().Be("insurance_refund");
        cout.Amount.Should().Be(900m);
        cout.Date.Should().Be(DepotSinistre);
    }
}
