using MediatR;

namespace GisAPI.Application.Features.Admin.Vehicles.Commands.DeleteAdminVehicle;

public record DeleteAdminVehicleCommand(int Id) : IRequest<bool>;
