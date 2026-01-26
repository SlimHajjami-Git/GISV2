using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.MaintenanceTemplates.Queries;
using GisAPI.Application.Features.MaintenanceTemplates.Commands;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/maintenance-templates")]
[Authorize]
public class MaintenanceTemplatesController : ControllerBase
{
    private readonly IMediator _mediator;

    public MaintenanceTemplatesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all maintenance templates
    /// </summary>
    [HttpGet]
    public async Task<ActionResult> GetTemplates(
        [FromQuery] string? category = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var result = await _mediator.Send(new GetMaintenanceTemplatesQuery(category, isActive, page, pageSize));
        return Ok(result);
    }

    /// <summary>
    /// Get template by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<MaintenanceTemplateDto>> GetTemplate(int id)
    {
        var result = await _mediator.Send(new GetMaintenanceTemplatesQuery(null, null, 1, 1000));
        var template = result.Items.FirstOrDefault(t => t.Id == id);
        if (template == null)
            return NotFound();
        return Ok(template);
    }

    /// <summary>
    /// Get available categories
    /// </summary>
    [HttpGet("categories")]
    public ActionResult<string[]> GetCategories()
    {
        return Ok(MaintenanceCategories.All);
    }

    /// <summary>
    /// Create a new maintenance template
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<int>> CreateTemplate([FromBody] CreateTemplateRequest request)
    {
        var command = new CreateMaintenanceTemplateCommand(
            request.Name,
            request.Description,
            request.Category,
            request.Priority ?? "medium",
            request.IntervalKm,
            request.IntervalMonths,
            request.EstimatedCost,
            request.IsActive ?? true
        );

        var templateId = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetTemplate), new { id = templateId }, templateId);
    }

    /// <summary>
    /// Update a maintenance template
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateTemplate(int id, [FromBody] UpdateTemplateRequest request)
    {
        var command = new UpdateMaintenanceTemplateCommand(
            id,
            request.Name,
            request.Description,
            request.Category,
            request.Priority,
            request.IntervalKm,
            request.IntervalMonths,
            request.EstimatedCost,
            request.IsActive
        );

        var success = await _mediator.Send(command);
        if (!success)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Delete a maintenance template
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTemplate(int id)
    {
        var success = await _mediator.Send(new DeleteMaintenanceTemplateCommand(id));
        if (!success)
            return NotFound();
        return NoContent();
    }
}

// Request DTOs
public record CreateTemplateRequest(
    string Name,
    string? Description,
    string Category,
    string? Priority,
    int? IntervalKm,
    int? IntervalMonths,
    decimal? EstimatedCost,
    bool? IsActive
);

public record UpdateTemplateRequest(
    string? Name,
    string? Description,
    string? Category,
    string? Priority,
    int? IntervalKm,
    int? IntervalMonths,
    decimal? EstimatedCost,
    bool? IsActive
);
