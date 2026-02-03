using MediatR;

namespace GisAPI.Application.Features.FuelEntries.Commands;

public record CreateFuelEntryCommand(
    string VehiclePlate,
    int FuelTypeId,
    decimal Volume,
    decimal PricePerLiter,
    DateTime InvoiceDate,
    string? StationName,
    string? InvoiceNumber,
    string? Notes,
    int? DriverId,
    long? OdometerKm
) : IRequest<int>;
