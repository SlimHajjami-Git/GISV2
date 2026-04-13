using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Reservations.Commands.SetVehicleRentalStatus;

public class SetVehicleRentalStatusCommandHandler : IRequestHandler<SetVehicleRentalStatusCommand, VehicleRentalStatusDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public SetVehicleRentalStatusCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<VehicleRentalStatusDto> Handle(SetVehicleRentalStatusCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var company = await _context.Societes
            .FirstOrDefaultAsync(s => s.Id == companyId, ct)
            ?? throw new NotFoundException("Societe", companyId);

        if (company.Type != "location")
            throw new DomainException("Cette fonctionnalité est réservée aux sociétés de type location");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);

        if (request.IsRented)
        {
            var hasActiveReservation = await _context.Reservations
                .AnyAsync(r => r.VehicleId == request.VehicleId && r.Status == "in_progress", ct);

            if (hasActiveReservation)
                throw new ConflictException("Ce véhicule a un emprunt en cours. Veuillez le retourner avant de le marquer comme loué.");
        }

        vehicle.IsRented = request.IsRented;
        vehicle.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return new VehicleRentalStatusDto(vehicle.Id, vehicle.Name, vehicle.IsRented);
    }
}
