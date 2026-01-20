using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Services;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Messaging;
using GisAPI.Infrastructure.MultiTenancy;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GisAPI.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Multi-tenancy - scoped per request
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();

        // Database - Configure Npgsql data source with dynamic JSON support
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.EnableDynamicJson(); // Required for Dictionary<string, object> with jsonb
        var dataSource = dataSourceBuilder.Build();

        services.AddDbContext<GisDbContext>((sp, options) =>
        {
            options.UseNpgsql(
                dataSource,
                npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(GisDbContext).Assembly.FullName);
                    npgsqlOptions.EnableRetryOnFailure(3);
                });
            
            // Suppress PendingModelChangesWarning to allow migrations to run
            options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        });

        services.AddScoped<IGisDbContext>(provider => provider.GetRequiredService<GisDbContext>());

        // Services
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        // RabbitMQ Messaging
        var rabbitSection = configuration.GetSection(RabbitMqSettings.SectionName);
        services.Configure<RabbitMqSettings>(options =>
        {
            options.HostName = rabbitSection["HostName"] ?? rabbitSection["Host"] ?? "localhost";
            options.Port = int.Parse(rabbitSection["Port"] ?? "5672");
            options.UserName = rabbitSection["UserName"] ?? rabbitSection["Username"] ?? "guest";
            options.Password = rabbitSection["Password"] ?? "guest";
            options.VirtualHost = rabbitSection["VirtualHost"] ?? "/";
            options.GpsExchange = rabbitSection["GpsExchange"] ?? "gis.gps";
            options.AlertsExchange = rabbitSection["AlertsExchange"] ?? "gis.alerts";
            options.EventsExchange = rabbitSection["EventsExchange"] ?? "gis.events";
            options.GpsPositionsQueue = rabbitSection["GpsPositionsQueue"] ?? "gis.gps.positions";
            options.AlertsQueue = rabbitSection["AlertsQueue"] ?? "gis.alerts.notifications";
            options.EventsQueue = rabbitSection["EventsQueue"] ?? "gis.events.all";
        });
        services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
        services.AddHostedService<RabbitMqConsumerService>();

        return services;
    }
}
