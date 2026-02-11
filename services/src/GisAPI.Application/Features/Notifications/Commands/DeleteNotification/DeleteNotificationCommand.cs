using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Notifications.Commands.DeleteNotification;

public record DeleteNotificationCommand(long Id) : ICommand;
