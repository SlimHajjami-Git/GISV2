using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Exceptions;
using GisAPI.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GisAPI.Application.Features.AccidentEvents.Commands;

/// <summary>
/// Calypso 6 (P9) — stores the URL of the PDF report attached to an
/// <c>AccidentEvent</c>. The PDF itself is written to disk by the
/// controller (under <c>uploads/accident-reports/{id}/{guid}.pdf</c>)
/// and the resulting public URL (e.g. <c>/uploads/accident-reports/42/abc.pdf</c>)
/// is what gets persisted here.
///
/// <para>Two callers:</para>
/// <list type="bullet">
///   <item><description><b>Auto path</b> — invoked right after
///     <see cref="ConfirmAccidentCommand"/>: the frontend renders the
///     report PDF with jsPDF and uploads it. The row is in
///     <c>awaiting_details</c> at that point.</description></item>
///   <item><description><b>Manual path</b> — called from
///     <see cref="CreateManualAccidentCommand"/> when the admin uploads
///     an external (e.g. insurance expert) PDF for an accident the system
///     missed. The row may already be <c>confirmed</c> and the PDF
///     replaces any previously attached one.</description></item>
/// </list>
///
/// <para>Tenant-scoped. Idempotent: re-uploading replaces the URL.</para>
/// </summary>
public record AttachAccidentPdfCommand(int AccidentEventId, string PdfReportUrl) : IRequest<Unit>;

public class AttachAccidentPdfCommandHandler : IRequestHandler<AttachAccidentPdfCommand, Unit>
{
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<AttachAccidentPdfCommandHandler> _logger;

    public AttachAccidentPdfCommandHandler(
        IGisDbContext context,
        ICurrentTenantService tenantService,
        ILogger<AttachAccidentPdfCommandHandler> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    public async Task<Unit> Handle(AttachAccidentPdfCommand request, CancellationToken ct)
    {
        var companyId = _tenantService.CompanyId
            ?? throw new DomainException("Société non identifiée");

        if (string.IsNullOrWhiteSpace(request.PdfReportUrl))
            throw new DomainException("URL du rapport PDF requise");

        var ev = await _context.AccidentEvents
            .FirstOrDefaultAsync(e => e.Id == request.AccidentEventId && e.CompanyId == companyId, ct)
            ?? throw new NotFoundException("AccidentEvent", request.AccidentEventId);

        ev.PdfReportUrl = request.PdfReportUrl;
        ev.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AttachAccidentPdfCommand: accident {AccidentId} now has PDF at {Url}",
            ev.Id, request.PdfReportUrl);

        return Unit.Value;
    }
}
