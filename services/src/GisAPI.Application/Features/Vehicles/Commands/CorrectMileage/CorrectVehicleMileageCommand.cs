using FluentValidation;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles.Commands.CorrectMileage;

/// <summary>
/// Correction ASSUMÉE du compteur d'un véhicule, motif obligatoire.
///
/// <para>Partout ailleurs le kilométrage ne recule pas : c'est la garde qui
/// attrape les fautes de frappe. Mais elle n'avait pas de porte de sortie — une
/// valeur aberrante entrée par l'import (recette du 08/09/2026 : un véhicule à
/// 216 000 km alors que ses relevés plafonnent à 85 550, un autre à 145 000 pour
/// une faute de frappe) bloquait DÉFINITIVEMENT toute saisie manuelle
/// ultérieure, et aucun écran ne permettait de revenir en arrière.</para>
///
/// <para>Ce chemin est le seul qui accepte une baisse. Il exige un motif et
/// journalise l'ancienne et la nouvelle valeur dans <c>audit_logs</c> : une
/// correction de compteur se justifie et se retrouve.</para>
/// </summary>
public record CorrectVehicleMileageCommand(int VehicleId, int Mileage, string Reason)
    : IRequest<CorrectVehicleMileageResult>;

public record CorrectVehicleMileageResult(int VehicleId, int PreviousMileage, int Mileage);

public class CorrectVehicleMileageCommandValidator : AbstractValidator<CorrectVehicleMileageCommand>
{
    public CorrectVehicleMileageCommandValidator()
    {
        RuleFor(x => x.Mileage)
            .GreaterThanOrEqualTo(0).WithMessage("Le kilométrage ne peut pas être négatif.")
            .LessThanOrEqualTo(5_000_000).WithMessage("Le kilométrage saisi est invraisemblable.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Indiquez le motif de la correction.")
            .MaximumLength(500);
    }
}

public class CorrectVehicleMileageCommandHandler
    : IRequestHandler<CorrectVehicleMileageCommand, CorrectVehicleMileageResult>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public CorrectVehicleMileageCommandHandler(IGisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<CorrectVehicleMileageResult> Handle(CorrectVehicleMileageCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId, ct)
            ?? throw new NotFoundException("Vehicle", request.VehicleId);

        var previous = vehicle.Mileage;
        if (previous == request.Mileage)
            return new CorrectVehicleMileageResult(vehicle.Id, previous, previous);

        vehicle.Mileage = request.Mileage;
        vehicle.UpdatedAt = DateTime.UtcNow;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = _tenantService.UserId,
            CompanyId = companyId,
            Action = "vehicle_mileage_corrected",
            EntityType = "Vehicle",
            EntityId = vehicle.Id,
            EntityName = vehicle.Plate,
            OldValues = new Dictionary<string, object> { ["mileage"] = previous },
            NewValues = new Dictionary<string, object> { ["mileage"] = request.Mileage },
            Description = $"Kilométrage corrigé de {previous:N0} à {request.Mileage:N0} km — {request.Reason.Trim()}",
            Timestamp = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(ct);

        return new CorrectVehicleMileageResult(vehicle.Id, previous, vehicle.Mileage);
    }
}
