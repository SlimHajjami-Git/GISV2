using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Models;

namespace GisAPI.Application.Features.Suppliers.Queries;

public record GetGaragesQuery(
    string? SearchTerm = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 50
) : IQuery<PaginatedList<SupplierDto>>;
