using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Services;

public class PermissionService : IPermissionService
{
    /// <summary>
    /// Unified Permission Hierarchy Structure
    /// 
    /// CATEGORIES (Main Modules):
    /// ├── dashboard          - Tableau de bord
    /// ├── monitoring         - Surveillance/Monitoring (requires: gps_tracking)
    /// │   ├── real_time      - Suivi GPS temps réel
    /// │   ├── history        - Historique des trajets
    /// │   └── alerts         - Alertes temps réel
    /// ├── vehicles           - Véhicules
    /// │   ├── list           - Liste des véhicules
    /// │   ├── details        - Détails véhicule
    /// │   └── gps_devices    - Appareils GPS
    /// ├── geofences          - Géofences
    /// ├── employees          - Employés (Chauffeurs + Employés merged)
    /// │   ├── drivers        - Chauffeurs
    /// │   └── staff          - Personnel
    /// ├── maintenance        - Maintenance
    /// │   ├── schedule       - Planification
    /// │   ├── history        - Historique
    /// │   └── alerts         - Alertes maintenance
    /// ├── costs              - Coûts
    /// │   ├── fuel           - Carburant
    /// │   ├── maintenance    - Coûts maintenance
    /// │   └── other          - Autres coûts
    /// ├── reports            - Rapports (base)
    /// ├── advanced_reports   - Rapports avancés (requires: advanced_reports subscription)
    /// │   ├── trip           - Rapport trajet
    /// │   ├── fuel           - Rapport carburant (requires: fuel_analysis)
    /// │   ├── speed          - Rapport vitesse
    /// │   ├── stops          - Rapport arrêt
    /// │   ├── distance       - Rapport distance
    /// │   ├── cost           - Rapport coût
    /// │   ├── maintenance    - Rapport maintenance
    /// │   └── behavior       - Comportement de conduite (requires: driving_behavior)
    /// ├── settings           - Paramètres
    /// ├── users              - Utilisateurs
    /// └── api_access         - Accès API (requires: api_access subscription)
    /// </summary>

    // Permission categories with their subscription requirements
    // Only "vehicles" and "maintenance" are BASE permissions (always available)
    // All others require the "gps_tracking" feature or specific features
    public static readonly Dictionary<string, PermissionCategory> PermissionCategories = new()
    {
        ["dashboard"] = new PermissionCategory
        {
            Key = "dashboard",
            Name = "Tableau de bord",
            Icon = "dashboard",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view" }
        },
        ["monitoring"] = new PermissionCategory
        {
            Key = "monitoring",
            Name = "Monitoring",
            Icon = "gps_fixed",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view", "real_time", "history", "alerts" }
        },
        ["vehicles"] = new PermissionCategory
        {
            Key = "vehicles",
            Name = "Véhicules",
            Icon = "directions_car",
            IsBase = true,
            SubPermissions = new[] { "view", "create", "edit", "delete", "gps_devices" }
        },
        ["geofences"] = new PermissionCategory
        {
            Key = "geofences",
            Name = "Géofences",
            Icon = "fence",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view", "create", "edit", "delete" }
        },
        ["employees"] = new PermissionCategory
        {
            Key = "employees",
            Name = "Employés",
            Icon = "people",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view", "create", "edit", "delete", "drivers", "staff" }
        },
        ["maintenance"] = new PermissionCategory
        {
            Key = "maintenance",
            Name = "Maintenance",
            Icon = "build",
            IsBase = true,
            SubPermissions = new[] { "view", "create", "edit", "delete", "schedule", "history", "alerts" }
        },
        ["costs"] = new PermissionCategory
        {
            Key = "costs",
            Name = "Coûts",
            Icon = "attach_money",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view", "create", "edit", "delete", "fuel", "maintenance", "other" }
        },
        ["reports"] = new PermissionCategory
        {
            Key = "reports",
            Name = "Rapports",
            Icon = "assessment",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view", "export" }
        },
        ["advanced_reports"] = new PermissionCategory
        {
            Key = "advanced_reports",
            Name = "Rapports avancés",
            Icon = "analytics",
            RequiresFeature = "advanced_reports",
            SubPermissions = new[] { "view", "trip", "fuel", "speed", "stops", "distance", "cost", "maintenance", "behavior" }
        },
        ["settings"] = new PermissionCategory
        {
            Key = "settings",
            Name = "Paramètres",
            Icon = "settings",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view", "edit" }
        },
        ["users"] = new PermissionCategory
        {
            Key = "users",
            Name = "Utilisateurs",
            Icon = "manage_accounts",
            RequiresFeature = "gps_tracking",
            SubPermissions = new[] { "view", "create", "edit", "delete" }
        },
        ["api_access"] = new PermissionCategory
        {
            Key = "api_access",
            Name = "Accès API",
            Icon = "api",
            RequiresFeature = "api_access",
            SubPermissions = new[] { "enabled", "read", "write" }
        }
    };

    // Sub-permission requirements (some sub-permissions require specific subscription features)
    private static readonly Dictionary<string, string> SubPermissionFeatureRequirements = new()
    {
        ["advanced_reports.fuel"] = "fuel_analysis",
        ["advanced_reports.behavior"] = "driving_behavior",
        ["monitoring.real_time"] = "gps_tracking",
        ["monitoring.history"] = "history_playback",
        ["monitoring.alerts"] = "real_time_alerts"
    };

    // Feature checkers mapping to SubscriptionType properties
    private static readonly Dictionary<string, Func<SubscriptionType, bool>> FeatureCheckers = new()
    {
        { "gps_tracking", st => st.GpsTracking },
        { "gps_installation", st => st.GpsInstallation },
        { "api_access", st => st.ApiAccess },
        { "advanced_reports", st => st.AdvancedReports },
        { "real_time_alerts", st => st.RealTimeAlerts },
        { "history_playback", st => st.HistoryPlayback },
        { "fuel_analysis", st => st.FuelAnalysis },
        { "driving_behavior", st => st.DrivingBehavior }
    };

    public Dictionary<string, object> GetSubscriptionPermissions(SubscriptionType subscriptionType)
    {
        var permissions = new Dictionary<string, object>();

        foreach (var (key, category) in PermissionCategories)
        {
            // Check if this category is available based on subscription
            var isAvailable = category.IsBase || 
                (category.RequiresFeature != null && 
                 FeatureCheckers.TryGetValue(category.RequiresFeature, out var checker) && 
                 checker(subscriptionType));

            if (!isAvailable) continue;

            var categoryPerms = new Dictionary<string, object>();
            
            foreach (var subPerm in category.SubPermissions)
            {
                var fullKey = $"{key}.{subPerm}";
                
                // Check if sub-permission requires a specific feature
                if (SubPermissionFeatureRequirements.TryGetValue(fullKey, out var requiredFeature))
                {
                    if (FeatureCheckers.TryGetValue(requiredFeature, out var subChecker))
                    {
                        categoryPerms[subPerm] = subChecker(subscriptionType);
                    }
                    else
                    {
                        categoryPerms[subPerm] = false;
                    }
                }
                else
                {
                    categoryPerms[subPerm] = true;
                }
            }

            permissions[key] = categoryPerms;
        }

        // Add subscription feature flags for reference
        permissions["features"] = new Dictionary<string, object>
        {
            { "gps_tracking", subscriptionType.GpsTracking },
            { "gps_installation", subscriptionType.GpsInstallation },
            { "api_access", subscriptionType.ApiAccess },
            { "advanced_reports", subscriptionType.AdvancedReports },
            { "real_time_alerts", subscriptionType.RealTimeAlerts },
            { "history_playback", subscriptionType.HistoryPlayback },
            { "fuel_analysis", subscriptionType.FuelAnalysis },
            { "driving_behavior", subscriptionType.DrivingBehavior }
        };

        // Add limits as metadata
        permissions["limits"] = new Dictionary<string, object>
        {
            { "max_vehicles", subscriptionType.MaxVehicles },
            { "max_users", subscriptionType.MaxUsers },
            { "max_gps_devices", subscriptionType.MaxGpsDevices },
            { "max_geofences", subscriptionType.MaxGeofences },
            { "history_retention_days", subscriptionType.HistoryRetentionDays }
        };

        return permissions;
    }

    public Dictionary<string, object> GetPermissionTemplate()
    {
        // Returns full permission structure template for UI display
        var template = new Dictionary<string, object>();
        
        foreach (var (key, category) in PermissionCategories)
        {
            template[key] = new Dictionary<string, object>
            {
                ["_meta"] = new Dictionary<string, object>
                {
                    ["name"] = category.Name,
                    ["icon"] = category.Icon,
                    ["isBase"] = category.IsBase,
                    ["requiresFeature"] = category.RequiresFeature ?? ""
                },
                ["subPermissions"] = category.SubPermissions
            };
        }
        
        return template;
    }

    public bool ValidateRolePermissions(Dictionary<string, object>? rolePermissions, SubscriptionType subscriptionType)
    {
        if (rolePermissions == null) return true;
        
        var exceeding = GetExceedingPermissions(rolePermissions, subscriptionType);
        return exceeding.Count == 0;
    }

    public List<string> GetExceedingPermissions(Dictionary<string, object>? rolePermissions, SubscriptionType subscriptionType)
    {
        var exceeding = new List<string>();
        
        if (rolePermissions == null) return exceeding;

        foreach (var (key, value) in rolePermissions)
        {
            // Check category-level permissions
            if (PermissionCategories.TryGetValue(key, out var category))
            {
                if (!category.IsBase && category.RequiresFeature != null)
                {
                    if (FeatureCheckers.TryGetValue(category.RequiresFeature, out var checker))
                    {
                        if (!checker(subscriptionType) && HasAnyEnabled(value))
                        {
                            exceeding.Add(key);
                        }
                    }
                }

                // Check sub-permissions if value is a dictionary
                if (value is Dictionary<string, object> subPerms)
                {
                    foreach (var (subKey, subValue) in subPerms)
                    {
                        var fullKey = $"{key}.{subKey}";
                        if (SubPermissionFeatureRequirements.TryGetValue(fullKey, out var requiredFeature))
                        {
                            if (FeatureCheckers.TryGetValue(requiredFeature, out var subChecker))
                            {
                                if (!subChecker(subscriptionType) && IsEnabled(subValue))
                                {
                                    exceeding.Add(fullKey);
                                }
                            }
                        }
                    }
                }
            }
            // Legacy flat permission check
            else if (FeatureCheckers.TryGetValue(key, out var legacyChecker))
            {
                if (!legacyChecker(subscriptionType) && IsEnabled(value))
                {
                    exceeding.Add(key);
                }
            }
        }

        return exceeding;
    }

    private static bool HasAnyEnabled(object value)
    {
        if (value is bool b) return b;
        if (value is Dictionary<string, object> dict)
        {
            return dict.Values.Any(v => IsEnabled(v));
        }
        return false;
    }

    private static bool IsEnabled(object value)
    {
        if (value is bool b) return b;
        if (value is string s) return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
    }
}

public class PermissionCategory
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool IsBase { get; set; } = false;
    public string? RequiresFeature { get; set; }
    public string[] SubPermissions { get; set; } = Array.Empty<string>();
}



