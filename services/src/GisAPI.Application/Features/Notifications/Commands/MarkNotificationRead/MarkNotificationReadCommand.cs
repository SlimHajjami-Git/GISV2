using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Notifications.Commands.MarkNotificationRead;

public record MarkNotificationReadCommand(long Id) : ICommand;
