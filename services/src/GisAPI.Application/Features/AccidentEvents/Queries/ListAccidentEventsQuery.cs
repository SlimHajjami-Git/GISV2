using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.AccidentEvents.Queries;

/// <summary>
/// Calypso 7 — paged list of accident events for the unified
/// <c>/accident-reports</c> admin page. Returns enough data to render
/// the table (vehicle, date, status, severity, repair cost, claim
/// status) plus a small derived progress indicator.
///
/// <para>Optional filters: <c>Status</c>, <c>VehicleId</c>,
/// <c>Origin</c> (<c>auto|manual</c>), <c>IncludeDismissed</c>.</para>
/// </summary>
public record ListAccidentEventsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    int? VehicleId = null,
    string? Origin = null,
    bool IncludeDismissed = false
) : IRequest<ListAccidentEventsResult>;

public record AccidentEventListItemDto(
    int Id,
    string Origin,
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
    // Phase 2 — initial damages
    string? InitialSeverity,
    // Phase 3 — expert
    DateTime? ExpertVisitedAt,
    decimal? ExpertEstimatedAmount,
    // Phase 4 — mechanic quote
    decimal? MechanicQuotedAmount,
    // Phase 5 — repair
    DateTime? RepairCompletedAt,
    decimal? ActualRepairCost,
    // Phase 6 — claim
    string? ClaimNumber,
    string? ClaimStatus,
    decimal? ClaimApprovedAmount,
    // Derived
    string CurrentPhase  // "detection" | "confirmed" | "expertise" | "quote" | "repair" | "claim" | "closed" | "dismissed"
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

    public ListAccidentEventsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
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

        if (!string.IsNullOrWhiteSpace(request.Origin))
            query = query.Where(e => e.Origin == request.Origin);

        if (request.VehicleId.HasValue)
            query = query.Where(e => e.VehicleId == request.VehicleId.Value);

        var totalCount = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(e => e.IncidentAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                .ToDictionaryAsync(u => u.Id, u => (string?)u.FullName ?? u.Email ?? string.Empty, ct);

        var items = rows.Select(r => new AccidentEventListItemDto(
            Id: r.Id,
            Origin: r.Origin,
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
            InitialSeverity: r.InitialSeverity,
            ExpertVisitedAt: r.ExpertVisitedAt,
            ExpertEstimatedAmount: r.ExpertEstimatedAmount,
            MechanicQuotedAmount: r.MechanicQuotedAmount,
            RepairCompletedAt: r.RepairCompletedAt,
            ActualRepairCost: r.ActualRepairCost,
            ClaimNumber: r.ClaimNumber,
            ClaimStatus: r.ClaimStatus,
            ClaimApprovedAmount: r.ClaimApprovedAmount,
            CurrentPhase: DerivePhase(r)
        )).ToList();

        return new ListAccidentEventsResult(items, totalCount, page, pageSize);
    }

    /// <summary>
    /// Tags a row with the most-advanced phase it has reached.
    /// Drives the phase chip / color in the list UI.
    /// </summary>
    private static string DerivePhase(GisAPI.Domain.Entities.AccidentEvent e)
    {
        if (e.Status == "dismissed") return "dismissed";
        if (e.Status == "pending") return "detection";
        if (!string.IsNullOrEmpty(e.ClaimStatus) && e.ClaimStatus is "closed" or "approved" or "rejected") return "closed";
        if (e.ClaimSubmittedAt.HasValue || !string.IsNullOrEmpty(e.ClaimNumber)) return "claim";
        if (e.RepairCompletedAt.HasValue || e.ActualRepairCost.HasValue) return "repair";
        if (e.MechanicQuoteAt.HasValue || e.MechanicQuotedAmount.HasValue) return "quote";
        if (e.ExpertVisitedAt.HasValue || e.ExpertEstimatedAmount.HasValue) return "expertise";
        return "confirmed";
    }
}
