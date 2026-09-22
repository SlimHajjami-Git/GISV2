using System.ComponentModel.DataAnnotations.Schema;
using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Societe : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "transport"; // transport, location, autre
    public string? Description { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string Country { get; set; } = "TN";
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public SocieteSettings? Settings { get; set; }
    
    public string? LogoUrl { get; set; }
    public string? TaxId { get; set; }
    public string? RC { get; set; }
    public string? IF { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Subscription dates and status
    public DateTime SubscriptionStartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubscriptionExpiresAt { get; set; }
    public string BillingCycle { get; set; } = "yearly";
    public string SubscriptionStatus { get; set; } = "active";
    public DateTime? LastPaymentAt { get; set; }
    public decimal? NextPaymentAmount { get; set; }
    
    // Subscription relation (direct link to subscription type/plan)
    public int? SubscriptionTypeId { get; set; }
    public SubscriptionType? SubscriptionType { get; set; }

    /// <summary>
    /// Tranche de véhicules déclarée à l'inscription (« 1-5 », « 6-20 »…).
    /// Donnée commerciale destinée à qualifier le prospect ; elle n'a aucun
    /// effet fonctionnel et ne limite rien.
    ///
    /// <para>On conserve le CODE de la tranche, pas un nombre : l'utilisateur
    /// déclare un ordre de grandeur. Le décompte réel se lit dans
    /// <c>vehicles</c>.</para>
    ///
    /// <para><c>null</c> pour toute société antérieure à ce champ, et pour les
    /// clients d'API qui ne l'envoient pas.</para>
    /// </summary>
    [Column("fleet_size_range")]
    public string? FleetSizeRange { get; set; }

    // Anti-doublon persistant pour l'envoi du rapport journalier : date du dernier
    // rapport deja envoye a cette societe. Survit aux redemarrages de l'API (evite
    // de renvoyer le rapport a tout le monde lors d'un deploiement apres 06:00).
    [Column("last_daily_report_sent_date")]
    public DateOnly? LastDailyReportSentDate { get; set; }

    // Anti-doublon persistant pour le récapitulatif HEBDOMADAIRE (envoyé le lundi).
    // Stocke le lundi (date) de la dernière semaine déjà envoyée à cette société.
    [Column("last_weekly_report_sent_date")]
    public DateOnly? LastWeeklyReportSentDate { get; set; }

    // ANCIEN quota mensuel de scans de factures IA, en NOMBRE de scans (NULL = défaut,
    // 0 = désactivé). Remplacé le 22/09/2026 par le crédit en jetons ci-dessous : il
    // n'est plus lu que lorsque InvoiceScanMonthlyTokens est NULL, converti à raison de
    // 3 000 jetons par scan (InvoiceScanCredit.EffectiveBudget). Le réglage admin y écrit
    // une OMBRE pour le retour arrière du pod (InvoiceScanCredit.LegacyScanLimitShadow :
    // NULL, 0, ou l'équivalent en scans), seule colonne que lit l'ancien code.
    [Column("invoice_scan_monthly_limit")]
    public int? InvoiceScanMonthlyLimit { get; set; }

    // Crédit IA MENSUEL du scan de factures, en jetons Groq (migration 052). NULL =
    // défaut plateforme (60 000 ≈ 20 scans), 0 = fonctionnalité désactivée. Remis à
    // zéro le 1er de chaque mois (UTC). Réglé par le sys admin depuis la fiche société.
    [Column("invoice_scan_monthly_tokens")]
    public int? InvoiceScanMonthlyTokens { get; set; }

    // Suspension AUTOMATIQUE à l'expiration de l'abonnement (après la grâce de
    // 7 j — voir SubscriptionPolicy). true = comportement standard ; false = la
    // société expirée garde l'accès (bannière rouge permanente, marquée impayée
    // côté admin) et SEULE la suspension manuelle du sys_admin coupe l'accès.
    // Modifiable par le sys_admin depuis la fiche société de l'admin.
    [Column("auto_suspend_enabled")]
    public bool AutoSuspendEnabled { get; set; } = true;

    // Navigation collections
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
    public ICollection<Vehicle> Vehicles { get; set; } = new List<Vehicle>();
    public ICollection<Geofence> Geofences { get; set; } = new List<Geofence>();
    public ICollection<GpsDevice> GpsDevices { get; set; } = new List<GpsDevice>();
    public ICollection<PointOfInterest> PointsOfInterest { get; set; } = new List<PointOfInterest>();
    public ICollection<Report> Reports { get; set; } = new List<Report>();
    public ICollection<ReportSchedule> ReportSchedules { get; set; } = new List<ReportSchedule>();
    public ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
}

public class SocieteSettings
{
    // Deployment default (TND / DZD) rather than a hardcoded "DT". Set at startup
    // from App:DefaultCurrency; EF overwrites this with the stored value on load.
    public string Currency { get; set; } = AppCurrency.Default;
    public string Timezone { get; set; } = "Africa/Tunis";
    public string Language { get; set; } = "fr";
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    public string DistanceUnit { get; set; } = "km";
    public string SpeedUnit { get; set; } = "kmh";
    public string VolumeUnit { get; set; } = "L";
}



