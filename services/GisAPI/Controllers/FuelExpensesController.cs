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
    /// Anti-fraud report: real (billed) fuel vs GPS/boitier-consumed fuel, per vehicle.
    /// </summary>
    [HttpGet("comparison")]
    public async Task<ActionResult<FuelComparisonReportDto>> GetComparison(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int? vehicleId)
    {
        var query = new GetFuelComparisonReportQuery(startDate, endDate, vehicleId);
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// Per-vehicle fuel audit: fuel-level curve + detected refills + per-fill verification
    /// (each billed card fill matched against an actual tank refill).
    /// </summary>
    [HttpGet("vehicle-audit")]
    public async Task<ActionResult<FuelAuditReportDto>> GetVehicleAudit(
        [FromQuery] int vehicleId,
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        var query = new GetFuelAuditReportQuery(vehicleId, startDate, endDate);
        var result = await _mediator.Send(query);
        return Ok(result);
    }

    /// <summary>
    /// GPS-independent fuel consumption ("Carburant réel"): L/100km, cost/km and
    /// distance derived purely from manually entered fill-ups (full-to-full).
    /// Works for vehicles WITHOUT a GPS box — for the "Gestion sans GPS" clients.
    /// </summary>
    [HttpGet("real-consumption")]
    public async Task<ActionResult<RealFuelConsumptionReportDto>> GetRealConsumption(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate,
        [FromQuery] int? vehicleId)
    {
        var query = new GetRealFuelConsumptionQuery(startDate, endDate, vehicleId);
        var result = await _mediator.Send(query);
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
