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
        var existingCompany = await context.Societes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Name == "Belive");

        if (existingCompany != null)
        {
            Console.WriteLine($"Company 'Belive' already exists with Id: {existingCompany.Id}");
            return;
        }

        // Create subscription type
        var subscriptionType = await context.SubscriptionTypes.FirstOrDefaultAsync(s => s.Name == "Plan Pro");
        if (subscriptionType == null)
        {
            subscriptionType = new SubscriptionType
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
            Console.WriteLine($"Created subscription type: {subscriptionType.Name} (Id: {subscriptionType.Id})");
        }

        // Create Belive company
        var company = new Societe
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
        Console.WriteLine($"Created company: {company.Name} (Id: {company.Id})");

        // Create admin user (password: Calypso@2026+)
        var adminUser = new User
        {
            Name = "Admin Belive",
            Email = "admin@belive.ma",
            Phone = "+212 600 000000",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Calypso@2026+"),
            Roles = new[] { "super_admin" },
            Permissions = new[] { "all" },
            AssignedVehicleIds = Array.Empty<int>(),
            Status = "active",
            CompanyId = company.Id
        };
        context.Users.Add(adminUser);
        await context.SaveChangesAsync();
        Console.WriteLine($"Created admin user: {adminUser.Email} (Id: {adminUser.Id})");

        Console.WriteLine("\n✅ Belive company seeded successfully!");
        Console.WriteLine($"   Company ID: {company.Id}");
        Console.WriteLine($"   Admin login: admin@belive.ma / Calypso@2026+");
    }
}
