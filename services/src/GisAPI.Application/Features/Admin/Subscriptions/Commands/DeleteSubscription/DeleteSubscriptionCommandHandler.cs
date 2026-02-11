using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Subscriptions.Commands.DeleteSubscription;

public class DeleteSubscriptionCommandHandler : IRequestHandler<DeleteSubscriptionCommand, string?>
{
    private readonly IGisDbContext _context;

    public DeleteSubscriptionCommandHandler(IGisDbContext context) => _context = context;

    public async Task<string?> Handle(DeleteSubscriptionCommand request, CancellationToken ct)
    {
        var subscription = await _context.SubscriptionTypes.FindAsync(new object[] { request.Id }, ct);
        if (subscription == null) return "not_found";

        var companiesUsing = await _context.Societes.CountAsync(c => c.SubscriptionTypeId == request.Id, ct);
        if (companiesUsing > 0)
            return $"Impossible de supprimer: {companiesUsing} société(s) utilisent cet abonnement";

        _context.SubscriptionTypes.Remove(subscription);
        await _context.SaveChangesAsync(ct);
        return null; // success
    }
}
