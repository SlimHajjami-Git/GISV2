using FluentValidation;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.FuelEntries.Commands;

public class BulkCreateFuelEntriesCommandHandler : IRequestHandler<BulkCreateFuelEntriesCommand, BulkCreateFuelEntriesResult>
{
    private readonly ISender _sender;
    private readonly IGisDbContext _context;
    private readonly ILogger<BulkCreateFuelEntriesCommandHandler> _logger;

    public BulkCreateFuelEntriesCommandHandler(ISender sender, IGisDbContext context, ILogger<BulkCreateFuelEntriesCommandHandler> logger)
    {
        _sender = sender;
        _context = context;
        _logger = logger;
    }

    public async Task<BulkCreateFuelEntriesResult> Handle(BulkCreateFuelEntriesCommand request, CancellationToken cancellationToken)
    {
        var results = new List<BulkFuelEntryLineResult>(request.Lignes.Count);

        for (var i = 0; i < request.Lignes.Count; i++)
        {
            var rang = i + 1;
            var ligne = request.Lignes[i];

            if (ligne == null)
            {
                results.Add(new BulkFuelEntryLineResult(rang, 0, false, "Ligne vide : aucune donnée de plein à enregistrer."));
                continue;
            }

            try
            {
                var id = await _sender.Send(ligne, cancellationToken);
                results.Add(new BulkFuelEntryLineResult(rang, id, true, null));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Import carburant : ligne {Row} refusée (matricule={Plate}, typeCarburant={FuelTypeId}, volume={Volume}, date={Date})",
                    rang, ligne.VehiclePlate, ligne.FuelTypeId, ligne.Volume, ligne.InvoiceDate);
                results.Add(new BulkFuelEntryLineResult(rang, 0, false, MotifDeRefus(ex)));
            }
            finally
            {
                // Toutes les lignes partagent le contexte EF de la requête. Une ligne
                // refusée à l'enregistrement (clé étrangère, taille de colonne…) y
                // laissait son plein « à insérer » et le kilométrage qu'elle avait
                // avancé : chaque SaveChanges suivant les rejouait et échouait, et
                // toutes les lignes valides après la première fautive étaient perdues
                // (recette GPA, DEF-010). Chaque ligne repart donc d'un suivi vide ;
                // les lignes précédentes sont déjà en base, rien d'utile n'est abandonné.
                _context.ChangeTracker.Clear();
            }
        }

        var success = results.Count(r => r.Success);
        return new BulkCreateFuelEntriesResult(results.Count, success, results.Count - success, results);
    }

    /// <summary>
    /// Motif lisible par l'exploitant. Les messages métier sont déjà en français ;
    /// les erreurs techniques (EF, framework) sont en anglais et ne disent rien
    /// d'utile à celui qui corrige son fichier Excel : on les remplace.
    /// </summary>
    private static string MotifDeRefus(Exception ex) => ex switch
    {
        NotFoundException => "Élément introuvable : vérifiez les références de la ligne.",
        ForbiddenAccessException => "Accès refusé : vous n'avez pas le droit d'enregistrer ce plein.",
        DomainException domain => domain.Message,
        ValidationException validation => string.Join(" ", validation.Errors.Select(e => e.ErrorMessage).Distinct()),
        // Le véhicule est retrouvé en base par son matricule et le type de carburant est
        // contrôlé en amont : restent le chauffeur (driver_id → users, non contrôlé) et
        // les valeurs plus grandes que leur colonne (textes, volume, prix). Le type n'est
        // cité que pour le cas d'une suppression entre le contrôle et l'enregistrement.
        DbUpdateException => "Enregistrement refusé par la base de données : le chauffeur ou le type de carburant " +
                             "indiqué n'existe pas, ou une valeur (station, n° de facture, notes, volume, prix) dépasse la taille permise.",
        UnauthorizedAccessException => "Société introuvable pour l'utilisateur connecté.",
        _ => "Erreur inattendue lors de l'enregistrement de cette ligne."
    };
}
