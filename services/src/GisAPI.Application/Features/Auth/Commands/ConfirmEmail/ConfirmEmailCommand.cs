using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.Auth.Commands.ConfirmEmail;

/// <summary>
/// Confirmation de l'adresse email d'un compte issu de l'inscription libre.
/// Bascule le compte de « pending » à « active ».
/// </summary>
public record ConfirmEmailCommand(string Token) : ICommand<ConfirmEmailResult>;

/// <summary>
/// <paramref name="AlreadyConfirmed"/> distingue « ce lien vient d'activer votre
/// compte » de « ce compte était déjà actif » : un utilisateur qui reclique sur son
/// lien, ou dont la messagerie préouvre les URL, ne doit pas voir une erreur.
/// </summary>
public record ConfirmEmailResult(bool Success, string Message, bool AlreadyConfirmed = false);

public class ConfirmEmailCommandHandler : IRequestHandler<ConfirmEmailCommand, ConfirmEmailResult>
{
    private readonly IGisDbContext _context;
    private readonly ILogger<ConfirmEmailCommandHandler> _logger;

    public ConfirmEmailCommandHandler(IGisDbContext context, ILogger<ConfirmEmailCommandHandler> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ConfirmEmailResult> Handle(ConfirmEmailCommand request, CancellationToken ct)
    {
        var token = (request.Token ?? string.Empty).Trim();
        if (token.Length == 0)
            throw new DomainException("Lien de confirmation invalide.");

        // IgnoreQueryFilters : la requête est anonyme, il n'y a aucun tenant courant,
        // et le filtre global sur CompanyId masquerait l'utilisateur recherché.
        var user = await _context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.EmailVerificationToken == token, ct);

        if (user == null)
            throw new DomainException(
                "Ce lien de confirmation n'est plus valable. Demandez-en un nouveau depuis la page de connexion.");

        // Compte déjà actif : le lien a déjà servi. C'est le cas le PLUS FRÉQUENT
        // en pratique — l'utilisateur reclique, ou l'antivirus de sa messagerie
        // préouvre l'URL avant lui. On l'accueille sereinement.
        if (user.Status == "active")
            return new ConfirmEmailResult(true, "Votre adresse est déjà confirmée. Vous pouvez vous connecter.", true);

        if (user.EmailVerificationExpiresAt is not DateTime expiry || expiry < DateTime.UtcNow)
            throw new DomainException(
                "Ce lien de confirmation a expiré. Demandez-en un nouveau depuis la page de connexion.");

        user.Status = "active";
        // Le jeton n'est VOLONTAIREMENT pas effacé : l'effacer rendait la branche
        // ci-dessus inatteignable, si bien qu'un second clic sur le même lien —
        // situation banale — répondait « lien invalide ». Une fois le compte actif
        // le jeton est inerte : il ne peut plus qu'activer ce qui l'est déjà.
        user.EmailVerificationExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Adresse confirmée pour {Email} (société #{CompanyId})", user.Email, user.CompanyId);

        return new ConfirmEmailResult(true, "Votre adresse est confirmée. Vous pouvez maintenant vous connecter.");
    }
}
