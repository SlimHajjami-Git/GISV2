using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Commands.DeleteCompany;

public record DeleteCompanyCommand(int Id) : IRequest<bool>;
