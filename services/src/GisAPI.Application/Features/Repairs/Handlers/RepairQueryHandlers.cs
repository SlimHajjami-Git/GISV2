using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Repairs.Queries;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Repairs.Handlers;

public class GetRepairsQueryHandler : IRequestHandler<GetRepairsQuery, RepairsListResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetRepairsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<RepairsListResult> Handle(GetRepairsQuery request, CancellationToken cancellationToken)
    {
        var societeId = _tenantService.CompanyId ?? 0;
        
        // Convert dates to UTC for Npgsql compatibility
        var fromDateUtc = request.FromDate.HasValue 
            ? DateTime.SpecifyKind(request.FromDate.Value, DateTimeKind.Utc) 
            : (DateTime?)null;
        var toDateUtc = request.ToDate.HasValue
            ? DateTime.SpecifyKind(request.ToDate.Value, DateTimeKind.Utc)
            : (DateTime?)null;

        // Portée véhicules : une réparation expose le véhicule (nom + MATRICULE),
        // le garage et le montant. Fuite constatée le 09/09/2026 : l'écran Dépenses
        // listait les 9 réparations de toute la société à un employé restreint à un
        // seul véhicule. null = admin (tout le parc) ; liste vide = rien.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);

        var query = _context.Repairs
            .Include(r => r.Vehicle)
            .Include(r => r.Parts)
            .Where(r => r.SocieteId == societeId);

        if (scope is not null)
            query = query.Where(r => scope.Contains(r.VehicleId));

        if (request.VehicleId.HasValue)
            query = query.Where(r => r.VehicleId == request.VehicleId.Value);

        if (!string.IsNullOrEmpty(request.Status))
            query = query.Where(r => r.Status == request.Status);

        if (fromDateUtc.HasValue)
            query = query.Where(r => r.RepairDate >= fromDateUtc.Value);

        if (toDateUtc.HasValue)
            query = query.Where(r => r.RepairDate <= toDateUtc.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        var repairs = await query
            .OrderByDescending(r => r.RepairDate)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = repairs.Select(r => new RepairDto(
            r.Id,
            r.VehicleId,
            r.Vehicle?.Name ?? "N/A",
            r.Vehicle?.Plate ?? "N/A",
            r.SupplierId,
            null, // SupplierName - would need to join with suppliers table
            r.Reference,
            r.Description,
            r.RepairDate,
            r.MileageAtRepair,
            r.LaborCost,
            r.PartsCost,
            r.TotalCost,
            r.Status,
            r.InvoiceNumber,
            r.Notes,
            r.CreatedAt,
            r.Parts.Select(p => new RepairPartDto(
                p.Id,
                p.PartName,
                p.PartReference,
                p.Quantity,
                p.UnitPrice,
                p.Subtotal,
                p.Notes
            )).ToList(),
            r.RepairType
        )).ToList();

        return new RepairsListResult(items, totalCount, request.Page, request.PageSize);
    }
}

public class GetRepairByIdQueryHandler : IRequestHandler<GetRepairByIdQuery, RepairDto?>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetRepairByIdQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<RepairDto?> Handle(GetRepairByIdQuery request, CancellationToken cancellationToken)
    {
        var societeId = _tenantService.CompanyId ?? 0;

        // Même portée que la liste : sans elle, un employé restreint ouvrait par
        // son identifiant le détail d'une réparation d'un véhicule non affecté.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);

        var query = _context.Repairs
            .Include(r => r.Vehicle)
            .Include(r => r.Parts)
            .Where(r => r.Id == request.Id && r.SocieteId == societeId);

        if (scope is not null)
            query = query.Where(r => scope.Contains(r.VehicleId));

        var repair = await query.FirstOrDefaultAsync(cancellationToken);

        if (repair == null) return null;

        return new RepairDto(
            repair.Id,
            repair.VehicleId,
            repair.Vehicle?.Name ?? "N/A",
            repair.Vehicle?.Plate ?? "N/A",
            repair.SupplierId,
            null,
            repair.Reference,
            repair.Description,
            repair.RepairDate,
            repair.MileageAtRepair,
            repair.LaborCost,
            repair.PartsCost,
            repair.TotalCost,
            repair.Status,
            repair.InvoiceNumber,
            repair.Notes,
            repair.CreatedAt,
            repair.Parts.Select(p => new RepairPartDto(
                p.Id,
                p.PartName,
                p.PartReference,
                p.Quantity,
                p.UnitPrice,
                p.Subtotal,
                p.Notes
            )).ToList(),
            repair.RepairType
        );
    }
}

public class GetRepairStatsQueryHandler : IRequestHandler<GetRepairStatsQuery, RepairStatsDto>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GetRepairStatsQueryHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<RepairStatsDto> Handle(GetRepairStatsQuery request, CancellationToken cancellationToken)
    {
        var societeId = _tenantService.CompanyId ?? 0;
        
        // Convert dates to UTC for Npgsql compatibility
        var fromDateUtc = request.FromDate.HasValue 
            ? DateTime.SpecifyKind(request.FromDate.Value, DateTimeKind.Utc) 
            : (DateTime?)null;
        var toDateUtc = request.ToDate.HasValue 
            ? DateTime.SpecifyKind(request.ToDate.Value, DateTimeKind.Utc) 
            : (DateTime?)null;

        // Les KPI de l'écran (nombre, coût total, moyenne) doivent porter sur les
        // mêmes réparations que la liste : même portée, sinon le total contredit
        // les lignes affichées.
        var scope = await VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, cancellationToken);

        var query = _context.Repairs
            .Where(r => r.SocieteId == societeId);

        if (scope is not null)
            query = query.Where(r => scope.Contains(r.VehicleId));

        if (request.VehicleId.HasValue)
            query = query.Where(r => r.VehicleId == request.VehicleId.Value);

        if (fromDateUtc.HasValue)
            query = query.Where(r => r.RepairDate >= fromDateUtc.Value);

        if (toDateUtc.HasValue)
            query = query.Where(r => r.RepairDate <= toDateUtc.Value);

        var repairs = await query.ToListAsync(cancellationToken);

        var totalRepairs = repairs.Count;
        var pendingRepairs = repairs.Count(r => r.Status == "pending" || r.Status == "in_progress");
        var completedRepairs = repairs.Count(r => r.Status == "completed");
        var totalCost = repairs.Sum(r => r.TotalCost);
        var averageCost = totalRepairs > 0 ? totalCost / totalRepairs : 0;
        var totalLaborCost = repairs.Sum(r => r.LaborCost);
        var totalPartsCost = repairs.Sum(r => r.PartsCost);

        return new RepairStatsDto(
            totalRepairs,
            pendingRepairs,
            completedRepairs,
            totalCost,
            averageCost,
            totalLaborCost,
            totalPartsCost
        );
    }
}
