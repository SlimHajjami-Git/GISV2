using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.ApiKeys;

/// <summary>
/// Qui gère les clés d'automatisation d'une société (liste, révocation) : son administrateur actif.
/// L'interrupteur d'accès API de Belive n'est PAS exigé ici : une société dont l'accès a été coupé
/// doit pouvoir révoquer ses clés (qui ne s'échangent de toute façon plus).
/// </summary>
internal static class ApiKeyAdmin
{
    public static async Task<(User User, int CompanyId)> RequireAsync(
        IGisDbContext context, ICurrentTenantService tenant, CancellationToken ct)
    {
        var userId = tenant.UserId ?? throw new DomainException("Utilisateur non identifié");
        var companyId = tenant.CompanyId ?? throw new DomainException("Société non identifiée");

        var user = await context.Users.IgnoreQueryFilters().AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId && u.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Utilisateur", userId);

        var refusal = ApiTokenAccess.AccountRefusal(user, companyApiAccessEnabled: true);
        if (refusal != null)
            throw new ForbiddenAccessException(refusal);

        return (user, companyId);
    }
}
