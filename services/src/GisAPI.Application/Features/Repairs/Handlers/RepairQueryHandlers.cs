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
    /// <summary>
    /// Plafond de lignes par appel. L'écran Réparations enchaîne les pages pour
    /// charger tout le parc : il lui faut une page large, mais chaque ligne
    /// traîne son véhicule et ses pièces, et la route accepte n'importe quel
    /// pageSize. Deux mille est la borne haute retenue le 20/09/2026, la même
    /// que celle au-delà de laquelle l'écran prévient qu'il ne charge plus tout.
    /// </summary>
    public const int TaillePageMax = 2000;

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

        // Comparaison sans casse ni espaces : « Cancelled » existe dans des données
        // anciennes (import) et échappait au filtre, alors que l'écran et les compteurs
        // le comptent bien pour « annulée » — le total démentait donc le tableau.
        var statutFiltre = request.Status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(statutFiltre))
            query = query.Where(r => r.Status.ToLower() == statutFiltre);

        if (fromDateUtc.HasValue)
            query = query.Where(r => r.RepairDate >= fromDateUtc.Value);

        if (toDateUtc.HasValue)
            query = query.Where(r => r.RepairDate <= toDateUtc.Value);

        var totalCount = await query.CountAsync(cancellationToken);

        // Bornes de pagination : la route accepte page et pageSize tels quels.
        // Un pageSize démesuré ferait matérialiser tout le parc avec ses pièces
        // et ses véhicules ; une page à zéro ou négative rendrait un Skip négatif.
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, TaillePageMax);

        // Départage par identifiant : l'écran Réparations enchaîne les pages
        // (Skip/Take) pour charger tout le parc. Sans second critère, PostgreSQL
        // ne garantit AUCUN ordre entre deux lignes de même repair_date — la même
        // réparation pouvait revenir sur deux pages et une autre disparaître,
        // donnant un coût total faux sans le moindre message (20/09/2026).
        var repairs = await query
            .OrderByDescending(r => r.RepairDate)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        // Nom du fournisseur. L'entite Repair ne porte que SupplierId, sans
        // propriete de navigation, et les deux projections de ce fichier
        // renvoyaient null en dur (« would need to join with suppliers table »).
        // Consequence : la colonne « Fournisseur » du rapport « Reparations
        // vehicules » et la fiche detail affichaient « - » pour TOUS les clients,
        // alors que la valeur est bien saisie (37 reparations sur 37 renseignees
        // sur le jeu de recette, 3 sur 19 en production le 10/09/2026).
        // Une seule requete pour toute la page : pas de N+1. Suppliers est une
        // TenantEntity, donc deja filtree sur la societe courante.
        var supplierIds = repairs
            .Where(r => r.SupplierId.HasValue)
            .Select(r => r.SupplierId!.Value)
            .Distinct()
            .ToList();

        var supplierNames = supplierIds.Count == 0
            ? new Dictionary<int, string>()
            : await _context.Suppliers
                .Where(s => supplierIds.Contains(s.Id))
                .Select(s => new { s.Id, s.Name })
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        string? SupplierNameOf(int? id) =>
            id.HasValue && supplierNames.TryGetValue(id.Value, out var name) ? name : null;

        var items = repairs.Select(r => new RepairDto(
            r.Id,
            r.VehicleId,
            r.Vehicle?.Name ?? "N/A",
            r.Vehicle?.Plate ?? "N/A",
            r.SupplierId,
            SupplierNameOf(r.SupplierId),
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
            r.RepairType,
            r.AccidentEventId
        )).ToList();

        return new RepairsListResult(items, totalCount, page, pageSize);
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

        // Meme correction que dans la liste ci-dessus : la fiche detail
        // renvoyait elle aussi un nom de fournisseur null en dur.
        var supplierName = repair.SupplierId.HasValue
            ? await _context.Suppliers
                .Where(s => s.Id == repair.SupplierId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        return new RepairDto(
            repair.Id,
            repair.VehicleId,
            repair.Vehicle?.Name ?? "N/A",
            repair.Vehicle?.Plate ?? "N/A",
            repair.SupplierId,
            supplierName,
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
            repair.RepairType,
            repair.AccidentEventId
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

        // Même filtre de statut que la liste, même comparaison sans casse : les compteurs
        // de l'écran suivent le filtre choisi, et ils portent sur TOUTE la société — pas
        // sur la page que le navigateur a chargée.
        var statutFiltre = request.Status?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(statutFiltre))
            query = query.Where(r => r.Status.ToLower() == statutFiltre);

        if (fromDateUtc.HasValue)
            query = query.Where(r => r.RepairDate >= fromDateUtc.Value);

        if (toDateUtc.HasValue)
            query = query.Where(r => r.RepairDate <= toDateUtc.Value);

        var repairs = await query.ToListAsync(cancellationToken);

        var totalRepairs = repairs.Count;
        var pendingRepairs = repairs.Count(r =>
            RepairInputRules.HasStatus(r.Status, RepairInputRules.Pending)
            || RepairInputRules.HasStatus(r.Status, RepairInputRules.InProgress));
        var completedRepairs = repairs.Count(r => RepairInputRules.HasStatus(r.Status, RepairInputRules.Completed));

        // Une réparation annulée reste une ligne de la liste mais ne coûte rien : les rapports
        // l'excluent déjà. Depuis que l'écran Réparations propose « Annulée », elle gonflait
        // le coût total et, sans compteur, en attente + terminées ne recoupait plus le total.
        var cancelledRepairs = repairs.Count(r => RepairInputRules.HasStatus(r.Status, RepairInputRules.Cancelled));
        var costed = repairs.Where(r => !RepairInputRules.HasStatus(r.Status, RepairInputRules.Cancelled)).ToList();
        var totalCost = costed.Sum(r => r.TotalCost);
        var averageCost = costed.Count > 0 ? totalCost / costed.Count : 0;
        var totalLaborCost = costed.Sum(r => r.LaborCost);
        var totalPartsCost = costed.Sum(r => r.PartsCost);

        return new RepairStatsDto(
            totalRepairs,
            pendingRepairs,
            completedRepairs,
            totalCost,
            averageCost,
            totalLaborCost,
            totalPartsCost,
            cancelledRepairs
        );
    }
}
