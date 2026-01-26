using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.Suppliers.Queries;

public record GetSuppliersQuery(
    string? SearchTerm = null,
    string? Type = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<PaginatedList<SupplierDto>>;

public record SupplierDto(
    int Id,
    string Name,
    string Type,
    string? Address,
    string? City,
    string? PostalCode,
    string? ContactName,
    string? Phone,
    string? Email,
    string? Website,
    string? TaxId,
    string? BankAccount,
    string PaymentTerms,
    decimal? DiscountPercent,
    decimal Rating,
    string? Notes,
    bool IsActive,
    List<string> Services,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record SupplierStatsDto(
    int Total,
    int Active,
    int Inactive,
    decimal AverageRating,
    Dictionary<string, int> ByType
);
