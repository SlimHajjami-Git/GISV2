using GisAPI.Application.Common;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.UpdateQuietHours;

/// <summary>
/// Persiste les heures silencieuses sur l'utilisateur courant (recette client du
/// 11/09/2026 : le réglage ne vivait que dans le localStorage, aucun envoi ne le lisait).
/// Même motif que ChangeMyPasswordCommandHandler : l'identité vient du jeton, jamais
/// du corps de la requête.
/// </summary>
public class UpdateQuietHoursCommandHandler : IRequestHandler<UpdateQuietHoursCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateQuietHoursCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<Unit> Handle(UpdateQuietHoursCommand request, CancellationToken ct)
    {
        var userId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var start = QuietHoursPolicy.ParseHourMinute(request.Start);
        var end = QuietHoursPolicy.ParseHourMinute(request.End);

        // Une heure fournie mais illisible est une erreur même plage désactivée :
        // l'enregistrer à null effacerait silencieusement la saisie.
        if (!string.IsNullOrWhiteSpace(request.Start) && start is null)
            throw new DomainException("Heure de début invalide (format attendu HH:mm)");
        if (!string.IsNullOrWhiteSpace(request.End) && end is null)
            throw new DomainException("Heure de fin invalide (format attendu HH:mm)");

        if (request.Enabled)
        {
            if (start is null || end is null)
                throw new DomainException("Les heures de début et de fin sont obligatoires pour activer les heures silencieuses");
            // Début == fin : plage vide (ou de 24 h, selon la lecture) — ambigu, refusé.
            if (start == end)
                throw new DomainException("L'heure de début et l'heure de fin doivent être différentes");
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", userId);

        user.QuietHoursEnabled = request.Enabled;
        // Désactivée sans heures fournies : on garde les anciennes pour la réactivation.
        if (start.HasValue) user.QuietHoursStart = start;
        if (end.HasValue) user.QuietHoursEnd = end;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
