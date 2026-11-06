using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Subscriptions.Commands.SubscriptionOrders;
using GisAPI.Domain.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Payments;

/// <summary>Réponse à « payer cette commande » : la page de paiement où envoyer le client.</summary>
public record StartOnlinePaymentDto(
    int PaymentId,
    string Reference,
    string? FormUrl,
    bool AlreadyPaid,
    bool TestMode);

/// <summary>Résultat VÉRIFIÉ d'une tentative de paiement, tel que l'écran de retour l'affiche.</summary>
public record OnlinePaymentResultDto(
    string Reference,
    string Status,
    string Message,
    int? OrderId,
    decimal Amount,
    string Currency,
    string? CardPanMasked,
    string? ApprovalCode,
    DateTime? PaidAt,
    bool OrderConfirmed,
    DateTime? SubscriptionExpiresAt,
    bool TestMode);

/// <summary>
/// Paiement par carte d'une commande d'abonnement, par ClicToPay (migration 067).
///
/// <para>RÈGLE DU MANUEL (étape 12) : le retour du navigateur sur la page « succès » ne prouve
/// rien. Seul getOrderStatusExtended.do fait foi, avec orderStatus = 2 ; Calypso vérifie EN PLUS
/// que le montant, la devise et le numéro de commande renvoyés sont ceux de la tentative. Une
/// tentative vérifiée valide la commande par <see cref="ConfirmSubscriptionOrderCommand"/>, le
/// chemin de la validation manuelle : abonnement prolongé une seule fois, même si l'écran de
/// retour et le rapprochement en tâche de fond vérifient en même temps.</para>
///
/// <para>Utilisé par l'écran (préparer, vérifier au retour) et par
/// OnlinePaymentReconciliationService (client qui ferme l'onglet après avoir payé).</para>
/// </summary>
public class OnlinePaymentProcessor
{
    /// <summary>Au-delà, une tentative jamais payée est close (la page ClicToPay a expiré depuis longtemps).</summary>
    public static readonly TimeSpan PendingLifetime = TimeSpan.FromHours(48);

    private readonly IGisDbContext _db;
    private readonly IClicToPayClient _client;
    private readonly ClicToPayOptions _options;
    private readonly IMediator _mediator;
    private readonly ILogger<OnlinePaymentProcessor> _logger;

    public OnlinePaymentProcessor(
        IGisDbContext db, IClicToPayClient client, ClicToPayOptions options,
        IMediator mediator, ILogger<OnlinePaymentProcessor> logger)
    {
        _db = db;
        _client = client;
        _options = options;
        _mediator = mediator;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Préparer : register.do
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<StartOnlinePaymentDto> StartAsync(
        int companyId, int? userId, int orderId, string? pageView, CancellationToken ct)
    {
        if (!_options.IsEnabledFor(companyId))
            throw new DomainException("Le paiement par carte n'est pas encore disponible. Réglez par virement, nous validons votre commande dès réception.");

        // CompanyId explicite : payer la commande d'une autre société ne dépend d'aucun filtre global.
        var order = await _db.SubscriptionOrders.IgnoreQueryFilters().AsNoTracking()
            .Include(o => o.SubscriptionType)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId, ct)
            ?? throw new DomainException("Commande introuvable.");

        if (order.Status == "confirmed")
            throw new DomainException("Cette commande est déjà réglée.");
        if (order.Status != "pending")
            throw new DomainException("Cette commande n'est plus en attente : rechargez la page.");

        // Une tentative précédente a pu être payée dans un autre onglet sans que le client revienne :
        // on la vérifie avant d'ouvrir une seconde page, sinon il paierait deux fois.
        var since = DateTime.UtcNow - TimeSpan.FromHours(2);
        var previous = await _db.OnlinePayments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.SubscriptionOrderId == order.Id
                        && (p.Status == OnlinePayment.StatusPaid
                            || (p.Status == OnlinePayment.StatusPending && p.CreatedAt >= since)))
            .OrderByDescending(p => p.Id)
            .ToListAsync(ct);
        foreach (var attempt in previous)
        {
            var verified = await VerifyAsync(attempt.Id, ct);
            if (verified.Status == OnlinePayment.StatusPaid)
                return new StartOnlinePaymentDto(attempt.Id, attempt.OrderNumber, null, true, _options.IsTestEnvironment);
        }

        var currency = await ReportCurrency.LoadAsync(_db, companyId, ct);
        var currencyCode = ClicToPayAmounts.CurrencyCode(currency)
            ?? throw new DomainException($"Le paiement par carte n'accepte pas la devise {currency}. Contactez-nous pour régler votre commande.");

        long amountMinor;
        try { amountMinor = ClicToPayAmounts.ToMinorUnits(order.Amount, currency); }
        catch (ArgumentException) { throw new DomainException("Le montant de cette commande ne peut pas être réglé par carte. Contactez-nous."); }
        if (amountMinor <= 0)
            throw new DomainException("Cette commande n'a pas de montant à régler.");

        var payment = new OnlinePayment
        {
            CompanyId = companyId,
            SubscriptionOrderId = order.Id,
            Provider = "clictopay",
            OrderNumber = NewOrderNumber(order.Id),
            AmountMinor = amountMinor,
            Currency = currency.ToUpperInvariant(),
            Status = OnlinePayment.StatusPending,
            CreatedByUserId = userId
        };
        _db.OnlinePayments.Add(payment);
        await _db.SaveChangesAsync(ct);

        var returnUrl = $"{ReturnBase()}/paiement/retour?ref={Uri.EscapeDataString(payment.OrderNumber)}";
        var request = new ClicToPayRegisterRequest(
            payment.OrderNumber, amountMinor, currencyCode,
            returnUrl, returnUrl + "&echec=1",
            Describe(order),
            string.IsNullOrWhiteSpace(_options.Language) ? "fr" : _options.Language,
            _options.MobilePageView && string.Equals(pageView, "MOBILE", StringComparison.OrdinalIgnoreCase) ? "MOBILE" : "DESKTOP");

        return await RegisterAsync(payment, request, $"commande #{order.Id}", ct);
    }

    private string ReturnBase() =>
        (string.IsNullOrWhiteSpace(_options.ReturnBaseUrl) ? AppUrls.PublicBaseUrl : _options.ReturnBaseUrl!).TrimEnd('/');

    /// <summary>register.do pour une tentative déjà enregistrée ; la clôt en « error » si ClicToPay refuse.</summary>
    private async Task<StartOnlinePaymentDto> RegisterAsync(
        OnlinePayment payment, ClicToPayRegisterRequest request, string label, CancellationToken ct)
    {
        ClicToPayRegisterResult result;
        try
        {
            result = await _client.RegisterAsync(request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "ClicToPay register.do injoignable ({Label}, {Reference})", label, payment.OrderNumber);
            await CloseAsync(payment.Id, OnlinePayment.StatusError, "ClicToPay injoignable à la préparation du paiement.", ct);
            throw new DomainException("Le service de paiement ne répond pas. Réessayez dans quelques minutes.");
        }

        if (!result.Succeeded || !IsClicToPayPage(result.FormUrl))
        {
            var why = result.Succeeded ? "page de paiement inattendue" : $"register.do {result.ErrorCode} : {result.ErrorMessage}";
            _logger.LogError("ClicToPay a refusé {Label} ({Reference}) : {Why}", label, payment.OrderNumber, why);
            await CloseAsync(payment.Id, OnlinePayment.StatusError, Truncate(why, 512), ct);
            throw new DomainException(result.Succeeded
                ? "Le service de paiement a renvoyé une réponse inattendue. Contactez-nous."
                : $"Le paiement n'a pas pu être préparé ({result.ErrorMessage ?? "erreur " + result.ErrorCode}). Réessayez ou contactez-nous.");
        }

        await _db.OnlinePayments.IgnoreQueryFilters()
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ProviderOrderId, result.OrderId)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);

        _logger.LogInformation(
            "Paiement ClicToPay préparé : {Label}, société {CompanyId}, {Reference}, {Amount} {Currency}",
            label, payment.CompanyId, payment.OrderNumber, payment.AmountMinor, payment.Currency);

        return new StartOnlinePaymentDto(payment.Id, payment.OrderNumber, result.FormUrl, false, _options.IsTestEnvironment);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Paiement de TEST depuis la console admin (aucune commande, aucun abonnement)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Bornes d'un paiement de test : assez pour éprouver, trop peu pour un vrai achat.</summary>
    public const decimal AdminTestMinAmount = 0.1m;
    public const decimal AdminTestMaxAmount = 1000m;

    /// <summary>
    /// Ouvre une page ClicToPay pour un montant libre, depuis la console admin, afin d'éprouver
    /// le compte marchand (identifiants, cartes de test, retours). Vérifiée ensuite comme toute
    /// tentative ; une tentative de test ne valide ni ne prolonge rien.
    /// </summary>
    public async Task<StartOnlinePaymentDto> StartAdminTestAsync(
        int companyId, int? userId, decimal amount, string? currency, CancellationToken ct)
    {
        if (!_options.IsConfigured)
            throw new DomainException("ClicToPay n'est pas configuré sur ce serveur (identifiants marchand absents).");

        var cur = string.IsNullOrWhiteSpace(currency) ? "TND" : currency.Trim().ToUpperInvariant();
        var currencyCode = ClicToPayAmounts.CurrencyCode(cur)
            ?? throw new DomainException($"Devise {cur} non acceptée par ClicToPay (TND, EUR ou USD).");
        if (amount < AdminTestMinAmount || amount > AdminTestMaxAmount)
            throw new DomainException($"Montant de test entre {AdminTestMinAmount} et {AdminTestMaxAmount} {cur}.");

        long amountMinor;
        try { amountMinor = ClicToPayAmounts.ToMinorUnits(amount, cur); }
        catch (ArgumentException) { throw new DomainException($"Montant non exprimable en {cur} (trop de décimales)."); }

        var payment = new OnlinePayment
        {
            CompanyId = companyId,
            SubscriptionOrderId = null,
            Kind = OnlinePayment.KindAdminTest,
            Provider = "clictopay",
            OrderNumber = NewOrderNumber(0, "CLYT"),
            AmountMinor = amountMinor,
            Currency = cur,
            Status = OnlinePayment.StatusPending,
            CreatedByUserId = userId
        };
        _db.OnlinePayments.Add(payment);
        await _db.SaveChangesAsync(ct);

        // Retour sur la console admin (session admin), pas sur l'écran client.
        var returnUrl = $"{ReturnBase()}/admin/paiement-test?ref={Uri.EscapeDataString(payment.OrderNumber)}";
        var request = new ClicToPayRegisterRequest(
            payment.OrderNumber, amountMinor, currencyCode,
            returnUrl, returnUrl + "&echec=1",
            "Test Calypso - console d'administration",
            string.IsNullOrWhiteSpace(_options.Language) ? "fr" : _options.Language,
            "DESKTOP");
        return await RegisterAsync(payment, request, "test admin", ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Vérifier : getOrderStatusExtended.do
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vérifie une tentative auprès de ClicToPay et en tire la conséquence (commande validée,
    /// refus, anomalie). Idempotent : une tentative déjà close est relue, jamais rejouée.
    /// </summary>
    public async Task<OnlinePaymentResultDto> VerifyAsync(int paymentId, CancellationToken ct)
    {
        var payment = await LoadAsync(paymentId, ct);

        if (payment.Status == OnlinePayment.StatusPaid)
        {
            await EnsureOrderConfirmedAsync(payment, ct);
            return await ResultAsync(paymentId, ct);
        }
        if (payment.Status != OnlinePayment.StatusPending || string.IsNullOrEmpty(payment.ProviderOrderId))
            return await ResultAsync(paymentId, ct);

        var now = DateTime.UtcNow;
        ClicToPayOrderStatus status;
        try
        {
            status = await _client.GetOrderStatusAsync(payment.ProviderOrderId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Rien n'est décidé sur une panne : la tentative reste en attente, revérifiée plus tard.
            _logger.LogWarning(ex, "ClicToPay getOrderStatusExtended.do injoignable ({Reference})", payment.OrderNumber);
            return await ResultAsync(paymentId, ct, unreachable: true);
        }

        if (status.ErrorCode != 0 || status.OrderStatus is null)
        {
            _logger.LogWarning("ClicToPay statut illisible pour {Reference} : {Code} {Message}",
                payment.OrderNumber, status.ErrorCode, status.ErrorMessage);
            if (now - payment.CreatedAt > PendingLifetime)
                await CloseAsync(payment.Id, OnlinePayment.StatusExpired, "Jamais payée dans les 48 h.", ct);
            else
                await TouchAsync(payment.Id, status, ct);
            return await ResultAsync(paymentId, ct);
        }

        switch (status.OrderStatus)
        {
            case ClicToPayAmounts.OrderStatusPaid:
            {
                var expectedCode = ClicToPayAmounts.CurrencyCode(payment.Currency);
                var anomaly =
                    status.Amount != payment.AmountMinor ? $"montant reçu {status.Amount}, attendu {payment.AmountMinor}"
                    : status.CurrencyCode is int code && code != expectedCode ? $"devise reçue {code}, attendue {expectedCode}"
                    : !string.IsNullOrEmpty(status.OrderNumber) && status.OrderNumber != payment.OrderNumber
                        ? $"numéro reçu {status.OrderNumber}, attendu {payment.OrderNumber}"
                    : null;

                if (anomaly != null)
                {
                    _logger.LogError("Paiement ClicToPay INCOHÉRENT {Reference} : {Anomaly}. Commande non validée.",
                        payment.OrderNumber, anomaly);
                    await CloseAsync(payment.Id, OnlinePayment.StatusAnomaly,
                        Truncate($"Payé mais incohérent ({anomaly}) : commande non validée, à vérifier.", 512), ct, status);
                    return await ResultAsync(paymentId, ct);
                }

                if (await CloseAsync(payment.Id, OnlinePayment.StatusPaid, null, ct, status, paidAt: now))
                    _logger.LogInformation("Paiement ClicToPay vérifié : {Reference}, commande #{OrderId}",
                        payment.OrderNumber, payment.SubscriptionOrderId);

                var paid = await LoadAsync(paymentId, ct);
                if (paid.Status == OnlinePayment.StatusPaid)
                    await EnsureOrderConfirmedAsync(paid, ct);
                return await ResultAsync(paymentId, ct);
            }

            // 3 annulée par le marchand, 4 remboursée, 6 refusée : la tentative est close.
            case 3:
            case 4:
            case 6:
                await CloseAsync(payment.Id, OnlinePayment.StatusFailed, null, ct, status);
                return await ResultAsync(paymentId, ct);

            // 0 enregistrée (page ouverte, pas encore payée), 1 pré-autorisation, 5 3-D Secure en cours.
            default:
                if (now - payment.CreatedAt > PendingLifetime)
                    await CloseAsync(payment.Id, OnlinePayment.StatusExpired, "Jamais payée dans les 48 h.", ct, status);
                else
                    await TouchAsync(payment.Id, status, ct);
                return await ResultAsync(paymentId, ct);
        }
    }

    /// <summary>
    /// Une tentative payée valide sa commande, une seule fois. Si la commande n'est plus en attente
    /// (annulée, rejetée, déjà validée par un autre règlement), l'argent est quand même encaissé :
    /// la tentative le dit dans sa note, pour que l'équipe rembourse ou applique à la main.
    /// </summary>
    private async Task EnsureOrderConfirmedAsync(OnlinePayment payment, CancellationToken ct)
    {
        // Paiement de test de la console admin : aucune commande à valider.
        if (payment.SubscriptionOrderId is null) return;
        var order = await _db.SubscriptionOrders.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == payment.SubscriptionOrderId, ct);
        if (order == null) return;

        var marker = OrderNoteFor(payment);
        if (order.Status == "pending")
        {
            try
            {
                await _mediator.Send(new ConfirmSubscriptionOrderCommand(order.Id, marker), ct);
                return;
            }
            catch (DomainException ex)
            {
                // Traitée entre-temps : relire pour savoir par qui.
                order = await _db.SubscriptionOrders.IgnoreQueryFilters().AsNoTracking()
                    .FirstAsync(o => o.Id == payment.SubscriptionOrderId, ct);
                _logger.LogWarning("Commande #{OrderId} non validée par le paiement {Reference} : {Message}",
                    order.Id, payment.OrderNumber, ex.Message);
            }
        }

        // Validée PAR CE paiement : la note de la commande porte sa référence.
        if (order.Status == "confirmed" && (order.Note ?? "").Contains(payment.OrderNumber, StringComparison.Ordinal))
            return;
        if (!string.IsNullOrEmpty(payment.Note))
            return;

        var note = order.Status == "confirmed"
            ? "Payé alors que la commande était déjà validée : paiement en double, à rembourser."
            : $"Payé alors que la commande était « {order.Status} » : à rembourser ou à appliquer à la main.";
        _logger.LogError("Paiement ClicToPay {Reference} À RÉGULARISER : {Note}", payment.OrderNumber, note);
        await _db.OnlinePayments.IgnoreQueryFilters()
            .Where(p => p.Id == payment.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Note, note)
                .SetProperty(p => p.UpdatedAt, DateTime.UtcNow), ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Outils
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Note posée sur la commande validée par ce paiement (montrée à la plateforme).</summary>
    public static string OrderNoteFor(OnlinePayment payment) =>
        $"Payée en ligne par carte (ClicToPay, réf. {payment.OrderNumber}).";

    /// <summary>Notre numéro de commande chez ClicToPay : unique, 32 caractères au plus (AN..32).</summary>
    public static string NewOrderNumber(int orderId, string prefix = "CLY")
    {
        var head = $"{prefix}-{orderId}-";
        var tail = Guid.NewGuid().ToString("N");
        return (head + tail)[..Math.Min(32, head.Length + tail.Length)];
    }

    /// <summary>La page de paiement doit être chez ClicToPay, en HTTPS : jamais une redirection ailleurs.</summary>
    public static bool IsClicToPayPage(string? formUrl) =>
        Uri.TryCreate(formUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("clictopay.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".clictopay.com", StringComparison.OrdinalIgnoreCase));

    private static string Describe(SubscriptionOrder order)
    {
        var cycle = order.BillingCycle switch
        {
            "monthly" => "mensuel",
            "quarterly" => "trimestriel",
            "semiannual" => "semestriel",
            "yearly" => "annuel",
            _ => order.BillingCycle
        };
        return Truncate(Ascii($"Abonnement {order.SubscriptionType?.Name} ({cycle}) - commande {order.Id}"), 512);
    }

    /// <summary>
    /// Texte sans accents : ClicToPay abîme les caractères non ASCII de la description (essai réel
    /// du 06/11/2026 : « intégration » relu « int?gration » dans le statut et le portail marchand).
    /// </summary>
    public static string Ascii(string value)
    {
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(c <= 0x7F ? c : c switch { '—' or '–' => '-', '’' => '\'', _ => ' ' });
        }
        return sb.ToString();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    private Task<OnlinePayment> LoadAsync(int paymentId, CancellationToken ct) =>
        _db.OnlinePayments.IgnoreQueryFilters().AsNoTracking().FirstAsync(p => p.Id == paymentId, ct);

    /// <summary>
    /// Sortie de « pending » en UNE écriture conditionnelle : de l'écran de retour et du
    /// rapprochement qui vérifient en même temps, un seul clôt la tentative.
    /// </summary>
    private async Task<bool> CloseAsync(int paymentId, string status, string? note, CancellationToken ct,
        ClicToPayOrderStatus? provider = null, DateTime? paidAt = null)
    {
        var now = DateTime.UtcNow;
        // Valeurs sorties de la requête : EF évaluerait provider.X même quand provider est null.
        var fromProvider = provider != null;
        var orderStatus = provider?.OrderStatus;
        var actionCode = provider?.ActionCode;
        var actionDescription = provider?.ActionCodeDescription;
        var pan = provider?.CardPanMasked;
        var approval = provider?.ApprovalCode;
        var updated = await _db.OnlinePayments.IgnoreQueryFilters()
            .Where(p => p.Id == paymentId && p.Status == OnlinePayment.StatusPending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, status)
                .SetProperty(p => p.Note, p => note ?? p.Note)
                .SetProperty(p => p.ProviderOrderStatus, p => fromProvider ? orderStatus : p.ProviderOrderStatus)
                .SetProperty(p => p.ActionCode, p => fromProvider ? actionCode : p.ActionCode)
                .SetProperty(p => p.ActionDescription, p => fromProvider ? actionDescription : p.ActionDescription)
                .SetProperty(p => p.CardPanMasked, p => fromProvider ? pan : p.CardPanMasked)
                .SetProperty(p => p.ApprovalCode, p => fromProvider ? approval : p.ApprovalCode)
                .SetProperty(p => p.PaidAt, p => paidAt ?? p.PaidAt)
                .SetProperty(p => p.LastCheckedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
        return updated == 1;
    }

    private Task TouchAsync(int paymentId, ClicToPayOrderStatus status, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return _db.OnlinePayments.IgnoreQueryFilters()
            .Where(p => p.Id == paymentId && p.Status == OnlinePayment.StatusPending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ProviderOrderStatus, status.OrderStatus)
                .SetProperty(p => p.ActionCode, status.ActionCode)
                .SetProperty(p => p.ActionDescription, status.ActionCodeDescription)
                .SetProperty(p => p.LastCheckedAt, now)
                .SetProperty(p => p.UpdatedAt, now), ct);
    }

    private async Task<OnlinePaymentResultDto> ResultAsync(int paymentId, CancellationToken ct, bool unreachable = false)
    {
        var p = await LoadAsync(paymentId, ct);
        var order = p.SubscriptionOrderId is int orderId
            ? await _db.SubscriptionOrders.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct)
            : null;
        var orderConfirmed = order?.Status == "confirmed" && (order.Note ?? "").Contains(p.OrderNumber, StringComparison.Ordinal);
        var expires = orderConfirmed
            ? await _db.Societes.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.Id == p.CompanyId).Select(s => s.SubscriptionExpiresAt).FirstOrDefaultAsync(ct)
            : null;

        return new OnlinePaymentResultDto(
            p.OrderNumber, p.Status, Message(p, orderConfirmed, unreachable), p.SubscriptionOrderId,
            ClicToPayAmounts.FromMinorUnits(p.AmountMinor, p.Currency), p.Currency,
            p.CardPanMasked, p.ApprovalCode, p.PaidAt, orderConfirmed, expires, _options.IsTestEnvironment);
    }

    /// <summary>Ce que le client lit sur l'écran de retour, en français.</summary>
    public static string Message(OnlinePayment p, bool orderConfirmed, bool unreachable = false) => p.Status switch
    {
        OnlinePayment.StatusPaid when p.Kind == OnlinePayment.KindAdminTest =>
            "Paiement de test accepté et vérifié auprès de ClicToPay.",
        OnlinePayment.StatusPaid when orderConfirmed =>
            "Merci pour votre règlement : votre abonnement est activé.",
        OnlinePayment.StatusPaid =>
            "Votre règlement est bien reçu. Notre équipe finalise votre commande et vous prévient dans l'application.",
        // Le motif de la banque arrive en anglais technique (« Rejected Authorization - Issuer bank is
        // not able… ») : il reste en base pour l'équipe, le client lit une phrase en français.
        OnlinePayment.StatusFailed =>
            "La banque a refusé le paiement. Aucun montant n'a été débité. Vérifiez votre carte ou essayez-en une autre.",
        OnlinePayment.StatusError =>
            "Le paiement n'a pas pu être préparé. Aucun montant n'a été débité.",
        OnlinePayment.StatusExpired =>
            "Cette page de paiement a expiré sans règlement.",
        OnlinePayment.StatusAnomaly =>
            "Votre paiement nécessite une vérification par notre équipe. Nous vous contactons rapidement.",
        _ when unreachable =>
            "Nous n'arrivons pas à joindre le service de paiement pour vérifier. Réessayez dans un instant.",
        _ =>
            "Votre paiement n'est pas encore confirmé par la banque."
    };
}
