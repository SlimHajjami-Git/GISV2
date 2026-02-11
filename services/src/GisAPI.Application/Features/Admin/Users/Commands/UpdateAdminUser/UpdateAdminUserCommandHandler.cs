using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Admin.Users.Commands.UpdateAdminUser;

public class UpdateAdminUserCommandHandler : IRequestHandler<UpdateAdminUserCommand, AdminUserDto>
{
    private readonly IGisDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public UpdateAdminUserCommandHandler(IGisDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<AdminUserDto> Handle(UpdateAdminUserCommand request, CancellationToken ct)
    {
        var user = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Societe)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == request.Id, ct)
            ?? throw new NotFoundException("Utilisateur", request.Id);

        if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
        {
            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == request.Email && u.Id != request.Id, ct))
                throw new ConflictException("Un utilisateur avec cet email existe déjà");
            user.Email = request.Email;
        }

        if (!string.IsNullOrEmpty(request.Name)) user.Name = request.Name;
        if (request.Phone != null) user.Phone = request.Phone;
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = _passwordHasher.HashPassword(request.Password);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        var onlineThreshold = DateTime.UtcNow.AddMinutes(-15);
        return new AdminUserDto
        {
            Id = user.Id,
            Name = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            CompanyId = user.CompanyId,
            CompanyName = user.Societe?.Name,
            RoleId = user.RoleId,
            RoleName = user.Role?.Name,
            Status = user.Status,
            LastLoginAt = user.LastLoginAt,
            CreatedAt = user.CreatedAt,
            IsOnline = user.LastLoginAt.HasValue && user.LastLoginAt.Value > onlineThreshold
        };
    }
}
