using GisAPI.Application.Features.FuelExpenses;
using GisAPI.Application.Features.FuelExpenses.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FuelExpensesController : ControllerBase
{
    private readonly IMediator _mediator;

    public FuelExpensesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get fleet-wide fuel expense statistics
    /// </summary>
    [HttpGet("statistics")]
    public async Task<ActionResult<FleetFuelStatisticsDto>> GetStatistics(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int? vehicleId,
        [FromQuery] string? fuelType)
    {
        var query = new GetFuelExpenseStatisticsQuery(startDate, endDate, vehicleId, fuelType);
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Get fuel expense for a specific vehicle
    /// </summary>
    [HttpGet("vehicle/{vehicleId}")]
    public async Task<ActionResult<VehicleFuelExpenseDto>> GetVehicleExpense(
        int vehicleId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var query = new GetVehicleFuelExpenseQuery(vehicleId, startDate, endDate);
        var result = await _mediator.Send(query);
        
        if (result == null)
            return NotFound();
            
        return Ok(result);
    }

    /// <summary>
    /// Get current fuel prices for the company
    /// </summary>
    [HttpGet("prices")]
    public async Task<ActionResult<List<FuelPriceDto>>> GetCurrentPrices()
    {
        var query = new GetCurrentFuelPricesQuery();
        var result = await _mediator.Send(query);
        return Ok(result);
    }
}
