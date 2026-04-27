using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

/// <summary>
/// Calypso 6 (P9) — manual accident creation path. When the system did
/// not auto-detect an accident (no MEMS spike, device offline, vehicle
/// off the network, etc.) but the accident really happened, the admin
/// fills this form to record the event after the fact, optionally
/// attaching a PDF received from an insurance expert.
///
/// <para>Unlike the auto path:</para>
/// <list type="bullet">
///   <item><description>The row is created directly with
///     <c>status = "confirmed"</c> — the admin is by definition confirming
///     the accident happened, no modal step.</description></item>
///   <item><description>The PDF (if any) is stored via
///     <see cref="AttachAccidentPdfCommand"/> in a separate request after
///     creation — keeps the endpoint contract simple.</description></item>
///   <item><description>Confidence is set to 100 because this is a
///     human-entered fact, not a sensor inference.</description></item>
/// </list>
///
/// <para>Returns the new <c>AccidentEvent.Id</c> so the caller can attach
/// the PDF and redirect to the report page.</para>
/// </summary>
public record CreateManualAccidentCommand(
    int VehicleId,
    DateTime IncidentAt,
    double? Latitude,
    double? Longitude,
    string? LocationCommune,
    string? LocationGovernorate,
    string? Description,
    string? Severity,
    decimal? EstimatedCost,
    string? ClaimNumber,
    string? InternalNotes
) : IRequest<int>;

public class CreateManualAccidentCommandHandler : IRequestHandler<CreateManualAccidentCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<CreateManualAccidentCommandHandler> _logger;

    public CreateManualAccidentCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        ILogger<CreateManualAccidentCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    public async Task<int> Handle(CreateManualAccidentCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var currentUserId = _tenantService.UserId
            ?? throw new DomainException("Utilisateur non identifié");

        // Resolve the vehicle (tenant-scoped) and copy a few denormalised
        // fields so the report page renders nicely even if the vehicle is
        // later renamed or detached from its device.
        var vehicle = await _context.Vehicles
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);

        var label = !string.IsNullOrWhiteSpace(vehicle.Plate) ? vehicle.Plate
                  : !string.IsNullOrWhiteSpace(vehicle.Name)  ? vehicle.Name
                  : $"Véhicule #{vehicle.Id}";

        // Calypso 7 — manual creation pre-fills Phase 2 (initial damages)
        // from the form. Phases 3-6 stay empty — the admin fills them as
        // the real-world events unfold (expert visit, mechanic quote,
        // repair, insurance settlement).
        var severity = NormaliseSeverity(request.Severity);
        var ev = new AccidentEvent
        {
            CompanyId = companyId,
            Origin = "manual",
            VehicleId = vehicle.Id,
            GpsDeviceId = vehicle.GpsDeviceId,
            DeviceUid = vehicle.GpsDevice?.DeviceUid ?? string.Empty,
            IncidentAt = request.IncidentAt.ToUniversalTime(),
            Latitude = request.Latitude ?? 0,
            Longitude = request.Longitude ?? 0,
            ReferenceCode = $"MAN-{DateTime.UtcNow:yyyyMMddHHmmss}",
            VehicleLabel = label,
            LocationCommune = request.LocationCommune,
            LocationGovernorate = request.LocationGovernorate,
            Confidence = 100,
            // No story / reasons / indicators — manual entry is not
            // sensor-derived.
            StoryJson = null,
            ReasonsJson = null,
            IndicatorsJson = null,
            Status = "confirmed",
            DecidedByUserId = currentUserId,
            DecidedAt = DateTime.UtcNow,
            // Phase 2 — initial damages
            InitialDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            InitialSeverity = severity,
            // Optional: seed Phase 6 if the admin already has a claim number / cost
            ClaimNumber = string.IsNullOrWhiteSpace(request.ClaimNumber) ? null : request.ClaimNumber.Trim(),
            ExpertEstimatedAmount = request.EstimatedCost,
            AdditionalNotes = string.IsNullOrWhiteSpace(request.InternalNotes) ? null : request.InternalNotes.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        _context.AccidentEvents.Add(ev);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CreateManualAccidentCommand: created manual accident {AccidentId} for vehicle {VehicleId} (company {CompanyId}) by user {UserId}",
            ev.Id, vehicle.Id, companyId, currentUserId);

        return ev.Id;
    }

    private static string? NormaliseSeverity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lc = raw.Trim().ToLowerInvariant();
        return lc switch
        {
            "minor" or "moderate" or "severe" or "total" => lc,
            _ => null,
        };
    }
}
