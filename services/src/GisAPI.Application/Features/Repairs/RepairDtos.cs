namespace GisAPI.Application.Features.Repairs;

public record RepairDto(
    int Id,
    int VehicleId,
    string VehicleName,
    string VehiclePlate,
    int? SupplierId,
    string? SupplierName,
    string Reference,
    string? Description,
    DateTime RepairDate,
    int? MileageAtRepair,
    decimal LaborCost,
    decimal PartsCost,
    decimal TotalCost,
    string Status,
    string? InvoiceNumber,
    string? Notes,
    DateTime CreatedAt,
    List<RepairPartDto> Parts,
    string? RepairType = null,           // electrique | mecanique | freinage | pneumatique | carrosserie | autre
    // Sinistre à l'origine de la réparation (migration 049). Sans lui, l'écran Dépenses
    // perdait le badge « Accident #N », son lien vers le dossier et le verrou qui
    // empêche de supprimer une ligne pilotée par la phase 5.
    int? AccidentEventId = null
);

public record RepairPartDto(
    int Id,
    string PartName,
    string? PartReference,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal,
    string? Notes
);

public record CreateRepairPartRequest(
    string PartName,
    string? PartReference,
    int Quantity,
    decimal UnitPrice,
    string? Notes
);

public record RepairStatsDto(
    int TotalRepairs,
    int PendingRepairs,
    int CompletedRepairs,
    decimal TotalCost,
    decimal AverageCost,
    decimal TotalLaborCost,
    decimal TotalPartsCost,
    // Annulées : comptées dans TotalRepairs, exclues des montants (comme les rapports).
    int CancelledRepairs = 0
);
