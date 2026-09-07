using System.Linq.Expressions;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AcquisitionPayments;

/// <summary>
/// Règle de comptage d'une échéance d'acquisition — définition UNIQUE, partagée
/// par le DTO (drapeaux counted / overdue), l'écran Dépenses et la somme du
/// tableau de bord.
///
/// <para>Une ligne COMPTE si elle est payée, ou planifiée et dont la date est
/// atteinte (« l'avenir n'est pas une dépense », même règle que
/// <see cref="Vehicles.AcquisitionSchedule.Cost"/>). Une ligne ignorée ne compte
/// jamais. Montant compté = montant réglé s'il est connu, sinon montant prévu.
/// Rattachement à une période par la date d'échéance.</para>
/// </summary>
public static class AcquisitionPaymentRules
{
    public static bool IsCounted(string status, DateOnly dueDate, DateOnly today) =>
        status == AcquisitionPayment.Statuses.Paid
        || (status == AcquisitionPayment.Statuses.Planned && dueDate <= today);

    public static bool IsOverdue(string status, DateOnly dueDate, DateOnly today) =>
        status == AcquisitionPayment.Statuses.Planned && dueDate < today;

    public static decimal CountedAmount(decimal amount, decimal? paidAmount) => paidAmount ?? amount;

    /// <summary>Même règle que <see cref="IsCounted"/>, traduisible en SQL par EF.</summary>
    public static Expression<Func<AcquisitionPayment, bool>> CountedOn(DateOnly today) =>
        p => p.Status == AcquisitionPayment.Statuses.Paid
             || (p.Status == AcquisitionPayment.Statuses.Planned && p.DueDate <= today);

    /// <summary>
    /// Σ des montants comptés dont la date d'échéance tombe dans [from, to],
    /// calculée en SQL sur la source fournie (déjà scopée société / véhicules
    /// visibles par l'appelant).
    /// </summary>
    public static async Task<decimal> CountedCostAsync(IQueryable<AcquisitionPayment> source,
        DateTime from, DateTime to, DateTime now, CancellationToken ct)
    {
        var f = DateOnly.FromDateTime(from.Date);
        var t = DateOnly.FromDateTime(to.Date);
        var today = DateOnly.FromDateTime(now.Date);

        return await source
            .Where(p => p.DueDate >= f && p.DueDate <= t)
            .Where(CountedOn(today))
            .Select(p => (decimal?)(p.PaidAmount ?? p.Amount))
            .SumAsync(ct) ?? 0m;
    }

    /// <summary>Version en mémoire de <see cref="CountedCostAsync"/> (tests, parité).</summary>
    public static decimal CountedCost(IEnumerable<AcquisitionPayment> lines, DateTime from, DateTime to, DateTime now)
    {
        var f = DateOnly.FromDateTime(from.Date);
        var t = DateOnly.FromDateTime(to.Date);
        var today = DateOnly.FromDateTime(now.Date);

        return lines
            .Where(p => p.DueDate >= f && p.DueDate <= t && IsCounted(p.Status, p.DueDate, today))
            .Sum(p => CountedAmount(p.Amount, p.PaidAmount));
    }
}
