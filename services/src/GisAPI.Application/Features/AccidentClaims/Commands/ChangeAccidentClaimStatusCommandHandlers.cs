using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public class SubmitAccidentClaimCommandHandler : IRequestHandler<SubmitAccidentClaimCommand, bool>
{
    private readonly IGisDbContext _context;

    public SubmitAccidentClaimCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(SubmitAccidentClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.AccidentClaims
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (claim == null) return false;

        if (claim.Status != "draft")
            throw new InvalidOperationException("Only draft claims can be submitted");

        claim.Status = "submitted";
        claim.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class ApproveAccidentClaimCommandHandler : IRequestHandler<ApproveAccidentClaimCommand, bool>
{
    private readonly IGisDbContext _context;

    public ApproveAccidentClaimCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(ApproveAccidentClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.AccidentClaims
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (claim == null) return false;

        if (claim.Status != "submitted" && claim.Status != "under_review")
            throw new InvalidOperationException("Only submitted or under review claims can be approved");

        claim.Status = "approved";
        claim.ApprovedAmount = request.ApprovedAmount;
        claim.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class RejectAccidentClaimCommandHandler : IRequestHandler<RejectAccidentClaimCommand, bool>
{
    private readonly IGisDbContext _context;

    public RejectAccidentClaimCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(RejectAccidentClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.AccidentClaims
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (claim == null) return false;

        if (claim.Status != "submitted" && claim.Status != "under_review")
            throw new InvalidOperationException("Only submitted or under review claims can be rejected");

        claim.Status = "rejected";
        if (!string.IsNullOrEmpty(request.Reason))
            claim.AdditionalNotes = (claim.AdditionalNotes ?? "") + $"\n[Rejet] {request.Reason}";
        claim.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class CloseAccidentClaimCommandHandler : IRequestHandler<CloseAccidentClaimCommand, bool>
{
    private readonly IGisDbContext _context;

    public CloseAccidentClaimCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(CloseAccidentClaimCommand request, CancellationToken cancellationToken)
    {
        var claim = await _context.AccidentClaims
            .FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken);

        if (claim == null) return false;

        if (claim.Status != "approved")
            throw new InvalidOperationException("Only approved claims can be closed");

        claim.Status = "closed";
        claim.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}



