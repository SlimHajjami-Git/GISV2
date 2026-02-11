using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Commands.ChangeCompanyStatus;

public record ChangeCompanyStatusCommand(int Id, bool Activate) : IRequest<bool>;
