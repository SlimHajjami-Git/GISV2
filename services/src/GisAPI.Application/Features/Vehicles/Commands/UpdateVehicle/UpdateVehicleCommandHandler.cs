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

        // Un véhicule n'est jamais lié au chauffeur d'une autre société : la clé
        // étrangère vers drivers ne contrôle que l'existence, pas la société, et
        // un identifiant inconnu finissait en erreur base (500). Même contrôle
        // que le volet chauffeur, qui refuse un véhicule d'une autre société.
        if (request.AssignedDriverId is { } requestedDriverId && requestedDriverId != 0)
        {
            var driverInCompany = await _context.Drivers
                .AnyAsync(d => d.Id == requestedDriverId && d.CompanyId == vehicle.CompanyId, ct);
            if (!driverInCompany)
                throw new DomainException(
                    "Chauffeur invalide : un véhicule ne peut être affecté qu'à un chauffeur de sa propre société.");
        }

        VehicleWriteRules.EnsurePaymentDay(request.LeasingPaymentDay, vehicle.LeasingPaymentDay);
        await VehicleWriteRules.EnsurePlateAvailableAsync(
            _context, vehicle.CompanyId, request.Plate, vehicle.Id, vehicle.Plate, ct);

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
        // Chauffeur et superviseur : champ ABSENT = champ non modifié, comme les
        // champs d'acquisition et de documents. Le formulaire véhicule ne porte
        // aucun champ chauffeur : l'écrasement inconditionnel effaçait
        // l'affectation à chaque changement de couleur (recette GPA du
        // 11/09/2026). 0 = désaffectation explicite (même convention que
        // DepartmentId dans le PATCH).
        var driverProvided = request.AssignedDriverId.HasValue;
        if (driverProvided)
            vehicle.AssignedDriverId = request.AssignedDriverId!.Value == 0
                ? null
                : request.AssignedDriverId.Value;
        if (request.AssignedSupervisorId.HasValue)
            vehicle.AssignedSupervisorId = request.AssignedSupervisorId.Value == 0
                ? null
                : request.AssignedSupervisorId.Value;

        // Acquisition info — l'empreinte des 7 champs est relevée avant/après :
        // l'échéancier persisté n'est recalé que si le contrat a changé.
        var acquisitionBefore = AcquisitionScheduleSync.Fingerprint(vehicle);
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

        // L'affectation a deux jambes : drivers.assigned_vehicle_id suit, sinon
        // l'écran Chauffeurs contredit l'écran Véhicules. Recalé dès que le champ
        // est fourni (même à valeur inchangée) : c'est ce qui répare les
        // affectations déjà divergentes en base.
        if (driverProvided)
            await VehicleDriverAssignment.SyncDriverSideAsync(_context, vehicle, ct);

        // Échéancier d'acquisition persisté (acquisition_payments) : recalé dans
        // la même transaction que le véhicule, seulement si le contrat a bougé.
        if (AcquisitionScheduleSync.Fingerprint(vehicle) != acquisitionBefore)
            await AcquisitionScheduleSync.SyncAsync(_context, vehicle, ct);

        await _context.SaveChangesAsync(ct);
    }
}
