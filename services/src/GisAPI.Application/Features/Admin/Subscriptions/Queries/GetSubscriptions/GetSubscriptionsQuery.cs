using MediatR;

namespace GisAPI.Application.Features.Admin.Subscriptions.Queries.GetSubscriptions;

public record GetSubscriptionsQuery() : IRequest<List<AdminSubscriptionDto>>;
