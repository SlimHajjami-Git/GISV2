using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.AlertEmails.Commands;

public record CreateAlertEmailCommand(string Email, string AlertType) : IRequest<int>;

public class CreateAlertEmailCommandHandler : IRequestHandler<CreateAlertEmailCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateAlertEmailCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(CreateAlertEmailCommand request, CancellationToken ct)
    {
        var entity = new AlertEmail
        {
            Email = request.Email,
            AlertType = request.AlertType,
            CompanyId = _tenantService.CompanyId ?? 0
        };

        _context.AlertEmails.Add(entity);
        await _context.SaveChangesAsync(ct);

        return entity.Id;
    }
}
