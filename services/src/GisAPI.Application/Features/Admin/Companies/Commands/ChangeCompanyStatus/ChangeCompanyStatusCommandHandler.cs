using GisAPI.Application.Common.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Commands.ChangeCompanyStatus;

public class ChangeCompanyStatusCommandHandler : IRequestHandler<ChangeCompanyStatusCommand, bool>
{
    private readonly IGisDbContext _context;

    public ChangeCompanyStatusCommandHandler(IGisDbContext context) => _context = context;

    public async Task<bool> Handle(ChangeCompanyStatusCommand request, CancellationToken ct)
    {
        var company = await _context.Societes.FindAsync(new object[] { request.Id }, ct);
        if (company == null) return false;

        company.IsActive = request.Activate;
        company.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
