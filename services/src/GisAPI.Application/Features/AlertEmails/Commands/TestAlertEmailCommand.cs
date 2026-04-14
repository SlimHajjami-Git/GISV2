using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AlertEmails.Commands;

/// <summary>
/// Sends a one-off test email to an <c>alert_emails</c> recipient so the user
/// can verify delivery from the UI.
/// </summary>
public record TestAlertEmailCommand(int Id) : IRequest<Unit>;

public class TestAlertEmailCommandHandler : IRequestHandler<TestAlertEmailCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IAlertEmailDispatcher _dispatcher;

    public TestAlertEmailCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IAlertEmailDispatcher dispatcher)
    {
        _context = context;
        _tenantService = tenantService;
        _dispatcher = dispatcher;
    }

    public async Task<Unit> Handle(TestAlertEmailCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        // Enforce tenant isolation: only let the caller test an alert_email row
        // that belongs to their own company.
        var entry = await _context.AlertEmails
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AlertEmail", request.Id);

        await _dispatcher.SendTestAsync(entry.Id, ct);

        return Unit.Value;
    }
}
