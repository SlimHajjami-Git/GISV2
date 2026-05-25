using System.ComponentModel.DataAnnotations.Schema;
using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Vehicle : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "camion";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Plate { get; set; }
    public int? Year { get; set; }
    public string? Color { get; set; }
    public string Status { get; set; } = "available";
    public bool HasGps { get; set; }
    public int Mileage { get; set; }
    public int? RentalMileage { get; set; }

    [Column("is_rented")]
    public bool IsRented { get; set; } = false;

    public int? AssignedDriverId { get; set; }
    public Driver? AssignedDriver { get; set; }
    
    public int? AssignedSupervisorId { get; set; }
    public User? AssignedSupervisor { get; set; }
    
    public int? GpsDeviceId { get; set; }
    public GpsDevice? GpsDevice { get; set; }
    
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    
    // Calypso 9 — default harmonised to 110 km/h (was 120). The over-speed
    // margin was removed, so the alert now fires at the exact limit; 110 =
    // Tunisia highway max → alert pile à 110 instead of an illogical 140.
    public int? SpeedLimit { get; set; } = 110;
    public string? FuelType { get; set; } = "diesel";
    [Column("fuel_tank_capacity")]
    public int? FuelTankCapacity { get; set; } // Capacité réservoir en litres

    public Societe? Societe { get; set; }
    
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }

    // Acquisition info
    public string AcquisitionType { get; set; } = "purchase"; // "purchase" or "leasing"
    public decimal? PurchasePrice { get; set; }
    // Calypso 6 (P5): separate "Date d'achat" from "Date mise en circulation"
    [Column("purchase_date")]
    public DateTime? PurchaseDate { get; set; }
    public decimal? LeasingMonthlyPayment { get; set; }
    public int? LeasingDurationMonths { get; set; } // Nombre de mois du leasing
    public DateTime? LeasingStartDate { get; set; } // Date début du leasing
    public int? LeasingPaymentDay { get; set; } // Jour du mois pour le paiement (1-28)
    public DateTime? RegistrationDate { get; set; } // Date de mise en circulation

    // Document expiry dates
    public DateTime? InsuranceExpiry { get; set; }
    public DateTime? TechnicalInspectionExpiry { get; set; }
    public DateTime? TaxExpiry { get; set; }
    public DateTime? RegistrationExpiry { get; set; }
    public DateTime? TransportPermitExpiry { get; set; }

    // Document start dates
    public DateTime? InsuranceStartDate { get; set; }
    public DateTime? TaxStartDate { get; set; }
    public DateTime? TechnicalInspectionStartDate { get; set; }

    // Reminder days before expiry
    public int InsuranceReminderDays { get; set; } = 30;
    public int TaxReminderDays { get; set; } = 30;
    public int TechnicalInspectionReminderDays { get; set; } = 30;

    public ICollection<VehicleDocument> Documents { get; set; } = new List<VehicleDocument>();
    public ICollection<MaintenanceRecord> MaintenanceRecords { get; set; } = new List<MaintenanceRecord>();
    public ICollection<VehicleCost> Costs { get; set; } = new List<VehicleCost>();

    // ── Immobilisation prévue ──────────────────────────────────────────────
    // When the operator deliberately takes a vehicle out of service
    // (mechanic intervention, long-term parking, boîtier removed for
    // maintenance, fleet seasonal pause…) the GPS device may emit
    // spurious data — wild MEMS readings while being towed, fake
    // speeding while on a flatbed truck, no signal at all — that would
    // otherwise spam the operator with false alarms.
    //
    // Setting `IsImmobilized = true` suppresses ALL automatic alert
    // services for this vehicle (accident detection, voltage health,
    // driving behaviour, predictive alerts, …) until the operator
    // clears the flag manually. The vehicle remains visible on the
    // monitoring page (with a clear "immobilisé" badge) so the
    // operator never loses sight of it — they just stop receiving
    // automated complaints about a known-offline asset.
    [Column("is_immobilized")]
    public bool IsImmobilized { get; set; } = false;

    /// <summary>
    /// Free-text reason captured at activation time (e.g. "Garage Mahmoud,
    /// changement courroie", "Parking longue durée Gabès Q2 2026", …).
    /// Shown in the immobilisation badge tooltip so a second admin
    /// understands why the vehicle is muted without asking around.
    /// </summary>
    [Column("immobilization_reason")]
    public string? ImmobilizationReason { get; set; }

    /// <summary>
    /// Timestamp the immobilisation was activated. Drives the
    /// "immobilisé depuis 3 jours" copy on the UI.
    /// </summary>
    [Column("immobilization_started_at")]
    public DateTime? ImmobilizationStartedAt { get; set; }

    /// <summary>
    /// User who flipped <see cref="IsImmobilized"/> to true — auditing
    /// helper so we can trace who muted alerts on a vehicle that later
    /// suffered a real incident.
    /// </summary>
    [Column("immobilization_started_by_user_id")]
    public int? ImmobilizationStartedByUserId { get; set; }
}

public class VehicleDocument : Entity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public string? FileUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


