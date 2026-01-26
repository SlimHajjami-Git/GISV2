using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Suppliers.Queries;
using GisAPI.Application.Features.Suppliers.Commands;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly IMediator _mediator;

    public SuppliersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all suppliers with optional filtering
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SupplierDto>>> GetSuppliers(
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? type = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetSuppliersQuery(searchTerm, type, isActive, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get supplier by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SupplierDto>> GetSupplier(int id)
    {
        var supplier = await _mediator.Send(new GetSupplierByIdQuery(id));
        
        if (supplier == null)
            return NotFound();

        return Ok(supplier);
    }

    /// <summary>
    /// Get supplier statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<SupplierStatsDto>> GetStats()
    {
        var stats = await _mediator.Send(new GetSupplierStatsQuery());
        return Ok(stats);
    }

    /// <summary>
    /// Get garages only (suppliers with type = 'garage')
    /// </summary>
    [HttpGet("garages")]
    public async Task<ActionResult<List<SupplierDto>>> GetGarages(
        [FromQuery] string? searchTerm = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetGaragesQuery(searchTerm, isActive, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Create a new supplier
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<int>> CreateSupplier([FromBody] CreateSupplierRequest request)
    {
        var command = new CreateSupplierCommand(
            request.Name,
            request.Type,
            request.Address,
            request.City,
            request.PostalCode,
            request.ContactName,
            request.Phone,
            request.Email,
            request.Website,
            request.TaxId,
            request.BankAccount,
            request.PaymentTerms,
            request.DiscountPercent,
            request.Rating,
            request.Notes,
            request.IsActive,
            request.Services
        );

        var supplierId = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetSupplier), new { id = supplierId }, supplierId);
    }

    /// <summary>
    /// Update an existing supplier
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateSupplier(int id, [FromBody] UpdateSupplierRequest request)
    {
        var command = new UpdateSupplierCommand(
            id,
            request.Name,
            request.Type,
            request.Address,
            request.City,
            request.PostalCode,
            request.ContactName,
            request.Phone,
            request.Email,
            request.Website,
            request.TaxId,
            request.BankAccount,
            request.PaymentTerms,
            request.DiscountPercent,
            request.Rating,
            request.Notes,
            request.IsActive,
            request.Services
        );

        var success = await _mediator.Send(command);
        
        if (!success)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Delete a supplier
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteSupplier(int id)
    {
        var success = await _mediator.Send(new DeleteSupplierCommand(id));
        
        if (!success)
            return NotFound();

        return NoContent();
    }

    /// <summary>
    /// Get services for a specific supplier
    /// </summary>
    [HttpGet("{id}/services")]
    public async Task<ActionResult<List<string>>> GetSupplierServices(int id)
    {
        var supplier = await _mediator.Send(new GetSupplierByIdQuery(id));
        
        if (supplier == null)
            return NotFound();

        return Ok(supplier.Services);
    }

    /// <summary>
    /// Update services for a specific supplier
    /// </summary>
    [HttpPut("{id}/services")]
    public async Task<ActionResult> UpdateSupplierServices(int id, [FromBody] UpdateServicesRequest request)
    {
        var success = await _mediator.Send(new UpdateSupplierServicesCommand(id, request.Services));
        
        if (!success)
            return NotFound();

        return NoContent();
    }
}

// Request DTOs
public record CreateSupplierRequest(
    string Name,
    string Type,
    string? Address,
    string? City,
    string? PostalCode,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Website,
    string? TaxId,
    string? BankAccount,
    string? PaymentTerms,
    decimal? DiscountPercent,
    decimal? Rating,
    string? Notes,
    bool IsActive = true,
    List<string>? Services = null
);

public record UpdateSupplierRequest(
    string? Name,
    string? Type,
    string? Address,
    string? City,
    string? PostalCode,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Website,
    string? TaxId,
    string? BankAccount,
    string? PaymentTerms,
    decimal? DiscountPercent,
    decimal? Rating,
    string? Notes,
    bool? IsActive,
    List<string>? Services
);

public record UpdateServicesRequest(List<string> Services);
