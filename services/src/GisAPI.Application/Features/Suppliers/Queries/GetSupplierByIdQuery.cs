using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Suppliers.Queries;

public record GetSupplierByIdQuery(int Id) : IQuery<SupplierDto?>;



