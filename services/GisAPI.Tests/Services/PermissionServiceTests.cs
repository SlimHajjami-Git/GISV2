using GisAPI.Application.Services;
using GisAPI.Domain.Entities;
using Xunit;

namespace GisAPI.Tests.Services;

public class PermissionServiceTests
{
    private readonly PermissionService _permissionService;

    public PermissionServiceTests()
    {
        _permissionService = new PermissionService();
    }

    #region GetPermissionTemplate Tests

    [Fact]
    public void GetPermissionTemplate_ReturnsAllCategories()
    {
        // Act
        var template = _permissionService.GetPermissionTemplate();

        // Assert
        Assert.NotNull(template);
        Assert.Contains("dashboard", template.Keys);
        Assert.Contains("monitoring", template.Keys);
        Assert.Contains("vehicles", template.Keys);
        Assert.Contains("geofences", template.Keys);
        Assert.Contains("employees", template.Keys);
        Assert.Contains("maintenance", template.Keys);
        Assert.Contains("costs", template.Keys);
        Assert.Contains("reports", template.Keys);
        Assert.Contains("advanced_reports", template.Keys);
        Assert.Contains("settings", template.Keys);
        Assert.Contains("users", template.Keys);
        Assert.Contains("api_access", template.Keys);
    }

    [Fact]
    public void GetPermissionTemplate_IncludesMetadataForEachCategory()
    {
        // Act
        var template = _permissionService.GetPermissionTemplate();

        // Assert
        foreach (var (key, value) in template)
        {
            var category = value as Dictionary<string, object>;
            Assert.NotNull(category);
            Assert.Contains("_meta", category.Keys);
            Assert.Contains("subPermissions", category.Keys);

            var meta = category["_meta"] as Dictionary<string, object>;
            Assert.NotNull(meta);
            Assert.Contains("name", meta.Keys);
            Assert.Contains("icon", meta.Keys);
            Assert.Contains("isBase", meta.Keys);
            Assert.Contains("requiresFeature", meta.Keys);
        }
    }

    #endregion

    #region GetSubscriptionPermissions Tests

    [Fact]
    public void GetSubscriptionPermissions_BasicSubscription_ReturnsBasePermissionsOnly()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Basic",
            GpsTracking = false,
            AdvancedReports = false,
            ApiAccess = false,
            FuelAnalysis = false,
            DrivingBehavior = false,
            RealTimeAlerts = false,
            HistoryPlayback = false
        };

        // Act
        var permissions = _permissionService.GetSubscriptionPermissions(subscription);

        // Assert - Only vehicles and maintenance are base permissions
        Assert.Contains("vehicles", permissions.Keys);
        Assert.Contains("maintenance", permissions.Keys);

        // Feature-based permissions should NOT be present (require gps_tracking)
        Assert.DoesNotContain("dashboard", permissions.Keys);
        Assert.DoesNotContain("monitoring", permissions.Keys);
        Assert.DoesNotContain("geofences", permissions.Keys);
        Assert.DoesNotContain("employees", permissions.Keys);
        Assert.DoesNotContain("costs", permissions.Keys);
        Assert.DoesNotContain("reports", permissions.Keys);
        Assert.DoesNotContain("settings", permissions.Keys);
        Assert.DoesNotContain("users", permissions.Keys);
        Assert.DoesNotContain("advanced_reports", permissions.Keys);
        Assert.DoesNotContain("api_access", permissions.Keys);
    }

    [Fact]
    public void GetSubscriptionPermissions_PremiumSubscription_ReturnsAllPermissions()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 2,
            Name = "Premium",
            GpsTracking = true,
            AdvancedReports = true,
            ApiAccess = true,
            FuelAnalysis = true,
            DrivingBehavior = true,
            RealTimeAlerts = true,
            HistoryPlayback = true,
            MaxVehicles = 100,
            MaxUsers = 50,
            MaxGpsDevices = 100,
            MaxGeofences = 50,
            HistoryRetentionDays = 365
        };

        // Act
        var permissions = _permissionService.GetSubscriptionPermissions(subscription);

        // Assert - All categories should be present
        Assert.Contains("dashboard", permissions.Keys);
        Assert.Contains("monitoring", permissions.Keys);
        Assert.Contains("vehicles", permissions.Keys);
        Assert.Contains("geofences", permissions.Keys);
        Assert.Contains("employees", permissions.Keys);
        Assert.Contains("maintenance", permissions.Keys);
        Assert.Contains("costs", permissions.Keys);
        Assert.Contains("reports", permissions.Keys);
        Assert.Contains("advanced_reports", permissions.Keys);
        Assert.Contains("settings", permissions.Keys);
        Assert.Contains("users", permissions.Keys);
        Assert.Contains("api_access", permissions.Keys);

        // Check limits
        var limits = permissions["limits"] as Dictionary<string, object>;
        Assert.NotNull(limits);
        Assert.Equal(100, limits["max_vehicles"]);
        Assert.Equal(50, limits["max_users"]);
        Assert.Equal(365, limits["history_retention_days"]);
    }

    [Fact]
    public void GetSubscriptionPermissions_WithGpsTracking_IncludesMonitoring()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 3,
            Name = "GPS Basic",
            GpsTracking = true,
            RealTimeAlerts = true,
            HistoryPlayback = true,
            AdvancedReports = false,
            ApiAccess = false
        };

        // Act
        var permissions = _permissionService.GetSubscriptionPermissions(subscription);

        // Assert
        Assert.Contains("monitoring", permissions.Keys);
        
        var monitoring = permissions["monitoring"] as Dictionary<string, object>;
        Assert.NotNull(monitoring);
        Assert.True((bool)monitoring["real_time"]);
        Assert.True((bool)monitoring["history"]);
        Assert.True((bool)monitoring["alerts"]);
    }

    [Fact]
    public void GetSubscriptionPermissions_AdvancedReportsWithoutFuelAnalysis_FuelSubPermissionFalse()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 4,
            Name = "Reports Basic",
            AdvancedReports = true,
            FuelAnalysis = false,
            DrivingBehavior = false,
            GpsTracking = false
        };

        // Act
        var permissions = _permissionService.GetSubscriptionPermissions(subscription);

        // Assert
        Assert.Contains("advanced_reports", permissions.Keys);
        
        var advReports = permissions["advanced_reports"] as Dictionary<string, object>;
        Assert.NotNull(advReports);
        Assert.True((bool)advReports["trip"]);
        Assert.True((bool)advReports["speed"]);
        Assert.True((bool)advReports["stops"]);
        Assert.True((bool)advReports["distance"]);
        Assert.True((bool)advReports["cost"]);
        Assert.True((bool)advReports["maintenance"]);
        
        // These require additional features
        Assert.False((bool)advReports["fuel"]);
        Assert.False((bool)advReports["behavior"]);
    }

    #endregion

    #region ValidateRolePermissions Tests

    [Fact]
    public void ValidateRolePermissions_NullPermissions_ReturnsTrue()
    {
        // Arrange
        var subscription = new SubscriptionType { Id = 1, Name = "Basic" };

        // Act
        var result = _permissionService.ValidateRolePermissions(null, subscription);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateRolePermissions_BasePermissionsOnly_ReturnsTrue()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Basic",
            GpsTracking = false,
            AdvancedReports = false,
            ApiAccess = false
        };

        // Only vehicles and maintenance are base permissions
        var rolePermissions = new Dictionary<string, object>
        {
            ["vehicles"] = new Dictionary<string, object> { ["view"] = true, ["create"] = true },
            ["maintenance"] = new Dictionary<string, object> { ["view"] = true }
        };

        // Act
        var result = _permissionService.ValidateRolePermissions(rolePermissions, subscription);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateRolePermissions_FeaturePermissionExceedsSubscription_ReturnsFalse()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Basic",
            GpsTracking = false,
            AdvancedReports = false,
            ApiAccess = false
        };

        var rolePermissions = new Dictionary<string, object>
        {
            ["dashboard"] = new Dictionary<string, object> { ["view"] = true },
            ["monitoring"] = new Dictionary<string, object> { ["view"] = true, ["real_time"] = true }
        };

        // Act
        var result = _permissionService.ValidateRolePermissions(rolePermissions, subscription);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateRolePermissions_ApiAccessWhenNotInSubscription_ReturnsFalse()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Standard",
            GpsTracking = true,
            AdvancedReports = true,
            ApiAccess = false
        };

        var rolePermissions = new Dictionary<string, object>
        {
            ["api_access"] = new Dictionary<string, object> { ["enabled"] = true, ["read"] = true }
        };

        // Act
        var result = _permissionService.ValidateRolePermissions(rolePermissions, subscription);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetExceedingPermissions Tests

    [Fact]
    public void GetExceedingPermissions_NoExceeding_ReturnsEmptyList()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Premium",
            GpsTracking = true,
            AdvancedReports = true,
            ApiAccess = true,
            FuelAnalysis = true,
            DrivingBehavior = true
        };

        var rolePermissions = new Dictionary<string, object>
        {
            ["dashboard"] = new Dictionary<string, object> { ["view"] = true },
            ["monitoring"] = new Dictionary<string, object> { ["view"] = true },
            ["api_access"] = new Dictionary<string, object> { ["enabled"] = true }
        };

        // Act
        var exceeding = _permissionService.GetExceedingPermissions(rolePermissions, subscription);

        // Assert
        Assert.Empty(exceeding);
    }

    [Fact]
    public void GetExceedingPermissions_MonitoringWithoutGpsTracking_ReturnsMonitoring()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Basic",
            GpsTracking = false
        };

        var rolePermissions = new Dictionary<string, object>
        {
            ["monitoring"] = new Dictionary<string, object> { ["view"] = true }
        };

        // Act
        var exceeding = _permissionService.GetExceedingPermissions(rolePermissions, subscription);

        // Assert
        Assert.Contains("monitoring", exceeding);
    }

    [Fact]
    public void GetExceedingPermissions_FuelReportWithoutFuelAnalysis_ReturnsFuelSubPermission()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Standard",
            AdvancedReports = true,
            FuelAnalysis = false
        };

        var rolePermissions = new Dictionary<string, object>
        {
            ["advanced_reports"] = new Dictionary<string, object> 
            { 
                ["view"] = true,
                ["trip"] = true,
                ["fuel"] = true  // This exceeds subscription
            }
        };

        // Act
        var exceeding = _permissionService.GetExceedingPermissions(rolePermissions, subscription);

        // Assert
        Assert.Contains("advanced_reports.fuel", exceeding);
    }

    [Fact]
    public void GetExceedingPermissions_MultipleExceeding_ReturnsAllExceeding()
    {
        // Arrange
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Basic",
            GpsTracking = false,
            AdvancedReports = false,
            ApiAccess = false
        };

        var rolePermissions = new Dictionary<string, object>
        {
            ["monitoring"] = new Dictionary<string, object> { ["view"] = true },
            ["advanced_reports"] = new Dictionary<string, object> { ["view"] = true },
            ["api_access"] = new Dictionary<string, object> { ["enabled"] = true }
        };

        // Act
        var exceeding = _permissionService.GetExceedingPermissions(rolePermissions, subscription);

        // Assert
        Assert.Equal(3, exceeding.Count);
        Assert.Contains("monitoring", exceeding);
        Assert.Contains("advanced_reports", exceeding);
        Assert.Contains("api_access", exceeding);
    }

    [Fact]
    public void GetExceedingPermissions_LegacyFlatPermissions_StillValidated()
    {
        // Arrange - Legacy format uses flat boolean permissions
        var subscription = new SubscriptionType
        {
            Id = 1,
            Name = "Basic",
            ApiAccess = false
        };

        var rolePermissions = new Dictionary<string, object>
        {
            ["api_access"] = true  // Legacy flat format
        };

        // Act
        var exceeding = _permissionService.GetExceedingPermissions(rolePermissions, subscription);

        // Assert
        Assert.Contains("api_access", exceeding);
    }

    #endregion

    #region Permission Categories Tests

    [Fact]
    public void PermissionCategories_AdvancedReports_HasAllRequiredSubPermissions()
    {
        // Assert
        var advReports = PermissionService.PermissionCategories["advanced_reports"];
        
        Assert.Contains("view", advReports.SubPermissions);
        Assert.Contains("trip", advReports.SubPermissions);
        Assert.Contains("fuel", advReports.SubPermissions);
        Assert.Contains("speed", advReports.SubPermissions);
        Assert.Contains("stops", advReports.SubPermissions);
        Assert.Contains("distance", advReports.SubPermissions);
        Assert.Contains("cost", advReports.SubPermissions);
        Assert.Contains("maintenance", advReports.SubPermissions);
        Assert.Contains("behavior", advReports.SubPermissions);
    }

    [Fact]
    public void PermissionCategories_BaseCategories_HaveIsBaseTrue()
    {
        // Assert - Only vehicles and maintenance are base permissions
        Assert.True(PermissionService.PermissionCategories["vehicles"].IsBase);
        Assert.True(PermissionService.PermissionCategories["maintenance"].IsBase);
        
        // All others require gps_tracking or other features
        Assert.False(PermissionService.PermissionCategories["dashboard"].IsBase);
        Assert.False(PermissionService.PermissionCategories["geofences"].IsBase);
        Assert.False(PermissionService.PermissionCategories["employees"].IsBase);
        Assert.False(PermissionService.PermissionCategories["costs"].IsBase);
        Assert.False(PermissionService.PermissionCategories["reports"].IsBase);
        Assert.False(PermissionService.PermissionCategories["settings"].IsBase);
        Assert.False(PermissionService.PermissionCategories["users"].IsBase);
    }

    [Fact]
    public void PermissionCategories_FeatureCategories_HaveRequiresFeature()
    {
        // Assert
        Assert.Equal("gps_tracking", PermissionService.PermissionCategories["monitoring"].RequiresFeature);
        Assert.Equal("advanced_reports", PermissionService.PermissionCategories["advanced_reports"].RequiresFeature);
        Assert.Equal("api_access", PermissionService.PermissionCategories["api_access"].RequiresFeature);
    }

    #endregion
}
