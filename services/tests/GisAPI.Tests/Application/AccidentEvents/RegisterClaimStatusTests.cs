using FluentAssertions;
using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.AccidentEvents;

/// <summary>
/// Recette GPA, DEF-021 : un statut de sinistre hors liste devenait NULL en
/// silence (204) et la phase 6 réécrite effaçait le suivi assurance déjà saisi ;
/// la dépense de remboursement était au passage re-datée au jour de l'appel.
/// </summary>
public class RegisterClaimStatusTests
{
    private const int CompanyId = 7;
    private const int AccidentId = 1;
    private static readonly DateTime DepotSinistre = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);

    private static TestGisDbContext CreerContexteAvecSinistreApprouve()
    {
        var context = TestDbContextFactory.Create();
        context.Vehicles.Add(TestDataBuilder.CreateVehicle(id: 49, companyId: CompanyId));
        context.AccidentEvents.Add(new AccidentEvent
        {
            Id = AccidentId,
            CompanyId = CompanyId,
            VehicleId = 49,
            DeviceUid = string.Empty,
            IncidentAt = DepotSinistre.AddDays(-3),
            Confidence = 100,
            Origin = "manual",
            Status = "confirmed",
            ClaimNumber = "QA-SIN-001",
            ClaimSubmittedAt = DepotSinistre,
            ClaimApprovedAmount = 900m,
            ClaimStatus = "approved",
        });
        context.VehicleCosts.Add(new VehicleCost
        {
            Id = 500,
            CompanyId = CompanyId,
            VehicleId = 49,
            AccidentEventId = AccidentId,
            Type = "insurance_refund",
            Amount = 900m,
            Date = DepotSinistre,
            Description = "Remboursement assurance — QA-SIN-001",
        });
        context.SaveChanges();
        context.ChangeTracker.Clear();
        return context;
    }

    private static RegisterClaimCommandHandler CreerHandler(TestGisDbContext context) =>
        new(context, TestDbContextFactory.CreateMockTenantService(companyId: CompanyId).Object,
            NullLogger<RegisterClaimCommandHandler>.Instance);

    [Fact]
    public async Task StatutInconnu_RefuseEnFrancais_EtRienNEstModifie()
    {
        using var context = CreerContexteAvecSinistreApprouve();
        var handler = CreerHandler(context);

        var act = () => handler.Handle(
            new RegisterClaimCommand(AccidentId, "QA-SIN-001", null, 900m, "bidule", null),
            CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("Statut de sinistre inconnu : « bidule »*Aucune modification*");

        context.ChangeTracker.Clear();
        var ev = await context.AccidentEvents.AsNoTracking().SingleAsync(e => e.Id == AccidentId);
        ev.ClaimStatus.Should().Be("approved");
        ev.ClaimSubmittedAt.Should().Be(DepotSinistre);
        ev.ClaimApprovedAmount.Should().Be(900m);

        var cout = await context.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 500);
        cout.Date.Should().Be(DepotSinistre);
    }

    [Theory]
    [InlineData("Approved", "approved")]
    [InlineData(" closed ", "closed")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task StatutConnuOuVide_Accepte(string? envoye, string? attendu)
    {
        using var context = CreerContexteAvecSinistreApprouve();
        var handler = CreerHandler(context);

        await handler.Handle(
            new RegisterClaimCommand(AccidentId, "QA-SIN-001", DepotSinistre, 900m, envoye, null),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        (await context.AccidentEvents.AsNoTracking().SingleAsync(e => e.Id == AccidentId))
            .ClaimStatus.Should().Be(attendu);
    }

    [Fact]
    public async Task SansDateDeDepot_LaDepenseDeRemboursementGardeSaDate()
    {
        using var context = CreerContexteAvecSinistreApprouve();
        var handler = CreerHandler(context);

        await handler.Handle(
            new RegisterClaimCommand(AccidentId, "QA-SIN-001", null, 950m, "approved", null),
            CancellationToken.None);

        context.ChangeTracker.Clear();
        var cout = await context.VehicleCosts.AsNoTracking().SingleAsync(c => c.Id == 500);
        cout.Amount.Should().Be(950m);
        cout.Date.Should().Be(DepotSinistre);
    }
}
