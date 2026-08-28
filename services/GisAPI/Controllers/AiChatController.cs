using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Domain.Common;
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
                sb.AppendLine($"Entretiens récents: {ctx.RecentMaintenance.Count} | Coût total: {ctx.RecentMaintenance.Sum(m => m.TotalCost):N0} {AppCurrency.Default}");
            if (ctx.RecentRepairs.Count > 0)
                sb.AppendLine($"Réparations récentes: {ctx.RecentRepairs.Count} | Coût total: {ctx.RecentRepairs.Sum(r => r.TotalCost):N0} {AppCurrency.Default}");
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
            TechnicalInspectionExpiry = vehicle.TechnicalInspectionExpiry,
            HasGpsDevice = vehicle.GpsDeviceId.HasValue
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

        // ═══ SYSTEM INSTRUCTIONS ═══
        sb.AppendLine("Tu es un expert en diagnostic automobile et gestion de flotte véhicules.");
        sb.AppendLine("RÈGLES STRICTES:");
        sb.AppendLine("1. Base ton analyse UNIQUEMENT sur les données fournies ci-dessous. Ne fabrique JAMAIS de chiffres.");
        sb.AppendLine("2. Si une section de données est vide ou absente, dis clairement 'Aucune donnée disponible' pour cette section.");
        sb.AppendLine("3. Si les données semblent incohérentes (ex: 0L consommé mais des pleins enregistrés), signale-le comme problème de capteur ou de saisie, ne tente pas de les réconcilier.");
        sb.AppendLine("4. Classe chaque problème par urgence: 🔴 Critique, 🟠 Important, 🟡 À surveiller.");
        sb.AppendLine("5. Réponds toujours en français, de manière structurée avec des titres et listes.");
        sb.AppendLine("6. Il y a DEUX sources de données carburant distinctes:");
        sb.AppendLine("   - 'Pleins manuels': saisis par le gestionnaire (factures de station-service)");
        sb.AppendLine("   - 'Capteur GPS': données temps réel du capteur de carburant embarqué (peut être absent ou mal calibré)");
        sb.AppendLine("7. Ne confonds jamais ces deux sources. Analyse-les séparément puis compare si les deux existent.");
        sb.AppendLine();

        // ═══ VEHICLE IDENTITY ═══
        sb.AppendLine("═══ VÉHICULE ═══");
        sb.AppendLine($"Nom: {ctx.Name}");
        sb.AppendLine($"Marque/Modèle: {ctx.Brand ?? "N/A"} {ctx.Model ?? "N/A"}");
        sb.AppendLine($"Type: {ctx.Type} | Année: {ctx.Year?.ToString() ?? "N/A"}");
        sb.AppendLine($"Carburant: {ctx.FuelType ?? "N/A"} | Kilométrage actuel: {ctx.Mileage:N0} km");
        sb.AppendLine($"Immatriculation: {ctx.Plate ?? "N/A"} | Statut: {ctx.Status}");

        if (ctx.InsuranceExpiry.HasValue)
            sb.AppendLine($"Expiration assurance: {ctx.InsuranceExpiry.Value:dd/MM/yyyy}");
        if (ctx.TechnicalInspectionExpiry.HasValue)
            sb.AppendLine($"Expiration contrôle technique: {ctx.TechnicalInspectionExpiry.Value:dd/MM/yyyy}");

        // ═══ DRIVING STATS (30 days) ═══
        // Vehicule gere SANS boitier GPS : ni trajets ni capteurs n existent.
        // On le dit une fois au modele plutot que d aligner des sections vides
        // qu il commenterait (« verifier le capteur ») a tort.
        if (!ctx.HasGpsDevice)
        {
            sb.AppendLine();
            sb.AppendLine("NOTE: Ce véhicule est géré SANS boîtier GPS (offre gestion de parc). " +
                "Aucune donnée de trajets, de vitesse ou de capteur carburant n'existe et c'est NORMAL : " +
                "ne recommande jamais de vérifier un capteur ou un boîtier. " +
                "Appuie-toi sur les pleins saisis, les entretiens et les dépenses.");
        }
        else if (ctx.DrivingStats != null)
        {
            var ds = ctx.DrivingStats;
            sb.AppendLine();
            sb.AppendLine("═══ ACTIVITÉ DE CONDUITE (30 derniers jours) ═══");
            sb.AppendLine($"Nombre de trajets: {ds.TripCount30Days}");
            sb.AppendLine($"Distance totale parcourue: {ds.TotalDistanceKm:N0} km");
            sb.AppendLine($"Distance moyenne par jour: {ds.AvgDistancePerDayKm:N1} km/jour");
            sb.AppendLine($"Temps de conduite total: {ds.TotalDrivingMinutes} min ({ds.TotalDrivingMinutes / 60}h{ds.TotalDrivingMinutes % 60:D2})");
            sb.AppendLine($"Temps au ralenti: {ds.TotalIdleMinutes} min ({(ds.TotalDrivingMinutes > 0 ? (ds.TotalIdleMinutes * 100 / ds.TotalDrivingMinutes) : 0)}% du temps)");
            sb.AppendLine($"Vitesse moyenne: {ds.AvgSpeedKph:N1} km/h | Vitesse max: {ds.MaxSpeedKph:N1} km/h");
            sb.AppendLine($"Freinages brusques: {ds.HarshBrakingTotal} | Accélérations brusques: {ds.HarshAccelerationTotal} | Excès de vitesse: {ds.OverspeedingTotal}");
            sb.AppendLine($"Score de conduite: {ds.DrivingScore}/100");
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine("═══ ACTIVITÉ DE CONDUITE (30 derniers jours) ═══");
            sb.AppendLine("Aucun trajet enregistré sur les 30 derniers jours.");
        }

        // ═══ FUEL - MANUAL ENTRIES ═══
        sb.AppendLine();
        sb.AppendLine("═══ PLEINS CARBURANT MANUELS (factures, 30 derniers jours) ═══");
        if (ctx.FuelEntries.Count > 0)
        {
            var totalLiters = ctx.FuelEntries.Sum(f => f.Liters);
            var totalCost = ctx.FuelEntries.Sum(f => f.TotalCost);
            sb.AppendLine($"Nombre de pleins saisis: {ctx.FuelEntries.Count}");
            sb.AppendLine($"Total litres ravitaillés: {totalLiters:N1} L");
            sb.AppendLine($"Coût total carburant: {totalCost:N2} {AppCurrency.Default}");
            if (totalLiters > 0)
                sb.AppendLine($"Prix moyen par litre: {(totalCost / totalLiters):N3} {AppCurrency.Default}/L");
            if (ctx.DrivingStats != null && ctx.DrivingStats.TotalDistanceKm > 0 && totalLiters > 0)
            {
                var consumptionPer100 = (double)totalLiters / (double)ctx.DrivingStats.TotalDistanceKm * 100;
                var costPerKm = (double)totalCost / (double)ctx.DrivingStats.TotalDistanceKm;
                sb.AppendLine($"Consommation calculée: {consumptionPer100:F1} L/100km (basé sur {ctx.DrivingStats.TotalDistanceKm:N0} km parcourus)");
                sb.AppendLine($"Coût au kilomètre: {costPerKm:F3} {AppCurrency.Default}/km");
            }
            sb.AppendLine("Détail des pleins:");
            foreach (var f in ctx.FuelEntries)
            {
                sb.AppendLine($"  - {f.Date:dd/MM/yyyy} | {f.Liters:N1}L | {f.CostPerLiter:N3} {AppCurrency.Default}/L | {f.TotalCost:N2} {AppCurrency.Default} | Odom: {f.Mileage} km");
            }
        }
        else
        {
            sb.AppendLine("Aucun plein de carburant saisi manuellement sur cette période.");
        }

        // ═══ FUEL - GPS SENSOR RECORDS ═══ (uniquement si un boitier existe)
        if (ctx.HasGpsDevice)
        {
        sb.AppendLine();
        sb.AppendLine("═══ DONNÉES CAPTEUR CARBURANT GPS (automatique, 30 derniers jours) ═══");
        if (ctx.FuelRecords.Count > 0)
        {
            var refuels = ctx.FuelRecords.Where(r => r.EventType == "refuel").ToList();
            var anomalies = ctx.FuelRecords.Where(r => r.IsAnomaly).ToList();
            var normalReadings = ctx.FuelRecords.Where(r => r.EventType == "normal" || r.EventType == "reading").ToList();
            var readings = ctx.FuelRecords.Where(r => r.ConsumptionRate.HasValue && r.ConsumptionRate > 0).ToList();
            var latest = ctx.FuelRecords.FirstOrDefault();

            sb.AppendLine($"Nombre total d'enregistrements capteur: {ctx.FuelRecords.Count}");

            if (latest != null)
                sb.AppendLine($"Dernier relevé capteur: {latest.Date:dd/MM/yyyy HH:mm} | Niveau: {latest.FuelPercent}% | {(latest.FuelLiters.HasValue ? $"{latest.FuelLiters:F1}L" : "litres non calculés")} | Odom: {(latest.OdometerKm.HasValue ? $"{latest.OdometerKm:N0} km" : "N/A")}");

            if (readings.Count > 0)
            {
                var avgConsumption = readings.Average(r => (double)(r.ConsumptionRate ?? 0));
                var avgAvgConsumption = readings.Where(r => r.AvgConsumption.HasValue && r.AvgConsumption > 0).Select(r => (double)(r.AvgConsumption ?? 0)).DefaultIfEmpty(0).Average();
                sb.AppendLine($"Consommation instantanée moyenne (capteur): {avgConsumption:F1} L/100km ({readings.Count} mesures)");
                if (avgAvgConsumption > 0)
                    sb.AppendLine($"Consommation lissée moyenne (capteur): {avgAvgConsumption:F1} L/100km");
            }
            else
            {
                sb.AppendLine("Aucune mesure de consommation valide du capteur (le capteur est peut-être absent ou non calibré).");
            }

            if (refuels.Count > 0)
            {
                var totalRefuelAmount = refuels.Sum(r => r.RefuelAmount ?? 0);
                sb.AppendLine($"Pleins détectés automatiquement par capteur: {refuels.Count}");
                if (totalRefuelAmount > 0)
                    sb.AppendLine($"  Volume total ravitaillé (capteur): {totalRefuelAmount:F1} L");
                foreach (var r in refuels)
                    sb.AppendLine($"  - {r.Date:dd/MM/yyyy HH:mm} | Niveau: {r.FuelPercent}% | Ravitaillé: {(r.RefuelAmount.HasValue ? $"{r.RefuelAmount:F1}L" : "non mesuré")}");
            }
            else
            {
                sb.AppendLine("Aucun plein de carburant détecté automatiquement par le capteur.");
            }

            if (anomalies.Count > 0)
            {
                sb.AppendLine($"Anomalies carburant détectées: {anomalies.Count}");
                sb.AppendLine("  (Une anomalie peut être: chute soudaine de niveau = vol potentiel, consommation anormalement élevée, capteur défaillant)");
                foreach (var a in anomalies.Take(5))
                    sb.AppendLine($"  - {a.Date:dd/MM/yyyy HH:mm} | Type: {a.EventType} | Raison: {a.AnomalyReason ?? "non spécifiée"} | Niveau: {a.FuelPercent}%");
            }
            else
            {
                sb.AppendLine("Aucune anomalie carburant détectée.");
            }
        }
        else
        {
            sb.AppendLine("Aucune donnée capteur carburant disponible. Le véhicule n'a peut-être pas de capteur de carburant ou il est hors service.");
        }
        }

        // ═══ MAINTENANCE ═══
        if (ctx.RecentMaintenance.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ ENTRETIENS RÉCENTS ═══");
            foreach (var m in ctx.RecentMaintenance)
                sb.AppendLine($"- {m.Date:dd/MM/yyyy} | {m.Type} | {m.Description ?? "N/A"} | {m.MileageAtService} km | {m.TotalCost:N0} {AppCurrency.Default} | Statut: {m.Status}");
        }

        if (ctx.RecentRepairs.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ RÉPARATIONS RÉCENTES ═══");
            foreach (var r in ctx.RecentRepairs)
                sb.AppendLine($"- {r.Date:dd/MM/yyyy} | {r.Description ?? "N/A"} | {r.MileageAtRepair} km | {r.TotalCost:N0} {AppCurrency.Default} | Statut: {r.Status}");
        }

        if (ctx.ScheduledMaintenance.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ ENTRETIENS PROGRAMMÉS (à venir) ═══");
            foreach (var s in ctx.ScheduledMaintenance)
            {
                var dueInfo = s.NextDueKm.HasValue ? $"à {s.NextDueKm:N0} km" : "";
                if (s.NextDueDate.HasValue) dueInfo += $" le {s.NextDueDate.Value:dd/MM/yyyy}";
                sb.AppendLine($"- {s.TemplateName} | Statut: {s.Status} | Prochain: {dueInfo}");
            }
        }

        // ═══ COSTS ═══
        if (ctx.RecentCosts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ COÛTS RÉCENTS ═══");
            var totalCosts = ctx.RecentCosts.Sum(c => c.Amount);
            sb.AppendLine($"Total des coûts listés: {totalCosts:N0} {AppCurrency.Default}");
            foreach (var c in ctx.RecentCosts)
                sb.AppendLine($"- {c.Date:dd/MM/yyyy} | {c.Type} | {c.Description ?? "N/A"} | {c.Amount:N0} {AppCurrency.Default}");
        }

        // ═══ ALERTS ═══
        if (ctx.RecentAlerts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("═══ ALERTES RÉCENTES ═══");
            foreach (var a in ctx.RecentAlerts)
                sb.AppendLine($"- {a.Date:dd/MM/yyyy HH:mm} | {a.Type} | Sévérité: {a.Severity} | {a.Message}");
        }

        sb.AppendLine();
        sb.AppendLine("═══ INSTRUCTIONS FINALES ═══");
        sb.AppendLine("Analyse ces données de manière logique et cohérente. Si des données manquent ou sont à zéro, dis-le clairement au lieu d'inventer des conclusions. Propose des recommandations concrètes et actionnables basées uniquement sur les faits ci-dessus.");

        return sb.ToString();
    }

    // ═══════ FLEET-WIDE AI REPORT ═══════

    /// <summary>
    /// Generate a comprehensive AI fleet report with charts data and Groq analysis
    /// </summary>
    [HttpPost("fleet-report")]
    public async Task<IActionResult> GenerateFleetReport([FromBody] FleetReportRequest request)
    {
        var companyId = GetCompanyId();
        var now = DateTime.UtcNow;
        var periodDays = (request.Period ?? "month") switch
        {
            "week" => 7, "quarter" => 90, "year" => 365, _ => 30
        };
        var periodStart = DateTime.SpecifyKind(now.AddDays(-periodDays).Date, DateTimeKind.Utc);

        // ── Company info ──
        var company = await _context.Societes.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == companyId);

        // ── Vehicles + health scores ──
        var vehicles = await _context.Vehicles.AsNoTracking()
            .Where(v => v.CompanyId == companyId)
            .ToListAsync();

        var healthScores = await _healthService.CalculateAllScoresAsync(companyId);
        var healthMap = healthScores.ToDictionary(h => h.VehicleId);

        // ── Trips data ──
        var trips = await _context.Trips.AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.StartTime >= periodStart && t.Status == "completed")
            .ToListAsync();
        var tripsByVehicle = trips.GroupBy(t => t.VehicleId).ToDictionary(g => g.Key, g => g.ToList());

        // ── Costs data ──
        var costs = await _context.VehicleCosts.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.Date >= periodStart)
            .ToListAsync();
        var costsByType = costs.GroupBy(c => c.Type).ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        // ── Maintenance & Repairs ──
        var vehicleIds = vehicles.Select(v => v.Id).ToList();
        var maintenance = await _context.MaintenanceRecords.AsNoTracking()
            .Where(m => vehicleIds.Contains(m.VehicleId) && m.Date >= periodStart)
            .ToListAsync();
        var repairs = await _context.Repairs.AsNoTracking()
            .Where(r => vehicleIds.Contains(r.VehicleId) && r.RepairDate >= periodStart)
            .ToListAsync();

        // ── Alerts ──
        var alertsByVehicle = await _context.GpsAlerts.AsNoTracking()
            .Where(a => a.VehicleId.HasValue && vehicleIds.Contains(a.VehicleId.Value) && a.Timestamp >= periodStart)
            .GroupBy(a => a.VehicleId!.Value)
            .Select(g => new { VehicleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.VehicleId, g => g.Count);
        var totalAlerts = alertsByVehicle.Values.Sum();

        // ── Fuel from GPS (FuelRateLPer100Km) ──
        var deviceMap = vehicles.Where(v => v.GpsDeviceId.HasValue).ToDictionary(v => v.GpsDeviceId!.Value, v => v);
        var deviceIds = deviceMap.Keys.ToList();
        var fuelData = await _context.GpsPositions.AsNoTracking()
            .Where(p => deviceIds.Contains(p.DeviceId) && p.RecordedAt >= periodStart &&
                        p.FuelRateLPer100Km != null && p.FuelRateLPer100Km > 0 && p.FuelRateLPer100Km < 80)
            .GroupBy(p => p.DeviceId)
            .Select(g => new { DeviceId = g.Key, AvgRate = g.Average(p => (double)p.FuelRateLPer100Km!), Count = g.Count() })
            .Where(x => x.Count >= 3)
            .ToListAsync();
        var fuelByVehicle = fuelData.ToDictionary(
            f => deviceMap.ContainsKey(f.DeviceId) ? deviceMap[f.DeviceId].Id : 0,
            f => f.AvgRate);

        // ── Scheduled maintenance status ──
        var schedules = await _context.VehicleMaintenanceSchedules.AsNoTracking()
            .Where(s => s.CompanyId == companyId && !s.IsPaused)
            .Include(s => s.Template)
            .ToListAsync();

        // ══════════ BUILD CHART DATA ══════════
        var totalDistance = trips.Sum(t => t.DistanceKm);
        var totalFuelCost = costsByType.GetValueOrDefault("fuel", 0) + costsByType.GetValueOrDefault("carburant", 0);
        var totalMaintCost = costsByType.GetValueOrDefault("maintenance", 0) + costsByType.GetValueOrDefault("entretien", 0);
        var totalRepairCost = costsByType.GetValueOrDefault("repair", 0) + costsByType.GetValueOrDefault("reparation", 0);
        var totalOtherCost = costs.Sum(c => c.Amount) - totalFuelCost - totalMaintCost - totalRepairCost;
        if (totalOtherCost < 0) totalOtherCost = 0;

        // Health distribution
        var healthDist = new { excellent = 0, good = 0, fair = 0, poor = 0, critical = 0 };
        var exc = healthScores.Count(h => h.Level == "excellent");
        var goo = healthScores.Count(h => h.Level == "good");
        var fai = healthScores.Count(h => h.Level == "fair");
        var poo = healthScores.Count(h => h.Level == "poor");
        var cri = healthScores.Count(h => h.Level == "critical");

        // Per-vehicle details
        var vehicleDetails = vehicles.Select(v =>
        {
            var vTrips = tripsByVehicle.GetValueOrDefault(v.Id, new List<Trip>());
            var vHealth = healthMap.GetValueOrDefault(v.Id);
            var vAlerts = alertsByVehicle.GetValueOrDefault(v.Id, 0);
            var vFuel = fuelByVehicle.GetValueOrDefault(v.Id, 0);
            var vCosts = costs.Where(c => c.VehicleId == v.Id).Sum(c => c.Amount);
            var vRepairs = repairs.Where(r => r.VehicleId == v.Id).ToList();
            var vMaint = maintenance.Where(m => m.VehicleId == v.Id).ToList();
            var vSchedules = schedules.Where(s => s.VehicleId == v.Id).ToList();

            // Driving score from trips
            var harshBrake = vTrips.Sum(t => t.HarshBrakingCount ?? 0);
            var harshAccel = vTrips.Sum(t => t.HarshAccelerationCount ?? 0);
            var overspeed = vTrips.Sum(t => t.OverspeedingCount ?? 0);
            var drivingScore = Math.Max(0, 100 - harshBrake * 3 - harshAccel * 2 - overspeed * 5);

            return new
            {
                id = v.Id, name = v.Name, plate = v.Plate ?? "", brand = v.Brand ?? "",
                model = v.Model ?? "", type = v.Type ?? "", year = v.Year,
                fuelType = v.FuelType ?? "", mileage = v.Mileage, status = v.Status ?? "",
                healthScore = vHealth?.Score ?? 0, healthLevel = vHealth?.Level ?? "unknown",
                warnings = vHealth?.Warnings ?? new List<string>(),
                distance = Math.Round(vTrips.Sum(t => t.DistanceKm), 1),
                tripCount = vTrips.Count,
                fuelConsumption = Math.Round(vFuel, 1),
                totalCosts = Math.Round(vCosts, 0),
                alertCount = vAlerts,
                drivingScore,
                repairCount = vRepairs.Count,
                repairCost = Math.Round(vRepairs.Sum(r => r.TotalCost), 0),
                maintCount = vMaint.Count,
                maintCost = Math.Round(vMaint.Sum(m => m.TotalCost), 0),
                overdueSchedules = vSchedules.Count(s => s.Status == "overdue" || s.Status == "critical"),
                topRepairs = vRepairs.OrderByDescending(r => r.TotalCost).Take(3)
                    .Select(r => new { description = r.Description ?? "N/A", cost = Math.Round(r.TotalCost, 0), date = r.RepairDate.ToString("dd/MM/yyyy") }).ToList()
            };
        }).OrderByDescending(v => v.totalCosts).ToList();

        // Top fuel consumers
        var topFuel = vehicleDetails.Where(v => v.fuelConsumption > 0)
            .OrderByDescending(v => v.fuelConsumption).Take(5)
            .Select(v => new { name = v.plate.Length > 0 ? v.plate : v.name, value = v.fuelConsumption }).ToList();

        // Mileage by vehicle
        var mileageChart = vehicleDetails.OrderByDescending(v => v.distance).Take(10)
            .Select(v => new { name = v.plate.Length > 0 ? v.plate : v.name, value = v.distance }).ToList();

        // Driving scores
        var drivingChart = vehicleDetails.Where(v => v.tripCount > 0)
            .OrderByDescending(v => v.drivingScore).Take(10)
            .Select(v => new { name = v.plate.Length > 0 ? v.plate : v.name, value = v.drivingScore }).ToList();

        // ══════════ BUILD GROQ PROMPT ══════════
        var sb = new StringBuilder();
        sb.AppendLine("Tu es un expert senior en gestion de flotte automobile et consultant TCO (Total Cost of Ownership).");
        sb.AppendLine("RÈGLES:");
        sb.AppendLine("1. Base ton analyse UNIQUEMENT sur les données fournies. Ne fabrique JAMAIS de chiffres.");
        sb.AppendLine("2. Réponds en français, structuré avec des titres markdown (##, ###) et des listes.");
        sb.AppendLine("3. Classe chaque problème: 🔴 Critique, 🟠 Important, 🟡 À surveiller, 🟢 OK.");
        sb.AppendLine("4. Fournis des conseils TCO concrets basés sur le type d'entreprise.");
        sb.AppendLine("5. Pour chaque modèle de véhicule, utilise ta connaissance des problèmes courants de cette marque/modèle.");
        sb.AppendLine("6. Propose un top 10 des pièces à surveiller par modèle si pertinent.");
        sb.AppendLine("7. Si des données manquent, dis-le clairement.");
        sb.AppendLine();

        // Company context
        sb.AppendLine("═══ ENTREPRISE ═══");
        sb.AppendLine($"Nom: {company?.Name ?? "N/A"}");
        sb.AppendLine($"Type d'activité: {company?.Type ?? "transport"} | Pays: {company?.Country ?? "TN"}");
        sb.AppendLine($"Devise: {AppCurrency.Default}");
        sb.AppendLine($"Nombre de véhicules: {vehicles.Count}");
        sb.AppendLine();

        // Fleet summary
        sb.AppendLine("═══ RÉSUMÉ FLOTTE ═══");
        sb.AppendLine($"Période analysée: {periodDays} derniers jours");
        sb.AppendLine($"Distance totale parcourue: {totalDistance:N0} km");
        sb.AppendLine($"Nombre de trajets: {trips.Count}");
        sb.AppendLine($"Véhicules actifs (avec trajets): {tripsByVehicle.Count}");
        sb.AppendLine($"Score santé moyen: {(healthScores.Any() ? healthScores.Average(h => h.Score) : 0):F0}/100");
        sb.AppendLine($"Distribution santé: Excellent={exc}, Bon={goo}, Moyen={fai}, Faible={poo}, Critique={cri}");
        sb.AppendLine($"Coûts totaux: {costs.Sum(c => c.Amount):N0} {AppCurrency.Default} (Carburant: {totalFuelCost:N0}, Maintenance: {totalMaintCost:N0}, Réparations: {totalRepairCost:N0}, Autres: {totalOtherCost:N0})");
        sb.AppendLine($"Alertes totales: {totalAlerts}");
        sb.AppendLine($"Entretiens réalisés: {maintenance.Count} | Réparations: {repairs.Count}");
        sb.AppendLine($"Entretiens en retard/critique: {schedules.Count(s => s.Status == "overdue" || s.Status == "critical")}");
        sb.AppendLine();

        // Per-vehicle detail — PLAFONNÉ. L'offre Groq (tier on_demand) est limitée
        // à 8000 tokens/minute : le détail complet d'une grande flotte (HERTZ =
        // 251 véhicules) dépassait la limite -> 413 « Request too large », rapport
        // en échec. On ne détaille donc que les véhicules qui APPELLENT l'attention
        // (santé faible/critique, entretien en retard, alertes, ou coût élevé),
        // plafonnés à 25 ; les autres sont résumés en une ligne agrégée à la fin.
        const int MaxDetailed = 25;
        var notable = vehicleDetails
            .OrderByDescending(v => (v.healthLevel == "critical" || v.healthLevel == "poor" ? 1000 : 0)
                                    + v.overdueSchedules * 200 + v.alertCount * 20 + (double)v.totalCosts / 100.0)
            .Take(MaxDetailed).ToList();
        var notableIds = notable.Select(v => v.id).ToHashSet();
        var rest = vehicleDetails.Where(v => !notableIds.Contains(v.id)).ToList();

        sb.AppendLine("═══ DÉTAIL PAR VÉHICULE ═══");
        if (rest.Count > 0)
            sb.AppendLine($"(Flotte de {vehicleDetails.Count} véhicules — détail des {notable.Count} plus notables ci-dessous ; les {rest.Count} autres sont résumés à la fin.)");
        foreach (var v in notable)
        {
            sb.AppendLine($"▸ {v.name} ({v.plate}) | {v.brand} {v.model} {v.year} | {v.type} | {v.fuelType} | {v.mileage:N0} km | Statut: {v.status}");
            sb.AppendLine($"  Santé: {v.healthScore}/100 ({v.healthLevel}) | Score conduite: {v.drivingScore}/100 | Alertes: {v.alertCount}");
            sb.AppendLine($"  Distance période: {v.distance:N0} km | Trajets: {v.tripCount} | Conso: {(v.fuelConsumption > 0 ? $"{v.fuelConsumption:F1} L/100km" : "N/A")}");
            sb.AppendLine($"  Coûts: {v.totalCosts:N0} {AppCurrency.Default} | Entretiens: {v.maintCount} ({v.maintCost:N0} {AppCurrency.Default}) | Réparations: {v.repairCount} ({v.repairCost:N0} {AppCurrency.Default})");
            if (v.overdueSchedules > 0)
                sb.AppendLine($"  ⚠️ {v.overdueSchedules} entretien(s) en retard/critique");
            if (v.warnings.Count > 0)
                sb.AppendLine($"  Avertissements: {string.Join("; ", v.warnings.Take(3))}");
            if (v.topRepairs.Count > 0)
                sb.AppendLine($"  Dernières réparations: {string.Join(", ", v.topRepairs.Select(r => $"{r.description} ({r.cost} {AppCurrency.Default}, {r.date})"))}");
        }

        if (rest.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"═══ RESTE DE LA FLOTTE ({rest.Count} véhicules, agrégé) ═══");
            sb.AppendLine($"Distance cumulée: {rest.Sum(v => v.distance):N0} km | Trajets: {rest.Sum(v => v.tripCount)} | Coûts cumulés: {rest.Sum(v => v.totalCosts):N0} {AppCurrency.Default}");
            sb.AppendLine($"Santé moyenne: {(rest.Any() ? rest.Average(v => v.healthScore) : 0):F0}/100 | Entretiens en retard: {rest.Sum(v => v.overdueSchedules)} | Alertes: {rest.Sum(v => v.alertCount)}");
            var byModel = rest.GroupBy(v => $"{v.brand} {v.model}").OrderByDescending(g => g.Count()).Take(8);
            sb.AppendLine("Répartition marque/modèle: " + string.Join(", ", byModel.Select(g => $"{g.Key.Trim()} ×{g.Count()}")));
        }

        var userMessage = request.Question ?? @"Génère un rapport d'analyse de flotte complet et structuré. Inclus:
## 1. Résumé exécutif
Vue d'ensemble de la flotte en 5 lignes max.

## 2. Analyse TCO (Total Cost of Ownership)
Coût total de possession, coût/km, projections, optimisations possibles.

## 3. Analyse par véhicule
Pour chaque véhicule problématique ou notable, analyse détaillée.

## 4. Problèmes par marque/modèle
Problèmes connus typiques pour chaque marque/modèle présent dans la flotte. Top 10 pièces à surveiller.

## 5. Maintenance préventive
Recommandations de maintenance basées sur le kilométrage et l'âge. Pièces d'usure à anticiper.

## 6. Efficacité carburant
Analyse des consommations, véhicules les plus gourmands, optimisations.

## 7. Sécurité et conduite
Analyse des scores de conduite, alertes, recommandations.

## 8. Recommandations prioritaires
Actions concrètes classées par urgence avec estimation d'impact.

## 9. Plan d'action
Calendrier recommandé pour les 3 prochains mois.";

        try
        {
            // max_tokens sous la limite TPM de l'offre (8000/min) : la réponse
            // (2500) + le contexte du prompt doivent tenir dans une minute de budget.
            var llmResponse = await _llmService.ChatAsync(sb.ToString(), new List<LlmMessage> { new("user", userMessage) }, 2500);

            var result = new
            {
                companyInfo = new { name = company?.Name ?? "N/A", type = company?.Type ?? "transport", vehicleCount = vehicles.Count },
                generatedAt = now,
                period = request.Period ?? "month",
                periodDays,
                fleetSummary = new
                {
                    totalVehicles = vehicles.Count,
                    activeVehicles = tripsByVehicle.Count,
                    totalDistance = Math.Round(totalDistance, 0),
                    totalTrips = trips.Count,
                    avgHealthScore = healthScores.Any() ? (int)Math.Round(healthScores.Average(h => h.Score)) : 0,
                    totalCosts = Math.Round(costs.Sum(c => c.Amount), 0),
                    totalAlerts,
                    overdueSchedules = schedules.Count(s => s.Status == "overdue" || s.Status == "critical")
                },
                charts = new
                {
                    healthDistribution = new { excellent = exc, good = goo, fair = fai, poor = poo, critical = cri },
                    costBreakdown = new { fuel = Math.Round(totalFuelCost, 0), maintenance = Math.Round(totalMaintCost, 0), repairs = Math.Round(totalRepairCost, 0), other = Math.Round(totalOtherCost, 0) },
                    topFuelConsumers = topFuel,
                    mileageByVehicle = mileageChart,
                    drivingScores = drivingChart
                },
                vehicleDetails,
                aiAnalysis = llmResponse.Content,
                tokensUsed = llmResponse.TokensUsed
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI fleet report error");
            return StatusCode(503, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Ask a follow-up question about the fleet report (interactive Q&A)
    /// </summary>
    [HttpPost("fleet-report/ask")]
    public async Task<IActionResult> AskFleetReport([FromBody] FleetReportAskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "La question ne peut pas être vide" });

        var companyId = GetCompanyId();
        var company = await _context.Societes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == companyId);
        var vehicles = await _context.Vehicles.AsNoTracking().Where(v => v.CompanyId == companyId).ToListAsync();

        var sb = new StringBuilder();
        sb.AppendLine("Tu es un expert en gestion de flotte et consultant TCO. L'utilisateur pose une question de suivi sur le rapport de flotte.");
        sb.AppendLine($"Entreprise: {company?.Name ?? "N/A"} | Type: {company?.Type ?? "transport"} | {vehicles.Count} véhicules");
        sb.AppendLine("Réponds en français, de manière précise et structurée avec markdown.");
        sb.AppendLine("Utilise ta connaissance des marques/modèles pour donner des conseils spécifiques.");

        if (!string.IsNullOrWhiteSpace(request.ReportContext))
        {
            sb.AppendLine();
            sb.AppendLine("═══ CONTEXTE DU RAPPORT PRÉCÉDENT ═══");
            // Include a truncated version of the report context to stay within token limits
            var ctx = request.ReportContext.Length > 3000 ? request.ReportContext[..3000] + "..." : request.ReportContext;
            sb.AppendLine(ctx);
        }

        // Quick vehicle summary for context
        sb.AppendLine();
        sb.AppendLine("═══ VÉHICULES ═══");
        foreach (var v in vehicles.Take(20))
            sb.AppendLine($"- {v.Name} ({v.Plate}) | {v.Brand} {v.Model} {v.Year} | {v.Type} | {v.Mileage:N0} km");

        try
        {
            // 2000 tokens de réponse : contexte tronqué (3000 car.) + 20 véhicules
            // + réponse restent sous la limite TPM de l'offre (8000/min).
            var llmResponse = await _llmService.ChatAsync(sb.ToString(),
                new List<LlmMessage> { new("user", request.Question) }, 2000);

            return Ok(new { answer = llmResponse.Content, tokensUsed = llmResponse.TokensUsed });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI fleet report Q&A error");
            return StatusCode(503, new { message = ex.Message });
        }
    }
}

// ═══════ REQUEST / CONTEXT DTOs ═══════

public record AiChatRequest(int VehicleId, string Message);
public record CompareVehiclesRequest(List<int> VehicleIds, string? Question);
public record FleetReportRequest(string? Period, string? Question);
public record FleetReportAskRequest(string Question, string? ReportContext);

public class VehicleDiagnosticContext
{
    public string Name { get; set; } = "";
    public bool HasGpsDevice { get; set; }
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
