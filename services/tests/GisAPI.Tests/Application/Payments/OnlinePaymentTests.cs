using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Features.Payments;
using GisAPI.Application.Features.Subscriptions.Commands.RenewSubscription;
using GisAPI.Application.Features.Subscriptions.Commands.SubscriptionOrders;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Services;
using GisAPI.Tests.Common;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Payments;

/// <summary>
/// Paiement par carte des commandes d'abonnement par ClicToPay (migration 067). Règle du manuel :
/// seul getOrderStatusExtended.do fait foi (orderStatus = 2), jamais le retour du navigateur ; et
/// Calypso vérifie en plus montant, devise et numéro avant de valider la commande.
/// </summary>
public class OnlinePaymentTests
{
    private const int CompanyId = 1;
    private const int OtherCompanyId = 2;
    private const int UserId = 10;
    private const int GpaPlanId = 1;

    // ── Montants et devises ──

    [Theory]
    [InlineData(50, "TND", 50000)]
    [InlineData(618, "TND", 618000)]
    [InlineData(36.5, "EUR", 3650)]
    [InlineData(84, "USD", 8400)]
    public void Montant_en_unites_minimales(decimal amount, string currency, long expected) =>
        ClicToPayAmounts.ToMinorUnits(amount, currency).Should().Be(expected);

    [Fact]
    public void Un_montant_qui_ne_tombe_pas_juste_est_refuse_plutot_qu_arrondi() =>
        FluentActions.Invoking(() => ClicToPayAmounts.ToMinorUnits(10.0005m, "TND"))
            .Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("TND", 788)]
    [InlineData("eur", 978)]
    [InlineData("USD", 840)]
    [InlineData("DZD", null)]
    public void Code_devise_ISO(string currency, int? expected) =>
        ClicToPayAmounts.CurrencyCode(currency).Should().Be(expected);

    [Fact]
    public void Numero_de_commande_unique_et_de_32_caracteres_au_plus()
    {
        var a = OnlinePaymentProcessor.NewOrderNumber(123456);
        var b = OnlinePaymentProcessor.NewOrderNumber(123456);
        a.Should().StartWith("CLY-123456-").And.NotBe(b);
        a.Length.Should().BeLessOrEqualTo(32);
        OnlinePaymentProcessor.NewOrderNumber(int.MaxValue).Length.Should().BeLessOrEqualTo(32);
    }

    [Theory]
    [InlineData("https://test.clictopay.com/merchants/test/payment_fr.html?mdOrder=1", true)]
    [InlineData("https://ipay.clictopay.com/payment/merchants/x/payment_fr.html", true)]
    [InlineData("http://test.clictopay.com/payment_fr.html", false)]
    [InlineData("https://clictopay.com.pirate.example/payment", false)]
    [InlineData("https://pirate.example/?u=clictopay.com", false)]
    [InlineData(null, false)]
    public void La_page_de_paiement_doit_etre_chez_ClicToPay_en_HTTPS(string? url, bool ok) =>
        OnlinePaymentProcessor.IsClicToPayPage(url).Should().Be(ok);

    // ── Lecture des réponses ClicToPay ──

    [Fact]
    public void Statut_lu_tel_que_le_manuel_le_montre()
    {
        // Exemple « paiement accepté » du manuel : errorCode et currency en TEXTE.
        const string json = """
        {
          "errorCode": "0", "errorMessage": "Success", "orderNumber": "177694-561065-961079",
          "orderStatus": 2, "actionCode": 0, "actionCodeDescription": "Request processed successfully",
          "amount": 618000, "currency": "788", "date": 1776945611925,
          "cardAuthInfo": { "expiration": "202904", "cardholderName": "Mr X", "paymentSystem": "VISA",
                            "approvalCode": "365067", "pan": "410000**9143" }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var s = ClicToPayClient.ParseStatus(doc.RootElement);

        s.ErrorCode.Should().Be(0);
        s.OrderStatus.Should().Be(2);
        s.Amount.Should().Be(618000);
        s.CurrencyCode.Should().Be(788);
        s.OrderNumber.Should().Be("177694-561065-961079");
        s.CardPanMasked.Should().Be("410000**9143");
        s.ApprovalCode.Should().Be("365067");
    }

    [Fact]
    public void Statut_PAYE_tel_que_l_environnement_de_test_reel_le_renvoie()
    {
        // Réponse réelle de test.clictopay.com (06/11/2026, carte de test acceptée), IP retirée.
        // Écarts avec le manuel : pas d'approvalCode mais authorizationResponseId.
        const string json = """
        {"errorCode":"0","errorMessage":"Success","orderNumber":"CLY-0-1793957863a1","orderStatus":2,"actionCode":0,
         "actionCodeDescription":"Request processed successfully","amount":36000,"currency":"788","date":1793957863375,
         "cardAuthInfo":{"expiration":"202612","cardholderName":"TEST CALYPSO","authorizationResponseId":"826279","paymentSystem":"VISA","pan":"450921**1119"},
         "authDateTime":1793958196712,"terminalId":"01234567","authRefNum":"000265826279",
         "paymentAmountInfo":{"paymentState":"DEPOSITED","approvedAmount":36000,"depositedAmount":36000,"refundedAmount":0},
         "chargeback":false,"paymentWay":"CARD","feeAmount":0,"fraudLevel":0}
        """;
        using var doc = JsonDocument.Parse(json);
        var s = ClicToPayClient.ParseStatus(doc.RootElement);

        s.OrderStatus.Should().Be(2);
        s.Amount.Should().Be(36000);
        s.CurrencyCode.Should().Be(788);
        s.OrderNumber.Should().Be("CLY-0-1793957863a1");
        s.CardPanMasked.Should().Be("450921**1119");
        s.ApprovalCode.Should().Be("826279");
    }

    [Fact]
    public void Statut_REFUSE_et_commande_inconnue_tels_que_l_environnement_de_test_reel_les_renvoie()
    {
        const string refuse = """
        {"errorCode":"0","errorMessage":"Success","orderNumber":"CLY-0-1793958242c3","orderStatus":6,"actionCode":76,
         "actionCodeDescription":"Rejected Authorization - Issuer bank is not able to process the transaction Cannot process transaction]",
         "amount":24000,"currency":"788",
         "cardAuthInfo":{"expiration":"202912","cardholderName":"TEST CALYPSO","paymentSystem":"MASTERCARD","pan":"510405**1115"},
         "paymentAmountInfo":{"paymentState":"DECLINED","approvedAmount":0,"depositedAmount":0,"refundedAmount":0}}
        """;
        using var d1 = JsonDocument.Parse(refuse);
        var s = ClicToPayClient.ParseStatus(d1.RootElement);
        s.OrderStatus.Should().Be(6);
        s.ActionCode.Should().Be(76);
        s.ApprovalCode.Should().BeNull();

        using var d2 = JsonDocument.Parse("""{"errorCode":"6","errorMessage":"Order not found","fraudLevel":0}""");
        var inconnue = ClicToPayClient.ParseStatus(d2.RootElement);
        inconnue.ErrorCode.Should().Be(6);
        inconnue.OrderStatus.Should().BeNull();
    }

    [Fact]
    public void Description_sans_accents_car_ClicToPay_les_abime() =>
        OnlinePaymentProcessor.Ascii("Abonnement Calypso GPA — intégration n° 3").Should().Be("Abonnement Calypso GPA - integration n  3");

    [Fact]
    public async Task Register_envoie_le_formulaire_du_manuel_et_lit_la_page_de_paiement()
    {
        var handler = new CapturingHandler("""{ "orderId": "70906e55-7114-41d6-8332-4609dc6590f4", "formUrl": "https://test.clictopay.com/merchants/test/payment_fr.html?mdOrder=70906e55" }""");
        var client = new ClicToPayClient(new HttpClient(handler), Options());

        var result = await client.RegisterAsync(new ClicToPayRegisterRequest(
            "CLY-1-abc", 300000, 788, "https://x/paiement/retour?ref=CLY-1-abc", "https://x/paiement/retour?ref=CLY-1-abc&echec=1",
            "Abonnement", "fr", "DESKTOP"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.OrderId.Should().Be("70906e55-7114-41d6-8332-4609dc6590f4");
        handler.Url.Should().Be("https://test.clictopay.com/payment/rest/register.do");
        handler.Url.Should().NotContain("password");   // identifiants dans le corps, jamais dans l'adresse
        handler.Form["userName"].Should().Be("marchand-test");
        handler.Form["password"].Should().Be("secret-test");
        handler.Form["orderNumber"].Should().Be("CLY-1-abc");
        handler.Form["amount"].Should().Be("300000");
        handler.Form["currency"].Should().Be("788");
        handler.Form["failUrl"].Should().EndWith("&echec=1");
        handler.Form["pageView"].Should().Be("DESKTOP");
    }

    [Fact]
    public async Task Register_en_erreur_lit_le_code_numerique()
    {
        var client = new ClicToPayClient(new HttpClient(new CapturingHandler("""{ "errorCode": 4, "errorMessage": "Numéro de commande vide" }""")), Options());
        var result = await client.RegisterAsync(new ClicToPayRegisterRequest("", 1, 788, "https://x", "https://x", "", "fr", "DESKTOP"), CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(4);
        result.ErrorMessage.Should().Be("Numéro de commande vide");
    }

    // ── Préparer le paiement ──

    [Fact]
    public async Task Preparer_envoie_le_montant_de_la_commande_en_millimes_et_renvoie_la_page_ClicToPay()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);

        var dto = await processor.StartAsync(CompanyId, UserId, order.Id, "MOBILE", CancellationToken.None);

        dto.AlreadyPaid.Should().BeFalse();
        dto.FormUrl.Should().StartWith("https://test.clictopay.com/");
        dto.TestMode.Should().BeTrue();
        var sent = fake.Registered.Single();
        sent.AmountMinor.Should().Be(300000);
        sent.CurrencyCode.Should().Be(788);
        // Gabarit mobile en 404 sur le compte de test : DESKTOP tant qu'il n'est pas activé.
        sent.PageView.Should().Be("DESKTOP");
        sent.Description.Should().Be($"Abonnement GPA (annuel) - commande {order.Id}");
        sent.ReturnUrl.Should().Be($"https://calypso.test/paiement/retour?ref={dto.Reference}");
        sent.FailUrl.Should().Be($"https://calypso.test/paiement/retour?ref={dto.Reference}&echec=1");

        var row = await db.OnlinePayments.AsNoTracking().SingleAsync();
        row.Status.Should().Be(OnlinePayment.StatusPending);
        row.ProviderOrderId.Should().Be(fake.LastOrderId);
        row.AmountMinor.Should().Be(300000);
        row.Currency.Should().Be("TND");
        row.CreatedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task Gabarit_mobile_seulement_s_il_est_active_pour_le_compte_marchand()
    {
        var (db, fake, processor, _) = Setup(mobilePageView: true);
        var order = await PendingOrderAsync(db, 300m);

        await processor.StartAsync(CompanyId, UserId, order.Id, "MOBILE", CancellationToken.None);

        fake.Registered.Single().PageView.Should().Be("MOBILE");
    }

    // Compte en euros : la devise vient de Societe.Settings, que TestGisDbContext n'enregistre pas
    // (type possédé ignoré). Le parcours EUR est couvert par les montants et codes ci-dessus.

    [Fact]
    public async Task Sans_identifiants_marchand_le_paiement_par_carte_est_ferme()
    {
        var (db, fake, processor, _) = Setup(configured: false);
        var order = await PendingOrderAsync(db, 300m);

        await FluentActions.Awaiting(() => processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage("*pas encore disponible*");
        fake.Registered.Should().BeEmpty();
    }

    [Fact]
    public void Societes_pilotes_seulement_quand_la_liste_est_posee()
    {
        var o = Options();
        o.IsEnabledFor(15).Should().BeTrue();          // liste vide : toutes
        o.PilotCompanyIds = "1, 15";
        o.IsEnabledFor(15).Should().BeTrue();
        o.IsEnabledFor(4).Should().BeFalse();
        o.IsEnabledFor(null).Should().BeFalse();
        Options(configured: false).IsEnabledFor(15).Should().BeFalse();
    }

    [Fact]
    public async Task Une_societe_hors_des_pilotes_ne_peut_pas_payer_par_carte()
    {
        var db = TestDbContextFactory.Create();
        var fake = new FakeClicToPay();
        var options = Options();
        options.PilotCompanyIds = "99";
        var processor = new OnlinePaymentProcessor(db, fake, options, Mock.Of<IMediator>(), Mock.Of<ILogger<OnlinePaymentProcessor>>());

        await FluentActions.Awaiting(() => processor.StartAsync(CompanyId, UserId, 1, null, CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage("*pas encore disponible*");
        fake.Registered.Should().BeEmpty();
    }

    [Fact]
    public async Task On_ne_paie_pas_la_commande_d_une_autre_societe()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);

        await FluentActions.Awaiting(() => processor.StartAsync(OtherCompanyId, UserId, order.Id, null, CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage("Commande introuvable.");
        fake.Registered.Should().BeEmpty();
    }

    [Fact]
    public async Task Une_commande_qui_n_est_plus_en_attente_ne_se_paie_pas()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m, status: "cancelled");

        await FluentActions.Awaiting(() => processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage("*plus en attente*");
        fake.Registered.Should().BeEmpty();
    }

    [Fact]
    public async Task Refus_de_ClicToPay_a_la_preparation_clot_la_tentative_et_l_explique()
    {
        var (db, fake, processor, _) = Setup();
        fake.RegisterResponse = new ClicToPayRegisterResult(5, "Accès refusé", null, null);
        var order = await PendingOrderAsync(db, 300m);

        await FluentActions.Awaiting(() => processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage("*Accès refusé*");

        var row = await db.OnlinePayments.AsNoTracking().SingleAsync();
        row.Status.Should().Be(OnlinePayment.StatusError);
        row.Note.Should().Contain("register.do 5");
    }

    [Fact]
    public async Task Une_page_de_paiement_hors_de_ClicToPay_n_est_jamais_suivie()
    {
        var (db, fake, processor, _) = Setup();
        fake.RegisterResponse = new ClicToPayRegisterResult(0, null, "uuid-1", "https://pirate.example/payer");
        var order = await PendingOrderAsync(db, 300m);

        await FluentActions.Awaiting(() => processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None))
            .Should().ThrowAsync<DomainException>();
        (await db.OnlinePayments.AsNoTracking().SingleAsync()).Status.Should().Be(OnlinePayment.StatusError);
    }

    // ── Vérifier le résultat ──

    [Fact]
    public async Task Paiement_verifie_valide_la_commande_et_prolonge_l_abonnement_une_seule_fois()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        var expiryBefore = (await db.Societes.AsNoTracking().SingleAsync(s => s.Id == CompanyId)).SubscriptionExpiresAt!.Value;
        var dto = await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.Statuses[payment.ProviderOrderId!] = Paid(payment);

        var result = await processor.VerifyAsync(payment.Id, CancellationToken.None);

        result.Status.Should().Be(OnlinePayment.StatusPaid);
        result.OrderConfirmed.Should().BeTrue();
        result.CardPanMasked.Should().Be("450921**1119");
        result.Message.Should().Contain("abonnement est activé");
        var savedOrder = await db.SubscriptionOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        savedOrder.Status.Should().Be("confirmed");
        savedOrder.Note.Should().Contain(dto.Reference);
        var expiryAfter = (await db.Societes.AsNoTracking().SingleAsync(s => s.Id == CompanyId)).SubscriptionExpiresAt!.Value;
        expiryAfter.Should().BeCloseTo(expiryBefore.AddDays(365), TimeSpan.FromMinutes(1));

        // Écran de retour rechargé, rapprochement en tâche de fond : rien n'est prolongé deux fois.
        await processor.VerifyAsync(payment.Id, CancellationToken.None);
        await processor.VerifyAsync(payment.Id, CancellationToken.None);
        (await db.Societes.AsNoTracking().SingleAsync(s => s.Id == CompanyId)).SubscriptionExpiresAt!.Value
            .Should().Be(expiryAfter);
    }

    [Fact]
    public async Task Paye_mais_montant_different_est_une_anomalie_et_la_commande_reste_en_attente()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.Statuses[payment.ProviderOrderId!] = Paid(payment) with { Amount = 1000 };

        var result = await processor.VerifyAsync(payment.Id, CancellationToken.None);

        result.Status.Should().Be(OnlinePayment.StatusAnomaly);
        result.OrderConfirmed.Should().BeFalse();
        (await db.SubscriptionOrders.AsNoTracking().SingleAsync()).Status.Should().Be("pending");
        (await db.OnlinePayments.AsNoTracking().SingleAsync()).Note.Should().Contain("montant reçu 1000");
    }

    [Fact]
    public async Task Paye_mais_dans_une_autre_devise_est_une_anomalie()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.Statuses[payment.ProviderOrderId!] = Paid(payment) with { CurrencyCode = 978 };

        (await processor.VerifyAsync(payment.Id, CancellationToken.None)).Status.Should().Be(OnlinePayment.StatusAnomaly);
        (await db.SubscriptionOrders.AsNoTracking().SingleAsync()).Status.Should().Be("pending");
    }

    [Fact]
    public async Task Paiement_refuse_par_la_banque_clot_la_tentative_sans_toucher_la_commande()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.Statuses[payment.ProviderOrderId!] = Status(6, actionCode: 2001, description: "Carte refusée");

        var result = await processor.VerifyAsync(payment.Id, CancellationToken.None);

        result.Status.Should().Be(OnlinePayment.StatusFailed);
        // Le client lit du français ; le motif technique de la banque reste en base pour l'équipe.
        result.Message.Should().Contain("La banque a refusé").And.Contain("Aucun montant").And.NotContain("Carte refusée");
        (await db.OnlinePayments.AsNoTracking().SingleAsync()).ActionDescription.Should().Be("Carte refusée");
        (await db.SubscriptionOrders.AsNoTracking().SingleAsync()).Status.Should().Be("pending");
    }

    [Fact]
    public async Task Page_ouverte_mais_pas_encore_payee_reste_en_attente()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.Statuses[payment.ProviderOrderId!] = Status(0);

        var result = await processor.VerifyAsync(payment.Id, CancellationToken.None);

        result.Status.Should().Be(OnlinePayment.StatusPending);
        (await db.OnlinePayments.AsNoTracking().SingleAsync()).ProviderOrderStatus.Should().Be(0);
    }

    [Fact]
    public async Task ClicToPay_injoignable_ne_decide_rien()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.StatusThrows = true;

        var result = await processor.VerifyAsync(payment.Id, CancellationToken.None);

        result.Status.Should().Be(OnlinePayment.StatusPending);
        result.Message.Should().Contain("n'arrivons pas à joindre");
        (await db.SubscriptionOrders.AsNoTracking().SingleAsync()).Status.Should().Be("pending");
    }

    [Fact]
    public async Task Jamais_payee_en_48_heures_la_tentative_expire()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        await db.OnlinePayments.Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, DateTime.UtcNow.AddHours(-49)));
        fake.Statuses[payment.ProviderOrderId!] = Status(0);

        (await processor.VerifyAsync(payment.Id, CancellationToken.None)).Status.Should().Be(OnlinePayment.StatusExpired);
    }

    [Fact]
    public async Task Paye_alors_que_la_commande_a_ete_annulee_le_signale_pour_remboursement()
    {
        var (db, fake, processor, tenant) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        await new CancelMySubscriptionOrderCommandHandler(db, tenant.Object)
            .Handle(new CancelMySubscriptionOrderCommand(order.Id), CancellationToken.None);
        fake.Statuses[payment.ProviderOrderId!] = Paid(payment);

        var result = await processor.VerifyAsync(payment.Id, CancellationToken.None);

        result.Status.Should().Be(OnlinePayment.StatusPaid);
        result.OrderConfirmed.Should().BeFalse();
        result.Message.Should().Contain("Notre équipe finalise");
        (await db.OnlinePayments.AsNoTracking().SingleAsync()).Note.Should().Contain("à rembourser");
        (await db.SubscriptionOrders.AsNoTracking().SingleAsync()).Status.Should().Be("cancelled");
    }

    [Fact]
    public async Task Une_tentative_deja_payee_dans_un_autre_onglet_n_ouvre_pas_une_seconde_page()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.Statuses[payment.ProviderOrderId!] = Paid(payment);

        var second = await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);

        second.AlreadyPaid.Should().BeTrue();
        second.FormUrl.Should().BeNull();
        fake.Registered.Should().HaveCount(1);
        (await db.SubscriptionOrders.AsNoTracking().SingleAsync()).Status.Should().Be("confirmed");
    }

    [Fact]
    public async Task La_reference_d_une_autre_societe_est_introuvable()
    {
        var (db, fake, processor, _) = Setup();
        var order = await PendingOrderAsync(db, 300m);
        var dto = await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);

        var otherTenant = TestDbContextFactory.CreateMockTenantService(companyId: OtherCompanyId, userId: 99);
        var handler = new VerifyOnlinePaymentCommandHandler(db, processor, otherTenant.Object);

        await FluentActions.Awaiting(() => handler.Handle(new VerifyOnlinePaymentCommand(dto.Reference), CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage("Paiement introuvable.");
    }

    // ── Console admin : paiement de TEST (aucune commande, aucun abonnement) ──

    [Fact]
    public void Identifiants_poses_sans_interrupteur_client_le_bouton_reste_ferme_aux_clients()
    {
        var o = Options();
        o.ClientCheckoutEnabled = false;
        o.IsConfigured.Should().BeTrue();
        o.IsEnabledFor(CompanyId).Should().BeFalse();
    }

    [Fact]
    public async Task Test_admin_ouvre_une_page_ClicToPay_sans_commande_et_revient_sur_la_console()
    {
        var (db, fake, processor, _) = Setup(clientCheckout: false);

        var dto = await processor.StartAdminTestAsync(CompanyId, UserId, 1.5m, "tnd", CancellationToken.None);

        dto.FormUrl.Should().StartWith("https://test.clictopay.com/");
        dto.Reference.Should().StartWith("CLYT-0-");
        var sent = fake.Registered.Single();
        sent.AmountMinor.Should().Be(1500);
        sent.CurrencyCode.Should().Be(788);
        sent.ReturnUrl.Should().Be($"https://calypso.test/admin/paiement-test?ref={dto.Reference}");
        sent.FailUrl.Should().EndWith("&echec=1");
        var row = await db.OnlinePayments.AsNoTracking().SingleAsync();
        row.Kind.Should().Be(OnlinePayment.KindAdminTest);
        row.SubscriptionOrderId.Should().BeNull();
        row.Currency.Should().Be("TND");
    }

    [Fact]
    public async Task Test_admin_paye_est_verifie_sans_rien_valider_ni_prolonger()
    {
        var (db, fake, processor, _) = Setup(clientCheckout: false);
        var order = await PendingOrderAsync(db, 300m);
        var expiryBefore = (await db.Societes.AsNoTracking().SingleAsync(s => s.Id == CompanyId)).SubscriptionExpiresAt;
        await processor.StartAdminTestAsync(CompanyId, UserId, 2m, "TND", CancellationToken.None);
        var payment = await db.OnlinePayments.AsNoTracking().SingleAsync();
        fake.Statuses[payment.ProviderOrderId!] = Paid(payment);

        var result = await processor.VerifyAsync(payment.Id, CancellationToken.None);

        result.Status.Should().Be(OnlinePayment.StatusPaid);
        result.OrderId.Should().BeNull();
        result.OrderConfirmed.Should().BeFalse();
        result.Message.Should().Contain("Paiement de test accepté");
        result.ApprovalCode.Should().Be("365067");
        (await db.SubscriptionOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.Should().Be("pending");
        (await db.Societes.AsNoTracking().SingleAsync(s => s.Id == CompanyId)).SubscriptionExpiresAt.Should().Be(expiryBefore);
        (await db.OnlinePayments.AsNoTracking().SingleAsync()).Note.Should().BeNull();
    }

    [Theory]
    [InlineData(0.05, "TND", "*entre*")]
    [InlineData(2000, "TND", "*entre*")]
    [InlineData(5, "DZD", "*non acceptée*")]
    [InlineData(1.0005, "TND", "*décimales*")]
    public async Task Test_admin_bornes_et_devises(decimal amount, string currency, string message)
    {
        var (_, fake, processor, _) = Setup(clientCheckout: false);
        await FluentActions.Awaiting(() => processor.StartAdminTestAsync(CompanyId, UserId, amount, currency, CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage(message);
        fake.Registered.Should().BeEmpty();
    }

    [Fact]
    public async Task Test_admin_impossible_sans_identifiants()
    {
        var (_, fake, processor, _) = Setup(configured: false);
        await FluentActions.Awaiting(() => processor.StartAdminTestAsync(CompanyId, UserId, 1m, "TND", CancellationToken.None))
            .Should().ThrowAsync<DomainException>().WithMessage("*pas configuré*");
        fake.Registered.Should().BeEmpty();
    }

    [Fact]
    public async Task Les_tests_admin_n_apparaissent_pas_dans_les_paiements_du_client_mais_bien_dans_la_console()
    {
        var (db, _, processor, tenant) = Setup(clientCheckout: true);
        await processor.StartAdminTestAsync(CompanyId, UserId, 1m, "TND", CancellationToken.None);
        var order = await PendingOrderAsync(db, 300m);
        await processor.StartAsync(CompanyId, UserId, order.Id, null, CancellationToken.None);

        var mine = await new GetMyOnlinePaymentsQueryHandler(db, tenant.Object).Handle(new GetMyOnlinePaymentsQuery(), CancellationToken.None);
        mine.Should().ContainSingle().Which.OrderId.Should().Be(order.Id);

        var all = await new GetAdminOnlinePaymentsQueryHandler(db).Handle(new GetAdminOnlinePaymentsQuery(), CancellationToken.None);
        all.Should().HaveCount(2);
        all.Should().Contain(p => p.Kind == OnlinePayment.KindAdminTest && p.CompanyName == "Ma Société");
        (await new GetAdminOnlinePaymentsQueryHandler(db).Handle(new GetAdminOnlinePaymentsQuery(Kind: OnlinePayment.KindAdminTest), CancellationToken.None))
            .Should().ContainSingle();
    }

    // ── Outillage ──

    private static ClicToPayOptions Options(bool configured = true, bool mobilePageView = false, bool clientCheckout = true) => new()
    {
        Enabled = configured,
        BaseUrl = ClicToPayOptions.TestBaseUrl,
        UserName = configured ? "marchand-test" : "",
        Password = configured ? "secret-test" : "",
        ReturnBaseUrl = "https://calypso.test",
        MobilePageView = mobilePageView,
        ClientCheckoutEnabled = clientCheckout
    };

    private static (TestGisDbContext db, FakeClicToPay fake, OnlinePaymentProcessor processor,
        Mock<GisAPI.Domain.Interfaces.ICurrentTenantService> tenant) Setup(bool configured = true, bool mobilePageView = false, bool clientCheckout = true)
    {
        var db = TestDbContextFactory.Create();
        var tenant = TestDbContextFactory.CreateMockTenantService(companyId: CompanyId, userId: UserId);
        db.SubscriptionTypes.Add(new SubscriptionType
        {
            Id = GpaPlanId, Name = "GPA", Code = "plan-basique", IsActive = true,
            MonthlyPrice = 30m, YearlyPrice = 300m
        });
        db.Societes.Add(new Societe
        {
            Id = CompanyId, Name = "Ma Société", Type = "autre", IsActive = true,
            SubscriptionTypeId = GpaPlanId, SubscriptionStatus = "trial",
            SubscriptionExpiresAt = DateTime.UtcNow.AddDays(5)
        });
        db.Societes.Add(new Societe { Id = OtherCompanyId, Name = "Autre", Type = "autre", IsActive = true });
        db.SaveChanges();

        // Le vrai chemin de validation : ConfirmSubscriptionOrder puis RenewSubscription.
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<RenewSubscriptionCommand>(), It.IsAny<CancellationToken>()))
            .Returns<RenewSubscriptionCommand, CancellationToken>((cmd, ct) => new RenewSubscriptionCommandHandler(db).Handle(cmd, ct));
        mediator.Setup(m => m.Send(It.IsAny<ConfirmSubscriptionOrderCommand>(), It.IsAny<CancellationToken>()))
            .Returns<ConfirmSubscriptionOrderCommand, CancellationToken>((cmd, ct) =>
                new ConfirmSubscriptionOrderCommandHandler(db, tenant.Object, mediator.Object,
                    Mock.Of<ILogger<ConfirmSubscriptionOrderCommandHandler>>()).Handle(cmd, ct));

        var fake = new FakeClicToPay();
        var processor = new OnlinePaymentProcessor(db, fake, Options(configured, mobilePageView, clientCheckout), mediator.Object,
            Mock.Of<ILogger<OnlinePaymentProcessor>>());
        return (db, fake, processor, tenant);
    }

    private static async Task<SubscriptionOrder> PendingOrderAsync(TestGisDbContext db, decimal amount, string status = "pending")
    {
        var order = new SubscriptionOrder
        {
            CompanyId = CompanyId, SubscriptionTypeId = GpaPlanId, BillingCycle = "yearly",
            Amount = amount, Status = status, CreatedByUserId = UserId
        };
        db.SubscriptionOrders.Add(order);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return order;
    }

    private static ClicToPayOrderStatus Paid(OnlinePayment p) => new(
        0, "Success", p.OrderNumber, 2, 0, "Request processed successfully",
        p.AmountMinor, ClicToPayAmounts.CurrencyCode(p.Currency), "450921**1119", "365067");

    private static ClicToPayOrderStatus Status(int orderStatus, int actionCode = 0, string? description = null) => new(
        0, "Success", null, orderStatus, actionCode, description, null, null, null, null);

    private sealed class FakeClicToPay : IClicToPayClient
    {
        public List<ClicToPayRegisterRequest> Registered { get; } = new();
        public Dictionary<string, ClicToPayOrderStatus> Statuses { get; } = new();
        public ClicToPayRegisterResult? RegisterResponse { get; set; }
        public bool StatusThrows { get; set; }
        public string? LastOrderId { get; private set; }

        public Task<ClicToPayRegisterResult> RegisterAsync(ClicToPayRegisterRequest request, CancellationToken ct)
        {
            Registered.Add(request);
            if (RegisterResponse != null) return Task.FromResult(RegisterResponse);
            LastOrderId = Guid.NewGuid().ToString();
            return Task.FromResult(new ClicToPayRegisterResult(0, null, LastOrderId,
                $"https://test.clictopay.com/merchants/test/payment_fr.html?mdOrder={LastOrderId}"));
        }

        public Task<ClicToPayOrderStatus> GetOrderStatusAsync(string providerOrderId, CancellationToken ct)
        {
            if (StatusThrows) throw new HttpRequestException("ClicToPay injoignable");
            return Task.FromResult(Statuses.TryGetValue(providerOrderId, out var s)
                ? s
                : new ClicToPayOrderStatus(6, "Commande inconnue", null, null, null, null, null, null, null, null));
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _response;
        public string? Url { get; private set; }
        public Dictionary<string, string> Form { get; } = new();

        public CapturingHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            var body = await request.Content!.ReadAsStringAsync(ct);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                Form[Uri.UnescapeDataString(kv[0].Replace('+', ' '))] = Uri.UnescapeDataString(kv[1].Replace('+', ' '));
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, Encoding.UTF8, "application/json")
            };
        }
    }
}
