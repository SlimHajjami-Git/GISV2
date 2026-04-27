using System.Text.Json;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

// =============================================================================
// Calypso 7 — phase-specific commands for the unified accident timeline.
//
// All commands share the same shape:
//   - tenant-scoped lookup by id + companyId
//   - guard against editing dismissed/pending rows (must be confirmed first)
//   - idempotent: re-submit replaces / merges values
//   - stamp UpdatedAt and SaveChangesAsync
//
// Phase 5 (RegisterRepair) and Phase 6 (RegisterClaim) additionally
// upsert a VehicleCost row so the /depenses module shows the expense
// (or insurance refund) immediately and can be filtered by accident.
// =============================================================================

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 2 — Initial damages capture (post-confirmation)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 2: initial visible damages captured by the admin
/// just after confirming the accident. No cost yet (it is unknown on
/// day 0). Description, severity, list of damaged zones.
/// </summary>
public record UpdateInitialDamagesCommand(
    int AccidentEventId,
    string? Description,
    string? Severity,
    List<string>? DamagedZones,
    string? PoliceReportNumber,
    int? MileageAtAccident,
    string? WeatherConditions,
    string? RoadConditions
) : IRequest<Unit>;

public class UpdateInitialDamagesCommandHandler : PhaseCommandHandlerBase, IRequestHandler<UpdateInitialDamagesCommand, Unit>
{
    public UpdateInitialDamagesCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<UpdateInitialDamagesCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(UpdateInitialDamagesCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.InitialDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        ev.InitialSeverity = NormaliseSeverity(request.Severity);
        ev.DamagedZonesJson = request.DamagedZones?.Count > 0
            ? JsonSerializer.Serialize(request.DamagedZones)
            : null;
        ev.PoliceReportNumber = string.IsNullOrWhiteSpace(request.PoliceReportNumber) ? null : request.PoliceReportNumber.Trim();
        ev.MileageAtAccident = request.MileageAtAccident;
        ev.WeatherConditions = string.IsNullOrWhiteSpace(request.WeatherConditions) ? null : request.WeatherConditions.Trim();
        ev.RoadConditions = string.IsNullOrWhiteSpace(request.RoadConditions) ? null : request.RoadConditions.Trim();
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 3 — Expert assessment (insurance expert visit)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 3: insurance expert visited the vehicle and gave
/// a written assessment + estimated repair amount. May happen days or
/// weeks after the accident.
/// </summary>
public record RegisterExpertAssessmentCommand(
    int AccidentEventId,
    DateTime? VisitedAt,
    string? ExpertName,
    string? ExpertCompany,
    string? Assessment,
    decimal? EstimatedAmount
) : IRequest<Unit>;

public class RegisterExpertAssessmentCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterExpertAssessmentCommand, Unit>
{
    public RegisterExpertAssessmentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterExpertAssessmentCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(RegisterExpertAssessmentCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.ExpertVisitedAt = request.VisitedAt?.ToUniversalTime();
        ev.ExpertName = string.IsNullOrWhiteSpace(request.ExpertName) ? null : request.ExpertName.Trim();
        ev.ExpertCompany = string.IsNullOrWhiteSpace(request.ExpertCompany) ? null : request.ExpertCompany.Trim();
        ev.ExpertAssessment = string.IsNullOrWhiteSpace(request.Assessment) ? null : request.Assessment.Trim();
        ev.ExpertEstimatedAmount = request.EstimatedAmount;
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 4 — Mechanic quote
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 4: garage / mechanic provided a repair quote.
/// </summary>
public record RegisterMechanicQuoteCommand(
    int AccidentEventId,
    DateTime? QuoteAt,
    string? MechanicName,
    decimal? QuotedAmount
) : IRequest<Unit>;

public class RegisterMechanicQuoteCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterMechanicQuoteCommand, Unit>
{
    public RegisterMechanicQuoteCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterMechanicQuoteCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(RegisterMechanicQuoteCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.MechanicQuoteAt = request.QuoteAt?.ToUniversalTime();
        ev.MechanicName = string.IsNullOrWhiteSpace(request.MechanicName) ? null : request.MechanicName.Trim();
        ev.MechanicQuotedAmount = request.QuotedAmount;
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 5 — Repair (auto-syncs VehicleCost)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 5: repair tracking + auto-sync to VehicleCost.
/// As soon as <c>ActualRepairCost</c> is set (the mechanic's invoice),
/// a corresponding <c>VehicleCost</c> row of type <c>"repair"</c> is
/// upserted so the cost shows up in /depenses immediately. Re-running
/// the command updates the same VehicleCost row (no duplicates).
/// </summary>
public record RegisterRepairCommand(
    int AccidentEventId,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    decimal? ActualCost
) : IRequest<Unit>;

public class RegisterRepairCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterRepairCommand, Unit>
{
    public RegisterRepairCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterRepairCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(RegisterRepairCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.RepairStartedAt = request.StartedAt?.ToUniversalTime();
        ev.RepairCompletedAt = request.CompletedAt?.ToUniversalTime();
        ev.ActualRepairCost = request.ActualCost;
        ev.UpdatedAt = DateTime.UtcNow;

        // Auto-sync: upsert VehicleCost("repair") if cost > 0 and vehicle known.
        if (request.ActualCost is > 0 && ev.VehicleId.HasValue)
        {
            await UpsertVehicleCostAsync(
                ev,
                type: "repair",
                amount: request.ActualCost.Value,
                date: ev.RepairCompletedAt ?? DateTime.UtcNow,
                description: $"Réparation accident — {ev.ReferenceCode ?? $"#{ev.Id}"}",
                ct: ct);
        }

        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Phase 6 — Insurance settlement (auto-syncs VehicleCost as refund)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — Phase 6: insurance claim tracking + auto-sync to
/// VehicleCost as a refund row (type <c>"insurance_refund"</c>) when
/// the claim is approved.
/// </summary>
public record RegisterClaimCommand(
    int AccidentEventId,
    string? ClaimNumber,
    DateTime? SubmittedAt,
    decimal? ApprovedAmount,
    string? Status,                  // pending|approved|partial|rejected|closed
    bool? ThirdPartyInvolved
) : IRequest<Unit>;

public class RegisterClaimCommandHandler : PhaseCommandHandlerBase, IRequestHandler<RegisterClaimCommand, Unit>
{
    public RegisterClaimCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<RegisterClaimCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(RegisterClaimCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        ev.ClaimNumber = string.IsNullOrWhiteSpace(request.ClaimNumber) ? null : request.ClaimNumber.Trim();
        ev.ClaimSubmittedAt = request.SubmittedAt?.ToUniversalTime();
        ev.ClaimApprovedAmount = request.ApprovedAmount;
        ev.ClaimStatus = NormaliseClaimStatus(request.Status);
        if (request.ThirdPartyInvolved.HasValue) ev.ThirdPartyInvolved = request.ThirdPartyInvolved.Value;
        ev.UpdatedAt = DateTime.UtcNow;

        // Auto-sync: upsert VehicleCost("insurance_refund") if approved > 0
        // and vehicle known. The refund row is positive amount but the
        // /depenses UI renders it as a credit (green).
        if (request.ApprovedAmount is > 0 && ev.VehicleId.HasValue)
        {
            await UpsertVehicleCostAsync(
                ev,
                type: "insurance_refund",
                amount: request.ApprovedAmount.Value,
                date: ev.ClaimSubmittedAt ?? DateTime.UtcNow,
                description: $"Remboursement assurance — {ev.ClaimNumber ?? ev.ReferenceCode ?? $"#{ev.Id}"}",
                ct: ct);
        }

        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static string? NormaliseClaimStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var lc = raw.Trim().ToLowerInvariant();
        return lc switch
        {
            "pending" or "approved" or "partial" or "rejected" or "closed" => lc,
            _ => null,
        };
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Third parties (other vehicles / parties involved)
// ─────────────────────────────────────────────────────────────────────────────

public record AddThirdPartyCommand(
    int AccidentEventId,
    string? Name,
    string? Phone,
    string? VehiclePlate,
    string? VehicleModel,
    string? InsuranceCompany,
    string? InsuranceNumber
) : IRequest<int>;

public class AddThirdPartyCommandHandler : PhaseCommandHandlerBase, IRequestHandler<AddThirdPartyCommand, int>
{
    public AddThirdPartyCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<AddThirdPartyCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<int> Handle(AddThirdPartyCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var tp = new AccidentEventThirdParty
        {
            AccidentEventId = ev.Id,
            Name = request.Name?.Trim(),
            Phone = request.Phone?.Trim(),
            VehiclePlate = request.VehiclePlate?.Trim(),
            VehicleModel = request.VehicleModel?.Trim(),
            InsuranceCompany = request.InsuranceCompany?.Trim(),
            InsuranceNumber = request.InsuranceNumber?.Trim(),
        };
        Context.AccidentEventThirdParties.Add(tp);
        ev.ThirdPartyInvolved = true;
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return tp.Id;
    }
}

public record DeleteThirdPartyCommand(int AccidentEventId, int ThirdPartyId) : IRequest<Unit>;

public class DeleteThirdPartyCommandHandler : PhaseCommandHandlerBase, IRequestHandler<DeleteThirdPartyCommand, Unit>
{
    public DeleteThirdPartyCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<DeleteThirdPartyCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(DeleteThirdPartyCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var tp = await Context.AccidentEventThirdParties
            .FirstOrDefaultAsync(t => t.Id == request.ThirdPartyId && t.AccidentEventId == ev.Id, ct);
        if (tp == null) return Unit.Value; // idempotent
        Context.AccidentEventThirdParties.Remove(tp);
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Documents (multi-file attachments typed by phase)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Calypso 7 — registers an already-uploaded file URL on the accident.
/// The controller takes care of writing the file to disk; this handler
/// only persists the metadata so the file is listed under the right
/// phase tab in the timeline UI.
/// </summary>
public record AddAccidentDocumentCommand(
    int AccidentEventId,
    string DocumentType,
    string FileName,
    string FileUrl,
    int? FileSize,
    string? MimeType
) : IRequest<int>;

public class AddAccidentDocumentCommandHandler : PhaseCommandHandlerBase, IRequestHandler<AddAccidentDocumentCommand, int>
{
    public AddAccidentDocumentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<AddAccidentDocumentCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<int> Handle(AddAccidentDocumentCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var doc = new AccidentEventDocument
        {
            AccidentEventId = ev.Id,
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? "other" : request.DocumentType.Trim(),
            FileName = request.FileName,
            FileUrl = request.FileUrl,
            FileSize = request.FileSize,
            MimeType = request.MimeType,
            UploadedByUserId = TenantService.UserId,
            UploadedAt = DateTime.UtcNow,
        };
        Context.AccidentEventDocuments.Add(doc);
        ev.UpdatedAt = DateTime.UtcNow;
        await Context.SaveChangesAsync(ct);
        return doc.Id;
    }
}

public record DeleteAccidentDocumentCommand(int AccidentEventId, int DocumentId) : IRequest<Unit>;

public class DeleteAccidentDocumentCommandHandler : PhaseCommandHandlerBase, IRequestHandler<DeleteAccidentDocumentCommand, Unit>
{
    public DeleteAccidentDocumentCommandHandler(IGisDbContext context, ICurrentTenantService tenantService, ILogger<DeleteAccidentDocumentCommandHandler> logger)
        : base(context, tenantService, logger) { }

    public async Task<Unit> Handle(DeleteAccidentDocumentCommand request, CancellationToken ct)
    {
        var ev = await LoadConfirmedAsync(request.AccidentEventId, ct);
        var doc = await Context.AccidentEventDocuments
            .FirstOrDefaultAsync(d => d.Id == request.DocumentId && d.AccidentEventId == ev.Id, ct);
        if (doc == null) return Unit.Value;
        Context.AccidentEventDocuments.Remove(doc);
        await Context.SaveChangesAsync(ct);
        return Unit.Value;
    }
}

// =============================================================================
//  Shared base class — tenant-aware loading + VehicleCost upsert
// =============================================================================

public abstract class PhaseCommandHandlerBase
{
    protected IGisDbContext Context { get; }
    protected ICurrentTenantService TenantService { get; }
    protected ILogger Logger { get; }

    protected PhaseCommandHandlerBase(IGisDbContext context, ICurrentTenantService tenantService, ILogger logger)
    {
        Context = context;
        TenantService = tenantService;
        Logger = logger;
    }

    /// <summary>
    /// Loads an accident row scoped to the caller's tenant and asserts
    /// it is in <c>"confirmed"</c> status. Pending / dismissed rows
    /// reject — phase data only makes sense after confirmation.
    /// </summary>
    protected async Task<AccidentEvent> LoadConfirmedAsync(int accidentEventId, CancellationToken ct)
    {
        var companyId = TenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");
        var ev = await Context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == accidentEventId && e.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AccidentEvent", accidentEventId);
        if (ev.Status is not "confirmed")
            throw new DomainException(
                $"Impossible de modifier les phases: l'accident est en statut '{ev.Status}'. Confirmez-le d'abord.");
        return ev;
    }

    /// <summary>
    /// Idempotent upsert of a <see cref="VehicleCost"/> row linked to
    /// an accident. Looks up an existing row by
    /// <c>(VehicleId, AccidentEventId, Type)</c> — if present, updates
    /// amount/date/description; otherwise inserts a new one.
    /// </summary>
    protected async Task UpsertVehicleCostAsync(
        AccidentEvent ev,
        string type,
        decimal amount,
        DateTime date,
        string description,
        CancellationToken ct)
    {
        if (!ev.VehicleId.HasValue) return;

        var existing = await Context.VehicleCosts
            .FirstOrDefaultAsync(c => c.AccidentEventId == ev.Id
                                   && c.Type == type
                                   && c.VehicleId == ev.VehicleId.Value, ct);

        if (existing != null)
        {
            existing.Amount = amount;
            existing.Date = date;
            existing.Description = description;
            Logger.LogDebug(
                "Phase auto-sync: updated VehicleCost {Cost} for accident {Accident} (type={Type}, amount={Amount})",
                existing.Id, ev.Id, type, amount);
            return;
        }

        var newCost = new VehicleCost
        {
            VehicleId = ev.VehicleId.Value,
            CompanyId = ev.CompanyId,
            AccidentEventId = ev.Id,
            Type = type,
            Description = description,
            Amount = amount,
            Date = date,
            CreatedByUserId = TenantService.UserId,
            CreatedAt = DateTime.UtcNow,
        };
        Context.VehicleCosts.Add(newCost);
        Logger.LogInformation(
            "Phase auto-sync: inserted VehicleCost for accident {Accident} (type={Type}, amount={Amount})",
            ev.Id, type, amount);
    }

    protected static string? NormaliseSeverity(string? raw)
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
