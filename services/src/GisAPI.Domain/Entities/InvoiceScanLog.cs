using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Domain.Entities;

/// <summary>
/// One successful AI invoice scan (Dépenses → « Scanner une facture »). Used to
/// enforce the per-société monthly quota (<see cref="Societe.InvoiceScanMonthlyLimit"/>)
/// and to audit real Groq token consumption per company/user.
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
