using System.Security.Claims;
using GisAPI.Domain.Entities;

namespace GisAPI.Application.Common.Interfaces;

public interface IJwtService
{
    string GenerateToken(User user);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateExpiredToken(string token);
}



