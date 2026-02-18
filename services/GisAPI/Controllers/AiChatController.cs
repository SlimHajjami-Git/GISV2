using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/ai-chat")]
[Authorize]
public class AiChatController : ControllerBase
{
    private readonly IGisDbContext _context;
    private readonly ILlmService _llmService;
    private readonly ILogger<AiChatController> _logger;

    public AiChatController(IGisDbContext context, ILlmService llmService, ILogger<AiChatController> logger)
    {
        _context = context;
        _llmService = llmService;
        _logger = logger;
    }

    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    /// <summary>
    /// Send a message to the AI diagnostic assistant for a specific vehicle
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] AiChatRequest request)
    {
        var userId = GetUserId();
        var companyId = GetCompanyId();

        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { message = "Le message ne peut pas être vide" });

        // Load vehicle with full context
        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == request.VehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound(new { message = "Véhicule introuvable" });

        // Build vehicle diagnostic context
        var vehicleContext = await BuildVehicleContext(vehicle, companyId);

        // Build system prompt
        var systemPrompt = BuildSystemPrompt(vehicleContext);

        // Load conversation history for this session (last 10 messages)
        var history = await _context.AiChatMessages
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.VehicleId == request.VehicleId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(10)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new LlmMessage(m.Role, m.Content))
            .ToListAsync();

        // Add the new user message
        history.Add(new LlmMessage("user", request.Message));

        // Save user message
        var userMsg = new AiChatMessage
        {
            CompanyId = companyId,
            UserId = userId,
            VehicleId = request.VehicleId,
            Role = "user",
            Content = request.Message
        };
        _context.AiChatMessages.Add(userMsg);
        await _context.SaveChangesAsync();

        // Call LLM
        try
        {
            var llmResponse = await _llmService.ChatAsync(systemPrompt, history);

            // Save assistant response
            var assistantMsg = new AiChatMessage
            {
                CompanyId = companyId,
                UserId = userId,
                VehicleId = request.VehicleId,
                Role = "assistant",
                Content = llmResponse.Content,
                TokensUsed = llmResponse.TokensUsed
            };
            _context.AiChatMessages.Add(assistantMsg);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = llmResponse.Content,
                tokensUsed = llmResponse.TokensUsed,
                messageId = assistantMsg.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI chat error for vehicle {VehicleId}", request.VehicleId);
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Get AI chat history for a specific vehicle
    /// </summary>
    [HttpGet("history/{vehicleId}")]
    public async Task<IActionResult> GetHistory(int vehicleId, [FromQuery] int limit = 50)
    {
        var userId = GetUserId();

        var messages = await _context.AiChatMessages
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.VehicleId == vehicleId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .Select(m => new
            {
                m.Id,
                m.Role,
                m.Content,
                m.TokensUsed,
                Timestamp = m.CreatedAt
            })
            .ToListAsync();

        messages.Reverse();
        return Ok(messages);
    }

    /// <summary>
    /// Clear AI chat history for a vehicle
    /// </summary>
    [HttpDelete("history/{vehicleId}")]
    public async Task<IActionResult> ClearHistory(int vehicleId)
    {
        var userId = GetUserId();

        var messages = await _context.AiChatMessages
            .Where(m => m.UserId == userId && m.VehicleId == vehicleId)
            .ToListAsync();

        if (messages.Count > 0)
        {
            _context.AiChatMessages.RemoveRange(messages);
            await _context.SaveChangesAsync();
        }

        return Ok(new { deleted = messages.Count });
    }

    /// <summary>
    /// Get vehicles available for AI diagnostic chat
    /// </summary>
    [HttpGet("vehicles")]
    public async Task<IActionResult> GetVehicles()
    {
        var companyId = GetCompanyId();

        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .Select(v => new
            {
                v.Id,
                v.Name,
                Plate = v.Plate,
                v.Brand,
                v.Model,
                v.Type,
                v.Mileage,
                v.Status
            })
            .OrderBy(v => v.Name)
            .ToListAsync();

        return Ok(vehicles);
    }

    // ═══════ VEHICLE CONTEXT BUILDER ═══════

    private async Task<VehicleDiagnosticContext> BuildVehicleContext(Vehicle vehicle, int companyId)
    {
        var ctx = new VehicleDiagnosticContext
        {
            Name = vehicle.Name,
            Brand = vehicle.Brand,
            Model = vehicle.Model,
            Type = vehicle.Type,
            Year = vehicle.Year,
            FuelType = vehicle.FuelType,
            Mileage = vehicle.Mileage,
            Plate = vehicle.Plate,
            Status = vehicle.Status,
            InsuranceExpiry = vehicle.InsuranceExpiry,
            TechnicalInspectionExpiry = vehicle.TechnicalInspectionExpiry
        };

        // Recent maintenance records (last 10)
        ctx.RecentMaintenance = await _context.MaintenanceRecords
            .AsNoTracking()
            .Where(m => m.VehicleId == vehicle.Id)
            .OrderByDescending(m => m.Date)
            .Take(10)
            .Select(m => new MaintenanceSummary
            {
                Date = m.Date,
                Type = m.Type,
                Description = m.Description,
                MileageAtService = m.MileageAtService,
                TotalCost = m.TotalCost,
                Status = m.Status
            })
            .ToListAsync();

        // Recent costs (last 10)
        ctx.RecentCosts = await _context.VehicleCosts
            .AsNoTracking()
            .Where(c => c.VehicleId == vehicle.Id)
            .OrderByDescending(c => c.Date)
            .Take(10)
            .Select(c => new CostSummary
            {
                Date = c.Date,
                Type = c.Type,
                Description = c.Description,
                Amount = c.Amount
            })
            .ToListAsync();

        // Fuel consumption stats (last 30 days)
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
        ctx.FuelEntries = await _context.FuelEntries
            .AsNoTracking()
            .Where(f => f.VehicleId == vehicle.Id && f.InvoiceDate >= thirtyDaysAgo)
            .OrderByDescending(f => f.InvoiceDate)
            .Take(10)
            .Select(f => new FuelEntrySummary
            {
                Date = f.InvoiceDate,
                Liters = f.Volume,
                CostPerLiter = f.PricePerLiter,
                TotalCost = f.TotalAmount,
                Mileage = (int)(f.OdometerKm ?? 0)
            })
            .ToListAsync();

        // Scheduled maintenance (upcoming)
        ctx.ScheduledMaintenance = await _context.VehicleMaintenanceSchedules
            .AsNoTracking()
            .Where(s => s.VehicleId == vehicle.Id && s.Status != "completed")
            .Include(s => s.Template)
            .Take(5)
            .Select(s => new ScheduledMaintenanceSummary
            {
                TemplateName = s.Template != null ? s.Template.Name : "N/A",
                Status = s.Status,
                NextDueKm = s.NextDueKm,
                NextDueDate = s.NextDueDate,
                LastPerformedKm = s.LastDoneKm
            })
            .ToListAsync();

        // Recent repairs (last 5)
        ctx.RecentRepairs = await _context.Repairs
            .AsNoTracking()
            .Where(r => r.VehicleId == vehicle.Id)
            .OrderByDescending(r => r.RepairDate)
            .Take(5)
            .Select(r => new RepairSummary
            {
                Date = r.RepairDate,
                Description = r.Description,
                TotalCost = r.TotalCost,
                MileageAtRepair = r.MileageAtRepair ?? 0,
                Status = r.Status
            })
            .ToListAsync();

        // Recent alerts (last 10)
        ctx.RecentAlerts = await _context.GpsAlerts
            .AsNoTracking()
            .Where(a => a.VehicleId == vehicle.Id)
            .OrderByDescending(a => a.Timestamp)
            .Take(10)
            .Select(a => new AlertSummary
            {
                Type = a.Type,
                Severity = a.Severity,
                Message = a.Message,
                Date = a.Timestamp
            })
            .ToListAsync();

        return ctx;
    }

    private static string BuildSystemPrompt(VehicleDiagnosticContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tu es un expert en diagnostic automobile et gestion de flotte. Tu assistes les gestionnaires de flotte en analysant les données de leurs véhicules.");
        sb.AppendLine("Réponds toujours en français. Sois précis, concis et actionnable dans tes recommandations.");
        sb.AppendLine("Quand tu identifies un problème potentiel, classe-le par urgence (critique, important, à surveiller).");
        sb.AppendLine();
        sb.AppendLine("═══ VÉHICULE EN CONTEXTE ═══");
        sb.AppendLine($"Nom: {ctx.Name}");
        sb.AppendLine($"Marque/Modèle: {ctx.Brand ?? "N/A"} {ctx.Model ?? "N/A"}");
        sb.AppendLine($"Type: {ctx.Type} | Année: {ctx.Year?.ToString() ?? "N/A"}");
        sb.AppendLine($"Carburant: {ctx.FuelType ?? "N/A"} | Kilométrage: {ctx.Mileage} km");
        sb.AppendLine($"Immatriculation: {ctx.Plate ?? "N/A"} | Statut: {ctx.Status}");

        if (ctx.InsuranceExpiry.HasValue)
            sb.AppendLine($"Expiration assurance: {ctx.InsuranceExpiry.Value:dd/MM/yyyy}");
        if (ctx.TechnicalInspectionExpiry.HasValue)
            sb.AppendLine($"Expiration contrôle technique: {ctx.TechnicalInspectionExpiry.Value:dd/MM/yyyy}");

        if (ctx.RecentMaintenance.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ ENTRETIENS RÉCENTS ═══");
            foreach (var m in ctx.RecentMaintenance)
            {
                sb.AppendLine($"- {m.Date:dd/MM/yyyy} | {m.Type} | {m.Description ?? "N/A"} | {m.MileageAtService} km | {m.TotalCost:N0} TND | {m.Status}");
            }
        }

        if (ctx.RecentRepairs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ RÉPARATIONS RÉCENTES ═══");
            foreach (var r in ctx.RecentRepairs)
            {
                sb.AppendLine($"- {r.Date:dd/MM/yyyy} | {r.Description ?? "N/A"} | {r.MileageAtRepair} km | {r.TotalCost:N0} TND | {r.Status}");
            }
        }

        if (ctx.RecentCosts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ COÛTS RÉCENTS ═══");
            foreach (var c in ctx.RecentCosts)
            {
                sb.AppendLine($"- {c.Date:dd/MM/yyyy} | {c.Type} | {c.Description ?? "N/A"} | {c.Amount:N0} TND");
            }
        }

        if (ctx.FuelEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ CONSOMMATION CARBURANT (30 derniers jours) ═══");
            foreach (var f in ctx.FuelEntries)
            {
                sb.AppendLine($"- {f.Date:dd/MM/yyyy} | {f.Liters:N1}L | {f.CostPerLiter:N3} TND/L | {f.TotalCost:N0} TND | {f.Mileage} km");
            }
        }

        if (ctx.ScheduledMaintenance.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ ENTRETIENS PROGRAMMÉS ═══");
            foreach (var s in ctx.ScheduledMaintenance)
            {
                var dueInfo = s.NextDueKm.HasValue ? $"à {s.NextDueKm} km" : "";
                if (s.NextDueDate.HasValue) dueInfo += $" le {s.NextDueDate.Value:dd/MM/yyyy}";
                sb.AppendLine($"- {s.TemplateName} | Statut: {s.Status} | Prochain: {dueInfo}");
            }
        }

        if (ctx.RecentAlerts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ ALERTES RÉCENTES ═══");
            foreach (var a in ctx.RecentAlerts)
            {
                sb.AppendLine($"- {a.Date:dd/MM/yyyy HH:mm} | {a.Type} | {a.Severity} | {a.Message}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Utilise ces données pour fournir un diagnostic intelligent et des prédictions sur les futurs problèmes potentiels de ce véhicule.");

        return sb.ToString();
    }
}

// ═══════ REQUEST / CONTEXT DTOs ═══════

public record AiChatRequest(int VehicleId, string Message);

public class VehicleDiagnosticContext
{
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string Type { get; set; } = "";
    public int? Year { get; set; }
    public string? FuelType { get; set; }
    public int Mileage { get; set; }
    public string? Plate { get; set; }
    public string Status { get; set; } = "";
    public DateTime? InsuranceExpiry { get; set; }
    public DateTime? TechnicalInspectionExpiry { get; set; }
    public List<MaintenanceSummary> RecentMaintenance { get; set; } = new();
    public List<RepairSummary> RecentRepairs { get; set; } = new();
    public List<CostSummary> RecentCosts { get; set; } = new();
    public List<FuelEntrySummary> FuelEntries { get; set; } = new();
    public List<ScheduledMaintenanceSummary> ScheduledMaintenance { get; set; } = new();
    public List<AlertSummary> RecentAlerts { get; set; } = new();
}

public class MaintenanceSummary
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public string? Description { get; set; }
    public int MileageAtService { get; set; }
    public decimal TotalCost { get; set; }
    public string Status { get; set; } = "";
}

public class RepairSummary
{
    public DateTime Date { get; set; }
    public string? Description { get; set; }
    public decimal TotalCost { get; set; }
    public int MileageAtRepair { get; set; }
    public string Status { get; set; } = "";
}

public class CostSummary
{
    public DateTime Date { get; set; }
    public string Type { get; set; } = "";
    public string? Description { get; set; }
    public decimal Amount { get; set; }
}

public class FuelEntrySummary
{
    public DateTime Date { get; set; }
    public decimal Liters { get; set; }
    public decimal CostPerLiter { get; set; }
    public decimal TotalCost { get; set; }
    public int Mileage { get; set; }
}

public class ScheduledMaintenanceSummary
{
    public string TemplateName { get; set; } = "";
    public string Status { get; set; } = "";
    public int? NextDueKm { get; set; }
    public DateTime? NextDueDate { get; set; }
    public int? LastPerformedKm { get; set; }
}

public class AlertSummary
{
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Date { get; set; }
}
