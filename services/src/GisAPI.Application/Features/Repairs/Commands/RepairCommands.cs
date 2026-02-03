using MediatR;

namespace GisAPI.Application.Features.Repairs.Commands;

public record CreateRepairCommand(
    int VehicleId,
    int? SupplierId,
    string? Description,
    DateTime RepairDate,
    int? MileageAtRepair,
    decimal LaborCost,
    string? InvoiceNumber,
    string? Notes,
    List<CreateRepairPartRequest> Parts
) : IRequest<int>;

public record UpdateRepairCommand(
    int Id,
    int VehicleId,
    int? SupplierId,
    string? Description,
    DateTime RepairDate,
    int? MileageAtRepair,
    decimal LaborCost,
    string Status,
    string? InvoiceNumber,
    string? Notes,
    List<CreateRepairPartRequest> Parts
) : IRequest<bool>;

public record DeleteRepairCommand(int Id) : IRequest<bool>;

public record UpdateRepairStatusCommand(int Id, string Status) : IRequest<bool>;
