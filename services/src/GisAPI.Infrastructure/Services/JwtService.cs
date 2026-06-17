using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace GisAPI.Infrastructure.Services;

public class JwtService : IJwtService
{
    private readonly IConfiguration _configuration;

    public JwtService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user, string? impersonatedBy = null)
    {
        var key = _configuration["Jwt:Key"] ?? "DefaultSecretKeyForDevelopment123!";
        var issuer = _configuration["Jwt:Issuer"] ?? "GisAPI";
        var audience = _configuration["Jwt:Audience"] ?? "GisAPI";
        var expiryMinutes = int.Parse(_configuration["Jwt:ExpiryMinutes"] ?? "60");

        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Name, user.FullName),
            new("companyId", user.CompanyId.ToString()),
            new("roleId", user.RoleId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        // Impersonation : trace l'admin à l'origine de la session "voir en tant que".
        // Le jeton reste au périmètre de l'utilisateur cible (companyId/rôle), seul ce
        // claim d'audit s'ajoute pour savoir qui impersonate.
        if (!string.IsNullOrWhiteSpace(impersonatedBy))
        {
            claims.Add(new Claim("impersonated_by", impersonatedBy));
        }

        // Add role name as claim
        if (user.Role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, user.Role.Name));
            
            // Add company_admin role if applicable
            if (user.Role.IsCompanyAdmin)
            {
                claims.Add(new Claim(ClaimTypes.Role, "company_admin"));
            }
            
            // Add system_admin role if applicable — bypasses tenant filters
            if (user.Role.IsSystemRole)
            {
                claims.Add(new Claim(ClaimTypes.Role, "system_admin"));
            }
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }

    public ClaimsPrincipal? ValidateExpiredToken(string token)
    {
        var key = _configuration["Jwt:Key"] ?? "DefaultSecretKeyForDevelopment123!";
        var issuer = _configuration["Jwt:Issuer"] ?? "GisAPI";
        var audience = _configuration["Jwt:Audience"] ?? "GisAPI";

        var tokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = false // Allow expired tokens for refresh
        };

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(token, tokenValidationParameters, out var securityToken);

            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                return null;

            return principal;
        }
        catch
        {
            return null;
        }
    }
}


