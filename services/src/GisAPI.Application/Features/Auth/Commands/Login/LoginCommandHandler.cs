using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResponse>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;

    public LoginCommandHandler(
        IGisDbContext context,
        IPasswordHasher passwordHasher,
        IJwtService jwtService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
    }

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .Include(u => u.Societe)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower(), ct);

        if (user == null)
            throw new NotFoundException("User", request.Email);

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new DomainException("Invalid credentials");

        user.LastLoginAt = DateTime.UtcNow;
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
                user.Societe?.Name ?? ""
            )
        );
    }
}
