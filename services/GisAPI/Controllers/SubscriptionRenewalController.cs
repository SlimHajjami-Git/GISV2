using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Subscriptions.Commands.RenewSubscription;
using GisAPI.Attributes;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/admin/subscriptions")]
[Authorize]
[RequireCompanyAdmin]
public class SubscriptionRenewalController : ControllerBase
{
    private readonly IMediator _mediator;

    public SubscriptionRenewalController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Renouvelle l'abonnement d'une société
    /// </summary>
    [HttpPost("{societeId}/renew")]
    public async Task<ActionResult<SubscriptionRenewalDto>> RenewSubscription(
        int societeId,
        [FromBody] RenewSubscriptionRequest request)
    {
        var result = await _mediator.Send(new RenewSubscriptionCommand(
            societeId,
            request.BillingCycle ?? "yearly",
            request.NewSubscriptionTypeId
        ));

        return Ok(result);
    }
}

public record RenewSubscriptionRequest(
    string? BillingCycle,
    int? NewSubscriptionTypeId
);
