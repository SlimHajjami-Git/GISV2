using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Users.Commands.ChangeMyPassword;

public class ChangeMyPasswordCommandHandler : IRequestHandler<ChangeMyPasswordCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPasswordHasher _passwordHasher;

    public ChangeMyPasswordCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        IPasswordHasher passwordHasher)
    {
        _context = context;
        _tenantService = tenantService;
        _passwordHasher = passwordHasher;
    }

    public async Task<Unit> Handle(ChangeMyPasswordCommand request, CancellationToken ct)
    {
        var userId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            throw new DomainException("Le mot de passe actuel est obligatoire");
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
            throw new DomainException("Le nouveau mot de passe doit contenir au moins 6 caractères");
        if (request.CurrentPassword == request.NewPassword)
            throw new DomainException("Le nouveau mot de passe doit être différent de l'ancien");

        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", userId);

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            throw new DomainException("Mot de passe actuel incorrect");

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
