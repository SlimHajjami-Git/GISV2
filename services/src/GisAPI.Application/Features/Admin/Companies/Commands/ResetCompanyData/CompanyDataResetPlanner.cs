namespace GisAPI.Application.Features.Admin.Companies.Commands.ResetCompanyData;

/// <summary>Une table du schéma, ses colonnes « société » (company_id, "CompanyId", societe_id…) et toutes ses colonnes.</summary>
public sealed record CatalogTable(string Name, IReadOnlyList<string> CompanyColumns, IReadOnlyList<string> AllColumns);

/// <summary>
/// Une clé étrangère mono-colonne. <see cref="DeleteRule"/> reprend pg_constraint.confdeltype :
/// a = no action, r = restrict, c = cascade, n = set null, d = set default.
/// </summary>
public sealed record CatalogForeignKey(string Child, string ChildColumn, string Parent, string ParentColumn, char DeleteRule);

public sealed record CompanyDataCatalog(IReadOnlyList<CatalogTable> Tables, IReadOnlyList<CatalogForeignKey> ForeignKeys);

/// <summary>Un ordre de suppression : table + prédicat SQL où {0} est l'identifiant société.</summary>
public sealed record ResetStep(string Table, string Where, IReadOnlyList<string> FileColumns);

/// <summary>
/// Un contrôle à faire AVANT de supprimer : des lignes qui portent explicitement une AUTRE société
/// mais que la base effacerait par CASCADE en supprimant leur parent (ex. l'historique d'un véhicule
/// transféré d'une société à l'autre). Un compte non nul interdit la remise à zéro.
/// </summary>
public sealed record ResetGuard(string Table, string Where, string Reason);

public sealed record ResetPlan(IReadOnlyList<ResetStep> Steps, IReadOnlyList<ResetGuard> Guards, IReadOnlyList<string> Protected, IReadOnlyList<string> Skipped);

/// <summary>
/// Construit le plan de remise à zéro d'une société à partir du catalogue de la base, sans rien
/// connaître du métier : tout ce qui porte une colonne société ou pointe (par clé étrangère) vers
/// une ligne de la société est supprimé, SAUF le noyau qui définit la société elle-même :
/// sa fiche, ses utilisateurs, ses rôles, leurs sessions, ses boîtiers GPS et leurs positions
/// brutes, et son historique d'abonnement.
///
/// Pourquoi découvrir plutôt que lister : la liste manuelle du 10/09/2026 comptait 27 tables et
/// en aurait oublié une dès la prochaine fonctionnalité. Le piège réel rencontré ce jour-là
/// (maintenance_logs.company_id vaut 0 partout) impose de suivre AUSSI les clés étrangères :
/// une ligne dont la colonne société est vide (0 ou NULL) mais qui pointe vers un véhicule de la
/// société lui appartient. Une ligne qui porte explicitement une AUTRE société n'est jamais
/// touchée — ni par nos ordres, ni par une cascade de la base (voir <see cref="ResetGuard"/>).
/// </summary>
public static class CompanyDataResetPlanner
{
    /// <summary>Jamais supprimées, quel que soit le schéma.</summary>
    public static readonly IReadOnlySet<string> ProtectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "societes", "users", "roles", "refresh_tokens", "user_device_tokens", "user_settings",
        "gps_devices", "gps_positions", "subscription_orders", "subscription_types",
    };

    private static readonly string[] CompanyColumnNames = { "company_id", "companyid", "societe_id", "societeid", "tenant_id" };
    // Large exprès : une valeur qui ne commence pas par /uploads/ est ignorée à la lecture.
    private static readonly string[] FileColumnHints = { "url", "path", "photo" };

    public static bool IsCompanyColumn(string column) => CompanyColumnNames.Contains(column.ToLowerInvariant());

    /// <summary>Tables de sauvegarde ou temporaires copiées à la main (ex. gps_devices_orphans_bak_20260715).</summary>
    public static bool IsScratchTable(string table)
    {
        var t = table.ToLowerInvariant();
        return t.Contains("_bak") || t.Contains("backup") || t.StartsWith("tmp_") || t.StartsWith("temp_");
    }

    private sealed class Node
    {
        public required string Name;
        public required IReadOnlyList<string> CompanyColumns;
        public readonly List<CatalogForeignKey> Edges = new();   // FK de cette table vers un parent scopé
    }

    public static ResetPlan Plan(CompanyDataCatalog catalog)
    {
        var tables = catalog.Tables.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var skipped = catalog.Tables.Where(t => IsScratchTable(t.Name)).Select(t => t.Name).ToList();

        // Niveau 1 : la colonne société.
        var nodes = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in catalog.Tables)
        {
            if (ProtectedTables.Contains(t.Name) || IsScratchTable(t.Name) || t.CompanyColumns.Count == 0) continue;
            nodes[t.Name] = new Node { Name = t.Name, CompanyColumns = t.CompanyColumns };
        }

        // Niveaux suivants : toute table qui pointe vers une table déjà scopée, jusqu'à stabilité.
        bool changed;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            changed = false;
            foreach (var fk in catalog.ForeignKeys)
            {
                if (!nodes.ContainsKey(fk.Parent)) continue;
                if (ProtectedTables.Contains(fk.Child) || IsScratchTable(fk.Child) || !tables.ContainsKey(fk.Child)) continue;
                if (fk.Child.Equals(fk.Parent, StringComparison.OrdinalIgnoreCase)) continue; // auto-référence
                if (!seen.Add($"{fk.Child}.{fk.ChildColumn}->{fk.Parent}")) continue;

                if (!nodes.TryGetValue(fk.Child, out var child))
                    nodes[fk.Child] = child = new Node { Name = fk.Child, CompanyColumns = tables[fk.Child].CompanyColumns };
                child.Edges.Add(fk);
                changed = true;
            }
        } while (changed);

        // Une table protégée qui pointe vers une table supprimée ferait soit échouer la transaction
        // (NO ACTION / RESTRICT), soit disparaître le noyau en silence (CASCADE) : plan refusé.
        foreach (var fk in catalog.ForeignKeys)
        {
            if (ProtectedTables.Contains(fk.Child) && nodes.ContainsKey(fk.Parent) && fk.DeleteRule is 'a' or 'r' or 'c')
                throw new InvalidOperationException(
                    $"La table protégée {fk.Child} référence {fk.Parent} ({fk.ChildColumn}) sans SET NULL : la remise à zéro violerait cette contrainte.");
        }

        // Ordre : enfants avant parents pour TOUTES les clés (les comptes de l'aperçu sont alors ceux
        // de nos ordres, pas d'une cascade). Un cycle (drivers ⇄ vehicles) ne se coupe que sur une
        // clé SET NULL / CASCADE ; un cycle NO ACTION / RESTRICT est refusé.
        var remaining = new HashSet<string>(nodes.Keys, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        while (remaining.Count > 0)
        {
            bool HasRemainingChild(string t, Func<CatalogForeignKey, bool> which) => catalog.ForeignKeys.Any(fk =>
                fk.Parent.Equals(t, StringComparison.OrdinalIgnoreCase)
                && !fk.Child.Equals(t, StringComparison.OrdinalIgnoreCase)
                && remaining.Contains(fk.Child)
                && which(fk));

            var free = remaining.Where(t => !HasRemainingChild(t, _ => true)).OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (free.Count == 0)
                free = remaining.Where(t => !HasRemainingChild(t, fk => fk.DeleteRule is 'a' or 'r')).OrderBy(t => t, StringComparer.Ordinal).Take(1).ToList();
            if (free.Count == 0)
                throw new InvalidOperationException("Cycle de clés étrangères bloquantes entre : " + string.Join(", ", remaining.OrderBy(x => x)));
            foreach (var t in free) { ordered.Add(t); remaining.Remove(t); }
        }

        var steps = ordered.Select(t =>
        {
            var where = Render(nodes, nodes[t], new List<string>());
            var fileCols = tables[t].AllColumns
                .Where(c => FileColumnHints.Any(h => c.ToLowerInvariant().Contains(h)) && !IsCompanyColumn(c))
                .ToList();
            return new ResetStep(t, where, fileCols);
        }).ToList();

        // Garde-fous : une ligne d'une AUTRE société suspendue à une ligne que nous supprimons serait
        // effacée par la cascade de la base. On la détecte avant, on refuse, on ne devine pas.
        var guards = new List<ResetGuard>();
        foreach (var fk in catalog.ForeignKeys)
        {
            if (fk.DeleteRule != 'c' || !nodes.ContainsKey(fk.Parent) || !tables.TryGetValue(fk.Child, out var child)) continue;
            if (child.CompanyColumns.Count == 0 || fk.Child.Equals(fk.Parent, StringComparison.OrdinalIgnoreCase)) continue;
            var parentWhere = Render(nodes, nodes[fk.Parent], new List<string>());
            var ownedOrUnfilled = string.Join(" OR ", child.CompanyColumns.Select(c => $"{Q(c)} = {{0}} OR {Q(c)} = 0 OR {Q(c)} IS NULL"));
            guards.Add(new ResetGuard(fk.Child,
                $"{Q(fk.ChildColumn)} IN (SELECT {Q(fk.ParentColumn)} FROM {Q(fk.Parent)} WHERE {parentWhere}) AND NOT ({ownedOrUnfilled})",
                $"{fk.Child} : des lignes d'une autre société sont rattachées à {fk.Parent} de cette société (la base les effacerait en cascade)"));
        }

        var protectedPresent = catalog.Tables.Where(t => ProtectedTables.Contains(t.Name)).Select(t => t.Name).OrderBy(x => x).ToList();
        return new ResetPlan(steps, guards, protectedPresent, skipped);
    }

    /// <summary>
    /// Prédicat d'appartenance d'une table à la société {0} :
    ///  - colonne société = {0} ;
    ///  - OU (colonne société vide ET clé étrangère vers une ligne de la société) ;
    ///  - sans colonne société : clé étrangère vers une ligne de la société.
    /// Les cycles (drivers ⇄ vehicles) sont coupés en n'empruntant pas une table déjà sur le chemin.
    /// </summary>
    private static string Render(Dictionary<string, Node> nodes, Node node, List<string> path)
    {
        path.Add(node.Name);
        try
        {
            var byCompany = node.CompanyColumns.Select(c => $"{Q(c)} = {{0}}").ToList();
            var byFk = new List<string>();
            foreach (var fk in node.Edges)
            {
                if (path.Contains(fk.Parent, StringComparer.OrdinalIgnoreCase)) continue;
                var parentWhere = Render(nodes, nodes[fk.Parent], path);
                byFk.Add($"{Q(fk.ChildColumn)} IN (SELECT {Q(fk.ParentColumn)} FROM {Q(fk.Parent)} WHERE {parentWhere})");
            }

            if (byCompany.Count == 0) return Or(byFk);
            if (byFk.Count == 0) return Or(byCompany);

            var unfilled = string.Join(" AND ", node.CompanyColumns.Select(c => $"({Q(c)} = 0 OR {Q(c)} IS NULL)"));
            return $"{Or(byCompany)} OR (({unfilled}) AND ({Or(byFk)}))";
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    private static string Or(IReadOnlyList<string> parts) => parts.Count == 1 ? parts[0] : string.Join(" OR ", parts.Select(p => $"({p})"));

    /// <summary>Identifiant SQL entre guillemets doubles (les colonnes PascalCase l'exigent).</summary>
    public static string Q(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
