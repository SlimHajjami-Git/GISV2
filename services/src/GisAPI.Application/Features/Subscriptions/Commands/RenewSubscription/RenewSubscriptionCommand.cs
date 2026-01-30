using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Subscriptions.Commands.RenewSubscription;

public record RenewSubscriptionCommand(
    int SocieteId,
    string BillingCycle,
    int? NewSubscriptionTypeId = null
) : ICommand<SubscriptionRenewalDto>;

public record SubscriptionRenewalDto(
    int SocieteId,
    string SocieteName,
    DateTime NewExpirationDate,
    string BillingCycle,
    decimal Amount,
    string Status
);



