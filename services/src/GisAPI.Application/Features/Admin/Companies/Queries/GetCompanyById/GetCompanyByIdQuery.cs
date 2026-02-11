using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Queries.GetCompanyById;

public record GetCompanyByIdQuery(int Id) : IRequest<AdminCompanyDto?>;
