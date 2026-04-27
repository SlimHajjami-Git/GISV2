using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

/// <summary>
/// Calypso 6 (P9) — final step of the accident workflow. After
/// <see cref="ConfirmAccidentCommand"/> moves the row to
/// <c>awaiting_details</c> and the auto-generated PDF is attached, the
/// admin opens the report and fills in the damages form (description,
/// severity, estimated cost, claim number, internal notes, manual tow
/// date). Submitting the form lands here and finalises the row to
/// <c>status = "confirmed"</c>.
///
/// <para>Idempotent on re-submit: a confirmed row stays confirmed; only
/// the damages JSON is updated. A row in <c>pending</c> or
/// <c>dismissed</c> is rejected (the admin must go through the modal
/// first).</para>
///
/// <para>Tenant-scoped via <see cref="ICurrentTenantService.CompanyId"/>.</para>
/// </summary>
public record UpdateAccidentDamagesCommand(
    int AccidentEventId,
    string? Description,
    string? Severity,            // "minor" | "moderate" | "severe" | "total"
    decimal? EstimatedCost,
    string? ClaimNumber,
    string? InternalNotes,
    DateTime? ManualTowDate
) : IRequest<Unit>;

public class UpdateAccidentDamagesCommandHandler : IRequestHandler<UpdateAccidentDamagesCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<UpdateAccidentDamagesCommandHandler> _logger;

    public UpdateAccidentDamagesCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        ILogger<UpdateAccidentDamagesCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    public async Task<Unit> Handle(UpdateAccidentDamagesCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        var ev = await _context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == request.AccidentEventId && e.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AccidentEvent", request.AccidentEventId);

        // Damages can only be filled after the modal step; we refuse to
        // touch a row that is still pending (admin has not decided yet) or
        // dismissed (false alarm — there are no damages to record).
        if (ev.Status is not ("awaiting_details" or "confirmed"))
        {
            throw new DomainException(
                $"Impossible de saisir les dégâts: l'accident est en statut '{ev.Status}'.");
        }

        var payload = new
        {
            description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            severity = NormaliseSeverity(request.Severity),
            estimatedCost = request.EstimatedCost,
            claimNumber = string.IsNullOrWhiteSpace(request.ClaimNumber) ? null : request.ClaimNumber.Trim(),
            internalNotes = string.IsNullOrWhiteSpace(request.InternalNotes) ? null : request.InternalNotes.Trim(),
            manualTowDate = request.ManualTowDate,
        };

        ev.DamagesJson = JsonSerializer.Serialize(payload);
        ev.Status = "confirmed";
        ev.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "UpdateAccidentDamagesCommand: accident {AccidentId} finalised with damages (severity={Severity})",
            ev.Id, payload.severity);

        return Unit.Value;
    }

    /// <summary>
    /// Whitelists the severity to one of the four UI values; anything else
    /// (or null/empty) lands as null so the row never carries garbage.
    /// </summary>
    private static string? NormaliseSeverity(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lc = raw.Trim().ToLowerInvariant();
        return lc switch
        {
            "minor" or "moderate" or "severe" or "total" => lc,
            _ => null,
        };
    }
}
