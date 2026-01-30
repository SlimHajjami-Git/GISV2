using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;

public record AcknowledgeSpeedAlertCommand(int AlertId) : ICommand;



