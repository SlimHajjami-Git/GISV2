using GisAPI.Application.Common.Interfaces;
using MediatR;

namespace GisAPI.Application.Features.Admin.Companies.Commands.DeleteCompany;

public class DeleteCompanyCommandHandler : IRequestHandler<DeleteCompanyCommand, bool>
{
    private readonly IGisDbContext _context;

    public DeleteCompanyCommandHandler(IGisDbContext context) => _context = context;

    public async Task<bool> Handle(DeleteCompanyCommand request, CancellationToken ct)
    {
        var company = await _context.Societes.FindAsync(new object[] { request.Id }, ct);
        if (company == null) return false;

        _context.Societes.Remove(company);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
