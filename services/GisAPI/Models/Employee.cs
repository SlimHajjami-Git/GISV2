using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Employee
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(50)]
    public string Role { get; set; } = "driver"; // driver, accountant, hr, supervisor, other

    [MaxLength(20)]
    public string Status { get; set; } = "active"; // active, inactive

    public DateTime? HireDate { get; set; }

    [MaxLength(100)]
    public string? LicenseNumber { get; set; }

    public DateTime? LicenseExpiry { get; set; }

    [MaxLength(20)]
    public string? CIN { get; set; } // Carte d'identité nationale

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? EmergencyContact { get; set; }

    [MaxLength(20)]
    public string? EmergencyPhone { get; set; }

    public DateTime? DateOfBirth { get; set; }

    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation - vehicles where this employee is driver
    public ICollection<Vehicle> AssignedVehiclesAsDriver { get; set; } = new List<Vehicle>();
    
    // Navigation - vehicles where this employee is supervisor
    public ICollection<Vehicle> AssignedVehiclesAsSupervisor { get; set; } = new List<Vehicle>();

    // Navigation - driver assignments history
    public ICollection<DriverAssignment> DriverAssignments { get; set; } = new List<DriverAssignment>();
    public ICollection<DriverScore> DriverScores { get; set; } = new List<DriverScore>();
    public ICollection<Trip> Trips { get; set; } = new List<Trip>();
}



