using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AccidentEvents.Queries;

/// <summary>
/// Calypso 6 (P9) — paged list of accident events for the new
/// <c>/accident-reports</c> admin page. Filters at the company level
/// (no global view), excludes <c>dismissed</c> rows by default since
/// they are noise, and orders newest first.
///
/// <para>Optional filters:</para>
/// <list type="bullet">
///   <item><description><c>Status</c> — filter to a specific lifecycle
///     value (<c>pending</c>, <c>awaiting_details</c>, <c>confirmed</c>, …).</description></item>
///   <item><description><c>VehicleId</c> — only events for a specific
///     vehicle.</description></item>
///   <item><description><c>IncludeDismissed</c> — true to also include
///     dismissed rows in the result.</description></item>
/// </list>
/// </summary>
public record ListAccidentEventsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    int? VehicleId = null,
    bool IncludeDismissed = false
) : IRequest<ListAccidentEventsResult>;

public record AccidentEventListItemDto(
    int Id,
    int? VehicleId,
    string? VehicleLabel,
    DateTime IncidentAt,
    double Latitude,
    double Longitude,
    string? LocationCommune,
    string? LocationGovernorate,
    int Confidence,
    string Status,
    DateTime? DecidedAt,
    string? DecidedByName,
    DateTime? TowDetectedAt,
    string? PdfReportUrl,
    string? Severity,           // Pulled out of damages_json for the table
    decimal? EstimatedCost      // Pulled out of damages_json for the table
);

public record ListAccidentEventsResult(
    List<AccidentEventListItemDto> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public class ListAccidentEventsQueryHandler : IRequestHandler<ListAccidentEventsQuery, ListAccidentEventsResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public ListAccidentEventsQueryHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<ListAccidentEventsResult> Handle(ListAccidentEventsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize is < 1 or > 200 ? 20 : request.PageSize;

        var query = _context.AccidentEvents
            .AsNoTracking()
            .Where(e => e.CompanyId == companyId);

        if (!request.IncludeDismissed)
            query = query.Where(e => e.Status != "dismissed");

        if (!string.IsNullOrWhiteSpace(request.Status))
            query = query.Where(e => e.Status == request.Status);

        if (request.VehicleId.HasValue)
            query = query.Where(e => e.VehicleId == request.VehicleId.Value);

        var totalCount = await query.CountAsync(ct);

        // Pull the rows + a small denormalised name lookup for the decider.
        // Keep it as a simple LINQ shape — the dataset is small (one row
        // per real accident, dozens per company per year at most).
        var rows = await query
            .OrderByDescending(e => e.IncidentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.VehicleId,
                e.VehicleLabel,
                e.IncidentAt,
                e.Latitude,
                e.Longitude,
                e.LocationCommune,
                e.LocationGovernorate,
                e.Confidence,
                e.Status,
                e.DecidedAt,
                e.DecidedByUserId,
                e.TowDetectedAt,
                e.PdfReportUrl,
                e.DamagesJson,
            })
            .ToListAsync(ct);

        var deciderIds = rows
            .Where(r => r.DecidedByUserId.HasValue)
            .Select(r => r.DecidedByUserId!.Value)
            .Distinct()
            .ToList();

        var deciderNames = deciderIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(u => deciderIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.FullName ?? u.Email, ct);

        var items = rows.Select(r =>
        {
            var (severity, estimatedCost) = ExtractDamagesSummary(r.DamagesJson);
            return new AccidentEventListItemDto(
                Id: r.Id,
                VehicleId: r.VehicleId,
                VehicleLabel: r.VehicleLabel,
                IncidentAt: r.IncidentAt,
                Latitude: r.Latitude,
                Longitude: r.Longitude,
                LocationCommune: r.LocationCommune,
                LocationGovernorate: r.LocationGovernorate,
                Confidence: r.Confidence,
                Status: r.Status,
                DecidedAt: r.DecidedAt,
                DecidedByName: r.DecidedByUserId.HasValue && deciderNames.TryGetValue(r.DecidedByUserId.Value, out var n) ? n : null,
                TowDetectedAt: r.TowDetectedAt,
                PdfReportUrl: r.PdfReportUrl,
                Severity: severity,
                EstimatedCost: estimatedCost
            );
        }).ToList();

        return new ListAccidentEventsResult(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Cheap parse of the damages JSON to extract just the two fields we
    /// surface in the list table. Anything unexpected (null, malformed,
    /// missing fields) safely returns nulls — the row stays usable.
    /// </summary>
    private static (string? Severity, decimal? EstimatedCost) ExtractDamagesSummary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return (null, null);
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? sev = null;
            decimal? cost = null;
            if (root.TryGetProperty("severity", out var sevEl) && sevEl.ValueKind == System.Text.Json.JsonValueKind.String)
                sev = sevEl.GetString();
            if (root.TryGetProperty("estimatedCost", out var costEl))
            {
                if (costEl.ValueKind == System.Text.Json.JsonValueKind.Number)
                    cost = costEl.GetDecimal();
            }
            return (sev, cost);
        }
        catch
        {
            return (null, null);
        }
    }
}
