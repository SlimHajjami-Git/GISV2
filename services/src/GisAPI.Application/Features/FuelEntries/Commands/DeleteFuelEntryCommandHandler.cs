using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries.Commands;

public class DeleteFuelEntryCommandHandler : IRequestHandler<DeleteFuelEntryCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteFuelEntryCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<bool> Handle(DeleteFuelEntryCommand request, CancellationToken cancellationToken)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");

        var entry = await _context.FuelEntries
            .FirstOrDefaultAsync(e => e.Id == request.Id && e.CompanyId == companyId, cancellationToken);

        if (entry == null)
            return false;

        _context.FuelEntries.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
