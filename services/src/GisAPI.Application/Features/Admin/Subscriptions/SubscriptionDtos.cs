namespace GisAPI.Application.Features.Admin.Subscriptions;

public class AdminSubscriptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "parc";
    public decimal Price { get; set; }
    public int MaxVehicles { get; set; }
    public bool GpsTracking { get; set; }
    public bool GpsInstallation { get; set; }
    public List<string> Features { get; set; } = new();
}
