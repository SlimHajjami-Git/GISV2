using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DrivingBehaviorController : ControllerBase
{
    private readonly IDrivingBehaviorService _drivingBehaviorService;
    private readonly ILogger<DrivingBehaviorController> _logger;
    private readonly IGisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public DrivingBehaviorController(
        IDrivingBehaviorService drivingBehaviorService,
        ILogger<DrivingBehaviorController> logger,
        IGisDbContext context,
        ICurrentTenantService tenantService)
    {
        _drivingBehaviorService = drivingBehaviorService;
        _logger = logger;
        _context = context;
        _tenantService = tenantService;
    }

    /// <summary>
    /// Le véhicule demandé est-il hors d'atteinte de l'appelant ? DEUX contrôles, et
    /// les deux sont indispensables :
    ///
    ///  1. LA SOCIÉTÉ, explicitement. <c>DrivingBehaviorService</c> ouvre une portée DI
    ///     NEUVE (<c>CreateScope()</c>) dont le <c>CurrentTenantService</c> n'est jamais
    ///     renseigné : son <c>CompanyId</c> vaut null, ce qui DÉSACTIVE le filtre global
    ///     de <c>GisDbContext</c>. Ces trois routes rendaient donc les événements de
    ///     conduite de n'importe quel véhicule de N'IMPORTE QUELLE société. Le contrôle
    ///     doit se faire ICI, dans la portée de la requête, qui porte le tenant.
    ///
    ///  2. LA PORTÉE VÉHICULES. TROIS états : <c>null</c> = administrateur, aucun filtre ;
    ///     liste non vide = ses véhicules ; liste VIDE = il ne voit RIEN. Chez un loueur,
    ///     le filtre société ne cloisonne rien entre locataires.
    ///
    /// Le refus est un 404 identique à celui d'un véhicule inexistant.
    /// </summary>
    private async Task<bool> HorsPorteeAsync(int vehicleId)
    {
        var companyId = _tenantService.CompanyId ?? 0;

        var existe = await _context.Vehicles
            .AsNoTracking()
            .AnyAsync(v => v.Id == vehicleId && v.CompanyId == companyId, HttpContext.RequestAborted);

        if (!existe) return true;

        return !await VehicleScope.CanAccessVehicleAsync(_context, _tenantService, vehicleId, HttpContext.RequestAborted);
    }

    /// <summary>
    /// Get driving score for a vehicle
    /// </summary>
    [HttpGet("score/{vehicleId}")]
    public async Task<ActionResult<DrivingScore>> GetDrivingScore(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        if (await HorsPorteeAsync(vehicleId)) return NotFound();

        var start = startDate ?? DateTime.UtcNow.AddDays(-30);
        var end = endDate ?? DateTime.UtcNow;

        var score = await _drivingBehaviorService.CalculateDrivingScoreAsync(vehicleId, start, end);
        return Ok(score);
    }

    /// <summary>
    /// Get driving events for a vehicle
    /// </summary>
    [HttpGet("events/{vehicleId}")]
    public async Task<ActionResult<List<DrivingEvent>>> GetDrivingEvents(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        if (await HorsPorteeAsync(vehicleId)) return NotFound();

        var start = startDate ?? DateTime.UtcNow.AddDays(-7);
        var end = endDate ?? DateTime.UtcNow;

        var events = await _drivingBehaviorService.GetDrivingEventsAsync(vehicleId, start, end);
        return Ok(events);
    }

    /// <summary>
    /// Get driving summary with statistics
    /// </summary>
    [HttpGet("summary/{vehicleId}")]
    public async Task<ActionResult<DrivingSummary>> GetDrivingSummary(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        if (await HorsPorteeAsync(vehicleId)) return NotFound();

        var start = startDate ?? DateTime.UtcNow.AddDays(-30);
        var end = endDate ?? DateTime.UtcNow;

        var score = await _drivingBehaviorService.CalculateDrivingScoreAsync(vehicleId, start, end);
        var events = await _drivingBehaviorService.GetDrivingEventsAsync(vehicleId, start, end);

        var summary = new DrivingSummary
        {
            VehicleId = vehicleId,
            Period = new PeriodInfo { StartDate = start, EndDate = end },
            Score = score,
            EventsByType = events
                .GroupBy(e => e.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            EventsBySeverity = events
                .GroupBy(e => e.Severity)
                .ToDictionary(g => g.Key.ToString(), g => g.Count()),
            RecentEvents = events.Take(10).ToList(),
            DailyScores = await CalculateDailyScoresAsync(vehicleId, start, end)
        };

        return Ok(summary);
    }

    private async Task<List<DailyScore>> CalculateDailyScoresAsync(int vehicleId, DateTime start, DateTime end)
    {
        var dailyScores = new List<DailyScore>();
        var currentDate = start.Date;

        while (currentDate <= end.Date)
        {
            var dayStart = currentDate;
            var dayEnd = currentDate.AddDays(1).AddSeconds(-1);
            
            var score = await _drivingBehaviorService.CalculateDrivingScoreAsync(vehicleId, dayStart, dayEnd);
            
            dailyScores.Add(new DailyScore
            {
                Date = currentDate,
                Score = score.Score,
                EventCount = score.TotalEvents
            });

            currentDate = currentDate.AddDays(1);
        }

        return dailyScores;
    }
}

public class DrivingSummary
{
    public int VehicleId { get; set; }
    public PeriodInfo Period { get; set; } = new();
    public DrivingScore Score { get; set; } = new();
    public Dictionary<string, int> EventsByType { get; set; } = new();
    public Dictionary<string, int> EventsBySeverity { get; set; } = new();
    public List<DrivingEvent> RecentEvents { get; set; } = new();
    public List<DailyScore> DailyScores { get; set; } = new();
}

public class PeriodInfo
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class DailyScore
{
    public DateTime Date { get; set; }
    public double Score { get; set; }
    public int EventCount { get; set; }
}
