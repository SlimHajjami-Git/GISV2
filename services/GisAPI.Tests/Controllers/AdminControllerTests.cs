using Microsoft.AspNetCore.Mvc;
using GisAPI.Controllers;

namespace GisAPI.Tests.Controllers;

public class AdminControllerTests
{
    // ==================== DTO TESTS ====================

    [Fact]
    public void SubscriptionDto_CanBeCreated()
    {
        var dto = new SubscriptionDto
        {
            Id = 1,
            Name = "Test Plan",
            Type = "parc",
            Price = 1500,
            MaxVehicles = 100,
            GpsTracking = true,
            GpsInstallation = false
        };

        Assert.Equal(1, dto.Id);
        Assert.Equal("Test Plan", dto.Name);
        Assert.Equal(1500, dto.Price);
        Assert.True(dto.GpsTracking);
    }

    [Fact]
    public void MaintenanceModeDto_CanBeCreated()
    {
        var dto = new MaintenanceModeDto
        {
            Enabled = true,
            Pages = new List<string> { "monitoring", "reports" },
            Message = "Maintenance en cours"
        };

        Assert.True(dto.Enabled);
        Assert.Equal(2, dto.Pages.Count);
        Assert.Equal("Maintenance en cours", dto.Message);
    }

    [Fact]
    public void ServiceHealthDto_CanBeCreated()
    {
        var dto = new ServiceHealthDto
        {
            Name = "GIS API",
            Status = "healthy",
            ResponseTime = 12,
            Uptime = 99.9,
            LastCheck = DateTime.UtcNow
        };

        Assert.Equal("GIS API", dto.Name);
        Assert.Equal("healthy", dto.Status);
        Assert.Equal(12, dto.ResponseTime);
    }

    [Fact]
    public void ActivityLogDto_CanBeCreated()
    {
        var dto = new ActivityLogDto
        {
            Id = "log-001",
            UserId = 1,
            UserName = "Test User",
            CompanyId = 1,
            CompanyName = "Test Company",
            Action = "login",
            Details = "User logged in",
            IpAddress = "192.168.1.1",
            Timestamp = DateTime.UtcNow
        };

        Assert.Equal("log-001", dto.Id);
        Assert.Equal("login", dto.Action);
    }

    [Fact]
    public void EstimateDto_CanBeCreated()
    {
        var dto = new EstimateDto
        {
            Id = "EST-001",
            ClientName = "New Client",
            ClientEmail = "client@test.com",
            Items = new List<EstimateItemDto>
            {
                new EstimateItemDto { Description = "GPS Service", Quantity = 10, UnitPrice = 100, Total = 1000 }
            },
            Subtotal = 1000,
            Tax = 190,
            Total = 1190,
            Status = "draft",
            CreatedAt = DateTime.UtcNow
        };

        Assert.Equal("EST-001", dto.Id);
        Assert.Equal("New Client", dto.ClientName);
        Assert.Equal(1000, dto.Subtotal);
        Assert.Single(dto.Items);
    }

    [Fact]
    public void CreateSubscriptionRequest_CanBeCreated()
    {
        var request = new CreateSubscriptionRequest
        {
            Name = "New Plan",
            Type = "parc",
            Price = 2000,
            MaxVehicles = 50,
            GpsTracking = true,
            GpsInstallation = true
        };

        Assert.Equal("New Plan", request.Name);
        Assert.Equal(2000, request.Price);
        Assert.True(request.GpsTracking);
    }

    [Fact]
    public void EstimateItemDto_CalculatesTotal()
    {
        var item = new EstimateItemDto
        {
            Description = "GPS Device",
            Quantity = 5,
            UnitPrice = 200,
            Total = 1000
        };

        Assert.Equal(5, item.Quantity);
        Assert.Equal(200, item.UnitPrice);
        Assert.Equal(1000, item.Total);
    }
}
