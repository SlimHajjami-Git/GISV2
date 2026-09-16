using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Suppliers;

/// <summary>
/// Services proposés par un fournisseur (table <c>supplier_services</c>).
/// La navigation <c>Supplier.Services</c> est ignorée par le mapping EF : on
/// passe toujours par la table, jamais par la navigation.
/// </summary>
internal static class SupplierServiceCodes
{
    public static readonly string[] Valides =
        { "mecanique", "carrosserie", "electricite", "pneumatique", "vidange", "climatisation", "diagnostic" };

    /// <summary>
    /// Codes de service par fournisseur, triés ; un fournisseur sans service est absent du dictionnaire.
    /// </summary>
    public static async Task<Dictionary<int, List<string>>> ParFournisseurAsync(
        IGisDbContext context, IReadOnlyCollection<int> supplierIds, CancellationToken ct)
    {
        if (supplierIds.Count == 0)
            return new Dictionary<int, List<string>>();

        var lignes = await context.SupplierServices
            .AsNoTracking()
            .Where(ss => supplierIds.Contains(ss.SupplierId))
            .Select(ss => new { ss.SupplierId, ss.ServiceCode })
            .ToListAsync(ct);

        return lignes
            .GroupBy(l => l.SupplierId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(l => l.ServiceCode).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// Remplacement complet de la liste ; les codes hors référentiel sont ignorés.
    /// Ne fait pas SaveChanges : l'appelant reste maître de la transaction.
    /// </summary>
    public static async Task RemplacerAsync(
        IGisDbContext context, int supplierId, IEnumerable<string> codes, CancellationToken ct)
    {
        var existants = await context.SupplierServices
            .Where(ss => ss.SupplierId == supplierId)
            .ToListAsync(ct);
        context.SupplierServices.RemoveRange(existants);

        foreach (var code in codes
                     .Where(c => !string.IsNullOrWhiteSpace(c))
                     .Select(c => c.Trim().ToLowerInvariant())
                     .Where(c => Valides.Contains(c))
                     .Distinct())
        {
            context.SupplierServices.Add(new SupplierService
            {
                SupplierId = supplierId,
                ServiceCode = code
            });
        }
    }
}
