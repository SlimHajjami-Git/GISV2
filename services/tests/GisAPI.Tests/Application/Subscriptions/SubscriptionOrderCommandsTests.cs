using FluentAssertions;
using GisAPI.Application.Features.Subscriptions.Commands.RenewSubscription;
using GisAPI.Application.Features.Subscriptions.Commands.SubscriptionOrders;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Subscriptions;

/// <summary>
/// Le parcours d'achat GPA en libre-service : le client COMMANDE (montant
/// calculé serveur, une seule commande en attente), la plateforme CONFIRME une
/// fois le règlement reçu — c'est la confirmation qui active l'abonnement, via
/// le même chemin que le renouvellement manuel.
/// </summary>
public class SubscriptionOrderCommandsTests
{
    private const int CompanyId = 1;
    private const int UserId = 10;
    private const int GpaPlanId = 1;
    private const int GpsPlanId = 2;

    private (TestGisDbContext context, Mock<GisAPI.Domain.Interfaces.ICurrentTenantService> tenant) SetupContext()
    {
        var context = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: UserId);

        context.SubscriptionTypes.Add(new SubscriptionType
        {
            Id = GpaPlanId, Name = "GPA", Code = "plan-basique", IsActive = true,
            MonthlyPrice = 30m, QuarterlyPrice = 0m, YearlyPrice = 300m
        });
        context.SubscriptionTypes.Add(new SubscriptionType
        {
            Id = GpsPlanId, Name = "GPS Pro", Code = "plan-standard", IsActive = true,
            MonthlyPrice = 60m, QuarterlyPrice = 170m, YearlyPrice = 600m, GpsTracking = true
        });
        context.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Ma Société", Type = "autre", IsActive = true,
            SubscriptionTypeId = GpaPlanId, SubscriptionStatus = "trial",
            SubscriptionExpiresAt = DateTime.UtcNow.AddDays(5)
        });
        context.SaveChanges();
        return (context, tenant);
    }

    private CreateSubscriptionOrderCommandHandler CreateHandler(TestGisDbContext context, Mock<GisAPI.Domain.Interfaces.ICurrentTenantService> tenant)
        => new(context, tenant.Object, Mock.Of<ILogger<CreateSubscriptionOrderCommandHandler>>());

    // ── CreateSubscriptionOrder ──

    [Fact]
    public async Task Create_ComputesAmountServerSide_FromPlanAndCycle()
    {
        var (context, tenant) = SetupContext();
        var handler = CreateHandler(context, tenant);

        var result = await handler.Handle(
            new CreateSubscriptionOrderCommand(GpaPlanId, "yearly"), CancellationToken.None);

        // Le montant vient du plan, jamais de la requête (qui n'en porte aucun).
        result.Amount.Should().Be(300m);
        result.Status.Should().Be("pending");
        result.BillingCycle.Should().Be("yearly");
        result.PlanCode.Should().Be("plan-basique");

        var saved = await context.SubscriptionOrders.FindAsync(result.Id);
        saved!.CompanyId.Should().Be(CompanyId);
        saved.CreatedByUserId.Should().Be(UserId);
        saved.Amount.Should().Be(300m);
    }

    [Fact]
    public async Task Create_RejectsSecondPendingOrder()
    {
        var (context, tenant) = SetupContext();
        var handler = CreateHandler(context, tenant);

        await handler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "yearly"), CancellationToken.None);

        var act = () => handler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "monthly"), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*déjà une commande en attente*");
    }

    [Fact]
    public async Task Create_AfterCancellation_NewOrderIsAllowed()
    {
        var (context, tenant) = SetupContext();
        var handler = CreateHandler(context, tenant);

        var first = await handler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "yearly"), CancellationToken.None);
        await new CancelMySubscriptionOrderCommandHandler(context, tenant.Object)
            .Handle(new CancelMySubscriptionOrderCommand(first.Id), CancellationToken.None);

        var second = await handler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "monthly"), CancellationToken.None);
        second.Amount.Should().Be(30m);
    }

    [Fact]
    public async Task Create_RejectsNonSelfPurchasablePlan()
    {
        var (context, tenant) = SetupContext();
        var handler = CreateHandler(context, tenant);

        // L'offre GPS implique du matériel : elle ne se vend pas en libre-service.
        var act = () => handler.Handle(new CreateSubscriptionOrderCommand(GpsPlanId, "yearly"), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*matériel GPS*");
    }

    [Fact]
    public async Task Create_RejectsInvalidCycle_AndZeroPricedCycle()
    {
        var (context, tenant) = SetupContext();
        var handler = CreateHandler(context, tenant);

        var invalid = () => handler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "weekly"), CancellationToken.None);
        await invalid.Should().ThrowAsync<DomainException>().WithMessage("*Cycle de facturation invalide*");

        // QuarterlyPrice = 0 sur le plan GPA : un tarif non renseigné n'est pas gratuit.
        var zero = () => handler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "quarterly"), CancellationToken.None);
        await zero.Should().ThrowAsync<DomainException>().WithMessage("*pas disponible*");
    }

    [Fact]
    public async Task Create_RejectsInactivePlan()
    {
        var (context, tenant) = SetupContext();
        var plan = await context.SubscriptionTypes.FindAsync(GpaPlanId);
        plan!.IsActive = false;
        await context.SaveChangesAsync(CancellationToken.None);
        var handler = CreateHandler(context, tenant);

        var act = () => handler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "yearly"), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*n'existe pas ou n'est plus proposée*");
    }

    // ── CancelMySubscriptionOrder ──

    [Fact]
    public async Task Cancel_OnlyPendingOrders_AndOnlyMine()
    {
        var (context, tenant) = SetupContext();
        context.SubscriptionOrders.Add(new SubscriptionOrder
        {
            Id = 50, CompanyId = CompanyId, SubscriptionTypeId = GpaPlanId,
            BillingCycle = "yearly", Amount = 300m, Status = "confirmed"
        });
        context.SubscriptionOrders.Add(new SubscriptionOrder
        {
            Id = 51, CompanyId = 99, SubscriptionTypeId = GpaPlanId,
            BillingCycle = "yearly", Amount = 300m, Status = "pending"
        });
        await context.SaveChangesAsync(CancellationToken.None);
        var handler = new CancelMySubscriptionOrderCommandHandler(context, tenant.Object);

        // Déjà traitée → refus.
        var processed = () => handler.Handle(new CancelMySubscriptionOrderCommand(50), CancellationToken.None);
        await processed.Should().ThrowAsync<DomainException>().WithMessage("*déjà été traitée*");

        // Commande d'une AUTRE société → introuvable, même si elle existe.
        var foreign = () => handler.Handle(new CancelMySubscriptionOrderCommand(51), CancellationToken.None);
        await foreign.Should().ThrowAsync<DomainException>().WithMessage("*introuvable*");
    }

    // ── ConfirmSubscriptionOrder (plateforme) ──

    /// <summary>Médiateur branché sur le VRAI RenewSubscriptionCommandHandler pour vérifier l'effet complet.</summary>
    private static Mock<IMediator> RealRenewMediator(TestGisDbContext context)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RenewSubscriptionCommand>(), It.IsAny<CancellationToken>()))
            .Returns<RenewSubscriptionCommand, CancellationToken>((cmd, ct) =>
                new RenewSubscriptionCommandHandler(context).Handle(cmd, ct));
        return mediator;
    }

    [Fact]
    public async Task Confirm_ActivatesSubscription_ExtendsExpiry_SetsLastPayment()
    {
        var (context, tenant) = SetupContext();
        var createHandler = CreateHandler(context, tenant);
        var order = await createHandler.Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "yearly"), CancellationToken.None);

        var expiryBefore = (await context.Societes.FindAsync(CompanyId))!.SubscriptionExpiresAt!.Value;

        var confirmHandler = new ConfirmSubscriptionOrderCommandHandler(
            context, tenant.Object, RealRenewMediator(context).Object,
            Mock.Of<ILogger<ConfirmSubscriptionOrderCommandHandler>>());
        var confirmed = await confirmHandler.Handle(new ConfirmSubscriptionOrderCommand(order.Id), CancellationToken.None);

        confirmed.Status.Should().Be("confirmed");

        var societe = await context.Societes.FindAsync(CompanyId);
        // L'échéance est prolongée depuis l'échéance COURANTE (encore future), pas depuis aujourd'hui.
        societe!.SubscriptionExpiresAt.Should().BeCloseTo(expiryBefore.AddDays(365), TimeSpan.FromMinutes(1));
        societe.SubscriptionStatus.Should().Be("active");
        societe.LastPaymentAt.Should().NotBeNull();
        societe.BillingCycle.Should().Be("yearly");
        societe.NextPaymentAmount.Should().Be(300m);

        var saved = await context.SubscriptionOrders.FindAsync(order.Id);
        saved!.Status.Should().Be("confirmed");
        saved.ProcessedAt.Should().NotBeNull();
        saved.ProcessedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Confirm_WhenActivationFails_OrderStaysPending()
    {
        var (context, tenant) = SetupContext();
        var order = await CreateHandler(context, tenant)
            .Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "yearly"), CancellationToken.None);

        var failingMediator = new Mock<IMediator>();
        failingMediator.Setup(m => m.Send(It.IsAny<RenewSubscriptionCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var handler = new ConfirmSubscriptionOrderCommandHandler(
            context, tenant.Object, failingMediator.Object,
            Mock.Of<ILogger<ConfirmSubscriptionOrderCommandHandler>>());

        var act = () => handler.Handle(new ConfirmSubscriptionOrderCommand(order.Id), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // La commande reste en attente : elle pourra être reconfirmée après
        // correction — l'inverse solderait une commande sans abonnement livré.
        (await context.SubscriptionOrders.FindAsync(order.Id))!.Status.Should().Be("pending");
    }

    [Fact]
    public async Task Confirm_AlreadyProcessedOrder_Throws()
    {
        var (context, tenant) = SetupContext();
        context.SubscriptionOrders.Add(new SubscriptionOrder
        {
            Id = 60, CompanyId = CompanyId, SubscriptionTypeId = GpaPlanId,
            BillingCycle = "yearly", Amount = 300m, Status = "cancelled"
        });
        await context.SaveChangesAsync(CancellationToken.None);

        var handler = new ConfirmSubscriptionOrderCommandHandler(
            context, tenant.Object, RealRenewMediator(context).Object,
            Mock.Of<ILogger<ConfirmSubscriptionOrderCommandHandler>>());

        var act = () => handler.Handle(new ConfirmSubscriptionOrderCommand(60), CancellationToken.None);
        await act.Should().ThrowAsync<DomainException>().WithMessage("*déjà*");
    }

    // ── RejectSubscriptionOrder (plateforme) ──

    [Fact]
    public async Task Reject_SetsRejectedStatus_WithNoteShownToClient()
    {
        var (context, tenant) = SetupContext();
        var order = await CreateHandler(context, tenant)
            .Handle(new CreateSubscriptionOrderCommand(GpaPlanId, "yearly"), CancellationToken.None);

        var handler = new RejectSubscriptionOrderCommandHandler(context, tenant.Object);
        await handler.Handle(new RejectSubscriptionOrderCommand(order.Id, "Règlement non reçu."), CancellationToken.None);

        var saved = await context.SubscriptionOrders.FindAsync(order.Id);
        // « rejected » et non « cancelled » : l'écran client n'affiche le motif que pour les rejets.
        saved!.Status.Should().Be("rejected");
        saved.Note.Should().Be("Règlement non reçu.");
        saved.ProcessedAt.Should().NotBeNull();
    }
}
