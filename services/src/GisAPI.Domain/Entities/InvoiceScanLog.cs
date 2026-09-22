using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Domain.Entities;

/// <summary>
/// One successful AI invoice scan (« Scanner une facture »). La somme de ses
/// <see cref="TokensUsed"/> sur le mois civil est la consommation du crédit IA mensuel
/// de la société (<see cref="Societe.InvoiceScanMonthlyTokens"/>, 22/09/2026) ; sert aussi
/// à auditer la consommation Groq réelle par société et par utilisateur.
/// </summary>
public class InvoiceScanLog
{
    [Column("id")]
    public long Id { get; set; }

    [Column("company_id")]
    public int CompanyId { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    /// <summary>Groq tokens consumed by the extraction (0 when unknown).</summary>
    [Column("tokens_used")]
    public int TokensUsed { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
