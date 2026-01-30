using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Subscriptions.Commands.RenewSubscription;

public class RenewSubscriptionCommandHandler : IRequestHandler<RenewSubscriptionCommand, SubscriptionRenewalDto>
{
    private readonly IGisDbContext _context;

    public RenewSubscriptionCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SubscriptionRenewalDto> Handle(RenewSubscriptionCommand request, CancellationToken ct)
    {
        var societe = await _context.Societes
            .Include(s => s.SubscriptionType)
            .FirstOrDefaultAsync(s => s.Id == request.SocieteId, ct)
            ?? throw new NotFoundException("Societe", request.SocieteId);

        // Update subscription type if requested
        if (request.NewSubscriptionTypeId.HasValue)
        {
            var newType = await _context.SubscriptionTypes
                .FirstOrDefaultAsync(st => st.Id == request.NewSubscriptionTypeId, ct)
                ?? throw new NotFoundException("SubscriptionType", request.NewSubscriptionTypeId);
            
            societe.SubscriptionTypeId = newType.Id;
            societe.SubscriptionType = newType;
        }

        // Calculate new expiration date and amount
        var subscriptionType = societe.SubscriptionType;
        decimal amount = 0;
        int daysToAdd = 365;

        if (subscriptionType != null)
        {
            switch (request.BillingCycle.ToLower())
            {
                case "monthly":
                    amount = subscriptionType.MonthlyPrice;
                    daysToAdd = subscriptionType.MonthlyDurationDays > 0 ? subscriptionType.MonthlyDurationDays : 30;
                    break;
                case "quarterly":
                    amount = subscriptionType.QuarterlyPrice;
                    daysToAdd = subscriptionType.QuarterlyDurationDays > 0 ? subscriptionType.QuarterlyDurationDays : 90;
                    break;
                case "yearly":
                default:
                    amount = subscriptionType.YearlyPrice;
                    daysToAdd = subscriptionType.YearlyDurationDays > 0 ? subscriptionType.YearlyDurationDays : 365;
                    break;
            }
        }

        // Calculate new expiration from current expiration or now
        var startDate = societe.SubscriptionExpiresAt > DateTime.UtcNow 
            ? societe.SubscriptionExpiresAt.Value 
            : DateTime.UtcNow;

        societe.SubscriptionExpiresAt = startDate.AddDays(daysToAdd);
        societe.BillingCycle = request.BillingCycle;
        societe.SubscriptionStatus = "active";
        societe.IsActive = true;
        societe.LastPaymentAt = DateTime.UtcNow;
        societe.NextPaymentAmount = amount;
        societe.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return new SubscriptionRenewalDto(
            societe.Id,
            societe.Name,
            societe.SubscriptionExpiresAt.Value,
            societe.BillingCycle,
            amount,
            "renewed"
        );
    }
}



