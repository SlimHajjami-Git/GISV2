using GisAPI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Services;

/// <summary>
/// Bounds disk growth from the invoice-scan feature. A scanned invoice is stored
/// under <c>uploads/invoices/{companyId}/{guid}{ext}</c> the moment it is
/// uploaded, BEFORE the user decides to save a cost — so abandoned reviews (and
/// costs later deleted) leave files no <see cref="Domain.Entities.VehicleCost.ReceiptUrl"/>
/// points at. This service periodically deletes such files once they are older
/// than the grace period. Files still referenced by a cost are always kept.
/// </summary>
public class InvoiceOrphanCleanupService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<InvoiceOrphanCleanupService> _logger;

    public InvoiceOrphanCleanupService(IServiceScopeFactory scopeFactory, IWebHostEnvironment env,
        ILogger<InvoiceOrphanCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _env = env;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Small startup delay so it doesn't compete with migrations/seed.
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await SweepAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Invoice orphan sweep failed"); }

            try { await Task.Delay(Interval, stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var root = Path.Combine(_env.ContentRootPath, "uploads", "invoices");
        if (!Directory.Exists(root)) return;

        // URLs still referenced by a cost — these files must never be deleted.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GisDbContext>();
        var referenced = await db.VehicleCosts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.ReceiptUrl != null && c.ReceiptUrl.Contains("/uploads/invoices/"))
            .Select(c => c.ReceiptUrl!)
            .ToListAsync(ct);
        var keep = new HashSet<string>(referenced, StringComparer.OrdinalIgnoreCase);

        var cutoff = DateTime.UtcNow - GracePeriod;
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) > cutoff) continue; // within grace window

            // Reconstruct the public URL this file would have: /uploads/invoices/<company>/<name>
            var rel = "/uploads/" + Path.GetRelativePath(Path.Combine(_env.ContentRootPath, "uploads"), file)
                .Replace(Path.DirectorySeparatorChar, '/');
            if (keep.Contains(rel)) continue;

            try { File.Delete(file); deleted++; }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete orphan invoice file {File}", file); }
        }

        if (deleted > 0)
            _logger.LogInformation("Invoice orphan sweep: deleted {Count} unreferenced file(s)", deleted);
    }
}
