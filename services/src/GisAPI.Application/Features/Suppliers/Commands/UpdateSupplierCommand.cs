using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Suppliers.Commands;

public record UpdateSupplierCommand(
    int Id,
    string? Name,
    string? Type,
    string? Address,
    string? City,
    string? PostalCode,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Website,
    string? TaxId,
    string? BankAccount,
    string? PaymentTerms,
    decimal? DiscountPercent,
    decimal? Rating,
    string? Notes,
    bool? IsActive,
    List<string>? Services
) : ICommand<bool>;
