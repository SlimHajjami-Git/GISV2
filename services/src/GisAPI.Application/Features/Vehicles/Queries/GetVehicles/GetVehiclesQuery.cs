using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicles;

public record GetVehiclesQuery(
    string? SearchTerm = null,
    string? Status = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<PaginatedList<VehicleDto>>;

public record VehicleDto(
    int Id,
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    string Status,
    bool HasGps,
    int Mileage,
    int? FuelTankCapacity,
    int? SpeedLimit,
    string? FuelType,
    int? AssignedDriverId,
    string? AssignedDriverName,
    int? AssignedSupervisorId,
    string? AssignedSupervisorName,
    GpsDeviceDto? GpsDevice,
    DateTime CreatedAt,
    // Document expiries
    DateTime? InsuranceExpiry,
    DateTime? TechnicalInspectionExpiry,
    DateTime? TaxExpiry,
    DateTime? RegistrationExpiry,
    DateTime? TransportPermitExpiry,
    // Acquisition / crédit-leasing : l'écran Dépenses génère les lignes
    // « Mensualité crédit/leasing » depuis CETTE liste — sans ces champs,
    // aucune échéance payée n'apparaissait dans les dépenses (recette
    // client 01/09/2026). Le détail /vehicles/{id} les portait déjà.
    string? AcquisitionType = null,
    decimal? LeasingMonthlyPayment = null,
    int? LeasingDurationMonths = null,
    DateTime? LeasingStartDate = null,
    int? LeasingPaymentDay = null,
    // Apport (contrat) ou prix d'achat comptant, daté : l'écran Dépenses en
    // fait une ligne « Achat véhicule » (recette client 04/09/2026).
    decimal? PurchasePrice = null,
    DateTime? PurchaseDate = null
);

public record GpsDeviceDto(
    int Id,
    string DeviceUid,
    string? Label,
    string Status,
    DateTime? LastCommunication,
    int? BatteryLevel,
    int? SignalStrength,
    string? Model,
    string? FirmwareVersion
);



