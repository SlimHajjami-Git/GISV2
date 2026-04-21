using GisAPI.Application.Features.AccidentEvents.Commands;
using GisAPI.Application.Features.AccidentEvents.Queries;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Access to persisted accident report data. Read via <c>GET {id}</c>,
/// decision workflow via <c>POST {id}/confirm</c> and
/// <c>POST {id}/dismiss</c> (both admin-initiated from the blocking
/// modal on the frontend).
/// </summary>
[ApiController]
[Route("api/accident-reports")]
[Authorize]
public class AccidentReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AccidentReportsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get the persisted accident report by id. Returns 404 if the row
    /// doesn't exist OR belongs to another company (tenant filter).
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<AccidentReportDto>> GetReport(int id, CancellationToken ct)
    {
        var report = await _mediator.Send(new GetAccidentReportQuery(id), ct);
        if (report == null) return NotFound();
        return Ok(report);
    }

    /// <summary>
    /// Confirm the accident — stamps <c>status = 'confirmed'</c>, records
    /// the deciding admin, and fans an audit notification to the other
    /// admins. Idempotent (a second call on an already-decided event is a
    /// no-op). 404 when the event doesn't exist or belongs to another
    /// company.
    /// </summary>
    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> Confirm(int id, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new ConfirmAccidentCommand(id), ct);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Dismiss the accident as a false positive — stamps
    /// <c>status = 'dismissed'</c> and broadcasts a "Choc violent détecté —
    /// dégâts possibles" notification to the OTHER admins of the company
    /// (no driver, no email). Idempotent. 404 on missing / cross-tenant.
    /// </summary>
    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id, CancellationToken ct)
    {
        try
        {
            await _mediator.Send(new DismissAccidentCommand(id), ct);
            return NoContent();
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
    }
}
