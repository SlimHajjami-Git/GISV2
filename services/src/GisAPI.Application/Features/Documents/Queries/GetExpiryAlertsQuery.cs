using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Documents.Queries;

public record GetExpiryAlertsQuery(int DaysThreshold = 30) : IQuery<List<VehicleExpiryDto>>;



