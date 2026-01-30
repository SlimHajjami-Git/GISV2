using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Contract : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public string ContractNumber { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Provider { get; set; }
    public string? ProviderContact { get; set; }
    public string? ProviderPhone { get; set; }
    public string? ProviderEmail { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public decimal TotalValue { get; set; }
    public decimal? MonthlyPayment { get; set; }
    public string PaymentFrequency { get; set; } = "monthly";
    public int? AllowedKmPerYear { get; set; }
    public decimal? ExcessKmRate { get; set; }
    public decimal? ResidualValue { get; set; }
    public string? PolicyNumber { get; set; }
    public string? CoverageType { get; set; }
    public decimal? Deductible { get; set; }
    public string Status { get; set; } = "active";
    public string? DocumentUrl { get; set; }
    public string? Notes { get; set; }
    public int? ReminderDaysBefore { get; set; }
    public bool ReminderSent { get; set; }
}

public class Reservation : TenantEntity
{
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? RequestedByUserId { get; set; }
    public User? RequestedByUser { get; set; }
    public int? AssignedDriverId { get; set; }
    public User? AssignedDriver { get; set; }
    public string? Purpose { get; set; }
    public string? Destination { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public int? EstimatedKm { get; set; }
    public int? ActualKm { get; set; }
    public int? StartMileage { get; set; }
    public int? EndMileage { get; set; }
    public string Status { get; set; } = "pending";
    public int? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? Notes { get; set; }
}


