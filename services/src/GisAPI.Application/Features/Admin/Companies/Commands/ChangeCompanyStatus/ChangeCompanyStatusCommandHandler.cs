using GisAPI.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Admin.Companies.Commands.ChangeCompanyStatus;

public class ChangeCompanyStatusCommandHandler : IRequestHandler<ChangeCompanyStatusCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly IGpsHubService _hub;
    private readonly ILogger<ChangeCompanyStatusCommandHandler> _logger;

    public ChangeCompanyStatusCommandHandler(IGisDbContext context, IGpsHubService hub,
        ILogger<ChangeCompanyStatusCommandHandler> logger)
    {
        _context = context;
        _hub = hub;
        _logger = logger;
    }

    public async Task<bool> Handle(ChangeCompanyStatusCommand request, CancellationToken ct)
    {
        var company = await _context.Societes.FindAsync(new object[] { request.Id }, ct);
        if (company == null) return false;

        // IsActive seul ne bloquait RIEN : le SubscriptionExpirationMiddleware ne
        // teste que SubscriptionStatus + dates. La suspension sys_admin pose donc
        // les DEUX champs pour être réellement effective.
        company.IsActive = request.Activate;
        company.SubscriptionStatus = request.Activate ? "active" : "suspended";
        company.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        // Les sessions déjà ouvertes basculent immédiatement (sans attendre la
        // prochaine requête API) — best-effort, la suspension reste effective
        // même si personne n'est connecté.
        try { await _hub.SendSubscriptionChangedAsync(company.Id, company.SubscriptionStatus); }
        catch (Exception ex) { _logger.LogWarning(ex, "SubscriptionChanged push failed for company {Id}", company.Id); }

        return true;
    }
}
