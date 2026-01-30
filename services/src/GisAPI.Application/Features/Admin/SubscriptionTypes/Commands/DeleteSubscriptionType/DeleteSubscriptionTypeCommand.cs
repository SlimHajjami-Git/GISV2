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

        // Check if any societes are using this type
        var societesCount = await _context.Societes
            .CountAsync(s => s.SubscriptionTypeId == request.Id, cancellationToken);

        if (societesCount > 0)
        {
            throw new InvalidOperationException($"Impossible de supprimer: {societesCount} société(s) utilisent ce type");
        }

        _context.SubscriptionTypes.Remove(subscriptionType);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}



