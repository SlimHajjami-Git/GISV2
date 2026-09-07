using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;

public class PatchVehicleCommandHandler : IRequestHandler<PatchVehicleCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public PatchVehicleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Unit> Handle(PatchVehicleCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.Id && v.CompanyId == companyId, cancellationToken);

        if (vehicle == null)
            throw new InvalidOperationException("Vehicle not found");

        if (request.SpeedLimit.HasValue)
            vehicle.SpeedLimit = request.SpeedLimit.Value;

        if (request.DepartmentId.HasValue)
            vehicle.DepartmentId = request.DepartmentId.Value == 0 ? null : request.DepartmentId.Value;

        if (!string.IsNullOrEmpty(request.FuelType))
            vehicle.FuelType = request.FuelType;

        // Identification
        if (request.Brand != null) vehicle.Brand = request.Brand;
        if (request.Model != null) vehicle.Model = request.Model;
        if (request.Plate != null) vehicle.Plate = request.Plate;
        if (request.Year.HasValue) vehicle.Year = request.Year;
        if (request.Color != null) vehicle.Color = request.Color;
        if (request.Mileage.HasValue) vehicle.Mileage = request.Mileage.Value;
        if (request.FuelTankCapacity.HasValue) vehicle.FuelTankCapacity = request.FuelTankCapacity;

        // Acquisition — l'empreinte des 7 champs est relevée avant/après :
        // l'échéancier persisté n'est recalé que si le contrat a changé.
        var acquisitionBefore = AcquisitionScheduleSync.Fingerprint(vehicle);
        if (!string.IsNullOrEmpty(request.AcquisitionType)) vehicle.AcquisitionType = request.AcquisitionType;
        if (request.PurchasePrice.HasValue) vehicle.PurchasePrice = request.PurchasePrice;
        if (request.LeasingMonthlyPayment.HasValue) vehicle.LeasingMonthlyPayment = request.LeasingMonthlyPayment;
        if (request.LeasingDurationMonths.HasValue) vehicle.LeasingDurationMonths = request.LeasingDurationMonths;
        if (request.LeasingStartDate.HasValue) vehicle.LeasingStartDate = request.LeasingStartDate;
        if (request.LeasingPaymentDay.HasValue) vehicle.LeasingPaymentDay = request.LeasingPaymentDay;
        if (request.RegistrationDate.HasValue) vehicle.RegistrationDate = request.RegistrationDate;
        if (request.PurchaseDate.HasValue) vehicle.PurchaseDate = request.PurchaseDate;

        vehicle.UpdatedAt = DateTime.UtcNow;

        // Échéancier d'acquisition persisté (acquisition_payments) : recalé dans
        // la même transaction que le véhicule, seulement si le contrat a bougé.
        if (AcquisitionScheduleSync.Fingerprint(vehicle) != acquisitionBefore)
            await AcquisitionScheduleSync.SyncAsync(_context, vehicle, cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}



