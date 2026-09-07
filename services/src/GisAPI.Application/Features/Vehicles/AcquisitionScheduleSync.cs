using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles;

/// <summary>
/// Synchronisation de l'échéancier PERSISTÉ (table acquisition_payments) avec
/// le contrat d'acquisition du véhicule. <see cref="AcquisitionSchedule"/> reste
/// le seul générateur de dates ; ce service ne fait que projeter ses résultats
/// en lignes et recaler ce qui existe déjà.
///
/// <para>Règles (contrat du 07/09/2026) : les lignes « planned » suivent le
/// contrat (recalées ou supprimées) ; les lignes « paid » / « skipped » sont des
/// faits saisis par l'exploitant et sont TOUJOURS conservées, même hors
/// contrat — un règlement réel ne disparaît pas parce qu'on a corrigé une
/// durée. Idempotent : un second appel sans changement du contrat ne touche à
/// rien. Ne fait jamais SaveChanges : l'appelant reste maître de la
/// transaction.</para>
/// </summary>
public static class AcquisitionScheduleSync
{
    public record Expected(string Kind, int Seq, DateOnly Due, decimal Amount);

    /// <summary>Le véhicule porte-t-il quelque chose à échéancer (contrat complet ou prix daté) ?</summary>
    public static bool HasSchedule(Vehicle v) => ExpectedLines(v).Count > 0;

    /// <summary>
    /// « apport » et « achat » sont le MÊME emplacement (le prix d'acquisition,
    /// seq 1) : seul le libellé change avec le type d'acquisition.
    /// </summary>
    private static bool IsAcquisitionKind(string kind) =>
        kind == AcquisitionPayment.Kinds.Apport || kind == AcquisitionPayment.Kinds.Achat;

    /// <summary>
    /// Lignes attendues pour le contrat courant : une « mensualite » par échéance
    /// de <see cref="AcquisitionSchedule.LeasingDues"/>, plus une ligne « apport »
    /// (leasing) ou « achat » (comptant) datée de la date d'achat si un prix est
    /// renseigné. Un véhicule repassé en achat comptant qui garde des résidus de
    /// contrat ne produit AUCUNE mensualité (LeasingDues le garantit).
    /// </summary>
    public static List<Expected> ExpectedLines(Vehicle v)
    {
        var list = new List<Expected>();

        foreach (var (index, due) in AcquisitionSchedule.LeasingDues(v))
            list.Add(new Expected(AcquisitionPayment.Kinds.Mensualite, index,
                DateOnly.FromDateTime(due), v.LeasingMonthlyPayment!.Value));

        if (v.PurchasePrice > 0 && v.PurchaseDate is { } purchased)
        {
            var kind = v.AcquisitionType == "leasing"
                ? AcquisitionPayment.Kinds.Apport
                : AcquisitionPayment.Kinds.Achat;
            list.Add(new Expected(kind, 1, DateOnly.FromDateTime(purchased.Date), v.PurchasePrice.Value));
        }

        return list;
    }

    /// <summary>
    /// Recale l'échéancier persisté du véhicule sur son contrat. Renvoie true si
    /// au moins une ligne a été ajoutée, modifiée ou supprimée (rien n'est
    /// enregistré : à l'appelant de faire SaveChanges).
    /// </summary>
    public static async Task<bool> SyncAsync(IGisDbContext ctx, Vehicle v, CancellationToken ct)
    {
        var expected = ExpectedLines(v).ToDictionary(e => (e.Kind, e.Seq));

        // Chargé sur le seul véhicule, filtres tenant ignorés : les lignes d'un
        // véhicule appartiennent par définition à la société du véhicule, et la
        // contrainte d'unicité en base porte sur (vehicle_id, kind, seq) SANS la
        // société. Un véhicule déplacé d'une société à l'autre par un
        // system_admin verrait sinon ses lignes invisibles ici, ré-insérées, et
        // rejetées par l'unicité à chaque lecture.
        var existing = await ctx.AcquisitionPayments
            .IgnoreQueryFilters()
            .Where(p => p.VehicleId == v.Id)
            .ToListAsync(ct);

        var changed = false;

        // Le véhicule a changé de société : ses échéances suivent, sinon elles
        // continueraient de peser dans le coût de l'ancienne société.
        foreach (var line in existing.Where(p => p.CompanyId != v.CompanyId))
        {
            line.CompanyId = v.CompanyId;
            line.UpdatedAt = DateTime.UtcNow;
            changed = true;
        }

        // Bascule du type d'acquisition : la ligne du prix existe déjà sous
        // l'autre libellé — on la RE-QUALIFIE au lieu d'en créer une seconde.
        // Sans cela, un apport déjà marqué payé (conservé, c'est la règle) et le
        // nouvel achat compteraient tous les deux : prix d'acquisition doublé.
        var wanted = expected.Keys.FirstOrDefault(k => IsAcquisitionKind(k.Kind));
        if (wanted.Kind != null && !existing.Any(p => p.Kind == wanted.Kind && p.Seq == wanted.Seq))
        {
            var previous = existing.FirstOrDefault(p => IsAcquisitionKind(p.Kind) && p.Seq == wanted.Seq);
            if (previous != null)
            {
                previous.Kind = wanted.Kind;
                previous.UpdatedAt = DateTime.UtcNow;
                changed = true;
            }
        }

        foreach (var line in existing)
        {
            if (expected.Remove((line.Kind, line.Seq), out var e))
            {
                if (line.Status == AcquisitionPayment.Statuses.Planned
                    && (line.DueDate != e.Due || line.Amount != e.Amount))
                {
                    line.DueDate = e.Due;
                    line.Amount = e.Amount;
                    line.UpdatedAt = DateTime.UtcNow;
                    changed = true;
                }
                // paid / skipped : la date et le montant du fait saisi ne bougent pas.
            }
            else if (line.Status == AcquisitionPayment.Statuses.Planned)
            {
                ctx.AcquisitionPayments.Remove(line);
                changed = true;
            }
            // paid / skipped hors contrat : conservées.
        }

        foreach (var e in expected.Values)
        {
            ctx.AcquisitionPayments.Add(new AcquisitionPayment
            {
                CompanyId = v.CompanyId,
                VehicleId = v.Id,
                Kind = e.Kind,
                Seq = e.Seq,
                DueDate = e.Due,
                Amount = e.Amount,
                Status = AcquisitionPayment.Statuses.Planned,
                Generated = true
            });
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Empreinte des sept champs d'acquisition : les handlers de mise à jour du
    /// véhicule la relèvent avant/après les affectations et ne synchronisent que
    /// si elle a changé (une édition de kilométrage ne doit pas rebalayer
    /// l'échéancier).
    /// </summary>
    public static (string Type, decimal? Price, DateTime? Purchased, decimal? Monthly, int? Months, DateTime? Start, int? Day)
        Fingerprint(Vehicle v) =>
        (v.AcquisitionType, v.PurchasePrice, v.PurchaseDate, v.LeasingMonthlyPayment,
         v.LeasingDurationMonths, v.LeasingStartDate, v.LeasingPaymentDay);
}
