using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Documents.Queries;

public record GetVehicleExpiriesQuery(int VehicleId) : IQuery<List<VehicleExpiryDto>>;



