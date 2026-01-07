using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;

public class CreateVehicleCommandHandler : IRequestHandler<CreateVehicleCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateVehicleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(CreateVehicleCommand request, CancellationToken ct)
    {
        var vehicle = new Vehicle
        {
            Name = request.Name,
            Type = request.Type,
            Brand = request.Brand,
            Model = request.Model,
            Plate = request.Plate,
            Year = request.Year,
            Color = request.Color,
            Mileage = request.Mileage,
            CompanyId = _tenantService.CompanyId ?? 0,
            Status = "available"
        };

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync(ct);

        return vehicle.Id;
    }
}
