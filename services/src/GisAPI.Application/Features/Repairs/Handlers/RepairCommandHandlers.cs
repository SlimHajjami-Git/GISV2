using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Features.Repairs.Commands;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Repairs.Handlers;

/// <summary>
/// 404 « Véhicule introuvable. », la même réponse que POST /api/costs pour le même refus.
/// NotFoundException compose un message anglais générique (« Entity ... was not found. »), or
/// le formulaire Dépenses affiche <c>err.error.message</c> tel quel à l'utilisateur.
/// </summary>
public sealed class VehiculeIntrouvableException : NotFoundException
{
    public VehiculeIntrouvableException(int vehicleId) : base("Véhicule", vehicleId) { }

    public override string Message => "Véhicule introuvable.";
}

/// <summary>
/// Le fournisseur d'une réparation doit exister DANS la société (DEF-057) : aucun contrôle
/// ni clé étrangère, un supplierId inventé était stocké et la colonne Fournisseur restait
/// vide sans explication. Le filtre multi-tenant est contourné pour l'administrateur
/// système, d'où le filtre société explicite.
/// </summary>
internal static class RepairSupplierGuard
{
    public const string NotFoundMessage =
        "Fournisseur introuvable : il n'existe pas ou n'appartient pas à votre société. " +
        "Rechargez la page et sélectionnez-le à nouveau.";

    /// <param name="currentSupplierId">
    /// Fournisseur déjà enregistré sur la réparation modifiée. Un fournisseur peut être
    /// supprimé après coup (suppression physique, sans clé étrangère) : renvoyer la valeur
    /// inchangée ne doit pas bloquer la modification d'une réparation ancienne.
    /// </param>
    public static async Task EnsureAsync(
        IGisDbContext context, int? supplierId, int societeId, int? currentSupplierId, CancellationToken ct)
    {
        if (supplierId is not int id || id == currentSupplierId) return;

        var exists = await context.Suppliers
            .AnyAsync(s => s.Id == id && s.CompanyId == societeId, ct);

        if (!exists) throw new DomainException(NotFoundMessage);
    }
}

public class CreateRepairCommandHandler : IRequestHandler<CreateRepairCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IPublisher _publisher;

    public CreateRepairCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, IPublisher publisher)
    {
        _context = context;
        _tenantService = tenantService;
        _publisher = publisher;
    }

    public async Task<int> Handle(CreateRepairCommand request, CancellationToken cancellationToken)
    {
        var societeId = _tenantService.CompanyId ?? 0;
        var parts = request.Parts ?? new List<CreateRepairPartRequest>();

        // Montants refusés AVANT toute lecture : un signe moins produisait un total négatif
        // déduit du poste « Réparations » des rapports (DEF-044).
        RepairInputRules.EnsureAmounts(request.LaborCost, parts);

        // Le véhicule est cherché DANS la société avant toute écriture, comme le fait
        // POST /api/costs : sans ce contrôle, un vehicleId étranger créait une réparation
        // rattachée à la société de l'appelant (comptée dans totalCount, absente de la liste)
        // et un vehicleId inexistant remontait en 500 par violation de clé étrangère.
        var repairVehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == societeId, cancellationToken)
            ?? throw new VehiculeIntrouvableException(request.VehicleId);

        await RepairSupplierGuard.EnsureAsync(_context, request.SupplierId, societeId, null, cancellationToken);

        // Calculate parts cost
        decimal partsCost = 0;
        foreach (var part in parts)
        {
            partsCost += part.Quantity * part.UnitPrice;
        }

        var reference = await NextReferenceAsync(societeId, DateTime.UtcNow, cancellationToken);

        var repairDate = request.RepairDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.RepairDate, DateTimeKind.Utc)
            : request.RepairDate.ToUniversalTime();

        var repair = new Repair
        {
            SocieteId = societeId,
            VehicleId = request.VehicleId,
            SupplierId = request.SupplierId,
            Reference = reference,
            Description = request.Description,
            RepairDate = repairDate,
            MileageAtRepair = request.MileageAtRepair,
            LaborCost = request.LaborCost,
            PartsCost = partsCost,
            TotalCost = request.LaborCost + partsCost,
            Status = "completed",
            RepairType = NormalizeRepairType(request.RepairType),
            InvoiceNumber = request.InvoiceNumber,
            Notes = request.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _context.Repairs.Add(repair);

        // Le kilométrage relevé à la réparation fait avancer la fiche véhicule,
        // comme le fait un plein — sans lui, un client sans boîtier voyait son
        // compteur figé alors qu'il venait de le saisir (recette du 08/09/2026).
        VehicleMileage.Advance(repairVehicle, request.MileageAtRepair);

        await _context.SaveChangesAsync(cancellationToken);

        // Add parts
        foreach (var partReq in parts)
        {
            var part = new RepairPart
            {
                RepairId = repair.Id,
                PartName = partReq.PartName,
                PartReference = partReq.PartReference,
                Quantity = partReq.Quantity,
                UnitPrice = partReq.UnitPrice,
                Subtotal = partReq.Quantity * partReq.UnitPrice,
                Notes = partReq.Notes
            };
            _context.RepairParts.Add(part);
        }

        await _context.SaveChangesAsync(cancellationToken);

        // Notify company admins
        try
        {
            var actorId = _tenantService.UserId ?? 0;
            var actor = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorId, cancellationToken);
            if (actor != null)
            {
                // Le véhicule est déjà chargé et vérifié dans la société : pas de seconde
                // lecture, surtout pas sans filtre société.
                var repairLabel = !string.IsNullOrEmpty(request.Description) ? request.Description
                    : !string.IsNullOrEmpty(repairVehicle.Name) ? repairVehicle.Name
                    : repairVehicle.Plate ?? "Véhicule";

                await _publisher.Publish(new AdminActionNotificationEvent(
                    societeId, actorId, actor.FullName,
                    "repair_created", repairLabel, repair.Id, "repair"
                ), cancellationToken);
            }
        }
        catch { }

        return repair.Id;
    }

    /// <summary>
    /// Référence REP-AAAAMM-NNNN du mois courant, pour la société.
    /// Elle était calculée comme « nombre de réparations de la société + 1 » : après une
    /// suppression, la création suivante réattribuait un numéro encore vivant et deux lignes
    /// portaient la même référence de facture (recette du 16/09/2026). Le nombre de lignes ne
    /// dit rien des numéros déjà sortis : on repart du plus grand numéro attribué dans le mois.
    /// </summary>
    private async Task<string> NextReferenceAsync(int societeId, DateTime nowUtc, CancellationToken ct)
    {
        var prefix = $"REP-{nowUtc:yyyyMM}-";
        var references = await _context.Repairs
            .Where(r => r.SocieteId == societeId && r.Reference.StartsWith(prefix))
            .Select(r => r.Reference)
            .ToListAsync(ct);

        return prefix + NextSequence(references, prefix).ToString("D4");
    }

    /// <summary>Plus grand numéro lisible derrière le préfixe, + 1 (1 si le mois est vierge).</summary>
    internal static int NextSequence(IEnumerable<string?> references, string prefix) =>
        references
            .Where(r => r != null && r.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(r => int.TryParse(r![prefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

    /// <summary>
    /// Vide → null (colonne nullable) ; sinon minuscules sans accents (« Électrique » →
    /// « electrique ») et repli sur « autre » hors des six types connus.
    /// </summary>
    internal static string? NormalizeRepairType(string? value)
    {
        var v = RepairTypeClassifier.Normalize(value);
        if (v.Length == 0) return null;
        return RepairTypeClassifier.KnownTypes.Contains(v) ? v : RepairTypeClassifier.Autre;
    }
}

public class UpdateRepairCommandHandler : IRequestHandler<UpdateRepairCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateRepairCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<bool> Handle(UpdateRepairCommand request, CancellationToken cancellationToken)
    {
        var societeId = _tenantService.CompanyId ?? 0;

        var repair = await _context.Repairs
            .Include(r => r.Parts)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.SocieteId == societeId, cancellationToken);

        if (repair == null) return false;

        // Mêmes refus qu'à la création et que PATCH …/status, tous AVANT la moindre
        // modification de l'entité suivie : en cas de refus, la réparation reste intacte.
        var parts = request.Parts ?? new List<CreateRepairPartRequest>();
        RepairInputRules.EnsureAmounts(request.LaborCost, parts);
        var status = RepairInputRules.NormalizeStatus(request.Status);

        // Même contrôle qu'à la création, et AVANT d'écrire quoi que ce soit : la modification
        // recopiait VehicleId sans le chercher dans la société, ce qui permettait de rattacher
        // une réparation existante au véhicule d'une autre société.
        var repairVehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == societeId, cancellationToken)
            ?? throw new VehiculeIntrouvableException(request.VehicleId);

        await RepairSupplierGuard.EnsureAsync(_context, request.SupplierId, societeId, repair.SupplierId, cancellationToken);

        // Calculate parts cost
        decimal partsCost = 0;
        foreach (var part in parts)
        {
            partsCost += part.Quantity * part.UnitPrice;
        }

        var repairDate = request.RepairDate.Kind == DateTimeKind.Unspecified 
            ? DateTime.SpecifyKind(request.RepairDate, DateTimeKind.Utc) 
            : request.RepairDate.ToUniversalTime();

        repair.VehicleId = request.VehicleId;
        repair.SupplierId = request.SupplierId;
        repair.Description = request.Description;
        repair.RepairDate = repairDate;
        repair.MileageAtRepair = request.MileageAtRepair;
        repair.LaborCost = request.LaborCost;
        repair.PartsCost = partsCost;
        repair.TotalCost = request.LaborCost + partsCost;
        repair.Status = status;
        repair.RepairType = CreateRepairCommandHandler.NormalizeRepairType(request.RepairType);
        repair.InvoiceNumber = request.InvoiceNumber;
        repair.Notes = request.Notes;
        repair.UpdatedAt = DateTime.UtcNow;

        // Même règle qu'à la création : le relevé fait avancer la fiche véhicule.
        VehicleMileage.Advance(repairVehicle, request.MileageAtRepair);

        // Remove old parts
        _context.RepairParts.RemoveRange(repair.Parts);

        // Add new parts
        foreach (var partReq in parts)
        {
            var part = new RepairPart
            {
                RepairId = repair.Id,
                PartName = partReq.PartName,
                PartReference = partReq.PartReference,
                Quantity = partReq.Quantity,
                UnitPrice = partReq.UnitPrice,
                Subtotal = partReq.Quantity * partReq.UnitPrice,
                Notes = partReq.Notes
            };
            _context.RepairParts.Add(part);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public class DeleteRepairCommandHandler : IRequestHandler<DeleteRepairCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DeleteRepairCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<bool> Handle(DeleteRepairCommand request, CancellationToken cancellationToken)
    {
        var societeId = _tenantService.CompanyId ?? 0;

        var repair = await _context.Repairs
            .Include(r => r.Parts)
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.SocieteId == societeId, cancellationToken);

        if (repair == null) return false;

        _context.RepairParts.RemoveRange(repair.Parts);
        _context.Repairs.Remove(repair);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}

public class UpdateRepairStatusCommandHandler : IRequestHandler<UpdateRepairStatusCommand, bool>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public UpdateRepairStatusCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<bool> Handle(UpdateRepairStatusCommand request, CancellationToken cancellationToken)
    {
        var societeId = _tenantService.CompanyId ?? 0;

        // Liste blanche avant toute lecture (DEF-043) : une chaîne libre était stockée, les
        // compteurs de l'écran ne se recoupaient plus et une réparation annulée repassée à
        // « nimportequoi » était recomptée dans les coûts.
        var status = RepairInputRules.NormalizeStatus(request.Status);

        var repair = await _context.Repairs
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.SocieteId == societeId, cancellationToken);

        if (repair == null) return false;

        repair.Status = status;
        repair.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
