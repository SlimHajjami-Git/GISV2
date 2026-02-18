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

// Tour Monitoring Service (auto-start, waypoint validation, auto-complete)
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

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "DefaultSecretKeyForDevelopment123!";
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

                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/gps"))
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

// Controllers with camelCase JSON serialization
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// CORS - Allow all for debugging
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
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
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPath),
    RequestPath = "/uploads"
});

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Permission middleware - checks system admin access for /api/admin routes
app.UsePermissionMiddleware();

// Multi-tenant middleware - sets tenant context from JWT claims
app.UseTenantMiddleware();

app.MapControllers();

// SignalR Hub endpoints
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
    
    await SeedBeliveCompany(context);
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
                    RoleId = adminRole.Id
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
                MaxVehicles = 100,
                MaxUsers = 20,
                MaxGpsDevices = 100,
                MaxGeofences = 50,
                IsActive = true
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
            RoleId = newAdminRole.Id
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
