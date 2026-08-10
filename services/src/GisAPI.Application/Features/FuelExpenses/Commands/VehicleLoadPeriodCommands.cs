using FluentValidation;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.FuelExpenses.Commands;

// ─────────────────────────────────────────────────────────────────────────────
// CRUD des périodes de chargement (tonnage paramétrable par véhicule).
// Règle métier : pas de chevauchement entre périodes d'un même véhicule —
// un segment ne peut hériter que d'UN tonnage.
// ─────────────────────────────────────────────────────────────────────────────

public record CreateVehicleLoadPeriodCommand(
    int VehicleId,
    DateTime StartTime,
    DateTime? EndTime,
    decimal TonnageT,
    string? Notes) : IRequest<int>;

public class CreateVehicleLoadPeriodValidator : AbstractValidator<CreateVehicleLoadPeriodCommand>
{
    public CreateVehicleLoadPeriodValidator()
    {
        RuleFor(x => x.TonnageT).InclusiveBetween(0, 100)
            .WithMessage("Le tonnage doit être entre 0 et 100 tonnes");
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime)
            .When(x => x.EndTime.HasValue)
            .WithMessage("La fin doit être après le début");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class CreateVehicleLoadPeriodHandler : IRequestHandler<CreateVehicleLoadPeriodCommand, int>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CreateVehicleLoadPeriodHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<int> Handle(CreateVehicleLoadPeriodCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? throw new InvalidOperationException("Company ID not set");

        // Le filtre tenant sur Vehicles garantit qu'on ne peut pas déclarer un
        // tonnage sur le véhicule d'une autre société.
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new InvalidOperationException($"Véhicule {request.VehicleId} introuvable");

        var overlaps = await _context.VehicleLoadPeriods.AnyAsync(lp =>
            lp.VehicleId == request.VehicleId &&
            lp.StartTime < (request.EndTime ?? DateTime.MaxValue) &&
            (lp.EndTime == null || lp.EndTime > request.StartTime), ct);
        if (overlaps)
            throw new InvalidOperationException(
                "Une période de chargement existe déjà sur ce créneau pour ce véhicule");

        var period = new VehicleLoadPeriod
        {
            VehicleId = vehicle.Id,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            TonnageT = request.TonnageT,
            Notes = request.Notes,
            CompanyId = companyId
        };

        _context.VehicleLoadPeriods.Add(period);
        await _context.SaveChangesAsync(ct);
        return period.Id;
    }
}

public record UpdateVehicleLoadPeriodCommand(
    int Id,
    DateTime StartTime,
    DateTime? EndTime,
    decimal TonnageT,
    string? Notes) : IRequest<bool>;

public class UpdateVehicleLoadPeriodValidator : AbstractValidator<UpdateVehicleLoadPeriodCommand>
{
    public UpdateVehicleLoadPeriodValidator()
    {
        RuleFor(x => x.TonnageT).InclusiveBetween(0, 100)
            .WithMessage("Le tonnage doit être entre 0 et 100 tonnes");
        RuleFor(x => x.EndTime).GreaterThan(x => x.StartTime)
            .When(x => x.EndTime.HasValue)
            .WithMessage("La fin doit être après le début");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public class UpdateVehicleLoadPeriodHandler : IRequestHandler<UpdateVehicleLoadPeriodCommand, bool>
{
    private readonly IGisDbContext _context;

    public UpdateVehicleLoadPeriodHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(UpdateVehicleLoadPeriodCommand request, CancellationToken ct)
    {
        // Le filtre tenant global sur VehicleLoadPeriods scope déjà la lecture.
        var period = await _context.VehicleLoadPeriods
            .FirstOrDefaultAsync(lp => lp.Id == request.Id, ct);
        if (period == null) return false;

        var overlaps = await _context.VehicleLoadPeriods.AnyAsync(lp =>
            lp.Id != request.Id &&
            lp.VehicleId == period.VehicleId &&
            lp.StartTime < (request.EndTime ?? DateTime.MaxValue) &&
            (lp.EndTime == null || lp.EndTime > request.StartTime), ct);
        if (overlaps)
            throw new InvalidOperationException(
                "Une période de chargement existe déjà sur ce créneau pour ce véhicule");

        period.StartTime = request.StartTime;
        period.EndTime = request.EndTime;
        period.TonnageT = request.TonnageT;
        period.Notes = request.Notes;

        await _context.SaveChangesAsync(ct);
        return true;
    }
}

public record DeleteVehicleLoadPeriodCommand(int Id) : IRequest<bool>;

public class DeleteVehicleLoadPeriodHandler : IRequestHandler<DeleteVehicleLoadPeriodCommand, bool>
{
    private readonly IGisDbContext _context;

    public DeleteVehicleLoadPeriodHandler(IGisDbContext context)
    {
        _context = context;
    }

    public async Task<bool> Handle(DeleteVehicleLoadPeriodCommand request, CancellationToken ct)
    {
        var period = await _context.VehicleLoadPeriods
            .FirstOrDefaultAsync(lp => lp.Id == request.Id, ct);
        if (period == null) return false;

        _context.VehicleLoadPeriods.Remove(period);
        await _context.SaveChangesAsync(ct);
        return true;
    }
}
