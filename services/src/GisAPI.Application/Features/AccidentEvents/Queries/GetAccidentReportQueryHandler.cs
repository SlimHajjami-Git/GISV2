using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AccidentEvents.Queries;

public class GetAccidentReportQueryHandler
    : IRequestHandler<GetAccidentReportQuery, AccidentReportDto?>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetAccidentReportQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<AccidentReportDto?> Handle(GetAccidentReportQuery request, CancellationToken ct)
    {
        // Scope by the caller's company even for system admins (admin@belive.tn):
        // the global query filter is bypassed for system roles, so without this
        // explicit check a system admin could open another company's accident
        // report by guessing its id.
        var companyId = _tenantService.CompanyId ?? 0;

        // Fuite constatée : le filtre société ne suffisait pas — un employé restreint à
        // quelques véhicules pouvait ouvrir le rapport (position GPS, plaque, expertise)
        // de n'importe quel accident du parc en devinant son identifiant. On limite donc
        // aux véhicules qui lui sont affectés ; un administrateur (scope == null) voit tout.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);

        var query = _context.AccidentEvents
            .AsNoTracking()
            .Include(e => e.Documents)
            .Include(e => e.ThirdParties)
            .Where(e => e.Id == request.AccidentId && e.CompanyId == companyId);

        if (scope is not null)
            query = query.Where(e => e.VehicleId != null && scope.Contains(e.VehicleId.Value));

        var ev = await query.FirstOrDefaultAsync(ct);

        if (ev == null) return null;

        string? decidedByName = null;
        if (ev.DecidedByUserId.HasValue)
        {
            decidedByName = await _context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.Id == ev.DecidedByUserId.Value)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct);
        }

        return new AccidentReportDto(
            Id: ev.Id,
            CompanyId: ev.CompanyId,
            Origin: ev.Origin,
            VehicleId: ev.VehicleId,
            GpsDeviceId: ev.GpsDeviceId,
            DriverId: ev.DriverId,
            DeviceUid: ev.DeviceUid,
            IncidentAt: ev.IncidentAt,
            Latitude: ev.Latitude,
            Longitude: ev.Longitude,
            ReferenceCode: ev.ReferenceCode,
            VehicleLabel: ev.VehicleLabel,
            LocationCommune: ev.LocationCommune,
            LocationGovernorate: ev.LocationGovernorate,
            LocationRoadType: ev.LocationRoadType,

            SynthesisText: ev.SynthesisText,
            Confidence: ev.Confidence,
            Story: Deserialize<List<AccidentReportStoryEventDto>>(ev.StoryJson),
            Reasons: Deserialize<List<AccidentReportReasonDto>>(ev.ReasonsJson),
            Indicators: Deserialize<List<AccidentReportIndicatorDto>>(ev.IndicatorsJson),
            WeatherConditions: ev.WeatherConditions,
            RoadConditions: ev.RoadConditions,
            PoliceReportNumber: ev.PoliceReportNumber,
            MileageAtAccident: ev.MileageAtAccident,

            Status: ev.Status,
            DecidedByUserId: ev.DecidedByUserId,
            DecidedByName: decidedByName,
            DecidedAt: ev.DecidedAt,
            InitialDescription: ev.InitialDescription,
            InitialSeverity: ev.InitialSeverity,
            DamagedZones: Deserialize<List<string>>(ev.DamagedZonesJson),

            ExpertVisitedAt: ev.ExpertVisitedAt,
            ExpertName: ev.ExpertName,
            ExpertCompany: ev.ExpertCompany,
            ExpertAssessment: ev.ExpertAssessment,
            ExpertEstimatedAmount: ev.ExpertEstimatedAmount,

            MechanicQuoteAt: ev.MechanicQuoteAt,
            MechanicName: ev.MechanicName,
            MechanicQuotedAmount: ev.MechanicQuotedAmount,

            RepairStartedAt: ev.RepairStartedAt,
            RepairCompletedAt: ev.RepairCompletedAt,
            ActualRepairCost: ev.ActualRepairCost,
            TowDetectedAt: ev.TowDetectedAt,

            ClaimNumber: ev.ClaimNumber,
            ClaimSubmittedAt: ev.ClaimSubmittedAt,
            ClaimApprovedAmount: ev.ClaimApprovedAmount,
            ClaimStatus: ev.ClaimStatus,
            ThirdPartyInvolved: ev.ThirdPartyInvolved,

            Witnesses: ev.Witnesses,
            AdditionalNotes: ev.AdditionalNotes,
            PdfReportUrl: ev.PdfReportUrl,

            Documents: ev.Documents
                .OrderBy(d => d.UploadedAt)
                .Select(d => new AccidentReportDocumentDto(d.Id, d.DocumentType, d.FileName, d.FileUrl, d.FileSize, d.MimeType, d.UploadedAt))
                .ToList(),

            ThirdParties: ev.ThirdParties
                .OrderBy(t => t.Id)
                .Select(t => new AccidentReportThirdPartyDto(t.Id, t.Name, t.Phone, t.VehiclePlate, t.VehicleModel, t.InsuranceCompany, t.InsuranceNumber, t.InsuranceExpiry))
                .ToList());
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<T>(json, JsonOpts); }
        catch { return null; }
    }
}
