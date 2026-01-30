using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.AccidentClaims.Queries;

public record GetAccidentClaimByIdQuery(int Id) : IQuery<AccidentClaimDto?>;



