using MediatR;

namespace GisAPI.Application.Features.FuelExpenses.Queries;

/// <summary>
/// Query to get fuel expense statistics for the fleet
/// </summary>
public record GetFuelExpenseStatisticsQuery(
    DateTime? StartDate,
    DateTime? EndDate,
    int? VehicleId,
    string? FuelType
) : IRequest<FleetFuelStatisticsDto>;

/// <summary>
/// Query to get fuel expense for a single vehicle
/// </summary>
public record GetVehicleFuelExpenseQuery(
    int VehicleId,
    DateTime? StartDate,
    DateTime? EndDate
) : IRequest<VehicleFuelExpenseDto?>;

/// <summary>
/// Query to get current fuel prices
/// </summary>
public record GetCurrentFuelPricesQuery : IRequest<List<FuelPriceDto>>;
