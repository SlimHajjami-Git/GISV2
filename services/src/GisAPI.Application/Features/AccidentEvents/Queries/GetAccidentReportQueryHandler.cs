using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
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

    public GetAccidentReportQueryHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<AccidentReportDto?> Handle(
        GetAccidentReportQuery request,
        CancellationToken cancellationToken)
    {
        var ev = await _context.AccidentEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.AccidentId, cancellationToken);

        if (ev == null) return null;

        // Resolve the decider's display name for the "Confirmé par X" /
        // "Fausse alerte par X" banner on the report page. IgnoreQueryFilters
        // because the decider may belong to a different tenant scope in
        // edge cases (system admin, etc.).
        string? decidedByName = null;
        if (ev.DecidedByUserId.HasValue)
        {
            decidedByName = await _context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => u.Id == ev.DecidedByUserId.Value)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new AccidentReportDto(
            ev.Id,
            ev.CompanyId,
            ev.VehicleId,
            ev.GpsDeviceId,
            ev.DeviceUid,
            ev.IncidentAt,
            ev.Latitude,
            ev.Longitude,
            ev.ReferenceCode,
            ev.VehicleLabel,
            ev.LocationCommune,
            ev.LocationGovernorate,
            ev.LocationRoadType,
            ev.SynthesisText,
            ev.Confidence,
            Deserialize<List<AccidentReportStoryEventDto>>(ev.StoryJson),
            Deserialize<List<AccidentReportReasonDto>>(ev.ReasonsJson),
            Deserialize<List<AccidentReportIndicatorDto>>(ev.IndicatorsJson),
            ev.Status,
            ev.DecidedByUserId,
            decidedByName,
            ev.DecidedAt,
            ev.TowDetectedAt);
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch
        {
            // Malformed JSON in the DB shouldn't crash the report — just drop
            // the affected section and let the frontend fall back to its
            // default text.
            return null;
        }
    }
}
