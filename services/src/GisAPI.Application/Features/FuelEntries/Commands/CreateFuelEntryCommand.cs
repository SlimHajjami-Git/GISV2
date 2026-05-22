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
    long? OdometerKm,
    // Optional gross amount override. When the operator only has the
    // total on a ticket (no volume × price breakdown), they can submit
    // the total directly. The handler uses it when > 0 and otherwise
    // computes it from Volume × PricePerLiter.
    decimal? TotalAmount = null
) : IRequest<int>;
