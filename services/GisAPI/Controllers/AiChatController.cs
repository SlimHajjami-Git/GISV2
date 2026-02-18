using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Entities;
using GisAPI.Services;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/ai-chat")]
[Authorize]
public class AiChatController : ControllerBase
{
    private readonly IGisDbContext _context;
    private readonly ILlmService _llmService;
    private readonly IVehicleHealthScoreService _healthService;
    private readonly ILogger<AiChatController> _logger;

    public AiChatController(IGisDbContext context, ILlmService llmService,
        IVehicleHealthScoreService healthService, ILogger<AiChatController> logger)
    {
        _context = context;
        _llmService = llmService;
        _healthService = healthService;
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

    // ═══════ HEALTH SCORE ENDPOINTS ═══════

    /// <summary>
    /// Get health score for a specific vehicle
    /// </summary>
    [HttpGet("health-score/{vehicleId}")]
    public async Task<IActionResult> GetHealthScore(int vehicleId)
    {
        var companyId = GetCompanyId();
        var result = await _healthService.CalculateScoreAsync(vehicleId, companyId);
        return Ok(result);
    }

    /// <summary>
    /// Get health scores for all vehicles in the company
    /// </summary>
    [HttpGet("health-scores")]
    public async Task<IActionResult> GetAllHealthScores()
    {
        var companyId = GetCompanyId();
        var results = await _healthService.CalculateAllScoresAsync(companyId);
        return Ok(results);
    }

    /// <summary>
    /// Compare multiple vehicles using AI analysis
    /// </summary>
    [HttpPost("compare")]
    public async Task<IActionResult> CompareVehicles([FromBody] CompareVehiclesRequest request)
    {
        var userId = GetUserId();
        var companyId = GetCompanyId();

        if (request.VehicleIds == null || request.VehicleIds.Count < 2 || request.VehicleIds.Count > 5)
            return BadRequest(new { message = "Sélectionnez entre 2 et 5 véhicules" });

        var vehicles = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .Where(v => request.VehicleIds.Contains(v.Id) && v.CompanyId == companyId)
            .ToListAsync();

        if (vehicles.Count < 2)
            return BadRequest(new { message = "Véhicules introuvables" });

        var sb = new StringBuilder();
        sb.AppendLine("Tu es un expert en gestion de flotte. Compare les véhicules suivants de manière détaillée.");
        sb.AppendLine("Fournis un tableau comparatif et une recommandation claire sur quel véhicule garder/renouveler.");
        sb.AppendLine();

        foreach (var vehicle in vehicles)
        {
            var ctx = await BuildVehicleContext(vehicle, companyId);
            sb.AppendLine($"═══ VÉHICULE: {ctx.Name} ═══");
            sb.AppendLine($"Marque/Modèle: {ctx.Brand} {ctx.Model} | Type: {ctx.Type} | Année: {ctx.Year}");
            sb.AppendLine($"Kilométrage: {ctx.Mileage:N0} km | Carburant: {ctx.FuelType} | Statut: {ctx.Status}");

            var healthScore = await _healthService.CalculateScoreAsync(vehicle.Id, companyId);
            sb.AppendLine($"Score santé: {healthScore.Score}/100 ({healthScore.Level})");

            if (ctx.RecentMaintenance.Count > 0)
                sb.AppendLine($"Entretiens récents: {ctx.RecentMaintenance.Count} | Coût total: {ctx.RecentMaintenance.Sum(m => m.TotalCost):N0} TND");
            if (ctx.RecentRepairs.Count > 0)
                sb.AppendLine($"Réparations récentes: {ctx.RecentRepairs.Count} | Coût total: {ctx.RecentRepairs.Sum(r => r.TotalCost):N0} TND");
            if (ctx.FuelEntries.Count > 0)
                sb.AppendLine($"Consommation moyenne: {ctx.FuelEntries.Average(f => f.Liters):F1} L/plein");
            sb.AppendLine();
        }

        var messages = new List<LlmMessage>
        {
            new("user", request.Question ?? "Compare ces véhicules et donne une recommandation détaillée.")
        };

        try
        {
            var llmResponse = await _llmService.ChatAsync(sb.ToString(), messages);
            return Ok(new { message = llmResponse.Content, tokensUsed = llmResponse.TokensUsed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI compare error");
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Generate an AI diagnostic report for a vehicle
    /// </summary>
    [HttpGet("report/{vehicleId}")]
    public async Task<IActionResult> GenerateReport(int vehicleId)
    {
        var companyId = GetCompanyId();

        var vehicle = await _context.Vehicles
            .AsNoTracking()
            .Include(v => v.GpsDevice)
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound(new { message = "Véhicule introuvable" });

        var vehicleContext = await BuildVehicleContext(vehicle, companyId);
        var healthScore = await _healthService.CalculateScoreAsync(vehicleId, companyId);

        var systemPrompt = BuildSystemPrompt(vehicleContext);
        systemPrompt += $"\n\nScore de santé actuel: {healthScore.Score}/100 ({healthScore.Level})";
        if (healthScore.Warnings.Count > 0)
            systemPrompt += "\nAvertissements: " + string.Join("; ", healthScore.Warnings);

        var messages = new List<LlmMessage>
        {
            new("user", @"Génère un rapport diagnostic complet et structuré pour ce véhicule. Inclus:
1. **Résumé exécutif** (état général en 2-3 lignes)
2. **Score de santé détaillé** (explique chaque facteur)
3. **Historique entretiens et réparations** (analyse des tendances)
4. **Analyse consommation carburant** (si données disponibles)
5. **Problèmes identifiés** (classés par urgence: critique, important, à surveiller)
6. **Prédictions** (futurs problèmes probables basés sur la marque/modèle et le kilométrage)
7. **Recommandations** (actions à entreprendre, priorisées avec estimation de coût si possible)
8. **Conclusion** (garder/renouveler/surveiller)")
        };

        try
        {
            var llmResponse = await _llmService.ChatAsync(systemPrompt, messages);
            return Ok(new
            {
                vehicleId,
                vehicleName = vehicle.Name,
                healthScore = healthScore,
                report = llmResponse.Content,
                tokensUsed = llmResponse.TokensUsed,
                generatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI report error for vehicle {VehicleId}", vehicleId);
            return StatusCode(503, new { message = ex.Message });
        }
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

        // GPS Driving stats (last 30 days)
        var trips30d = await _context.Trips
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicle.Id && t.StartTime >= thirtyDaysAgo && t.Status == "completed")
            .ToListAsync();

        if (trips30d.Count > 0)
        {
            var totalDist = trips30d.Sum(t => t.DistanceKm);
            var totalDur = trips30d.Sum(t => t.DurationMinutes);
            var totalIdle = trips30d.Sum(t => t.IdleTimeMinutes ?? 0);
            var harshBrake = trips30d.Sum(t => t.HarshBrakingCount ?? 0);
            var harshAccel = trips30d.Sum(t => t.HarshAccelerationCount ?? 0);
            var overspeed = trips30d.Sum(t => t.OverspeedingCount ?? 0);
            var maxSpeed = trips30d.Max(t => t.MaxSpeedKph ?? 0);
            var avgSpeed = trips30d.Where(t => t.AverageSpeedKph > 0).Select(t => t.AverageSpeedKph ?? 0).DefaultIfEmpty(0).Average();
            var daysSpan = Math.Max(1, (DateTime.UtcNow - trips30d.Min(t => t.StartTime)).TotalDays);

            // Driving score: start at 100, deduct for bad behavior
            var drivingScore = 100;
            drivingScore -= Math.Min(30, harshBrake * 3);
            drivingScore -= Math.Min(20, harshAccel * 2);
            drivingScore -= Math.Min(30, overspeed * 5);
            drivingScore -= Math.Min(10, (int)(totalIdle / Math.Max(1, totalDur) * 20));
            drivingScore = Math.Max(0, drivingScore);

            ctx.DrivingStats = new DrivingStatsSummary
            {
                TripCount30Days = trips30d.Count,
                TotalDistanceKm = totalDist,
                AvgDistancePerDayKm = Math.Round(totalDist / (decimal)daysSpan, 1),
                TotalDrivingMinutes = totalDur,
                TotalIdleMinutes = totalIdle,
                AvgSpeedKph = Math.Round(avgSpeed, 1),
                MaxSpeedKph = Math.Round(maxSpeed, 1),
                HarshBrakingTotal = harshBrake,
                HarshAccelerationTotal = harshAccel,
                OverspeedingTotal = overspeed,
                DrivingScore = drivingScore
            };
        }

        // GPS-based fuel records (last 30 days) from fuel_records table
        ctx.FuelRecords = await _context.FuelRecords
            .AsNoTracking()
            .Where(fr => fr.VehicleId == vehicle.Id && fr.RecordedAt >= thirtyDaysAgo)
            .OrderByDescending(fr => fr.RecordedAt)
            .Take(20)
            .Select(fr => new FuelRecordSummary
            {
                Date = fr.RecordedAt,
                FuelPercent = fr.FuelPercent,
                FuelLiters = fr.FuelLiters,
                ConsumptionRate = fr.ConsumptionRateLPer100Km,
                AvgConsumption = fr.AverageConsumptionLPer100Km,
                OdometerKm = fr.OdometerKm,
                EventType = fr.EventType,
                IsAnomaly = fr.IsAnomaly,
                AnomalyReason = fr.AnomalyReason,
                RefuelAmount = fr.RefuelAmount
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
            sb.AppendLine("═══ PLEINS CARBURANT (30 derniers jours) ═══");
            foreach (var f in ctx.FuelEntries)
            {
                sb.AppendLine($"- {f.Date:dd/MM/yyyy} | {f.Liters:N1}L | {f.CostPerLiter:N3} TND/L | {f.TotalCost:N0} TND | {f.Mileage} km");
            }
        }

        if (ctx.FuelRecords.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ CONSOMMATION GPS EN TEMPS RÉEL (30 derniers jours) ═══");
            var refuels = ctx.FuelRecords.Where(r => r.EventType == "refuel").ToList();
            var anomalies = ctx.FuelRecords.Where(r => r.IsAnomaly).ToList();
            var readings = ctx.FuelRecords.Where(r => r.ConsumptionRate.HasValue && r.ConsumptionRate > 0).ToList();
            if (readings.Count > 0)
            {
                var avgConsumption = readings.Average(r => (double)(r.ConsumptionRate ?? 0));
                sb.AppendLine($"Consommation moyenne GPS: {avgConsumption:F1} L/100km");
            }
            if (refuels.Count > 0)
                sb.AppendLine($"Pleins détectés par capteur: {refuels.Count} | Total: {refuels.Sum(r => r.RefuelAmount ?? 0):F1}L");
            if (anomalies.Count > 0)
            {
                sb.AppendLine($"Anomalies détectées: {anomalies.Count}");
                foreach (var a in anomalies.Take(5))
                    sb.AppendLine($"  - {a.Date:dd/MM HH:mm} | {a.AnomalyReason}");
            }
            var latest = ctx.FuelRecords.FirstOrDefault();
            if (latest != null)
                sb.AppendLine($"Dernier niveau: {latest.FuelPercent}% | {latest.FuelLiters?.ToString("F1") ?? "N/A"}L | Odom: {latest.OdometerKm?.ToString("N0") ?? "N/A"} km");
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

        if (ctx.DrivingStats != null)
        {
            var ds = ctx.DrivingStats;
            sb.AppendLine();
            sb.AppendLine("═══ STATISTIQUES DE CONDUITE (30 jours) ═══");
            sb.AppendLine($"Trajets: {ds.TripCount30Days} | Distance totale: {ds.TotalDistanceKm:N0} km | Moyenne: {ds.AvgDistancePerDayKm:N1} km/jour");
            sb.AppendLine($"Temps conduite: {ds.TotalDrivingMinutes} min | Ralenti: {ds.TotalIdleMinutes} min");
            sb.AppendLine($"Vitesse moy: {ds.AvgSpeedKph:N1} km/h | Max: {ds.MaxSpeedKph:N1} km/h");
            sb.AppendLine($"Freinages brusques: {ds.HarshBrakingTotal} | Accélérations brusques: {ds.HarshAccelerationTotal} | Excès vitesse: {ds.OverspeedingTotal}");
            sb.AppendLine($"Score de conduite: {ds.DrivingScore}/100");
        }

        sb.AppendLine();
        sb.AppendLine("Utilise ces données pour fournir un diagnostic intelligent et des prédictions sur les futurs problèmes potentiels de ce véhicule.");

        return sb.ToString();
    }
}

// ═══════ REQUEST / CONTEXT DTOs ═══════

public record AiChatRequest(int VehicleId, string Message);
public record CompareVehiclesRequest(List<int> VehicleIds, string? Question);

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
    public List<FuelRecordSummary> FuelRecords { get; set; } = new();
    public List<ScheduledMaintenanceSummary> ScheduledMaintenance { get; set; } = new();
    public List<AlertSummary> RecentAlerts { get; set; } = new();
    public DrivingStatsSummary? DrivingStats { get; set; }
}

public class DrivingStatsSummary
{
    public int TripCount30Days { get; set; }
    public decimal TotalDistanceKm { get; set; }
    public decimal AvgDistancePerDayKm { get; set; }
    public int TotalDrivingMinutes { get; set; }
    public int TotalIdleMinutes { get; set; }
    public decimal AvgSpeedKph { get; set; }
    public decimal MaxSpeedKph { get; set; }
    public int HarshBrakingTotal { get; set; }
    public int HarshAccelerationTotal { get; set; }
    public int OverspeedingTotal { get; set; }
    public int DrivingScore { get; set; }
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

public class FuelRecordSummary
{
    public DateTime Date { get; set; }
    public short FuelPercent { get; set; }
    public decimal? FuelLiters { get; set; }
    public decimal? ConsumptionRate { get; set; }
    public decimal? AvgConsumption { get; set; }
    public long? OdometerKm { get; set; }
    public string EventType { get; set; } = "";
    public bool IsAnomaly { get; set; }
    public string? AnomalyReason { get; set; }
    public decimal? RefuelAmount { get; set; }
}

public class AlertSummary
{
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime Date { get; set; }
}
