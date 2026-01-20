using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Societes.Queries.GetSocieteById;

public class GetSocieteByIdQueryHandler : IRequestHandler<GetSocieteByIdQuery, SocieteDetailDto>
{
    private readonly IGisDbContext _context;

    public GetSocieteByIdQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SocieteDetailDto> Handle(GetSocieteByIdQuery request, CancellationToken ct)
    {
        var societe = await _context.Societes
            .Include(s => s.SubscriptionType)
            .Include(s => s.Users)
            .Include(s => s.Vehicles)
            .Include(s => s.GpsDevices)
            .Include(s => s.Geofences)
            .Include(s => s.Roles)
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct)
            ?? throw new NotFoundException("Societe", request.Id);

        return new SocieteDetailDto(
            societe.Id,
            societe.Name,
            societe.Type,
            societe.Description,
            societe.Address,
            societe.City,
            societe.Country,
            societe.Phone,
            societe.Email,
            societe.LogoUrl,
            societe.TaxId,
            societe.RC,
            societe.IF,
            societe.IsActive,
            societe.SubscriptionStatus,
            societe.BillingCycle,
            societe.SubscriptionStartedAt,
            societe.SubscriptionExpiresAt,
            societe.LastPaymentAt,
            societe.NextPaymentAmount,
            societe.SubscriptionTypeId,
            societe.SubscriptionType?.Name,
            societe.Settings != null ? new SocieteSettingsDto(
                societe.Settings.Currency,
                societe.Settings.Timezone,
                societe.Settings.Language,
                societe.Settings.DateFormat,
                societe.Settings.DistanceUnit,
                societe.Settings.SpeedUnit,
                societe.Settings.VolumeUnit
            ) : null,
            societe.Users.Count,
            societe.Vehicles.Count,
            societe.GpsDevices.Count,
            societe.Geofences.Count,
            societe.Roles.Count,
            societe.CreatedAt,
            societe.UpdatedAt
        );
    }
}
