using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Documents.Queries;

public record GetRenewalHistoryQuery(int VehicleId) : IQuery<List<RenewalHistoryDto>>;

public record RenewalHistoryDto(
    int Id,
    string DocumentType,
    decimal Amount,
    DateTime PaymentDate,
    DateTime? ExpiryDate,
    string? DocumentNumber,
    string? Provider,
    string? Notes,
    string? DocumentUrl
);
