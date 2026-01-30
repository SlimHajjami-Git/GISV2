using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Societes.Queries.GetSocieteById;

namespace GisAPI.Application.Features.Societes.Commands.CreateSociete;

public record CreateSocieteCommand(
    string Name,
    string Type,
    string? Description,
    string? Address,
    string? City,
    string Country,
    string? Phone,
    string? Email,
    int? SubscriptionTypeId,
    // Admin user info
    string AdminName,
    string AdminEmail,
    string AdminPassword
) : ICommand<SocieteDetailDto>;



