using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.FleetManagement.SpeedLimits.Commands;

public record SetVehicleSpeedLimitCommand(
    int VehicleId,
    int SpeedLimit
) : ICommand;



