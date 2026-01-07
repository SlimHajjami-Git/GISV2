namespace GisAPI.Infrastructure.Messaging;

public class RabbitMqSettings
{
    public const string SectionName = "RabbitMQ";
    
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    
    // Exchanges
    public string GpsExchange { get; set; } = "gis.gps";
    public string AlertsExchange { get; set; } = "gis.alerts";
    public string EventsExchange { get; set; } = "gis.events";
    
    // Queues
    public string GpsPositionsQueue { get; set; } = "gis.gps.positions";
    public string AlertsQueue { get; set; } = "gis.alerts.notifications";
    public string EventsQueue { get; set; } = "gis.events.all";
}
