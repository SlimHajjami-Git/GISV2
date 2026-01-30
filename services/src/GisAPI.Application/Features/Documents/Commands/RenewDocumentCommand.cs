using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Documents.Commands;

public record RenewDocumentCommand(
    int VehicleId,
    string DocumentType,
    decimal Amount,
    DateTime PaymentDate,
    DateTime NewExpiryDate,
    string? DocumentNumber,
    string? Provider,
    string? Notes,
    string? DocumentUrl
) : ICommand<int>;



