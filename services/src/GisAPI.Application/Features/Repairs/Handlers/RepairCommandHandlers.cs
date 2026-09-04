using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.Notifications.Events;
using GisAPI.Application.Features.Repairs.Commands;
using GisAPI.Application.Features.Reports.Common;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Repairs.Handlers;

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

        // Calculate parts cost
        decimal partsCost = 0;
        foreach (var part in request.Parts)
        {
            partsCost += part.Quantity * part.UnitPrice;
        }

        // Generate reference
        var count = await _context.Repairs
            .Where(r => r.SocieteId == societeId)
            .CountAsync(cancellationToken);
        var reference = $"REP-{DateTime.UtcNow:yyyyMM}-{(count + 1):D4}";

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
        await _context.SaveChangesAsync(cancellationToken);

        // Add parts
        foreach (var partReq in request.Parts)
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
            var vehicle = await _context.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == request.VehicleId, cancellationToken);
            if (actor != null)
            {
                var repairLabel = !string.IsNullOrEmpty(request.Description)
                    ? request.Description
                    : vehicle?.Name ?? vehicle?.Plate ?? "Véhicule";

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

        // Calculate parts cost
        decimal partsCost = 0;
        foreach (var part in request.Parts)
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
        repair.Status = request.Status;
        repair.RepairType = CreateRepairCommandHandler.NormalizeRepairType(request.RepairType);
        repair.InvoiceNumber = request.InvoiceNumber;
        repair.Notes = request.Notes;
        repair.UpdatedAt = DateTime.UtcNow;

        // Remove old parts
        _context.RepairParts.RemoveRange(repair.Parts);

        // Add new parts
        foreach (var partReq in request.Parts)
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

        var repair = await _context.Repairs
            .FirstOrDefaultAsync(r => r.Id == request.Id && r.SocieteId == societeId, cancellationToken);

        if (repair == null) return false;

        repair.Status = request.Status;
        repair.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
