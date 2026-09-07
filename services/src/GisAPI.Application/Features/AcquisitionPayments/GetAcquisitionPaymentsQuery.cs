using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AcquisitionPayments;

/// <summary>
/// Échéances d'acquisition visibles par l'appelant (VehicleScope), triées par
/// date d'échéance décroissante puis seq décroissant. Sans
/// <paramref name="IncludeFuture"/> : les lignes payées, plus les planifiées et
/// ignorées dont la date est atteinte (l'écran Dépenses) ; avec : tout
/// l'échéancier (la fiche véhicule). <paramref name="StartDate"/> /
/// <paramref name="EndDate"/> bornent la date d'échéance (inclusives, jour).
/// Pas de pagination : l'échéancier est borné par les contrats (quelques
/// centaines de lignes au plus).
/// </summary>
public record GetAcquisitionPaymentsQuery(
    int? VehicleId = null,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    bool IncludeFuture = false) : IRequest<List<AcquisitionPaymentDto>>;

public class GetAcquisitionPaymentsQueryHandler : IRequestHandler<GetAcquisitionPaymentsQuery, List<AcquisitionPaymentDto>>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IDateTimeProvider _clock;
    private readonly ILogger<GetAcquisitionPaymentsQueryHandler> _logger;

    public GetAcquisitionPaymentsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService,
        IDateTimeProvider clock, ILogger<GetAcquisitionPaymentsQueryHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<List<AcquisitionPaymentDto>> Handle(GetAcquisitionPaymentsQuery request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new UnauthorizedAccessException("Company ID not found");
        var today = DateOnly.FromDateTime(_clock.UtcNow.Date);

        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, ct);
        if (scope is { Count: 0 }) return new List<AcquisitionPaymentDto>();

        // ── Génération paresseuse (transition sans backfill) ──
        // Tout véhicule visible qui porte un contrat ou un prix mais n'a encore
        // AUCUNE ligne reçoit son échéancier ici, une fois pour toutes. Le
        // pré-filtre SQL est un sur-ensemble de HasSchedule (un contrat leasing
        // incomplet ne produit rien, c'est inoffensif) ; le filtre société est
        // explicite pour ne pas dépendre du court-circuit sys_admin du filtre tenant.
        var candidates = _context.Vehicles
            .Where(v => v.CompanyId == companyId
                        && (v.AcquisitionType == "leasing" || (v.PurchasePrice > 0 && v.PurchaseDate != null))
                        && !_context.AcquisitionPayments.Any(p => p.VehicleId == v.Id && p.CompanyId == v.CompanyId));
        if (scope != null) candidates = candidates.Where(v => scope.Contains(v.Id));
        if (request.VehicleId.HasValue) candidates = candidates.Where(v => v.Id == request.VehicleId.Value);

        // Un contrat leasing à moitié saisi ne produit aucune ligne : sans ce
        // filtre il resterait candidat à chaque appel, avec une requête inutile
        // par véhicule.
        var toGenerate = (await candidates.AsNoTracking().ToListAsync(ct))
            .Where(AcquisitionScheduleSync.HasSchedule)
            .ToList();

        if (toGenerate.Count > 0)
        {
            try
            {
                var generated = false;
                foreach (var v in toGenerate)
                    generated |= await AcquisitionScheduleSync.SyncAsync(_context, v, ct);
                if (generated) await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Deux lectures simultanées ont généré le même échéancier (la
                // contrainte d'unicité a tranché). La génération est idempotente :
                // on abandonne nos insertions et on lit ce que l'autre a écrit.
                _logger.LogInformation(ex,
                    "Échéancier d'acquisition déjà généré par une requête concurrente (société {CompanyId}) — lecture de l'existant", companyId);
                foreach (var entry in _context.ChangeTracker.Entries<AcquisitionPayment>().ToList())
                    entry.State = EntityState.Detached;
            }
        }

        // ── Lecture ──
        var query = _context.AcquisitionPayments
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId);

        if (scope != null) query = query.Where(p => scope.Contains(p.VehicleId));
        if (request.VehicleId.HasValue) query = query.Where(p => p.VehicleId == request.VehicleId.Value);

        if (!request.IncludeFuture)
            query = query.Where(p => p.Status == AcquisitionPayment.Statuses.Paid || p.DueDate <= today);

        if (request.StartDate.HasValue)
        {
            var from = DateOnly.FromDateTime(request.StartDate.Value.Date);
            query = query.Where(p => p.DueDate >= from);
        }
        if (request.EndDate.HasValue)
        {
            var to = DateOnly.FromDateTime(request.EndDate.Value.Date);
            query = query.Where(p => p.DueDate <= to);
        }

        var rows = await query
            .OrderByDescending(p => p.DueDate)
            .ThenByDescending(p => p.Seq)
            .Select(p => new { Payment = p, Vehicle = p.Vehicle! })
            .ToListAsync(ct);

        return rows.Select(r => AcquisitionPaymentDto.From(r.Payment, r.Vehicle, today)).ToList();
    }
}
