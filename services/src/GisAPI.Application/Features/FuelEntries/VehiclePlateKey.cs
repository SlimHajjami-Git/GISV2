using System.Linq.Expressions;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelEntries;

/// <summary>
/// Règle UNIQUE de rattachement d'une saisie à un véhicule par son matricule :
/// seuls lettres et chiffres comptent, casse ignorée, des deux côtés (frappe et
/// matricule enregistré). Partagée par la saisie d'un plein (unitaire et import en
/// masse), l'import Excel et le contrôle de doublon de la fiche véhicule.
///
/// <para>Constat de la campagne de test GPA (DEF-039) : la saisie d'un plein ne
/// retirait que les espaces, l'import Excel tout ce qui n'était ni lettre ni
/// chiffre. Sur un parc immatriculé « GA-214-RK », la frappe « ga 214 rk » était
/// refusée au plein alors que l'import Excel la rattachait.</para>
///
/// <para>La clé garde toute lettre, arabe comprise (« 123 تونس 4567 ») : ni une
/// suite de REPLACE ni une classe d'expression régulière dépendante de la locale
/// du serveur ne la reproduisent à l'identique en SQL. La base ne fait donc que
/// présélectionner (<see cref="MayMatch"/>) et la clé exacte se compare ici.</para>
/// </summary>
public static class VehiclePlateKey
{
    /// <summary>Clé de comparaison : lettres et chiffres seuls, en majuscules. Vide sans matricule.</summary>
    public static string Normalize(string? plate) =>
        string.IsNullOrWhiteSpace(plate)
            ? string.Empty
            : new string(plate.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    /// <summary>
    /// Présélection en base des véhicules dont le matricule, ou à défaut le nom,
    /// PEUT avoir la clé <paramref name="key"/> (déjà passée par <see cref="Normalize"/>) :
    /// ses caractères y figurent dans l'ordre, séparés par n'importe quoi. Tout véhicule
    /// de même clé est retenu ; les rares faux positifs sont écartés en comparant
    /// <see cref="Normalize"/>. Le motif est paramétré : une seule requête compilée.
    /// </summary>
    public static Expression<Func<Vehicle, bool>> MayMatch(string key)
    {
        // Hors ASCII, « _ » (un caractère quelconque) : la mise en majuscules d'une
        // base en locale C ne suit pas celle de .NET, la lettre exacte est vérifiée ensuite.
        var pattern = string.Concat(key.Select(c => c < 128 ? $"%{c}" : "%_")) + "%";
        return v => (v.Plate != null && EF.Functions.Like(v.Plate.ToUpper(), pattern))
                    || EF.Functions.Like(v.Name.ToUpper(), pattern);
    }
}
