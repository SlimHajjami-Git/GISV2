using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class VehicleCost
{
    [Key]
    public int Id { get; set; }

    public int VehicleId { get; set; }

    [ForeignKey("VehicleId")]
    public Vehicle? Vehicle { get; set; }

    public int CompanyId { get; set; }

    [ForeignKey("CompanyId")]
    public Company? Company { get; set; }

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty; // fuel, maintenance, insurance, tax, toll, parking, fine, other

    [MaxLength(500)]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal Amount { get; set; }

    public DateTime Date { get; set; }

    public int? Mileage { get; set; }

    [MaxLength(100)]
    public string? ReceiptNumber { get; set; }

    [MaxLength(500)]
    public string? ReceiptUrl { get; set; }

    // Fuel specific fields
    [MaxLength(20)]
    public string? FuelType { get; set; } // diesel, gasoline, electric

    [Column(TypeName = "decimal(10,2)")]
    public decimal? Liters { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    public decimal? PricePerLiter { get; set; }

    public int? CreatedByUserId { get; set; }

    [ForeignKey("CreatedByUserId")]
    public User? CreatedByUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
