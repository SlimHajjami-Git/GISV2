using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;

public class UpdateVehicleCommandHandler : IRequestHandler<UpdateVehicleCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateVehicleCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(UpdateVehicleCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.Id && v.CompanyId == companyId, ct);

        if (vehicle == null)
            throw new NotFoundException("Vehicle", request.Id);

        vehicle.Name = request.Name;
        vehicle.Type = request.Type;
        vehicle.Brand = request.Brand;
        vehicle.Model = request.Model;
        vehicle.Plate = request.Plate;
        vehicle.Year = request.Year;
        vehicle.Color = request.Color;
        vehicle.Status = request.Status;
        // Kilométrage : un compteur ne recule pas (recette client du 25/08/2026,
        // « toujours vérifier si nouveau kilométrage > ancien »). On refuse une
        // valeur inférieure à l'actuelle ; une valeur à 0 = champ non renseigné,
        // on conserve alors le kilométrage existant plutôt que de l'écraser.
        if (request.Mileage > 0 && request.Mileage < vehicle.Mileage)
        {
            throw new GisAPI.Domain.Exceptions.DomainException(
                $"Le kilométrage saisi ({request.Mileage:N0} km) est inférieur au kilométrage " +
                $"actuel du véhicule ({vehicle.Mileage:N0} km). Un compteur ne recule pas : vérifiez la valeur.");
        }
        if (request.Mileage > 0)
            vehicle.Mileage = request.Mileage;
        if (request.FuelType != null) vehicle.FuelType = request.FuelType;
        vehicle.FuelTankCapacity = request.FuelTankCapacity;
        vehicle.AssignedDriverId = request.AssignedDriverId;
        vehicle.AssignedSupervisorId = request.AssignedSupervisorId;

        // Acquisition info
        if (request.AcquisitionType != null) vehicle.AcquisitionType = request.AcquisitionType;
        if (request.PurchasePrice.HasValue) vehicle.PurchasePrice = request.PurchasePrice;
        // Calypso 6 (P5): persist purchase date separate from registration date
        if (request.PurchaseDate.HasValue) vehicle.PurchaseDate = request.PurchaseDate;
        if (request.LeasingMonthlyPayment.HasValue) vehicle.LeasingMonthlyPayment = request.LeasingMonthlyPayment;
        if (request.LeasingDurationMonths.HasValue) vehicle.LeasingDurationMonths = request.LeasingDurationMonths;
        if (request.LeasingStartDate.HasValue) vehicle.LeasingStartDate = request.LeasingStartDate;
        if (request.LeasingPaymentDay.HasValue) vehicle.LeasingPaymentDay = request.LeasingPaymentDay;
        if (request.RegistrationDate.HasValue) vehicle.RegistrationDate = request.RegistrationDate;

        // Document dates
        if (request.InsuranceStartDate.HasValue) vehicle.InsuranceStartDate = request.InsuranceStartDate;
        if (request.InsuranceExpiry.HasValue) vehicle.InsuranceExpiry = request.InsuranceExpiry;
        if (request.InsuranceReminderDays.HasValue) vehicle.InsuranceReminderDays = request.InsuranceReminderDays.Value;
        if (request.TaxStartDate.HasValue) vehicle.TaxStartDate = request.TaxStartDate;
        if (request.TaxExpiry.HasValue) vehicle.TaxExpiry = request.TaxExpiry;
        if (request.TaxReminderDays.HasValue) vehicle.TaxReminderDays = request.TaxReminderDays.Value;
        if (request.TechnicalInspectionStartDate.HasValue) vehicle.TechnicalInspectionStartDate = request.TechnicalInspectionStartDate;
        if (request.TechnicalInspectionExpiry.HasValue) vehicle.TechnicalInspectionExpiry = request.TechnicalInspectionExpiry;
        if (request.TechnicalInspectionReminderDays.HasValue) vehicle.TechnicalInspectionReminderDays = request.TechnicalInspectionReminderDays.Value;

        vehicle.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
    }
}
