using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using GisAPI.Application;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Infrastructure;
using GisAPI.Middleware;
using GisAPI.Hubs;
using GisAPI.Domain.Constants;

// Force Npgsql to return DateTime with Kind=Utc (fixes timezone serialization)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Add Application & Infrastructure layers (CQRS, MediatR, EF Core, Multi-tenant, RabbitMQ)
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// SignalR for real-time updates with camelCase JSON
builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddSingleton<IGpsHubService, GpsHubService>();
builder.Services.AddScoped<INotificationService, GisAPI.Services.NotificationService>();
builder.Services.AddSingleton<ILlmService, GisAPI.Services.GroqLlmService>();
builder.Services.AddSingleton<GisAPI.Services.IVehicleHealthScoreService, GisAPI.Services.VehicleHealthScoreService>();

// GPS Telemetry Consumer (RabbitMQ -> SignalR)
builder.Services.AddHostedService<GisAPI.Services.GpsTelemetryConsumer>();

// Redis PubSub Consumer (Redis -> SignalR) - LOWER LATENCY than RabbitMQ
builder.Services.AddHostedService<GisAPI.Services.RedisPubSubConsumer>();

// Tour Monitoring Service - auto-start, waypoint detection, auto-complete
builder.Services.AddHostedService<GisAPI.Services.TourMonitoringService>();

// Predictive Alert Service (document expiry, maintenance due, fuel anomaly)
builder.Services.AddHostedService<GisAPI.Services.PredictiveAlertService>();

// Geocoding Service with cache
builder.Services.AddSingleton<GisAPI.Domain.Interfaces.IGeocodingService, GisAPI.Services.GeocodingService>();

// Redis Cache Service for real-time positions
builder.Services.AddSingleton<GisAPI.Services.IRedisCacheService, GisAPI.Services.RedisCacheService>();

// Driving Behavior Detection Service
builder.Services.AddScoped<GisAPI.Services.IDrivingBehaviorService, GisAPI.Services.DrivingBehaviorService>();

// Maintenance Scheduler Service
builder.Services.AddScoped<GisAPI.Application.Services.IMaintenanceSchedulerService, GisAPI.Application.Services.MaintenanceSchedulerService>();

// Valhalla Road Snapping Service (replaces OSRM - better map-matching for GPS)
builder.Services.AddSingleton<GisAPI.Services.IValhallaService, GisAPI.Services.ValhallaService>();

// GPS Interpolation Service (smart interpolation based on speed and time)
builder.Services.AddSingleton<GisAPI.Services.IGpsInterpolationService, GisAPI.Services.GpsInterpolationService>();

// HttpClient for health checks (GPS ingest, RabbitMQ, Frontend)
builder.Services.AddHttpClient();

// JWT Authentication — require a real key in production
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Jwt:Key must be configured in production. Set it in appsettings or environment variables.");
    jwtKey = "DefaultSecretKeyForDevelopment123!";
}
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "GisAPI",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "GisAPI",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // Allow SignalR clients to pass JWT via query string (access_token)
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) && (path.StartsWithSegments("/api/hubs/gps") || path.StartsWithSegments("/hubs/gps")))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.All)
    {
        options.AddPolicy(permission, policy =>
            policy.RequireClaim("permission", permission));
    }

    options.AddPolicy(Permissions.Admin, policy =>
        policy.RequireClaim("permission", Permissions.Admin));
});

// Controllers with camelCase JSON serialization + UTC DateTime converter
builder.Services.AddControllers(options =>
    {
        // Ensure all DateTime query/route parameters are parsed as UTC
        options.ModelBinderProviders.Insert(0, new GisAPI.Middleware.UtcDateTimeModelBinderProvider());
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new GisAPI.Middleware.UtcDateTimeConverter());
    });

// CORS - Restrict origins in production, allow all in development
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:4200", "http://localhost:4201", "http://frontend:80",
               "http://localhost", "capacitor://localhost", "https://localhost" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
    });
});

// In-memory cache for dashboard and reports
builder.Services.AddMemoryCache();

// Response compression for JSON payloads
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = Microsoft.AspNetCore.ResponseCompression.ResponseCompressionDefaults.MimeTypes
        .Concat(new[] { "application/json" });
});

// OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Exception handling middleware
app.UseExceptionHandling();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Response compression
app.UseResponseCompression();

// app.UseHttpsRedirection(); // Disabled for Docker HTTP

// Static file serving for uploads (photos, documents)
var uploadsPath = Path.Combine(app.Environment.ContentRootPath, "uploads");
if (!Directory.Exists(uploadsPath)) Directory.CreateDirectory(uploadsPath);
var contentTypeProvider = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypeProvider.Mappings[".webp"] = "image/webp";
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads",
    ContentTypeProvider = contentTypeProvider,
    ServeUnknownFileTypes = true,
    DefaultContentType = "application/octet-stream"
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Subscription expiration middleware - blocks access for expired/suspended subscriptions
app.UseSubscriptionExpiration();

// Permission middleware - checks subscription module + user-level permissions
app.UsePermissionMiddleware();

// Multi-tenant middleware - sets tenant context from JWT claims
app.UseTenantMiddleware();

app.MapControllers();

// SignalR Hub endpoints
app.MapHub<GpsHub>("/api/hubs/gps");
app.MapHub<GpsHub>("/hubs/gps");

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Apply migrations and seed default company "Belive" for testing
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<GisAPI.Infrastructure.Persistence.GisDbContext>();
    
    // Auto-apply pending migrations
    Console.WriteLine("[Startup] Checking for pending migrations...");
    var pendingMigrations = await context.Database.GetPendingMigrationsAsync();
    if (pendingMigrations.Any())
    {
        Console.WriteLine($"[Startup] Applying {pendingMigrations.Count()} pending migrations...");
        await context.Database.MigrateAsync();
        Console.WriteLine("[Startup] Migrations applied successfully");
    }
    else
    {
        Console.WriteLine("[Startup] No pending migrations");
    }
    
    // Fix: ensure all subscription types have module_employees enabled (was defaulting to false in DB)
    var subTypesToFix = await context.SubscriptionTypes
        .Where(s => !s.ModuleEmployees)
        .ToListAsync();
    if (subTypesToFix.Any())
    {
        foreach (var st in subTypesToFix)
        {
            st.ModuleEmployees = true;
            Console.WriteLine($"[Startup] Fixed module_employees=true for subscription '{st.Name}'");
        }
        await context.SaveChangesAsync();
    }

    await SeedBeliveCompany(context);
    await SeedSubscriptionPlansAndTestCompany(context);
    await SeedFuelTypesAndPricing(context);
}

app.Run();

// Seed method for Belive company
static async Task SeedBeliveCompany(GisAPI.Infrastructure.Persistence.GisDbContext context)
{
    try
    {
        // Check if Belive already exists
        var existingCompany = await context.Societes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Name == "Belive");

        if (existingCompany != null)
        {
            Console.WriteLine($"[Seed] Company 'Belive' already exists (Id: {existingCompany.Id})");
            
            // Check if admin user exists, create if not
            var existingAdmin = await context.Users
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.Email == "admin@belive.tn");
            
            if (existingAdmin == null)
            {
                // Get or create admin role
                var adminRole = await context.Roles
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(r => r.SocieteId == existingCompany.Id && r.IsCompanyAdmin);
                
                if (adminRole == null)
                {
                    adminRole = new GisAPI.Domain.Entities.Role
                    {
                        Name = "Administrateur",
                        Description = "Administrateur système",
                        SocieteId = existingCompany.Id,
                        IsCompanyAdmin = true,
                        IsSystemRole = true
                    };
                    context.Roles.Add(adminRole);
                    await context.SaveChangesAsync();
                    Console.WriteLine($"[Seed] Created admin role (Id: {adminRole.Id})");
                }
                
                var newAdminUser = new GisAPI.Domain.Entities.User
                {
                    FirstName = "Admin",
                    LastName = "Belive",
                    Email = "admin@belive.tn",
                    Phone = "+216 00 000 000",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@2026"),
                    Status = "active",
                    CompanyId = existingCompany.Id,
                    RoleId = adminRole.Id,
                    AccessLevel = "admin",
                    CanMonitoring = true, CanVehicles = true, CanDrivers = true,
                    CanReports = true, CanGeofences = true, CanMaintenance = true,
                    CanCosts = true, CanDocuments = true, CanAccidents = true,
                    CanUsers = true, CanSettings = true, CanSuppliers = true,
                    CanFleetManagement = true
                };
                context.Users.Add(newAdminUser);
                await context.SaveChangesAsync();
                Console.WriteLine($"[Seed] Created admin user: {newAdminUser.Email} (Id: {newAdminUser.Id})");
            }
            else
            {
                Console.WriteLine($"[Seed] Admin user already exists (Id: {existingAdmin.Id})");
            }
            
            return;
        }

        // Create subscription type
        var subscriptionType = await context.SubscriptionTypes.FirstOrDefaultAsync(s => s.Name == "Plan Pro");
        if (subscriptionType == null)
        {
            subscriptionType = new GisAPI.Domain.Entities.SubscriptionType
            {
                Name = "Plan Pro",
                Code = "plan-pro",
                TargetCompanyType = "all",
                YearlyPrice = 999.00m,
                GpsTracking = true,
                GpsInstallation = true,
                RealTimeAlerts = true,
                HistoryPlayback = true,
                MaxVehicles = 100,
                MaxUsers = 20,
                MaxGpsDevices = 100,
                MaxGeofences = 50,
                HistoryRetentionDays = 90,
                IsActive = true,
                ModuleEmployees = true,
                ModuleDashboard = true,
                ModuleVehicles = true,
                ModuleMaintenance = true,
                ModuleCosts = true,
                ModuleReports = true,
                ModuleSettings = true,
                ModuleUsers = true,
                ModuleDocuments = true,
                ModuleMonitoring = true,
                ModuleGeofences = true,
                ModuleSuppliers = true,
                ModuleAccidents = true,
                ModuleFleetManagement = true,
                ReportTrips = true,
                ReportSpeed = true,
                ReportStops = true,
                ReportMileage = true,
                ReportCosts = true,
                ReportMaintenance = true,
                ReportDaily = true,
                ReportSpeedInfraction = true
            };
            context.SubscriptionTypes.Add(subscriptionType);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] Created subscription type: {subscriptionType.Name} (Id: {subscriptionType.Id})");
        }

        // Create Belive company
        var company = new GisAPI.Domain.Entities.Societe
        {
            Name = "Belive",
            Type = "transport",
            Address = "123 Avenue Mohammed V",
            City = "Casablanca",
            Country = "MA",
            Phone = "+212 522 123456",
            Email = "contact@belive.ma",
            SubscriptionTypeId = subscriptionType.Id,
            IsActive = true,
            SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
        };
        context.Societes.Add(company);
        await context.SaveChangesAsync();
        Console.WriteLine($"[Seed] Created company: {company.Name} (Id: {company.Id})");

        // Create admin role first
        var newAdminRole = new GisAPI.Domain.Entities.Role
        {
            Name = "Administrateur",
            Description = "Administrateur système avec tous les droits",
            SocieteId = company.Id,
            IsCompanyAdmin = true,
            IsSystemRole = true
        };
        context.Roles.Add(newAdminRole);
        await context.SaveChangesAsync();
        Console.WriteLine($"[Seed] Created admin role: {newAdminRole.Name} (Id: {newAdminRole.Id})");

        // Create admin user (password: Admin@2026)
        var newAdmin = new GisAPI.Domain.Entities.User
        {
            FirstName = "Admin",
            LastName = "Belive",
            Email = "admin@belive.tn",
            Phone = "+216 00 000 000",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@2026"),
            Status = "active",
            CompanyId = company.Id,
            RoleId = newAdminRole.Id,
            AccessLevel = "admin",
            CanMonitoring = true, CanVehicles = true, CanDrivers = true,
            CanReports = true, CanGeofences = true, CanMaintenance = true,
            CanCosts = true, CanDocuments = true, CanAccidents = true,
            CanUsers = true, CanSettings = true, CanSuppliers = true,
            CanFleetManagement = true
        };
        context.Users.Add(newAdmin);
        await context.SaveChangesAsync();
        Console.WriteLine($"[Seed] Created admin user: {newAdmin.Email} (Id: {newAdmin.Id})");

        Console.WriteLine("[Seed] ✅ Belive company seeded successfully!");
        
        // Seed test vehicle "Opel Can" with GPS device
        await SeedTestVehicle(context, company.Id);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] Warning: Could not seed Belive company: {ex.Message}");
    }
}

static async Task SeedTestVehicle(GisAPI.Infrastructure.Persistence.GisDbContext context, int companyId)
{
    try
    {
        // Check if GPS device already exists
        var existingDevice = await context.GpsDevices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(d => d.DeviceUid == "860141071579602");

        if (existingDevice != null)
        {
            Console.WriteLine($"[Seed] GPS device '860141071579602' already exists (Id: {existingDevice.Id})");
            return;
        }

        // Check if vehicle already exists
        var existingVehicle = await context.Vehicles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.Name == "Opel Can");

        if (existingVehicle != null)
        {
            Console.WriteLine($"[Seed] Vehicle 'Opel Can' already exists (Id: {existingVehicle.Id})");
            return;
        }

        // Create GPS device first
        var gpsDevice = new GisAPI.Domain.Entities.GpsDevice
        {
            DeviceUid = "860141071579602",
            Mat = "NR08G0664",
            Label = "GPS Opel Can",
            ProtocolType = "gps_type_1",
            Status = "active",
            CompanyId = companyId,
            Model = "GT06N",
            Brand = "Concox",
            SimOperator = "Maroc Telecom"
        };
        context.GpsDevices.Add(gpsDevice);
        await context.SaveChangesAsync();
        Console.WriteLine($"[Seed] Created GPS device: {gpsDevice.DeviceUid} (Id: {gpsDevice.Id})");

        // Create vehicle (Plate is vehicle's own plate, not GPS MAT)
        var vehicle = new GisAPI.Domain.Entities.Vehicle
        {
            Name = "Opel Can",
            Plate = "A-12345-MA",
            Brand = "Opel",
            Model = "Combo",
            Type = "utilitaire",
            Year = 2020,
            Color = "Blanc",
            Status = "available",
            HasGps = true,
            Mileage = 45000,
            CompanyId = companyId,
            GpsDeviceId = gpsDevice.Id
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();
        Console.WriteLine($"[Seed] Created vehicle: {vehicle.Name} - {vehicle.Plate} (Id: {vehicle.Id})");

        // Update GPS device to link back to vehicle
        gpsDevice.Status = "assigned";
        await context.SaveChangesAsync();

        Console.WriteLine("[Seed] ✅ Test vehicle 'Opel Can' with GPS seeded successfully!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] Warning: Could not seed test vehicle: {ex.Message}");
    }
}

// Seed fuel types with correct codes matching frontend + default pricing per company
static async Task SeedFuelTypesAndPricing(GisAPI.Infrastructure.Persistence.GisDbContext context)
{
    try
    {
        // Define canonical fuel types (code must match frontend vehicle-popup fuelTypes)
        var canonicalFuelTypes = new[]
        {
            new { Code = "diesel", Name = "Diesel", DefaultPrice = 2.075m },
            new { Code = "essence", Name = "Essence", DefaultPrice = 2.225m },
            new { Code = "sans_plomb", Name = "Essence Sans Plomb", DefaultPrice = 2.185m },
            new { Code = "gpl", Name = "GPL", DefaultPrice = 0.850m },
            new { Code = "gnv", Name = "GNV", DefaultPrice = 0.650m },
            new { Code = "electrique", Name = "Électrique", DefaultPrice = 0.0m },
            new { Code = "hybride", Name = "Hybride", DefaultPrice = 2.075m },
            new { Code = "hybride_rechargeable", Name = "Hybride Rechargeable", DefaultPrice = 2.075m }
        };

        // Legacy code mapping (old DB codes -> new frontend codes)
        var legacyCodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "gasoline", "essence" },
            { "unleaded", "sans_plomb" },
            { "lpg", "gpl" },
            { "cng", "gnv" },
            { "electric", "electrique" },
            { "hybrid", "hybride" },
            { "plugin_hybrid", "hybride_rechargeable" }
        };

        var existingTypes = await context.FuelTypes.ToListAsync();

        // Fix legacy codes on existing records
        foreach (var existing in existingTypes)
        {
            if (legacyCodeMap.TryGetValue(existing.Code, out var newCode))
            {
                var canonical = canonicalFuelTypes.First(c => c.Code == newCode);
                existing.Code = canonical.Code;
                existing.Name = canonical.Name;
                Console.WriteLine($"[Seed] Fixed fuel type code: '{existing.Code}' -> '{newCode}'");
            }
        }

        // Add any missing fuel types
        foreach (var ft in canonicalFuelTypes)
        {
            var exists = existingTypes.Any(e => 
                string.Equals(e.Code, ft.Code, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                context.FuelTypes.Add(new GisAPI.Domain.Entities.FuelType
                {
                    Code = ft.Code,
                    Name = ft.Name,
                    IsSystem = true
                });
                Console.WriteLine($"[Seed] Added fuel type: {ft.Code} ({ft.Name})");
            }
        }

        await context.SaveChangesAsync();

        // Reload fuel types to get IDs
        var allFuelTypes = await context.FuelTypes.ToListAsync();

        // Seed default pricing for every company that has no pricing yet
        var companies = await context.Societes.IgnoreQueryFilters().ToListAsync();
        foreach (var company in companies)
        {
            var existingPricing = await context.FuelPricings
                .IgnoreQueryFilters()
                .Where(fp => fp.CompanyId == company.Id)
                .ToListAsync();

            if (existingPricing.Any())
            {
                // Fix pricing that references old fuel type IDs (codes already fixed above)
                Console.WriteLine($"[Seed] Company '{company.Name}' already has {existingPricing.Count} fuel price(s)");
                
                // Add missing fuel type pricing for this company
                foreach (var ft in canonicalFuelTypes.Where(f => f.DefaultPrice > 0))
                {
                    var fuelType = allFuelTypes.FirstOrDefault(t => 
                        string.Equals(t.Code, ft.Code, StringComparison.OrdinalIgnoreCase));
                    if (fuelType == null) continue;

                    var hasPricing = existingPricing.Any(p => p.FuelTypeId == fuelType.Id);
                    if (!hasPricing)
                    {
                        context.FuelPricings.Add(new GisAPI.Domain.Entities.FuelPricing
                        {
                            FuelTypeId = fuelType.Id,
                            CompanyId = company.Id,
                            PricePerLiter = ft.DefaultPrice,
                            EffectiveFrom = DateTime.UtcNow,
                            IsActive = true
                        });
                        Console.WriteLine($"[Seed] Added default pricing for '{ft.Code}' ({ft.DefaultPrice} TND/L) to company '{company.Name}'");
                    }
                }
            }
            else
            {
                // No pricing at all — seed all defaults
                foreach (var ft in canonicalFuelTypes.Where(f => f.DefaultPrice > 0))
                {
                    var fuelType = allFuelTypes.FirstOrDefault(t => 
                        string.Equals(t.Code, ft.Code, StringComparison.OrdinalIgnoreCase));
                    if (fuelType == null) continue;

                    context.FuelPricings.Add(new GisAPI.Domain.Entities.FuelPricing
                    {
                        FuelTypeId = fuelType.Id,
                        CompanyId = company.Id,
                        PricePerLiter = ft.DefaultPrice,
                        EffectiveFrom = DateTime.UtcNow,
                        IsActive = true
                    });
                }
                Console.WriteLine($"[Seed] Seeded all default fuel pricing for company '{company.Name}'");
            }
        }

        await context.SaveChangesAsync();
        Console.WriteLine("[Seed] ✅ Fuel types and pricing seeded successfully!");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] Warning: Could not seed fuel types/pricing: {ex.Message}");
    }
}

// =============================================================================
// Seed: 3 Subscription Plans + Test Company + Admin + User + Access Verification
// =============================================================================
static async Task SeedSubscriptionPlansAndTestCompany(GisAPI.Infrastructure.Persistence.GisDbContext context)
{
    try
    {
        Console.WriteLine("\n[Seed] ========== SUBSCRIPTION PLANS & TEST COMPANY ==========");

        // ──────────────────────────────────────────────────────────
        // STEP 1: Create 3 Subscription Types (Basique, Standard, Premium)
        // ──────────────────────────────────────────────────────────
        var planBasique = await context.SubscriptionTypes.FirstOrDefaultAsync(s => s.Code == "plan-basique");
        if (planBasique == null)
        {
            planBasique = new GisAPI.Domain.Entities.SubscriptionType
            {
                Name = "Plan Basique",
                Code = "plan-basique",
                Description = "Gestion de parc sans GPS - Véhicules et maintenance uniquement",
                TargetCompanyType = "all",
                MonthlyPrice = 29.00m,
                QuarterlyPrice = 79.00m,
                YearlyPrice = 299.00m,
                MaxVehicles = 15,
                MaxUsers = 3,
                MaxGpsDevices = 0,
                MaxGeofences = 0,
                // Features OFF
                GpsTracking = false,
                GpsInstallation = false,
                ApiAccess = false,
                AdvancedReports = false,
                RealTimeAlerts = false,
                HistoryPlayback = false,
                FuelAnalysis = false,
                DrivingBehavior = false,
                HistoryRetentionDays = 0,
                SortOrder = 1,
                IsActive = true,
                // Modules: only base fleet management
                ModuleDashboard = true,
                ModuleMonitoring = false,
                ModuleVehicles = true,
                ModuleEmployees = true,
                ModuleGeofences = false,
                ModuleMaintenance = true,
                ModuleCosts = true,
                ModuleReports = false,
                ModuleSettings = true,
                ModuleUsers = true,
                ModuleSuppliers = true,
                ModuleDocuments = true,
                ModuleAccidents = true,
                ModuleFleetManagement = false,
                // Reports: minimal
                ReportTrips = false,
                ReportFuel = false,
                ReportSpeed = false,
                ReportStops = false,
                ReportMileage = true,
                ReportCosts = true,
                ReportMaintenance = true,
                ReportDaily = false,
                ReportMonthly = false,
                ReportMileagePeriod = false,
                ReportSpeedInfraction = false,
                ReportDrivingBehavior = false
            };
            context.SubscriptionTypes.Add(planBasique);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created subscription: {planBasique.Name} (Id: {planBasique.Id}) - No GPS, fleet management only");
        }
        else
        {
            Console.WriteLine($"[Seed] Plan Basique already exists (Id: {planBasique.Id})");
        }

        var planStandard = await context.SubscriptionTypes.FirstOrDefaultAsync(s => s.Code == "plan-standard");
        if (planStandard == null)
        {
            planStandard = new GisAPI.Domain.Entities.SubscriptionType
            {
                Name = "Plan Standard",
                Code = "plan-standard",
                Description = "Gestion de parc avec GPS - Suivi temps réel, rapports de base",
                TargetCompanyType = "all",
                MonthlyPrice = 79.00m,
                QuarterlyPrice = 219.00m,
                YearlyPrice = 799.00m,
                MaxVehicles = 50,
                MaxUsers = 10,
                MaxGpsDevices = 50,
                MaxGeofences = 20,
                // Features: GPS enabled, basic reports
                GpsTracking = true,
                GpsInstallation = false,
                ApiAccess = false,
                AdvancedReports = false,
                RealTimeAlerts = true,
                HistoryPlayback = true,
                FuelAnalysis = false,
                DrivingBehavior = false,
                HistoryRetentionDays = 90,
                SortOrder = 2,
                IsActive = true,
                // Modules: most enabled except advanced
                ModuleDashboard = true,
                ModuleMonitoring = true,
                ModuleVehicles = true,
                ModuleEmployees = true,
                ModuleGeofences = true,
                ModuleMaintenance = true,
                ModuleCosts = true,
                ModuleReports = true,
                ModuleSettings = true,
                ModuleUsers = true,
                ModuleSuppliers = true,
                ModuleDocuments = true,
                ModuleAccidents = true,
                ModuleFleetManagement = false,
                // Reports: base only (no fuel, monthly, driving behavior)
                ReportTrips = true,
                ReportFuel = false,
                ReportSpeed = true,
                ReportStops = true,
                ReportMileage = true,
                ReportCosts = true,
                ReportMaintenance = true,
                ReportDaily = true,
                ReportMonthly = false,
                ReportMileagePeriod = false,
                ReportSpeedInfraction = true,
                ReportDrivingBehavior = false
            };
            context.SubscriptionTypes.Add(planStandard);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created subscription: {planStandard.Name} (Id: {planStandard.Id}) - GPS + base reports");
        }
        else
        {
            Console.WriteLine($"[Seed] Plan Standard already exists (Id: {planStandard.Id})");
        }

        var planPremium = await context.SubscriptionTypes.FirstOrDefaultAsync(s => s.Code == "plan-premium");
        if (planPremium == null)
        {
            planPremium = new GisAPI.Domain.Entities.SubscriptionType
            {
                Name = "Plan Premium",
                Code = "plan-premium",
                Description = "Tout inclus - GPS, installation, API, rapports avancés, analyse carburant, comportement",
                TargetCompanyType = "all",
                MonthlyPrice = 199.00m,
                QuarterlyPrice = 549.00m,
                YearlyPrice = 1999.00m,
                MaxVehicles = 200,
                MaxUsers = 50,
                MaxGpsDevices = 200,
                MaxGeofences = 100,
                // ALL features enabled
                GpsTracking = true,
                GpsInstallation = true,
                ApiAccess = true,
                AdvancedReports = true,
                RealTimeAlerts = true,
                HistoryPlayback = true,
                FuelAnalysis = true,
                DrivingBehavior = true,
                HistoryRetentionDays = 365,
                SortOrder = 3,
                IsActive = true,
                // ALL modules enabled
                ModuleDashboard = true,
                ModuleMonitoring = true,
                ModuleVehicles = true,
                ModuleEmployees = true,
                ModuleGeofences = true,
                ModuleMaintenance = true,
                ModuleCosts = true,
                ModuleReports = true,
                ModuleSettings = true,
                ModuleUsers = true,
                ModuleSuppliers = true,
                ModuleDocuments = true,
                ModuleAccidents = true,
                ModuleFleetManagement = true,
                // ALL reports enabled
                ReportTrips = true,
                ReportFuel = true,
                ReportSpeed = true,
                ReportStops = true,
                ReportMileage = true,
                ReportCosts = true,
                ReportMaintenance = true,
                ReportDaily = true,
                ReportMonthly = true,
                ReportMileagePeriod = true,
                ReportSpeedInfraction = true,
                ReportDrivingBehavior = true
            };
            context.SubscriptionTypes.Add(planPremium);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created subscription: {planPremium.Name} (Id: {planPremium.Id}) - ALL features");
        }
        else
        {
            Console.WriteLine($"[Seed] Plan Premium already exists (Id: {planPremium.Id})");
        }

        // ──────────────────────────────────────────────────────────
        // STEP 2: Create Test Company "TransportTest" with Plan Standard
        // ──────────────────────────────────────────────────────────
        var testCompany = await context.Societes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Name == "TransportTest");

        if (testCompany != null)
        {
            Console.WriteLine($"[Seed] Company 'TransportTest' already exists (Id: {testCompany.Id})");
        }
        else
        {
            testCompany = new GisAPI.Domain.Entities.Societe
            {
                Name = "TransportTest",
                Type = "transport",
                Description = "Société de test avec abonnement Standard",
                Address = "45 Rue de la Liberté",
                City = "Tunis",
                Country = "TN",
                Phone = "+216 71 234 567",
                Email = "contact@transporttest.tn",
                TaxId = "TN123456789",
                SubscriptionTypeId = planStandard.Id,
                IsActive = true,
                SubscriptionStartedAt = DateTime.UtcNow,
                SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1),
                BillingCycle = "yearly",
                SubscriptionStatus = "active",
                NextPaymentAmount = planStandard.YearlyPrice,
                Settings = new GisAPI.Domain.Entities.SocieteSettings
                {
                    Currency = "DT",
                    Timezone = "Africa/Tunis",
                    Language = "fr"
                }
            };
            context.Societes.Add(testCompany);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created company: {testCompany.Name} (Id: {testCompany.Id}) -> Subscription: {planStandard.Name}");
        }

        // ──────────────────────────────────────────────────────────
        // STEP 3: Create Admin Role + Admin User for TransportTest
        // ──────────────────────────────────────────────────────────
        var adminRole = await context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.SocieteId == testCompany.Id && r.IsCompanyAdmin);

        if (adminRole == null)
        {
            adminRole = new GisAPI.Domain.Entities.Role
            {
                Name = "Administrateur",
                Description = "Administrateur de la société TransportTest",
                SocieteId = testCompany.Id,
                IsCompanyAdmin = true,
                IsSystemRole = false,
                Permissions = new Dictionary<string, object>
                {
                    { "dashboard", true }, { "monitoring", true }, { "vehicles", true },
                    { "employees", true }, { "maintenance", true }, { "costs", true },
                    { "reports", true }, { "geofences", true }, { "settings", true },
                    { "users", true }, { "suppliers", true }, { "documents", true },
                    { "accidents", true }, { "fleet_management", true }
                }
            };
            context.Roles.Add(adminRole);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created admin role: {adminRole.Name} (Id: {adminRole.Id}) for company {testCompany.Name}");
        }

        var adminUser = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "admin@transporttest.tn");

        if (adminUser == null)
        {
            adminUser = new GisAPI.Domain.Entities.User
            {
                FirstName = "Admin",
                LastName = "TransportTest",
                Email = "admin@transporttest.tn",
                Phone = "+216 71 234 567",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@2026"),
                Status = "active",
                CompanyId = testCompany.Id,
                RoleId = adminRole.Id,
                AccessLevel = "admin",
                CanMonitoring = true,
                CanVehicles = true,
                CanDrivers = true,
                CanReports = true,
                CanGeofences = true,
                CanMaintenance = true,
                CanCosts = true,
                CanDocuments = true,
                CanAccidents = true,
                CanUsers = true,
                CanSettings = true,
                CanSuppliers = true,
                CanFleetManagement = true
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created admin user: {adminUser.Email} (Id: {adminUser.Id}) - Password: Admin@2026");
        }
        else
        {
            Console.WriteLine($"[Seed] Admin user {adminUser.Email} already exists (Id: {adminUser.Id})");
        }

        // ──────────────────────────────────────────────────────────
        // STEP 4: Create Employee Role + Standard User (restricted permissions)
        // ──────────────────────────────────────────────────────────
        var employeeRole = await context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.SocieteId == testCompany.Id && r.Name == "Opérateur");

        if (employeeRole == null)
        {
            employeeRole = new GisAPI.Domain.Entities.Role
            {
                Name = "Opérateur",
                Description = "Utilisateur standard - monitoring et véhicules uniquement",
                SocieteId = testCompany.Id,
                IsCompanyAdmin = false,
                IsSystemRole = false,
                Permissions = new Dictionary<string, object>
                {
                    { "dashboard", true }, { "monitoring", true }, { "vehicles", true },
                    { "employees", false }, { "maintenance", false }, { "costs", false },
                    { "reports", false }, { "geofences", false }, { "settings", false },
                    { "users", false }, { "suppliers", false }, { "documents", false },
                    { "accidents", false }, { "fleet_management", false }
                }
            };
            context.Roles.Add(employeeRole);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created employee role: {employeeRole.Name} (Id: {employeeRole.Id}) for company {testCompany.Name}");
        }

        var standardUser = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == "operateur@transporttest.tn");

        if (standardUser == null)
        {
            standardUser = new GisAPI.Domain.Entities.User
            {
                FirstName = "Mohamed",
                LastName = "Ben Ali",
                Email = "operateur@transporttest.tn",
                Phone = "+216 71 345 678",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("User@2026"),
                Status = "active",
                CompanyId = testCompany.Id,
                RoleId = employeeRole.Id,
                AccessLevel = "user",
                CanMonitoring = true,
                CanVehicles = true,
                CanDrivers = false,
                CanReports = false,
                CanGeofences = false,
                CanMaintenance = false,
                CanCosts = false,
                CanDocuments = false,
                CanAccidents = false,
                CanUsers = false,
                CanSettings = false,
                CanSuppliers = false,
                CanFleetManagement = false
            };
            context.Users.Add(standardUser);
            await context.SaveChangesAsync();
            Console.WriteLine($"[Seed] ✅ Created standard user: {standardUser.Email} (Id: {standardUser.Id}) - Password: User@2026");
        }
        else
        {
            Console.WriteLine($"[Seed] Standard user {standardUser.Email} already exists (Id: {standardUser.Id})");
        }

        // ──────────────────────────────────────────────────────────
        // STEP 5: Verify Access Rights Conformity
        // ──────────────────────────────────────────────────────────
        Console.WriteLine("\n[Seed] ========== ACCESS RIGHTS VERIFICATION ==========");
        Console.WriteLine($"[Verify] Company: {testCompany.Name}");
        Console.WriteLine($"[Verify] Subscription: {planStandard.Name} (Code: {planStandard.Code})");
        Console.WriteLine($"[Verify] Limits: MaxVehicles={planStandard.MaxVehicles}, MaxUsers={planStandard.MaxUsers}, MaxGpsDevices={planStandard.MaxGpsDevices}, MaxGeofences={planStandard.MaxGeofences}");

        // Count current usage
        var currentUsers = await context.Users.IgnoreQueryFilters().CountAsync(u => u.CompanyId == testCompany.Id);
        var currentVehicles = await context.Vehicles.IgnoreQueryFilters().CountAsync(v => v.CompanyId == testCompany.Id);
        Console.WriteLine($"[Verify] Current usage: Users={currentUsers}/{planStandard.MaxUsers}, Vehicles={currentVehicles}/{planStandard.MaxVehicles}");

        var limitsOk = currentUsers <= planStandard.MaxUsers && currentVehicles <= planStandard.MaxVehicles;
        Console.WriteLine($"[Verify] Limits check: {(limitsOk ? "✅ OK" : "❌ EXCEEDED")}");

        // Verify subscription module features
        Console.WriteLine("\n[Verify] --- Subscription Module Access (Plan Standard) ---");
        var moduleChecks = new Dictionary<string, bool>
        {
            { "Dashboard", planStandard.ModuleDashboard },
            { "Monitoring", planStandard.ModuleMonitoring },
            { "Vehicles", planStandard.ModuleVehicles },
            { "Employees", planStandard.ModuleEmployees },
            { "Geofences", planStandard.ModuleGeofences },
            { "Maintenance", planStandard.ModuleMaintenance },
            { "Costs", planStandard.ModuleCosts },
            { "Reports", planStandard.ModuleReports },
            { "Settings", planStandard.ModuleSettings },
            { "Users", planStandard.ModuleUsers },
            { "Suppliers", planStandard.ModuleSuppliers },
            { "Documents", planStandard.ModuleDocuments },
            { "Accidents", planStandard.ModuleAccidents },
            { "Fleet Management", planStandard.ModuleFleetManagement }
        };

        foreach (var (module, enabled) in moduleChecks)
        {
            Console.WriteLine($"[Verify]   {module}: {(enabled ? "✅ Enabled" : "🚫 Disabled")}");
        }

        // Verify subscription features
        Console.WriteLine("\n[Verify] --- Subscription Features (Plan Standard) ---");
        var featureChecks = new Dictionary<string, bool>
        {
            { "GPS Tracking", planStandard.GpsTracking },
            { "GPS Installation", planStandard.GpsInstallation },
            { "API Access", planStandard.ApiAccess },
            { "Advanced Reports", planStandard.AdvancedReports },
            { "Real-Time Alerts", planStandard.RealTimeAlerts },
            { "History Playback", planStandard.HistoryPlayback },
            { "Fuel Analysis", planStandard.FuelAnalysis },
            { "Driving Behavior", planStandard.DrivingBehavior }
        };

        foreach (var (feature, enabled) in featureChecks)
        {
            Console.WriteLine($"[Verify]   {feature}: {(enabled ? "✅ Enabled" : "🚫 Disabled")}");
        }

        // Verify Admin permissions vs subscription
        Console.WriteLine("\n[Verify] --- Admin User Access (admin@transporttest.tn) ---");
        Console.WriteLine($"[Verify]   Role: {adminRole.Name} (IsCompanyAdmin: {adminRole.IsCompanyAdmin})");
        Console.WriteLine($"[Verify]   AccessLevel: admin -> Bypass user-level checks ✅");
        Console.WriteLine($"[Verify]   BUT still limited by subscription modules:");
        Console.WriteLine($"[Verify]     - Monitoring: subscription={planStandard.ModuleMonitoring} -> {(planStandard.ModuleMonitoring ? "✅ ACCESS" : "🚫 BLOCKED")}");
        Console.WriteLine($"[Verify]     - Fleet Management: subscription={planStandard.ModuleFleetManagement} -> {(planStandard.ModuleFleetManagement ? "✅ ACCESS" : "🚫 BLOCKED by subscription")}");
        Console.WriteLine($"[Verify]     - Fuel Analysis: subscription={planStandard.FuelAnalysis} -> {(planStandard.FuelAnalysis ? "✅ ACCESS" : "🚫 BLOCKED by subscription")}");

        // Verify Standard user permissions vs subscription
        Console.WriteLine("\n[Verify] --- Standard User Access (operateur@transporttest.tn) ---");
        Console.WriteLine($"[Verify]   Role: {employeeRole.Name} (IsCompanyAdmin: {employeeRole.IsCompanyAdmin})");
        Console.WriteLine($"[Verify]   AccessLevel: user -> Must check per-user permissions");

        var userModuleChecks = new Dictionary<string, (bool subscriptionAllows, bool userHas)>
        {
            { "Dashboard", (planStandard.ModuleDashboard, true) }, // always accessible
            { "Monitoring", (planStandard.ModuleMonitoring, standardUser.CanMonitoring) },
            { "Vehicles", (planStandard.ModuleVehicles, standardUser.CanVehicles) },
            { "Employees", (planStandard.ModuleEmployees, standardUser.CanDrivers) },
            { "Reports", (planStandard.ModuleReports, standardUser.CanReports) },
            { "Geofences", (planStandard.ModuleGeofences, standardUser.CanGeofences) },
            { "Maintenance", (planStandard.ModuleMaintenance, standardUser.CanMaintenance) },
            { "Costs", (planStandard.ModuleCosts, standardUser.CanCosts) },
            { "Settings", (planStandard.ModuleSettings, standardUser.CanSettings) },
            { "Users", (planStandard.ModuleUsers, standardUser.CanUsers) },
            { "Fleet Management", (planStandard.ModuleFleetManagement, standardUser.CanFleetManagement) }
        };

        var allConform = true;
        foreach (var (module, (subAllows, userHas)) in userModuleChecks)
        {
            var finalAccess = subAllows && userHas;
            var status = !subAllows ? "🚫 BLOCKED (subscription)" : (userHas ? "✅ ACCESS" : "🔒 DENIED (user perm)");
            var conform = !userHas || subAllows; // conform = user doesn't have perm OR subscription allows it
            if (!conform) allConform = false;
            Console.WriteLine($"[Verify]   {module}: sub={subAllows}, user={userHas} -> {status} {(conform ? "" : "⚠️ NON-CONFORM")}");
        }

        Console.WriteLine($"\n[Verify] ========== CONFORMITY RESULT: {(allConform ? "✅ ALL ACCESS RIGHTS CONFORM TO SUBSCRIPTION" : "❌ SOME RIGHTS EXCEED SUBSCRIPTION")} ==========");

        // Summary
        Console.WriteLine("\n[Seed] ========== SEED SUMMARY ==========");
        Console.WriteLine("[Seed] Subscription Plans:");
        Console.WriteLine($"[Seed]   1. {planBasique.Name} (Id:{planBasique.Id}) - {planBasique.YearlyPrice} DT/an - No GPS, {planBasique.MaxVehicles} vehicles");
        Console.WriteLine($"[Seed]   2. {planStandard.Name} (Id:{planStandard.Id}) - {planStandard.YearlyPrice} DT/an - GPS+Monitoring, {planStandard.MaxVehicles} vehicles");
        Console.WriteLine($"[Seed]   3. {planPremium.Name} (Id:{planPremium.Id}) - {planPremium.YearlyPrice} DT/an - ALL features, {planPremium.MaxVehicles} vehicles");
        Console.WriteLine($"[Seed] Company: {testCompany.Name} (Id:{testCompany.Id}) -> {planStandard.Name}");
        Console.WriteLine($"[Seed] Admin: admin@transporttest.tn / Admin@2026 (all permissions, limited by subscription)");
        Console.WriteLine($"[Seed] User:  operateur@transporttest.tn / User@2026 (monitoring + vehicles only)");
        Console.WriteLine("[Seed] ✅ Subscription plans & test company seeded successfully!\n");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Seed] Warning: Could not seed subscription plans & test company: {ex.Message}");
        if (ex.InnerException != null)
            Console.WriteLine($"[Seed]   Inner: {ex.InnerException.Message}");
    }
}
