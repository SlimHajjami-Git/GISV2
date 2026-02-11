using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Queries.GetCompanies;

public record GetCompaniesQuery(string? Search = null, string? Status = null) : IRequest<List<AdminCompanyDto>>;
