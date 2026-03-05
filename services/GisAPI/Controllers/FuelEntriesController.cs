using GisAPI.Application.Features.FuelEntries;
using GisAPI.Application.Features.FuelEntries.Commands;
using GisAPI.Application.Features.FuelEntries.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FuelEntriesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FuelEntriesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult> GetFuelEntries(
        [FromQuery] int? fuelTypeId = null,
        [FromQuery] string? vehiclePlate = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetFuelEntriesQuery(fuelTypeId, vehiclePlate, startDate, endDate, page, pageSize));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateFuelEntry([FromBody] CreateFuelEntryRequest request)
    {
        var command = new CreateFuelEntryCommand(
            request.VehiclePlate,
            request.FuelTypeId,
            request.Volume,
            request.PricePerLiter,
            request.InvoiceDate,
            request.StationName,
            request.InvoiceNumber,
            request.Notes,
            request.DriverId,
            request.OdometerKm
        );

        var id = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetFuelEntries), new { id }, new { Id = id });
    }

    [HttpPost("bulk")]
    public async Task<ActionResult> BulkCreateFuelEntries([FromBody] List<CreateFuelEntryRequest> requests)
    {
        var results = new List<object>();
        foreach (var request in requests)
        {
            try
            {
                var command = new CreateFuelEntryCommand(
                    request.VehiclePlate,
                    request.FuelTypeId,
                    request.Volume,
                    request.PricePerLiter,
                    request.InvoiceDate,
                    request.StationName,
                    request.InvoiceNumber,
                    request.Notes,
                    request.DriverId,
                    request.OdometerKm
                );
                var id = await _mediator.Send(command);
                results.Add(new { Id = id, Success = true, Error = (string?)null });
            }
            catch (Exception ex)
            {
                results.Add(new { Id = 0, Success = false, Error = ex.Message });
            }
        }
        var successCount = results.Count(r => ((dynamic)r).Success);
        return Ok(new { Total = requests.Count, Success = successCount, Failed = requests.Count - successCount, Results = results });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteFuelEntry(int id)
    {
        var success = await _mediator.Send(new DeleteFuelEntryCommand(id));
        if (!success)
            return NotFound();
        return NoContent();
    }
}
