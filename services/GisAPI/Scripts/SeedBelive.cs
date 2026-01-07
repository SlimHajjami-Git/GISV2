// Run this with: dotnet run --project GisAPI.csproj -- --seed-belive
// Or execute the SQL directly in your database client

using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Scripts;

public static class SeedBelive
{
    public static async Task ExecuteAsync(GisDbContext context)
    {
        // Check if Belive already exists
        var existingCompany = await context.Companies
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Name == "Belive");

        if (existingCompany != null)
        {
            Console.WriteLine($"Company 'Belive' already exists with Id: {existingCompany.Id}");
            return;
        }

        // Create subscription
        var subscription = await context.Subscriptions.FirstOrDefaultAsync(s => s.Name == "Plan Pro");
        if (subscription == null)
        {
            subscription = new Subscription
            {
                Name = "Plan Pro",
                Type = "parc_gps",
                Price = 999.00m,
                Features = new[] { "gps_tracking", "geofencing", "reports", "alerts", "maintenance" },
                GpsTracking = true,
                GpsInstallation = true,
                MaxVehicles = 100,
                MaxUsers = 20,
                MaxGpsDevices = 100,
                MaxGeofences = 50,
                BillingCycle = "monthly",
                IsActive = true
            };
            context.Subscriptions.Add(subscription);
            await context.SaveChangesAsync();
            Console.WriteLine($"Created subscription: {subscription.Name} (Id: {subscription.Id})");
        }

        // Create Belive company
        var company = new Company
        {
            Name = "Belive",
            Type = "transport",
            Address = "123 Avenue Mohammed V",
            City = "Casablanca",
            Country = "MA",
            Phone = "+212 522 123456",
            Email = "contact@belive.ma",
            SubscriptionId = subscription.Id,
            IsActive = true,
            SubscriptionExpiresAt = DateTime.UtcNow.AddYears(1)
        };
        context.Companies.Add(company);
        await context.SaveChangesAsync();
        Console.WriteLine($"Created company: {company.Name} (Id: {company.Id})");

        // Create admin user (password: Admin123!)
        var adminUser = new User
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
        Console.WriteLine($"Created admin user: {adminUser.Email} (Id: {adminUser.Id})");

        Console.WriteLine("\n✅ Belive company seeded successfully!");
        Console.WriteLine($"   Company ID: {company.Id}");
        Console.WriteLine($"   Admin login: admin@belive.ma / Admin123!");
    }
}
