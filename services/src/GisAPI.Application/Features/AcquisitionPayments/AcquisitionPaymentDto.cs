using GisAPI.Domain.Entities;

namespace GisAPI.Application.Features.AcquisitionPayments;

/// <summary>
/// Échéance d'acquisition telle que l'écran Dépenses et la fiche véhicule la
/// consomment. <c>Total</c> = nombre de mensualités du contrat pour afficher
/// « 3/36 » (1 pour un apport ou un achat). <c>Counted</c> et <c>Overdue</c>
/// sont calculés par le serveur (règle unique, AcquisitionPaymentRules).
/// </summary>
public record AcquisitionPaymentDto(
    int Id,
    int VehicleId,
    string? VehiclePlate,
    string VehicleName,
    string Kind,
    int Seq,
    int Total,
    DateOnly DueDate,
    decimal Amount,
    string Status,
    DateTime? PaidAt,
    decimal? PaidAmount,
    string? ReceiptUrl,
    string? Note,
    bool Generated,
    bool Counted,
    bool Overdue)
{
    public static AcquisitionPaymentDto From(AcquisitionPayment p, Vehicle v, DateOnly today) =>
        new(
            p.Id,
            p.VehicleId,
            v.Plate,
            v.Name,
            p.Kind,
            p.Seq,
            TotalFor(p, v),
            p.DueDate,
            p.Amount,
            p.Status,
            p.PaidAt,
            p.PaidAmount,
            p.ReceiptUrl,
            p.Note,
            p.Generated,
            AcquisitionPaymentRules.IsCounted(p.Status, p.DueDate, today),
            AcquisitionPaymentRules.IsOverdue(p.Status, p.DueDate, today));

    /// <summary>
    /// Dénominateur de « i/N » : la durée du contrat pour une mensualité, 1
    /// sinon. Une mensualité payée conservée après une réduction de durée
    /// (seq 30 sur un contrat ramené à 24 mois) garde un dénominateur ≥ seq
    /// pour ne pas afficher « 30/24 ».
    /// </summary>
    private static int TotalFor(AcquisitionPayment p, Vehicle v) =>
        p.Kind == AcquisitionPayment.Kinds.Mensualite
            ? Math.Max(v.LeasingDurationMonths ?? 0, p.Seq)
            : 1;
}
