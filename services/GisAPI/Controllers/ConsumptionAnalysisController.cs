using GisAPI.Application.Features.FuelExpenses;
using GisAPI.Application.Features.FuelExpenses.Commands;
using GisAPI.Application.Features.FuelExpenses.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/consumption-analysis")]
[Authorize]
public class ConsumptionAnalysisController : ControllerBase
{
    private readonly IMediator _mediator;

    public ConsumptionAnalysisController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Consommation par tranches de X km (paramétrable) : chaque segment avec
    /// ses litres, L/100km, tonnage déclaré et fiabilité, plus min/max/moyenne.
    /// </summary>
    [HttpGet("segments")]
    public async Task<ActionResult<ConsumptionSegmentsReportDto>> GetSegments(
        [FromQuery] int vehicleId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int segmentKm = 100)
    {
        var result = await _mediator.Send(
            new GetConsumptionSegmentsQuery(vehicleId, startDate, endDate, segmentKm));
        return Ok(result);
    }

    /// <summary>
    /// Comparaison de consommation par tonnage déclaré (segments fiables
    /// groupés par tonnage, écart relatif vs le chargement le plus léger).
    /// </summary>
    [HttpGet("by-tonnage")]
    public async Task<ActionResult<ConsumptionByTonnageReportDto>> GetByTonnage(
        [FromQuery] int vehicleId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int segmentKm = 100)
    {
        var result = await _mediator.Send(
            new GetConsumptionByTonnageQuery(vehicleId, startDate, endDate, segmentKm));
        return Ok(result);
    }

    /// <summary>
    /// Explication IA d'une tranche (clic sur une barre du rapport) : profil de
    /// conduite reconstitué + tonnage + contexte, soumis à Groq. Réponse en
    /// cache 15 min côté serveur.
    /// </summary>
    [HttpPost("explain-segment")]
    public async Task<ActionResult<ExplainSegmentResultDto>> ExplainSegment(
        [FromBody] ExplainConsumptionSegmentQuery query)
    {
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    [HttpGet("load-periods")]
    public async Task<ActionResult<List<VehicleLoadPeriodDto>>> GetLoadPeriods([FromQuery] int vehicleId)
    {
        var result = await _mediator.Send(new GetVehicleLoadPeriodsQuery(vehicleId));
        return Ok(result);
    }

    [HttpPost("load-periods")]
    public async Task<ActionResult<int>> CreateLoadPeriod([FromBody] CreateVehicleLoadPeriodCommand command)
    {
        try
        {
            var id = await _mediator.Send(command);
            return Ok(id);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("load-periods/{id}")]
    public async Task<ActionResult> UpdateLoadPeriod(int id, [FromBody] UpdateVehicleLoadPeriodCommand command)
    {
        if (id != command.Id)
            return BadRequest(new { message = "Id incohérent" });

        try
        {
            var ok = await _mediator.Send(command);
            return ok ? Ok() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("load-periods/{id}")]
    public async Task<ActionResult> DeleteLoadPeriod(int id)
    {
        var ok = await _mediator.Send(new DeleteVehicleLoadPeriodCommand(id));
        return ok ? Ok() : NotFound();
    }
}
