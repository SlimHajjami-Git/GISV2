using GisAPI.Domain.Common;

namespace GisAPI.Domain.Entities;

public class Subscription : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "parc";
    public decimal Price { get; set; }
    public string[] Features { get; set; } = [];
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
    public int MaxVehicles { get; set; } = 10;
    public int MaxUsers { get; set; } = 5;
    public int MaxGpsDevices { get; set; } = 10;
    public int MaxGeofences { get; set; } = 10;
    public string BillingCycle { get; set; } = "monthly";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Company> Companies { get; set; } = new List<Company>();
}
