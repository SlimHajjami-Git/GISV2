using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.SubscriptionTypes.Commands.DeleteSubscriptionType;

public record DeleteSubscriptionTypeCommand(int Id) : IRequest<bool>;

public class DeleteSubscriptionTypeCommandHandler : IRequestHandler<DeleteSubscriptionTypeCommand, bool>
{
    private readonly IGisDbContext _context;

    public DeleteSubscriptionTypeCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(DeleteSubscriptionTypeCommand request, CancellationToken cancellationToken)
    {
        var subscriptionType = await _context.SubscriptionTypes
            .FirstOrDefaultAsync(st => st.Id == request.Id, cancellationToken);

        if (subscriptionType == null)
        {
            throw new InvalidOperationException("Type d'abonnement non trouvé");
        }

        // Check if any subscriptions are using this type
        var subscriptionsCount = await _context.Subscriptions
            .CountAsync(s => s.SubscriptionTypeId == request.Id, cancellationToken);

        if (subscriptionsCount > 0)
        {
            throw new InvalidOperationException($"Impossible de supprimer: {subscriptionsCount} abonnement(s) utilisent ce type");
        }

        _context.SubscriptionTypes.Remove(subscriptionType);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
