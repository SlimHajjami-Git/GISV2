using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Auth.Commands.Login;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Auth.Commands.Register;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;

    public RegisterCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IJwtService jwtService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
    }

    public async Task<LoginResponse> Handle(RegisterCommand request, CancellationToken ct)
    {
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct);

        if (existingUser != null)
            throw new ConflictException("User with this email already exists");

        var defaultSubscription = await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.Type == "parc", ct);

        if (defaultSubscription == null)
        {
            defaultSubscription = new Subscription
            {
                Name = "Parc Basic",
                Type = "parc",
                Price = 0,
                MaxVehicles = 10,
                GpsTracking = false,
                GpsInstallation = false,
                Features = ["Vehicle Management", "Basic Reports"]
            };
            _context.Subscriptions.Add(defaultSubscription);
            await _context.SaveChangesAsync(ct);
        }

        var company = new Company
        {
            Name = request.CompanyName,
            Type = "transport",
            SubscriptionId = defaultSubscription.Id,
            Settings = new CompanySettings()
        };
        _context.Companies.Add(company);
        await _context.SaveChangesAsync(ct);

        var user = new User
        {
            Name = request.Name,
            Email = request.Email.ToLower(),
            Phone = request.Phone,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            CompanyId = company.Id,
            Roles = ["admin"],
            Permissions = ["dashboard", "monitoring", "vehicles", "employees", "gps-devices", 
                          "maintenance", "costs", "reports", "geofences", "settings", "users"],
            Status = "active"
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);

        var token = _jwtService.GenerateToken(user);
        var refreshToken = _jwtService.GenerateRefreshToken();

        return new LoginResponse(
            token,
            refreshToken,
            new UserDto(
                user.Id,
                user.Name,
                user.Email,
                user.Phone,
                user.Roles,
                user.Permissions,
                user.CompanyId,
                company.Name
            )
        );
    }
}
