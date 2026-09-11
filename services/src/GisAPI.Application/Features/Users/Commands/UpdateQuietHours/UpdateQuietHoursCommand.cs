using MediatR;

namespace GisAPI.Application.Features.Users.Commands.UpdateQuietHours;

/// <summary>
/// Heures silencieuses de l'utilisateur CONNECTÉ (PUT /api/users/me/quiet-hours).
/// Start / End au format « HH:mm », heure locale de la société. Obligatoires et
/// différents quand la plage est activée ; désactivée, les heures fournies sont
/// conservées pour une réactivation ultérieure.
/// </summary>
public record UpdateQuietHoursCommand(
    bool Enabled,
    string? Start,
    string? End
) : IRequest<Unit>;
