using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Reservations.Commands.SetVehicleRentalStatus;

public record SetVehicleRentalStatusCommand(int VehicleId, bool IsRented) : ICommand<VehicleRentalStatusDto>;

public record VehicleRentalStatusDto(int VehicleId, string VehicleName, bool IsRented);
