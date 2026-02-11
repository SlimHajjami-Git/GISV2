using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Notifications;
using GisAPI.Application.Features.Notifications.Queries.GetNotifications;
using GisAPI.Application.Features.Notifications.Queries.GetUnreadCount;
using GisAPI.Application.Features.Notifications.Commands.MarkNotificationRead;
using GisAPI.Application.Features.Notifications.Commands.MarkAllNotificationsRead;
using GisAPI.Application.Features.Notifications.Commands.DeleteNotification;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;

    public NotificationsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationPageDto>> GetNotifications(
        [FromQuery] int? pageSize = 20,
        [FromQuery] int? page = 1,
        [FromQuery] bool? unreadOnly = false,
        [FromQuery] string? type = null)
    {
        var result = await _mediator.Send(new GetNotificationsQuery(pageSize, page, unreadOnly, type));
        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount()
    {
        var result = await _mediator.Send(new GetUnreadCountQuery());
        return Ok(result);
    }

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkAsRead(long id)
    {
        await _mediator.Send(new MarkNotificationReadCommand(id));
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await _mediator.Send(new MarkAllNotificationsReadCommand());
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteNotification(long id)
    {
        await _mediator.Send(new DeleteNotificationCommand(id));
        return NoContent();
    }
}
