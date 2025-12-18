using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GisAPI.Models;

public class Subscription
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = "parc"; // parc, parc_gps, parc_gps_install

    [Column(TypeName = "decimal(10,2)")]
    public decimal Price { get; set; }

    public string[] Features { get; set; } = Array.Empty<string>();

    public bool GpsTracking { get; set; }

    public bool GpsInstallation { get; set; }

    public int MaxVehicles { get; set; } = 10;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Company> Companies { get; set; } = new List<Company>();
}
