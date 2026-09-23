using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TramOnLineController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ILogger<TramOnLineController> _logger;
    private readonly ICurrentTenantService _tenantService;

    public TramOnLineController(GisDbContext context, ILogger<TramOnLineController> logger, ICurrentTenantService tenantService)
    {
        _context = context;
        _logger = logger;
        _tenantService = tenantService;
    }

    /// <summary>
    /// Enregistre un MAT (identifiant logique du boîtier). S'il est inconnu, CRÉE un boîtier
    /// et un véhicule « HTZ X ».
    ///
    /// <para><b>La route créait un véhicule sans aucun droit et dans la mauvaise société.</b>
    /// Le préfixe « /api/tramonline » ne figure dans aucune table de
    /// <c>PermissionMiddleware</c> : n'importe quel compte connecté, même restreint à deux
    /// véhicules, pouvait l'appeler. Et la société d'accueil n'était pas celle de l'appelant
    /// mais <c>Societes.OrderBy(Id).First()</c> — la PREMIÈRE société de la plate-forme, la
    /// table des tenants n'ayant volontairement aucun filtre multi-tenant. Un utilisateur
    /// d'une société créait donc boîtier et véhicule chez une AUTRE société, où ils
    /// apparaissaient dans les écrans d'un client qui n'avait rien demandé.</para>
    ///
    /// <para>Fermeture : créer un véhicule est une opération de parc, réservée à un appelant
    /// dont le périmètre EST la flotte entière (administrateur de société, administrateur
    /// système) ; et la création se fait dans la société DE L'APPELANT, jamais dans une
    /// société devinée.</para>
    /// </summary>
    [HttpPost("register/{mat}")]
    public async Task<ActionResult<MatRegistrationResponse>> RegisterMat(string mat)
    {
        if (!VehicleScope.SeesWholeFleet(_tenantService))
        {
            return new ObjectResult(new
            {
                message = "La création d'un véhicule est réservée aux administrateurs de la société."
            })
            { StatusCode = 403 };
        }

        if (string.IsNullOrWhiteSpace(mat))
        {
            return BadRequest("MAT is required.");
        }

        mat = mat.Trim();

        // Search by Mat field (GPS logical identifier), not DeviceUid (IMEI).
        // Le filtre global de GisDbContext borne déjà cette recherche à la société de
        // l'appelant — l'administrateur système, lui, a la vue plate-forme par définition.
        var existingDevice = await _context.GpsDevices
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync(d => d.Mat == mat);

        if (existingDevice != null)
        {
            return Ok(new MatRegistrationResponse(
                false,
                existingDevice.Mat ?? existingDevice.DeviceUid,
                existingDevice.Vehicle?.Name,
                existingDevice.Vehicle?.Id,
                existingDevice.Vehicle?.GpsDeviceId ?? existingDevice.Id));
        }

        // La société de l'APPELANT, jamais la première de la table.
        var companyId = _tenantService.CompanyId ?? 0;

        if (companyId == 0)
        {
            return BadRequest("No company configured. Please create a company first.");
        }

        // Comptage borné à la société : sans ce filtre explicite, un administrateur système
        // (filtres globaux levés) numéroterait « HTZ X » sur tout le parc de la plate-forme.
        var htzCount = await _context.Vehicles
            .CountAsync(v => v.CompanyId == companyId && v.Name.StartsWith("HTZ"));
        var vehicleName = $"HTZ {htzCount}";

        var gpsDevice = new GpsDevice
        {
            DeviceUid = $"AUTO_{mat}_{DateTime.UtcNow:yyyyMMddHHmmss}", // Temporary IMEI until real device connects
            Mat = mat, // MAT is the logical GPS identifier
            CompanyId = companyId,
            Status = "active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.GpsDevices.Add(gpsDevice);
        await _context.SaveChangesAsync();

        var vehicle = new Vehicle
        {
            Name = vehicleName,
            CompanyId = companyId,
            Status = "available",
            HasGps = true,
            GpsDeviceId = gpsDevice.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Vehicles.Add(vehicle);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created vehicle {VehicleName} for MAT {Mat}", vehicleName, mat);

        return Ok(new MatRegistrationResponse(true, mat, vehicle.Name, vehicle.Id, gpsDevice.Id));
    }

    /// <summary>
    /// Get positions for a MAT.
    /// </summary>
    [HttpGet("{mat}/positions")]
    public async Task<ActionResult<IEnumerable<MatPositionDto>>> GetPositions(string mat, [FromQuery] int limit = 100)
    {
        if (string.IsNullOrWhiteSpace(mat))
        {
            return BadRequest("MAT is required.");
        }

        // Search by Mat field (GPS logical identifier)
        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Mat == mat.Trim());

        if (device == null)
        {
            return NotFound($"No device found for MAT {mat}.");
        }

        // ROUTE SŒUR du rejeu de trajectoire : jusqu'à 1000 points du boîtier, indexés
        // par MAT. Le boîtier est résolu vers SON véhicule et on applique la portée
        // (null = administrateur, aucun filtre). Un boîtier rattaché à aucun véhicule
        // n'entre dans la portée de personne. Le refus reste le 404 « MAT inconnu ».
        var vehiculeDuBoitier = await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.GpsDeviceId == device.Id)
            .Select(v => (int?)v.Id)
            .FirstOrDefaultAsync();

        var horsPortee = vehiculeDuBoitier.HasValue
            ? !await VehicleScope.CanAccessVehicleAsync(_context, _tenantService, vehiculeDuBoitier.Value, HttpContext.RequestAborted)
            : !VehicleScope.SeesWholeFleet(_tenantService);

        if (horsPortee)
        {
            return NotFound($"No device found for MAT {mat}.");
        }

        limit = Math.Clamp(limit, 1, 1000);

        var positions = await _context.GpsPositions
            .Where(p => p.DeviceId == device.Id)
            .OrderByDescending(p => p.RecordedAt)
            .Take(limit)
            .Select(p => new MatPositionDto(
                p.Id,
                p.RecordedAt,
                p.Latitude,
                p.Longitude,
                p.SpeedKph,
                p.CourseDeg,
                p.IgnitionOn,
                p.OdometerKm,
                p.Address))
            .ToListAsync();

        return Ok(positions);
    }
}

public record MatRegistrationResponse(
    bool Created,
    string Mat,
    string? VehicleName,
    int? VehicleId,
    int DeviceId);

public record MatPositionDto(
    long Id,
    DateTime RecordedAt,
    double Latitude,
    double Longitude,
    double? SpeedKph,
    double? CourseDeg,
    bool? IgnitionOn,
    long? OdometerKm,
    string? Address);
