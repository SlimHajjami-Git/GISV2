using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class AccidentClaim : TenantEntity
{
    public string ClaimNumber { get; set; } = string.Empty;
    public int VehicleId { get; set; }
    public Vehicle? Vehicle { get; set; }
    public int? DriverId { get; set; }
    public User? Driver { get; set; }
    
    public DateTime AccidentDate { get; set; }
    public TimeSpan AccidentTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    
    public string? WeatherConditions { get; set; }
    public string? RoadConditions { get; set; }
    public string Description { get; set; } = string.Empty;
    
    public string Severity { get; set; } = "minor"; // minor, moderate, major, total_loss
    public decimal EstimatedDamage { get; set; }
    public decimal? ApprovedAmount { get; set; }
    public string Status { get; set; } = "draft"; // draft, submitted, under_review, approved, rejected, closed
    
    public bool ThirdPartyInvolved { get; set; }
    public string? PoliceReportNumber { get; set; }
    public int? MileageAtAccident { get; set; }
    public string? DamagedZones { get; set; } // JSON array: ["Avant", "Arrière", "Côté gauche"]
    public string? Witnesses { get; set; }
    public string? AdditionalNotes { get; set; }
    
    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    
    // Navigation properties
    public ICollection<AccidentClaimThirdParty> ThirdParties { get; set; } = new List<AccidentClaimThirdParty>();
    public ICollection<AccidentClaimDocument> Documents { get; set; } = new List<AccidentClaimDocument>();
}

public class AccidentClaimThirdParty : Entity
{
    public int ClaimId { get; set; }
    public AccidentClaim? Claim { get; set; }
    
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public string? VehiclePlate { get; set; }
    public string? VehicleModel { get; set; }
    public string? InsuranceCompany { get; set; }
    public string? InsuranceNumber { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AccidentClaimDocument : Entity
{
    public int ClaimId { get; set; }
    public AccidentClaim? Claim { get; set; }
    
    public string DocumentType { get; set; } = string.Empty; // photo, police_report, insurance_form, repair_estimate, other
    public string FileName { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public int? FileSize { get; set; }
    public string? MimeType { get; set; }
    
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}


