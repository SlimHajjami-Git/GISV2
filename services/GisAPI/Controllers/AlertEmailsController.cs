using GisAPI.Application.Features.AlertEmails.Commands;
using GisAPI.Application.Features.AlertEmails.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GisAPI.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class AlertEmailsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AlertEmailsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? alertType)
    {
        var result = await _mediator.Send(new GetAlertEmailsQuery(alertType));
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAlertEmailRequest request)
    {
        var id = await _mediator.Send(new CreateAlertEmailCommand(request.Email, request.AlertType));
        return Ok(new { id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAlertEmailRequest request)
    {
        await _mediator.Send(new UpdateAlertEmailCommand(id, request.Email, request.AlertType));
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await _mediator.Send(new DeleteAlertEmailCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Sends a one-off test email to the configured recipient so the user
    /// can verify delivery directly from the UI.
    /// </summary>
    [HttpPost("{id}/test")]
    public async Task<IActionResult> SendTest(int id)
    {
        await _mediator.Send(new TestAlertEmailCommand(id));
        return Ok(new { sent = true });
    }
}

public record CreateAlertEmailRequest(string Email, string AlertType);
public record UpdateAlertEmailRequest(string Email, string AlertType);
