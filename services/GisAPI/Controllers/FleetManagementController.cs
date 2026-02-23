using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediatR;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Application.Features.FleetManagement.Departments.Commands;
using GisAPI.Application.Features.FleetManagement.Departments.Queries;
using GisAPI.Application.Features.FleetManagement.FuelTypes.Commands;
using GisAPI.Application.Features.FleetManagement.FuelTypes.Queries;
using GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;
using GisAPI.Application.Features.FleetManagement.SpeedLimits.Queries;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/fleet")]
[Authorize]
public class FleetManagementController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IGisDbContext _context;

    public FleetManagementController(IMediator mediator, IGisDbContext context)
    {
        _mediator = mediator;
        _context = context;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    #region Departments

    /// <summary>
    /// Get all departments
    /// </summary>
    [HttpGet("departments")]
    public async Task<ActionResult<List<DepartmentDto>>> GetDepartments(
        [FromQuery] string? searchTerm = null,
        [FromQuery] bool? isActive = null)
    {
        var result = await _mediator.Send(new GetDepartmentsQuery(searchTerm, isActive));
        return Ok(result);
    }

    /// <summary>
    /// Get department by ID with vehicles
    /// </summary>
    [HttpGet("departments/{id}")]
    public async Task<ActionResult<DepartmentDetailDto>> GetDepartment(int id)
    {
        var department = await _mediator.Send(new GetDepartmentByIdQuery(id));
        
        if (department == null)
            return NotFound();

        return Ok(department);
    }

    /// <summary>
    /// Create a new department
    /// </summary>
    [HttpPost("departments")]
    public async Task<ActionResult<int>> CreateDepartment([FromBody] CreateDepartmentRequest request)
    {
        var command = new CreateDepartmentCommand(
            request.Name,
            request.Description,
            request.IsActive
        );

        var departmentId = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetDepartment), new { id = departmentId }, departmentId);
    }

    /// <summary>
    /// Update an existing department
    /// </summary>
    [HttpPut("departments/{id}")]
    public async Task<ActionResult> UpdateDepartment(int id, [FromBody] UpdateDepartmentRequest request)
    {
        try
        {
            await _mediator.Send(new UpdateDepartmentCommand(
                id,
                request.Name,
                request.Description,
                request.IsActive
            ));
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Delete a department
    /// </summary>
    [HttpDelete("departments/{id}")]
    public async Task<ActionResult> DeleteDepartment(int id)
    {
        try
        {
            await _mediator.Send(new DeleteDepartmentCommand(id));
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Assign vehicles to a department
    /// </summary>
    [HttpPut("departments/{id}/vehicles")]
    public async Task<ActionResult> AssignVehiclesToDepartment(int id, [FromBody] AssignVehiclesRequest request)
    {
        try
        {
            await _mediator.Send(new AssignVehiclesToDepartmentCommand(id, request.VehicleIds));
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get vehicles available for department assignment
    /// </summary>
    [HttpGet("departments/{id}/available-vehicles")]
    public async Task<ActionResult<List<VehicleForAssignmentDto>>> GetAvailableVehicles(int id)
    {
        var result = await _mediator.Send(new GetVehiclesForDepartmentQuery(id));
        return Ok(result);
    }

    #endregion

    #region Fuel Types & Pricing

    /// <summary>
    /// Get all fuel types with current pricing
    /// </summary>
    [HttpGet("fuel-types")]
    public async Task<ActionResult<List<FuelTypeDto>>> GetFuelTypes()
    {
        var result = await _mediator.Send(new GetFuelTypesQuery());
        return Ok(result);
    }

    /// <summary>
    /// Get pricing history for a fuel type
    /// </summary>
    [HttpGet("fuel-types/{id}/pricing-history")]
    public async Task<ActionResult<List<FuelPricingDto>>> GetFuelPricingHistory(int id)
    {
        var result = await _mediator.Send(new GetFuelPricingHistoryQuery(id));
        return Ok(result);
    }

    /// <summary>
    /// Set new price for a fuel type
    /// </summary>
    [HttpPost("fuel-types/{id}/price")]
    public async Task<ActionResult<int>> SetFuelPrice(int id, [FromBody] SetFuelPriceRequest request)
    {
        try
        {
            var pricingId = await _mediator.Send(new SetFuelPriceCommand(
                id,
                request.PricePerLiter,
                request.EffectiveFrom
            ));
            return Ok(new { id = pricingId });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    #endregion

    #region Speed Limits & Alerts

    /// <summary>
    /// Set speed limit for a vehicle
    /// </summary>
    [HttpPut("vehicles/{vehicleId}/speed-limit")]
    public async Task<ActionResult> SetVehicleSpeedLimit(int vehicleId, [FromBody] SetSpeedLimitRequest request)
    {
        try
        {
            await _mediator.Send(new SetVehicleSpeedLimitCommand(vehicleId, request.SpeedLimit));
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get speed limit alerts
    /// </summary>
    [HttpGet("speed-alerts")]
    public async Task<ActionResult<SpeedAlertsResult>> GetSpeedAlerts(
        [FromQuery] int? vehicleId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] bool? isAcknowledged = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetSpeedAlertsQuery(
            vehicleId, fromDate, toDate, isAcknowledged, page, pageSize
        ));
        return Ok(result);
    }

    /// <summary>
    /// Acknowledge a speed alert
    /// </summary>
    [HttpPost("speed-alerts/{id}/acknowledge")]
    public async Task<ActionResult> AcknowledgeSpeedAlert(int id)
    {
        try
        {
            await _mediator.Send(new AcknowledgeSpeedAlertCommand(id));
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    #endregion

    #region Part Pricing

    /// <summary>
    /// Get all part pricing for the company
    /// </summary>
    [HttpGet("part-pricing")]
    public async Task<ActionResult<List<PartPricingDto>>> GetPartPricing()
    {
        var companyId = GetCompanyId();

        var pricing = await _context.PartPricings
            .Where(p => p.CompanyId == companyId)
            .Include(p => p.Part)
            .ThenInclude(p => p.Category)
            .OrderBy(p => p.Part.Category.Name)
            .ThenBy(p => p.Part.Name)
            .Select(p => new PartPricingDto(
                p.Id,
                p.PartId,
                p.Part.Name,
                p.Part.Category.Name,
                p.Price,
                p.Supplier,
                p.Notes,
                p.UpdatedAt
            ))
            .ToListAsync();

        return Ok(pricing);
    }

    /// <summary>
    /// Set or update part pricing
    /// </summary>
    [HttpPost("part-pricing")]
    public async Task<ActionResult<PartPricingDto>> SetPartPrice([FromBody] SetPartPriceRequest request)
    {
        var companyId = GetCompanyId();

        var part = await _context.VehicleParts
            .Include(p => p.Category)
            .FirstOrDefaultAsync(p => p.Id == request.PartId);

        if (part == null)
            return BadRequest("Part not found");

        var existing = await _context.PartPricings
            .FirstOrDefaultAsync(p => p.CompanyId == companyId && p.PartId == request.PartId);

        if (existing != null)
        {
            existing.Price = request.Price;
            existing.Supplier = request.Supplier;
            existing.Notes = request.Notes;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new PartPricing
            {
                CompanyId = companyId,
                PartId = request.PartId,
                Price = request.Price,
                Supplier = request.Supplier,
                Notes = request.Notes,
                UpdatedAt = DateTime.UtcNow
            };
            _context.PartPricings.Add(existing);
        }

        await _context.SaveChangesAsync();

        return Ok(new PartPricingDto(
            existing.Id,
            existing.PartId,
            part.Name,
            part.Category.Name,
            existing.Price,
            existing.Supplier,
            existing.Notes,
            existing.UpdatedAt
        ));
    }

    /// <summary>
    /// Delete part pricing
    /// </summary>
    [HttpDelete("part-pricing/{id}")]
    public async Task<ActionResult> DeletePartPrice(int id)
    {
        var companyId = GetCompanyId();

        var pricing = await _context.PartPricings
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId);

        if (pricing == null)
            return NotFound();

        _context.PartPricings.Remove(pricing);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    #endregion
}

// Request DTOs
public record CreateDepartmentRequest(
    string Name,
    string? Description = null,
    bool IsActive = true
);

public record UpdateDepartmentRequest(
    string Name,
    string? Description,
    bool IsActive
);

public record SetFuelPriceRequest(
    decimal PricePerLiter,
    DateTime? EffectiveFrom = null
);

public record SetSpeedLimitRequest(
    int SpeedLimit
);

public record AssignVehiclesRequest(
    List<int> VehicleIds
);

public record SetPartPriceRequest(
    int PartId,
    decimal Price,
    string? Supplier = null,
    string? Notes = null
);

public record PartPricingDto(
    int Id,
    int PartId,
    string PartName,
    string CategoryName,
    decimal Price,
    string? Supplier,
    string? Notes,
    DateTime UpdatedAt
);




