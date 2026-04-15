using GisAPI.Application.Features.AccidentEvents.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

/// <summary>
/// Read-only access to persisted accident report data. The only write path
/// today is <c>AccidentNotificationSeeder</c>, which inserts rows on API
/// boot — no POST endpoint yet, that will come with server-side impact
/// detection.
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
}
