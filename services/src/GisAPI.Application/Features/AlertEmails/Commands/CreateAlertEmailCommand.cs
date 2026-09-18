using System.Net.Mail;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AlertEmails.Commands;

public record CreateAlertEmailCommand(string Email, string AlertType) : IRequest<int>;

/// <summary>
/// Contrôles de la liste de diffusion des échéances, communs à la création et à la modification.
/// Rien n'était vérifié côté serveur : une adresse vide ou malformée était enregistrée avec un
/// 200, puis AlertEmailDispatcher la présentait telle quelle au serveur SMTP — l'alerte
/// d'assurance ou de visite technique partait dans le vide sans que personne ne le sache
/// (recette du 16/09/2026). Les deux valeurs sont normalisées, jamais devinées.
/// </summary>
public static class AlertEmailInput
{
    /// <summary>
    /// Types émis par les services d'alerte, avec le libellé de l'écran alert-emails repris dans
    /// les messages de refus. Une seule liste : deux copies finiraient par diverger.
    /// </summary>
    private static readonly (string Type, string Libelle)[] Types =
    {
        ("assurance", "Assurance"),
        ("taxe_circulation", "Taxe Circulation"),
        ("visite_technique", "Visite Technique"),
        ("entretien", "Entretien"),
        ("permis", "Permis"),
        ("accident", "Accident")
    };

    /// <summary>Types émis par les services d'alerte (dispatcher, PredictiveAlertService, détection d'accident).</summary>
    public static readonly IReadOnlyList<string> TypesConnus = Types.Select(t => t.Type).ToArray();

    /// <summary>Adresse nettoyée, ou <see cref="DomainException"/> (HTTP 400) si elle est inutilisable.</summary>
    public static string NormalizeEmail(string? email)
    {
        var value = (email ?? string.Empty).Trim();

        if (value.Length == 0)
            throw new DomainException("L'adresse e-mail est obligatoire.");

        if (value.Length > 254 || value.Any(char.IsWhiteSpace) || !MailAddress.TryCreate(value, out var parsed)
            // MailAddress accepte « Nom <a@b> » et les domaines sans point : on exige la forme
            // simple locale@domaine.tld, la seule que le serveur SMTP saura router.
            || !string.Equals(parsed!.Address, value, StringComparison.Ordinal)
            || !parsed.Host.Contains('.') || parsed.Host.StartsWith('.') || parsed.Host.EndsWith('.'))
        {
            throw new DomainException($"Adresse e-mail invalide : {value}");
        }

        return value;
    }

    /// <summary>Type d'alerte en minuscules, ou <see cref="DomainException"/> (HTTP 400) s'il n'est pas émis.</summary>
    public static string NormalizeAlertType(string? alertType)
    {
        var value = (alertType ?? string.Empty).Trim().ToLowerInvariant();

        if (value.Length == 0)
            throw new DomainException("Le type d'alerte est obligatoire.");

        if (!TypesConnus.Contains(value))
            throw new DomainException($"Type d'alerte inconnu : {value}");

        return value;
    }

    /// <summary>
    /// <see cref="ConflictException"/> (HTTP 409) si la société a déjà ce destinataire pour ce type.
    /// Deux POST identiques créaient deux lignes : la liste montrait le doublon et le client
    /// croyait à deux abonnements (recette GPA, DEF-053). L'adresse est comparée sans la casse,
    /// comme le dispatcher dédoublonne ses envois ; le type exactement, comme il le lit.
    /// </summary>
    /// <param name="exceptId">
    /// Ligne en cours de modification, exclue de la recherche. La modification n'appelle ce
    /// contrôle que si le couple change, mais sa comparaison C# (Trim, OrdinalIgnoreCase) et
    /// celle du SQL (btrim, lower) peuvent diverger sur une adresse ancienne non ASCII : la
    /// ligne ne doit jamais pouvoir se refuser elle-même.
    /// </param>
    public static async Task EnsureUniqueAsync(
        IGisDbContext context, int companyId, string email, string alertType, int? exceptId, CancellationToken ct)
    {
        var emailLower = email.ToLowerInvariant();

        // Filtre société explicite : le filtre global est levé pour l'administrateur système.
        var exists = await context.AlertEmails
            .IgnoreQueryFilters()
            .AnyAsync(a => a.CompanyId == companyId
                        && a.AlertType == alertType
                        && a.Email.Trim().ToLower() == emailLower
                        && (exceptId == null || a.Id != exceptId), ct);

        if (exists)
        {
            var libelle = Types.FirstOrDefault(t => t.Type == alertType).Libelle ?? alertType;
            throw new ConflictException($"{email} reçoit déjà les alertes « {libelle} ».");
        }
    }
}

public class CreateAlertEmailCommandHandler : IRequestHandler<CreateAlertEmailCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateAlertEmailCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(CreateAlertEmailCommand request, CancellationToken ct)
    {
        var entity = new AlertEmail
        {
            Email = AlertEmailInput.NormalizeEmail(request.Email),
            AlertType = AlertEmailInput.NormalizeAlertType(request.AlertType),
            CompanyId = _tenantService.CompanyId ?? 0
        };

        await AlertEmailInput.EnsureUniqueAsync(
            _context, entity.CompanyId, entity.Email, entity.AlertType, exceptId: null, ct);

        _context.AlertEmails.Add(entity);
        await _context.SaveChangesAsync(ct);

        return entity.Id;
    }
}
