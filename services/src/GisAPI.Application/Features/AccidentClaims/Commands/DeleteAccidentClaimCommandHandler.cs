using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public class DeleteAccidentClaimCommandHandler : IRequestHandler<DeleteAccidentClaimCommand, bool>
{
    private readonly IGisDbContext _context;

    public DeleteAccidentClaimCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(DeleteAccidentClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.AccidentClaims
            .Include(c => c.ThirdParties)
            .Include(c => c.Documents)
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (claim == null) return false;

        // Only drafts can be deleted
        if (claim.Status != "draft")
            throw new InvalidOperationException("Only draft claims can be deleted");

        _context.AccidentClaimDocuments.RemoveRange(claim.Documents);
        _context.AccidentClaimThirdParties.RemoveRange(claim.ThirdParties);
        _context.AccidentClaims.Remove(claim);

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
