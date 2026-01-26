using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using MediatR;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using GisAPI.Application.Features.Reports.Queries.GetDailyActivityReport;
using GisAPI.Application.Features.Reports.Queries.GetMileageReport;
using GisAPI.Application.Features.Reports.Queries.GetMonthlyFleetReport;
using GisAPI.Application.Features.Reports.Queries.GetMileagePeriodReport;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly IMediator _mediator;
    
    private const double DefaultStopSpeedThresholdKph = 3.0;
    private const int DefaultMinStopDurationSeconds = 120;

    public ReportsController(GisDbContext context, IMediator mediator)
    {
        _context = context;
        _mediator = mediator;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

    [HttpGet]
    public async Task<ActionResult<List<Report>>> GetReports([FromQuery] int limit = 50)
    {
        var companyId = GetCompanyId();

        var reports = await _context.Reports
            .Where(r => r.CompanyId == companyId)
            .Include(r => r.CreatedByUser)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync();

        return Ok(reports);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Report>> GetReport(int id)
    {
        var companyId = GetCompanyId();

        var report = await _context.Reports
            .Where(r => r.Id == id && r.CompanyId == companyId)
            .Include(r => r.CreatedByUser)
            .FirstOrDefaultAsync();

        if (report == null)
            return NotFound();

        return Ok(report);
    }

    [HttpPost]
    public async Task<ActionResult<Report>> CreateReport([FromBody] CreateReportRequest request)
    {
        var companyId = GetCompanyId();
        var userId = GetUserId();

        var report = new Report
        {
            CompanyId = companyId,
            CreatedByUserId = userId,
            Name = request.Name,
            Type = request.Type,
            Description = request.Description,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            VehicleIds = request.VehicleIds,
            DriverIds = request.DriverIds,
            Format = request.Format ?? "pdf",
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.Reports.Add(report);
        await _context.SaveChangesAsync();

        // TODO: Queue report generation job

        return CreatedAtAction(nameof(GetReport), new { id = report.Id }, report);
    }

    [HttpPost("{id}/generate")]
    public async Task<ActionResult> GenerateReport(int id)
    {
        var companyId = GetCompanyId();

        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

        if (report == null)
            return NotFound();

        report.Status = "generating";
        await _context.SaveChangesAsync();

        // TODO: Trigger actual report generation
        // For now, simulate completion
        report.Status = "completed";
        report.GeneratedAt = DateTime.UtcNow;
        report.FileUrl = $"/reports/{report.Id}.{report.Format}";
        await _context.SaveChangesAsync();

        return Ok(new { message = "Report generation started", reportId = report.Id });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteReport(int id)
    {
        var companyId = GetCompanyId();

        var report = await _context.Reports
            .FirstOrDefaultAsync(r => r.Id == id && r.CompanyId == companyId);

        if (report == null)
            return NotFound();

        _context.Reports.Remove(report);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // Report Schedules

    [HttpGet("schedules")]
    public async Task<ActionResult<List<ReportSchedule>>> GetSchedules()
    {
        var companyId = GetCompanyId();

        var schedules = await _context.ReportSchedules
            .Where(s => s.CompanyId == companyId)
            .Include(s => s.CreatedByUser)
            .OrderBy(s => s.Name)
            .ToListAsync();

        return Ok(schedules);
    }

    [HttpPost("schedules")]
    public async Task<ActionResult<ReportSchedule>> CreateSchedule([FromBody] ReportSchedule schedule)
    {
        var companyId = GetCompanyId();
        var userId = GetUserId();

        schedule.CompanyId = companyId;
        schedule.CreatedByUserId = userId;
        schedule.CreatedAt = DateTime.UtcNow;
        schedule.NextRunAt = CalculateNextRun(schedule);

        _context.ReportSchedules.Add(schedule);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetSchedules), new { id = schedule.Id }, schedule);
    }

    [HttpPut("schedules/{id}")]
    public async Task<ActionResult> UpdateSchedule(int id, [FromBody] ReportSchedule updated)
    {
        var companyId = GetCompanyId();

        var schedule = await _context.ReportSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);

        if (schedule == null)
            return NotFound();

        schedule.Name = updated.Name;
        schedule.ReportType = updated.ReportType;
        schedule.Frequency = updated.Frequency;
        schedule.DayOfWeek = updated.DayOfWeek;
        schedule.DayOfMonth = updated.DayOfMonth;
        schedule.TimeOfDay = updated.TimeOfDay;
        schedule.EmailRecipients = updated.EmailRecipients;
        schedule.VehicleIds = updated.VehicleIds;
        schedule.DriverIds = updated.DriverIds;
        schedule.Format = updated.Format;
        schedule.IsActive = updated.IsActive;
        schedule.UpdatedAt = DateTime.UtcNow;
        schedule.NextRunAt = CalculateNextRun(schedule);

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("schedules/{id}")]
    public async Task<ActionResult> DeleteSchedule(int id)
    {
        var companyId = GetCompanyId();

        var schedule = await _context.ReportSchedules
            .FirstOrDefaultAsync(s => s.Id == id && s.CompanyId == companyId);

        if (schedule == null)
            return NotFound();

        _context.ReportSchedules.Remove(schedule);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private DateTime? CalculateNextRun(ReportSchedule schedule)
    {
        if (!schedule.IsActive)
            return null;

        var now = DateTime.UtcNow;
        var time = TimeSpan.Parse(schedule.TimeOfDay);

        return schedule.Frequency switch
        {
            "daily" => now.Date.Add(time) > now ? now.Date.Add(time) : now.Date.AddDays(1).Add(time),
            "weekly" when schedule.DayOfWeek.HasValue => GetNextWeekday(now, (DayOfWeek)schedule.DayOfWeek.Value, time),
            "monthly" when schedule.DayOfMonth.HasValue => GetNextMonthDay(now, schedule.DayOfMonth.Value, time),
            _ => null
        };
    }

    private DateTime GetNextWeekday(DateTime now, DayOfWeek targetDay, TimeSpan time)
    {
        var daysUntil = ((int)targetDay - (int)now.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && now.TimeOfDay >= time)
            daysUntil = 7;
        return now.Date.AddDays(daysUntil).Add(time);
    }

    private DateTime GetNextMonthDay(DateTime now, int day, TimeSpan time)
    {
        var targetDate = new DateTime(now.Year, now.Month, Math.Min(day, DateTime.DaysInMonth(now.Year, now.Month)));
        if (targetDate.Add(time) <= now)
            targetDate = targetDate.AddMonths(1);
        return targetDate.Add(time);
    }

    // ==================== DAILY ACTIVITY REPORT ====================

    /// <summary>
    /// Get daily activity report for a vehicle
    /// Tracks ignition events, stops, drives, and durations throughout the day
    /// </summary>
    [HttpGet("daily/{vehicleId}")]
    public async Task<ActionResult<DailyActivityReportDto>> GetDailyReport(
        int vehicleId,
        [FromQuery] DateTime? date = null,
        [FromQuery] int minStopDurationSeconds = DefaultMinStopDurationSeconds,
        [FromQuery] double stopSpeedThresholdKph = DefaultStopSpeedThresholdKph)
    {
        var companyId = GetCompanyId();
        var reportDate = date?.Date ?? DateTime.UtcNow.Date;

        // Verify vehicle belongs to company
        var vehicleExists = await _context.Vehicles
            .AnyAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (!vehicleExists)
            return NotFound(new { message = "Vehicle not found" });

        var result = await _mediator.Send(new GetDailyActivityReportQuery(
            vehicleId,
            reportDate,
            minStopDurationSeconds,
            stopSpeedThresholdKph));

        return Ok(result);
    }

    /// <summary>
    /// Get daily reports for multiple vehicles
    /// </summary>
    [HttpGet("daily")]
    public async Task<ActionResult<List<DailyActivityReportDto>>> GetDailyReports(
        [FromQuery] DateTime? date = null,
        [FromQuery] int[]? vehicleIds = null,
        [FromQuery] int minStopDurationSeconds = DefaultMinStopDurationSeconds,
        [FromQuery] double stopSpeedThresholdKph = DefaultStopSpeedThresholdKph)
    {
        var reportDate = date?.Date ?? DateTime.UtcNow.Date;

        var result = await _mediator.Send(new GetDailyActivityReportsQuery(
            reportDate,
            vehicleIds,
            minStopDurationSeconds,
            stopSpeedThresholdKph));

        return Ok(result);
    }

    // ==================== MILEAGE REPORT ====================

    /// <summary>
    /// Get mileage report for a vehicle
    /// Tracks distance traveled, odometer readings, and provides daily/weekly/monthly breakdowns
    /// </summary>
    [HttpGet("mileage/{vehicleId}")]
    public async Task<ActionResult<MileageReportDto>> GetMileageReport(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var companyId = GetCompanyId();

        // Verify vehicle belongs to company
        var vehicleExists = await _context.Vehicles
            .AnyAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (!vehicleExists)
            return NotFound(new { message = "Vehicle not found" });

        var start = startDate?.Date ?? DateTime.UtcNow.Date.AddDays(-30);
        var end = endDate?.Date ?? DateTime.UtcNow.Date;

        var result = await _mediator.Send(new GetMileageReportQuery(vehicleId, start, end));

        return Ok(result);
    }

    /// <summary>
    /// Get mileage reports for multiple vehicles
    /// </summary>
    [HttpGet("mileage")]
    public async Task<ActionResult<List<MileageReportDto>>> GetMileageReports(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int[]? vehicleIds = null)
    {
        var start = startDate?.Date ?? DateTime.UtcNow.Date.AddDays(-30);
        var end = endDate?.Date ?? DateTime.UtcNow.Date;

        var result = await _mediator.Send(new GetMileageReportsQuery(start, end, vehicleIds));

        return Ok(result);
    }

    // ==================== MONTHLY FLEET REPORT ====================

    /// <summary>
    /// Get comprehensive monthly fleet report with analytics, charts, and KPIs
    /// Includes: fleet overview, utilization, fuel analytics, maintenance, driver performance,
    /// operational efficiency, cost analysis, MoM/YoY comparisons, alerts, and chart data
    /// </summary>
    [HttpGet("monthly")]
    public async Task<ActionResult<MonthlyFleetReportDto>> GetMonthlyFleetReport(
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] int? vehicleTypeFilter = null,
        [FromQuery] int? departmentFilter = null,
        [FromQuery] int[]? vehicleIds = null)
    {
        var reportYear = year ?? DateTime.UtcNow.Year;
        var reportMonth = month ?? DateTime.UtcNow.Month;

        // If current month, use previous month to have complete data
        if (year == null && month == null)
        {
            var lastMonth = DateTime.UtcNow.AddMonths(-1);
            reportYear = lastMonth.Year;
            reportMonth = lastMonth.Month;
        }

        var result = await _mediator.Send(new GetMonthlyFleetReportQuery(
            reportYear,
            reportMonth,
            vehicleTypeFilter,
            departmentFilter,
            vehicleIds));

        return Ok(result);
    }

    // ==================== MILEAGE PERIOD REPORT (Hour/Day/Month) ====================

    /// <summary>
    /// Get mileage report broken down by period (Hour, Day, or Month)
    /// - Hour: 24-hour breakdown for a specific date
    /// - Day: Daily breakdown between two dates
    /// - Month: Monthly breakdown for date range
    /// </summary>
    [HttpGet("mileage-period/{vehicleId}")]
    public async Task<ActionResult<MileagePeriodReportDto>> GetMileagePeriodReport(
        int vehicleId,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string periodType = "day")
    {
        var companyId = GetCompanyId();

        // Verify vehicle belongs to company
        var vehicleExists = await _context.Vehicles
            .AnyAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (!vehicleExists)
            return NotFound(new { message = "Vehicle not found" });

        // Parse period type
        var period = periodType.ToLower() switch
        {
            "hour" => MileagePeriodType.Hour,
            "day" => MileagePeriodType.Day,
            "month" => MileagePeriodType.Month,
            _ => MileagePeriodType.Day
        };

        // Set default dates based on period type
        var start = startDate?.Date ?? DateTime.UtcNow.Date;
        var end = endDate?.Date ?? DateTime.UtcNow.Date;

        // For hourly report, use same day if no end date specified
        if (period == MileagePeriodType.Hour && endDate == null)
        {
            end = start;
        }
        // For daily report, default to last 30 days
        else if (period == MileagePeriodType.Day && startDate == null)
        {
            start = DateTime.UtcNow.Date.AddDays(-30);
        }
        // For monthly report, default to last 12 months
        else if (period == MileagePeriodType.Month && startDate == null)
        {
            start = DateTime.UtcNow.Date.AddMonths(-12);
        }

        var result = await _mediator.Send(new GetMileagePeriodReportQuery(vehicleId, start, end, period));

        return Ok(result);
    }
}

public record CreateReportRequest(
    string Name,
    string Type,
    string? Description,
    DateTime? StartDate,
    DateTime? EndDate,
    int[]? VehicleIds,
    int[]? DriverIds,
    string? Format
);
