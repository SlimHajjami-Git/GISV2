using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GisAPI.Services;

namespace GisAPI.Controllers;

/// <summary>
/// Sauvegardes et purge de l'historique. Sous /api/admin → verrouillé sys_admin
/// par PermissionMiddleware. La purge exige une phrase de confirmation exacte et
/// crée toujours un backup de sécurité avant de supprimer.
/// </summary>
[ApiController]
[Route("api/admin/database")]
[Authorize]
public class DatabaseMaintenanceController : ControllerBase
{
    private const string ConfirmPhrase = "SUPPRIMER";
    private readonly DatabaseMaintenanceService _svc;
    private readonly ILogger<DatabaseMaintenanceController> _logger;

    public DatabaseMaintenanceController(DatabaseMaintenanceService svc, ILogger<DatabaseMaintenanceController> logger)
    {
        _svc = svc;
        _logger = logger;
    }

    /// <summary>Tables purgeables (pour peupler l'UI) + phrase de confirmation.</summary>
    [HttpGet("purgeable-tables")]
    public IActionResult PurgeableTables()
        => Ok(new
        {
            confirmPhrase = ConfirmPhrase,
            tables = DatabaseMaintenanceService.PurgeableTables
                .Select(t => new { t.Table, t.Label }).ToList()
        });

    [HttpGet("backups")]
    public IActionResult ListBackups() => Ok(_svc.ListBackups());

    /// <summary>Taille base + poids des backups + espace disque total/restant.</summary>
    [HttpGet("storage")]
    public async Task<IActionResult> Storage(CancellationToken ct)
        => Ok(await _svc.GetStorageAsync(ct));

    [HttpPost("backups")]
    public async Task<IActionResult> CreateBackup(CancellationToken ct)
    {
        try
        {
            var info = await _svc.CreateBackupAsync("manual", ct);
            return Ok(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Création backup manuel échouée");
            return StatusCode(500, new { message = "La sauvegarde a échoué : " + ex.Message });
        }
    }

    [HttpGet("backups/{name}/download")]
    public IActionResult Download(string name)
    {
        var path = _svc.ResolveBackupPath(name);
        if (path == null) return NotFound(new { message = "Sauvegarde introuvable." });
        var stream = System.IO.File.OpenRead(path);
        return File(stream, "application/octet-stream", name);
    }

    [HttpDelete("backups/{name}")]
    public IActionResult DeleteBackup(string name)
        => _svc.DeleteBackup(name)
            ? Ok(new { message = "Sauvegarde supprimée." })
            : NotFound(new { message = "Sauvegarde introuvable." });

    public record PurgeRequest(int Months, List<string> Tables, string? Confirm);

    [HttpPost("purge/preview")]
    public async Task<IActionResult> PurgePreview([FromBody] PurgeRequest req, CancellationToken ct)
    {
        if (req.Months < 1) return BadRequest(new { message = "La borne doit être d'au moins 1 mois." });
        var rows = await _svc.PreviewPurgeAsync(req.Months, req.Tables ?? new(), ct);
        return Ok(new { cutoffMonths = req.Months, rows });
    }

    [HttpPost("purge")]
    public async Task<IActionResult> Purge([FromBody] PurgeRequest req, CancellationToken ct)
    {
        if (req.Confirm != ConfirmPhrase)
            return BadRequest(new { message = $"Confirmation invalide. Tapez exactement « {ConfirmPhrase} »." });
        if (req.Months < 1)
            return BadRequest(new { message = "La borne doit être d'au moins 1 mois." });
        if (req.Tables == null || req.Tables.Count == 0)
            return BadRequest(new { message = "Sélectionnez au moins une table à purger." });

        // Garde-fou absolu : backup de sécurité AVANT toute suppression.
        BackupInfo safety;
        try
        {
            safety = await _svc.CreateBackupAsync("presuppr", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup de sécurité pré-purge échoué — PURGE ANNULÉE");
            return StatusCode(500, new { message = "Le backup de sécurité a échoué — purge annulée. Aucune donnée supprimée. " + ex.Message });
        }

        var user = User.Identity?.Name ?? "sys_admin";
        _logger.LogWarning("PURGE lancée par {User} : {Months} mois, tables {Tables}, backup {Backup}",
            user, req.Months, string.Join(",", req.Tables), safety.Name);

        var deleted = await _svc.PurgeAsync(req.Months, req.Tables, ct);
        return Ok(new { safetyBackup = safety.Name, deleted });
    }
}
