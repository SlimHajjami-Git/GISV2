using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Commands.DeleteVehicle;

public record DeleteVehicleCommand(int Id) : ICommand;
