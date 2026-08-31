using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries.Commands;

public class CreateFuelEntryCommandHandler : IRequestHandler<CreateFuelEntryCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPublisher _publisher;

    public CreateFuelEntryCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task<int> Handle(CreateFuelEntryCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        // Convert date to UTC
        var invoiceDate = request.InvoiceDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.InvoiceDate, DateTimeKind.Utc)
            : request.InvoiceDate.ToUniversalTime();

        // Rattachement au véhicule par matricule.
        //
        // L'égalité stricte laissait échapper « 123 TU 4567 » face à « 123TU4567 »
        // ou « 123 tu 4567 » : la ligne était alors enregistrée SANS véhicule, et
        // l'import la comptait comme un succès. Le client croyait ses pleins
        // chargés alors qu'ils n'entraient dans aucun calcul de consommation.
        // On compare donc sans espaces ni casse, et on refuse explicitement un
        // matricule renseigné qui ne correspond à rien.
        var plate = (request.VehiclePlate ?? string.Empty).Trim();
        Vehicle? vehicle = null;

        if (plate.Length > 0)
        {
            var normalized = plate.Replace(" ", "").ToUpper();
            vehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.CompanyId == companyId &&
                    ((v.Plate != null && v.Plate.Replace(" ", "").ToUpper() == normalized) ||
                     v.Name.Replace(" ", "").ToUpper() == normalized),
                    cancellationToken);

            if (vehicle == null)
                throw new GisAPI.Domain.Exceptions.DomainException(
                    $"Aucun véhicule ne correspond au matricule « {plate} ». " +
                    "Vérifiez le matricule ou créez d'abord le véhicule.");
        }

        // Calypso 9 p2 — operator may submit a ticket where only the
        // gross total is legible. Honour an explicit TotalAmount > 0
        // and fall back to volume × price otherwise. Volume / price
        // are still persisted as-is (may be 0) so the per-litre stats
        // simply skip rows with missing breakdown.
        var totalAmount = request.TotalAmount.HasValue && request.TotalAmount.Value > 0
            ? request.TotalAmount.Value
            : request.Volume * request.PricePerLiter;

        var entry = new FuelEntry
        {
            CompanyId = companyId,
            VehicleId = vehicle?.Id,
            // Le matricule tel que reconnu si le véhicule a été retrouvé : on
            // enregistre la forme canonique plutôt que la frappe de l'opérateur,
            // pour que l'historique reste lisible après un import approximatif.
            VehiclePlate = vehicle?.Plate ?? vehicle?.Name ?? plate,
            FuelTypeId = request.FuelTypeId,
            Volume = request.Volume,
            PricePerLiter = request.PricePerLiter,
            TotalAmount = totalAmount,
            InvoiceDate = invoiceDate,
            StationName = request.StationName,
            InvoiceNumber = request.InvoiceNumber,
            Notes = request.Notes,
            DriverId = request.DriverId,
            OdometerKm = request.OdometerKm,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Contrôle du kilométrage : un relevé au compteur ne peut pas être
        // INFÉRIEUR au kilométrage actuel du véhicule — un compteur ne recule
        // pas (recette client du 25/08/2026 : « le kilométrage du 27 inférieur
        // à celui du 25 »). On refuse la saisie avec un message explicite plutôt
        // que de l'accepter en silence. Le zéro / l'absence de relevé restent
        // permis (le compteur est simplement inchangé).
        if (vehicle != null && request.OdometerKm.HasValue
            && request.OdometerKm.Value > 0 && request.OdometerKm.Value < vehicle.Mileage)
        {
            throw new GisAPI.Domain.Exceptions.DomainException(
                $"Le kilométrage saisi ({request.OdometerKm.Value:N0} km) est inférieur au kilométrage " +
                $"actuel du véhicule ({vehicle.Mileage:N0} km). Un compteur ne recule pas : vérifiez la valeur.");
        }

        _context.FuelEntries.Add(entry);

        // Le relevé au compteur fait avancer le kilométrage du véhicule.
        //
        // C'est le pivot de l'offre « gestion de parc sans GPS » : sans boîtier,
        // vehicles.mileage est la SEULE source des échéances d'entretien au
        // kilométrage et du coût au kilomètre. Le plein est le moment naturel où
        // le conducteur lit le compteur ; jusqu'ici la valeur était enregistrée
        // sur la ligne de plein et n'allait pas plus loin, si bien qu'un client
        // sans GPS voyait son kilométrage figé à la valeur saisie à la création
        // du véhicule.
        //
        // On ne recule jamais : un relevé inférieur (erreur de frappe, ticket
        // ancien saisi après coup) ne doit pas faire baisser le compteur.
        if (vehicle != null && request.OdometerKm.HasValue && request.OdometerKm.Value > vehicle.Mileage)
        {
            vehicle.Mileage = (int)request.OdometerKm.Value;
            vehicle.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Notify company admins (must be awaited to avoid DbContext concurrency issues in bulk scenarios)
        var actorId = _tenantService.UserId ?? 0;
        var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);
        if (actor != null)
        {
            var fuelLabel = $"{request.Volume:0.#}L — {request.VehiclePlate ?? vehicle?.Name ?? "Véhicule"}";
            await _publisher.Publish(new AdminActionNotificationEvent(
                companyId, actorId, actor.FullName,
                "fuel_created", fuelLabel, entry.Id, "fuel_entry"
            ), cancellationToken);
        }

        return entry.Id;
    }
}
