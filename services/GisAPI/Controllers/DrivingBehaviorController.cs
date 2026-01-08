using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public DrivingBehaviorController(
        IDrivingBehaviorService drivingBehaviorService,
        ILogger<DrivingBehaviorController> logger)
    {
        _drivingBehaviorService = drivingBehaviorService;
        _logger = logger;
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
