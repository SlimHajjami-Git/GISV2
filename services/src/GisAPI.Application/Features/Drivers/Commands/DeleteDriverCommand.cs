using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Drivers.Commands;

public record DeleteDriverCommand(int Id) : IRequest;

public class DeleteDriverCommandHandler : IRequestHandler<DeleteDriverCommand>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteDriverCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task Handle(DeleteDriverCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.Id == request.Id && d.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Chauffeur", request.Id);

        _context.Drivers.Remove(driver);
        await _context.SaveChangesAsync(ct);
    }
}
