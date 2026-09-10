namespace GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;

/// <summary>
/// Accès brut à la base pour la remise à zéro d'une société : lecture du catalogue et exécution
/// d'ordres SQL paramétrés dans une transaction. Séparé du handler pour que la logique (plan,
/// garde-fous, rapport) soit testable sur SQLite pendant que la production lit pg_catalog.
/// </summary>
public interface ICompanyDataStore
{
    Task<CompanyDataCatalog> LoadCatalogAsync(CancellationToken ct);

    /// <summary>Ouvre la transaction ; tout ce qui suit s'exécute dedans jusqu'à Commit/Rollback.</summary>
    Task BeginTransactionAsync(CancellationToken ct);
    Task CommitAsync(CancellationToken ct);
    Task RollbackAsync(CancellationToken ct);

    /// <summary>Exécute un ordre (DELETE…) où {0} est remplacé par le paramètre ; renvoie le nombre de lignes.</summary>
    Task<int> ExecuteAsync(string sql, int companyId, CancellationToken ct);

    /// <summary>Compte les lignes d'une table (identifiant déjà entre guillemets) satisfaisant un prédicat.</summary>
    Task<long> CountAsync(string quotedTable, string where, int companyId, CancellationToken ct);

    /// <summary>Valeurs texte d'une colonne pour les lignes qui vont être supprimées (chemins de fichiers).</summary>
    Task<IReadOnlyList<string>> SelectStringsAsync(string quotedTable, string quotedColumn, string where, int companyId, CancellationToken ct);
}
