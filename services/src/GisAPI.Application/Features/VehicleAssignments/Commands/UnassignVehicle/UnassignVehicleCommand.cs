using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.VehicleAssignments.Commands.UnassignVehicle;

public record UnassignVehicleCommand(int VehicleId, int UserId) : ICommand;



