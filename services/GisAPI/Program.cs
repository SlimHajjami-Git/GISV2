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

// GPS Telemetry Consumer (RabbitMQ -> SignalR)
builder.Services.AddHostedService<GisAPI.Services.GpsTelemetryConsumer>();

// Geocoding Service with cache
builder.Services.AddSingleton<GisAPI.Domain.Interfaces.IGeocodingService, GisAPI.Services.GeocodingService>();

// Driving Behavior Detection Service
builder.Services.AddScoped<GisAPI.Services.IDrivingBehaviorService, GisAPI.Services.DrivingBehaviorService>();

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

// Controllers
builder.Services.AddControllers();

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

// app.UseHttpsRedirection(); // Disabled for Docker HTTP
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

        // Create admin user (password: Admin123!)
        var adminUser = new GisAPI.Domain.Entities.User
        {
            Name = "Admin Belive",
            Email = "admin@belive.ma",
            Phone = "+212 600 000000",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin123!"),
            Roles = new[] { "admin" },
            Permissions = new[] { "all" },
            Status = "active",
            CompanyId = company.Id
        };
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
        Console.WriteLine($"[Seed] Created admin user: {adminUser.Email} (Id: {adminUser.Id})");

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



