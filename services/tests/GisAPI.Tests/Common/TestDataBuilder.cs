using GisAPI.Domain.Entities;

namespace GisAPI.Tests.Common;

public static class TestDataBuilder
{
    public static SubscriptionType CreateSubscriptionType(int id = 1)
    {
        return new SubscriptionType
        {
            Id = id,
            Name = "Test Subscription",
            Code = "test",
            YearlyPrice = 1200,
            MaxVehicles = 50,
            MaxUsers = 10,
            GpsTracking = true,
            IsActive = true
        };
    }

    public static Societe CreateSociete(int id = 1, int? subscriptionTypeId = 1)
    {
        return new Societe
        {
            Id = id,
            Name = "Test Company",
            Type = "transport",
            SubscriptionTypeId = subscriptionTypeId,
            Email = "company@test.com",
            Phone = "+212600000000",
            Settings = new SocieteSettings()
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

    public static User CreateDriver(int id = 1, int companyId = 1, string name = "Test Driver")
    {
        return new User
        {
            Id = id,
            Name = name,
            Email = "driver@test.com",
            Phone = "+212600000001",
            Roles = new[] { "driver" },
            UserType = "employee",
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
