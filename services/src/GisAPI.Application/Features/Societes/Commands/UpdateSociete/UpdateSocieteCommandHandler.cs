using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Societes.Queries.GetSocieteById;
using GisAPI.Domain.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Societes.Commands.UpdateSociete;

public class UpdateSocieteCommandHandler : IRequestHandler<UpdateSocieteCommand, SocieteDetailDto>
{
    private readonly IGisDbContext _context;

    public UpdateSocieteCommandHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<SocieteDetailDto> Handle(UpdateSocieteCommand request, CancellationToken ct)
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

        // Update fields if provided
        if (request.Name != null) societe.Name = request.Name;
        if (request.Type != null) societe.Type = request.Type;
        if (request.Description != null) societe.Description = request.Description;
        if (request.Address != null) societe.Address = request.Address;
        if (request.City != null) societe.City = request.City;
        if (request.Country != null) societe.Country = request.Country;
        if (request.Phone != null) societe.Phone = request.Phone;
        if (request.Email != null) societe.Email = request.Email;
        if (request.LogoUrl != null) societe.LogoUrl = request.LogoUrl;
        if (request.TaxId != null) societe.TaxId = request.TaxId;
        if (request.RC != null) societe.RC = request.RC;
        if (request.IF != null) societe.IF = request.IF;
        if (request.IsActive.HasValue) societe.IsActive = request.IsActive.Value;
        if (request.SubscriptionStatus != null) societe.SubscriptionStatus = request.SubscriptionStatus;
        if (request.BillingCycle != null) societe.BillingCycle = request.BillingCycle;
        if (request.SubscriptionTypeId.HasValue) societe.SubscriptionTypeId = request.SubscriptionTypeId;
        if (request.Settings != null) societe.Settings = request.Settings;

        societe.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

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



