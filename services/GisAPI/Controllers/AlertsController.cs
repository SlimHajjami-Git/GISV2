using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using MediatR;
using GisAPI.Application.Features.Alerts;
using GisAPI.Application.Features.Alerts.Queries.GetAlerts;
using GisAPI.Application.Features.Alerts.Queries.GetAlertUnreadCount;
using GisAPI.Application.Features.Alerts.Commands.ResolveAlert;
using GisAPI.Application.Features.Alerts.Commands.ResolveAllAlerts;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AlertsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AlertsController(IMediator mediator) => _mediator = mediator;

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<AlertDto>>> GetAlerts(
        [FromQuery] bool? resolved = null,
        [FromQuery] string? type = null,
        [FromQuery] int limit = 50)
    {
        var result = await _mediator.Send(new GetAlertsQuery(GetCompanyId(), resolved, type, limit));
        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> GetUnreadCount()
    {
        var count = await _mediator.Send(new GetAlertUnreadCountQuery(GetCompanyId()));
        return Ok(count);
    }

    [HttpPost("{id}/resolve")]
    public async Task<ActionResult> ResolveAlert(int id)
    {
        var found = await _mediator.Send(new ResolveAlertCommand(id, GetUserId()));
        return found ? NoContent() : NotFound();
    }

    [HttpPost("resolve-all")]
    public async Task<ActionResult> ResolveAllAlerts()
    {
        var resolved = await _mediator.Send(new ResolveAllAlertsCommand(GetCompanyId(), GetUserId()));
        return Ok(new { resolved });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AlertDto>> GetAlert(int id)
    {
        var alerts = await _mediator.Send(new GetAlertsQuery(GetCompanyId(), Limit: 1));
        var alert = alerts.FirstOrDefault(a => a.Id == id);
        return alert != null ? Ok(alert) : NotFound();
    }
}
