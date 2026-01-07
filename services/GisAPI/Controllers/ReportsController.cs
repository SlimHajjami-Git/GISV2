using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly GisDbContext _context;

    public ReportsController(GisDbContext context)
    {
        _context = context;
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
