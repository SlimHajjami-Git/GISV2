using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Documents.Queries;

public record GetExpiryStatsQuery() : IQuery<ExpiryStatsDto>;
