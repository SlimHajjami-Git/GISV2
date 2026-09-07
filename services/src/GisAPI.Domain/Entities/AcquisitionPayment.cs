using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

/// <summary>
/// Échéance d'acquisition persistée d'un véhicule : apport ou achat comptant
/// (une ligne, seq 1) et mensualités de crédit/leasing (seq 1..N).
///
/// <para>Recette client du 07/09/2026 : jusqu'ici ces échéances étaient
/// RECALCULÉES à chaque affichage (écran Dépenses, fiche véhicule, tableau de
/// bord) et « Payé » n'était qu'une présomption calendaire. Rien n'était en
/// base : impossible de marquer une mensualité payée à une autre date, d'y
/// joindre une quittance ou d'en ignorer une. Cette table est générée et
/// recalée par le serveur (AcquisitionScheduleSync) à partir des champs
/// d'acquisition du véhicule ; jamais par SQL.</para>
/// </summary>
public class AcquisitionPayment : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }

    /// <summary>apport | mensualite | achat (voir <see cref="Kinds"/>)</summary>
    public string Kind { get; set; } = Kinds.Mensualite;

    /// <summary>mensualite : 1..N (index de AcquisitionSchedule.LeasingDues) ; apport/achat : 1</summary>
    public int Seq { get; set; }

    /// <summary>Date d'échéance calendaire (jour de prélèvement) — pas d'heure, pas de fuseau.</summary>
    public DateOnly DueDate { get; set; }

    public decimal Amount { get; set; }

    /// <summary>planned | paid | skipped (voir <see cref="Statuses"/>)</summary>
    public string Status { get; set; } = Statuses.Planned;

    public DateTime? PaidAt { get; set; }
    public decimal? PaidAmount { get; set; }
    public string? ReceiptUrl { get; set; }
    public string? Note { get; set; }

    /// <summary>true = produite par le générateur ; false = ajoutée à la main (réservé).</summary>
    public bool Generated { get; set; } = true;

    public static class Kinds
    {
        public const string Apport = "apport";
        public const string Mensualite = "mensualite";
        public const string Achat = "achat";
        public static readonly string[] All = { Apport, Mensualite, Achat };
    }

    public static class Statuses
    {
        public const string Planned = "planned";
        public const string Paid = "paid";
        public const string Skipped = "skipped";
        public static readonly string[] All = { Planned, Paid, Skipped };
    }
}
