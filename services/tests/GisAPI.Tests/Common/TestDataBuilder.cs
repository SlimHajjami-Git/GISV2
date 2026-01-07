using GisAPI.Domain.Entities;

namespace GisAPI.Tests.Common;

public static class TestDataBuilder
{
    public static Subscription CreateSubscription(int id = 1)
    {
        return new Subscription
        {
            Id = id,
            Name = "Test Subscription",
            Type = "parc",
            Price = 100,
            MaxVehicles = 50,
            GpsTracking = true,
            Features = new[] { "Feature1", "Feature2" }
        };
    }

    public static Company CreateCompany(int id = 1, int subscriptionId = 1)
    {
        return new Company
        {
            Id = id,
            Name = "Test Company",
            Type = "transport",
            SubscriptionId = subscriptionId,
            Email = "company@test.com",
            Phone = "+212600000000",
            Settings = new CompanySettings()
        };
    }

    public static User CreateUser(int id = 1, int companyId = 1, string email = "user@test.com")
    {
        return new User
        {
            Id = id,
            Name = "Test User",
            Email = email,
            CompanyId = companyId,
            PasswordHash = "$2a$11$K5pP5P5P5P5P5P5P5P5P5OqzqzqzqzqzqzqzqzqzqzqzqzqzqzqzqzqA", // BCrypt hash
            Roles = new[] { "admin" },
            Permissions = new[] { "dashboard", "vehicles" },
            Status = "active"
        };
    }

    public static Vehicle CreateVehicle(int id = 1, int companyId = 1, string name = "Test Vehicle")
    {
        return new Vehicle
        {
            Id = id,
            Name = name,
            Type = "camion",
            Brand = "Mercedes",
            Model = "Actros",
            Plate = "12345-A-1",
            Year = 2023,
            Color = "White",
            Status = "available",
            Mileage = 50000,
            CompanyId = companyId
        };
    }

    public static Employee CreateEmployee(int id = 1, int companyId = 1, string name = "Test Driver")
    {
        return new Employee
        {
            Id = id,
            Name = name,
            Email = "driver@test.com",
            Phone = "+212600000001",
            Role = "driver",
            Status = "active",
            CompanyId = companyId,
            LicenseNumber = "ABC123",
            LicenseExpiry = DateTime.UtcNow.AddYears(2)
        };
    }

    public static GpsDevice CreateGpsDevice(int id = 1, int companyId = 1, string uid = "GPS001")
    {
        return new GpsDevice
        {
            Id = id,
            DeviceUid = uid,
            Label = "Test GPS",
            Status = "active",
            CompanyId = companyId,
            SimNumber = "0600000000",
            BatteryLevel = 85,
            SignalStrength = 90
        };
    }

    public static Geofence CreateGeofence(int id = 1, int companyId = 1, string name = "Test Zone")
    {
        return new Geofence
        {
            Id = id,
            Name = name,
            Description = "Test geofence zone",
            Type = "polygon",
            Color = "#3b82f6",
            CompanyId = companyId,
            AlertOnEntry = true,
            AlertOnExit = true,
            IsActive = true,
            Coordinates = new[]
            {
                new GeofencePoint { Lat = 33.5731, Lng = -7.5898 },
                new GeofencePoint { Lat = 33.5831, Lng = -7.5898 },
                new GeofencePoint { Lat = 33.5831, Lng = -7.5798 },
                new GeofencePoint { Lat = 33.5731, Lng = -7.5798 }
            }
        };
    }
}
