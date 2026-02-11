using MediatR;

namespace GisAPI.Application.Features.Admin.Subscriptions.Commands.DeleteSubscription;

public record DeleteSubscriptionCommand(int Id) : IRequest<string?>;
