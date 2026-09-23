using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GisAPI.Application.Common.Security;
using GisAPI.Domain.Interfaces;
using GisAPI.Infrastructure.Persistence;
using GisAPI.Domain.Entities;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GpsDevicesController : ControllerBase
{
    private readonly GisDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public GpsDevicesController(GisDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    private int GetCompanyId() => int.Parse(User.FindFirst("companyId")?.Value ?? "0");

    /// <summary>
    /// Véhicules visibles par l'appelant. TROIS états, à ne jamais confondre :
    /// <c>null</c> = administrateur, AUCUN filtre ; liste non vide = ses véhicules ;
    /// liste VIDE = non-administrateur sans affectation, il ne voit RIEN.
    /// </summary>
    private Task<List<int>?> PorteeVehiculesAsync() =>
        VehicleScope.AccessibleVehicleIdsAsync(_context, _tenantService, HttpContext.RequestAborted);

    /// <summary>
    /// Boîtiers rattachés aux véhicules de la portée : <c>null</c> pour un administrateur
    /// (aucun filtre), sinon la liste — VIDE si l'appelant n'a aucun véhicule équipé.
    ///
    /// Ce contrôleur est la ROUTE SŒUR de « /api/gps/devices » : mêmes boîtiers, même
    /// chaîne d'exploitation (lister les IMEI, puis rejouer l'historique du boîtier),
    /// simplement sous une autre URL. Un boîtier rattaché à AUCUN véhicule n'entre dans
    /// la portée de personne : un non-administrateur n'y a donc pas accès.
    /// </summary>
    private async Task<List<int>?> PorteeBoitiersAsync(int companyId)
    {
        var porteeVehicules = await PorteeVehiculesAsync();
        if (porteeVehicules is null) return null;
        if (porteeVehicules.Count == 0) return new List<int>();

        return await _context.Vehicles
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.GpsDeviceId.HasValue && porteeVehicules.Contains(v.Id))
            .Select(v => v.GpsDeviceId!.Value)
            .Distinct()
            .ToListAsync(HttpContext.RequestAborted);
    }

    /// <summary>
    /// Ce boîtier est-il hors de la portée de l'appelant ? Le refus reste un 404
    /// identique à celui d'un boîtier inexistant : on ne révèle pas qu'il existe.
    /// </summary>
    private async Task<bool> BoitierHorsPorteeAsync(int companyId, int deviceId)
    {
        var portee = await PorteeBoitiersAsync(companyId);
        return portee is not null && !portee.Contains(deviceId);
    }

    [HttpGet]
    public async Task<ActionResult<List<GpsDevice>>> GetDevices()
    {
        var companyId = GetCompanyId();

        var query = _context.GpsDevices
            .Where(d => d.CompanyId == companyId)
            .Include(d => d.Vehicle)
            .AsQueryable();

        var portee = await PorteeBoitiersAsync(companyId);
        if (portee is not null)
        {
            List<int> ids = portee;
            query = query.Where(d => ids.Contains(d.Id));
        }

        var devices = await query
            .OrderBy(d => d.Label)
            .ToListAsync();

        return Ok(devices);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GpsDevice>> GetDevice(int id)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .Where(d => d.Id == id && d.CompanyId == companyId)
            .Include(d => d.Vehicle)
            .FirstOrDefaultAsync();

        if (device == null)
            return NotFound();

        if (await BoitierHorsPorteeAsync(companyId, id))
            return NotFound();

        return Ok(device);
    }

    [HttpGet("unassigned")]
    public async Task<ActionResult<List<GpsDevice>>> GetUnassignedDevices()
    {
        var companyId = GetCompanyId();

        // Boîtiers EN STOCK : rattachés à aucun véhicule, donc dans la portée de
        // personne. Liste vide pour tout non-administrateur — même décision que
        // « /api/gps/devices/available », qui publiait IMEI et numéro de SIM de tout
        // le stock de la société.
        if (!VehicleScope.SeesWholeFleet(_tenantService))
            return Ok(new List<GpsDevice>());

        var devices = await _context.GpsDevices
            .Where(d => d.CompanyId == companyId && d.Status == "unassigned")
            .OrderBy(d => d.Label)
            .ToListAsync();

        return Ok(devices);
    }

    [HttpPost]
    public async Task<ActionResult<GpsDevice>> CreateDevice([FromBody] GpsDevice device)
    {
        var companyId = GetCompanyId();

        if (await _context.GpsDevices.AnyAsync(d => d.DeviceUid == device.DeviceUid))
        {
            return BadRequest(new { message = "Un appareil avec cet IMEI existe déjà" });
        }

        device.CompanyId = companyId;
        device.Status = "unassigned";
        device.CreatedAt = DateTime.UtcNow;

        _context.GpsDevices.Add(device);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetDevice), new { id = device.Id }, device);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateDevice(int id, [FromBody] GpsDevice updated)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        if (await BoitierHorsPorteeAsync(companyId, id))
            return NotFound();

        device.Label = updated.Label;
        device.SimNumber = updated.SimNumber;
        device.SimOperator = updated.SimOperator;
        device.Model = updated.Model;
        device.Brand = updated.Brand;
        device.FirmwareVersion = updated.FirmwareVersion;
        device.Status = updated.Status;
        device.FuelSensorMode = updated.FuelSensorMode ?? "raw_255";
        device.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/assign/{vehicleId}")]
    public async Task<ActionResult> AssignToVehicle(int id, int vehicleId)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound(new { message = "Appareil non trouvé" });

        // Un boîtier EN STOCK est hors de la portée de tout non-administrateur : il ne
        // peut donc pas s'équiper lui-même (même conséquence assumée que pour la liste
        // des boîtiers disponibles). Un administrateur, lui, n'est pas filtré.
        if (await BoitierHorsPorteeAsync(companyId, id))
            return NotFound(new { message = "Appareil non trouvé" });

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.Id == vehicleId && v.CompanyId == companyId);

        if (vehicle == null)
            return NotFound(new { message = "Véhicule non trouvé" });

        // Rattacher un boîtier au véhicule d'un autre locataire, c'est lui voler sa
        // télémétrie : même 404 qu'un véhicule inexistant.
        if (!await VehicleScope.CanAccessVehicleAsync(_context, _tenantService, vehicleId, HttpContext.RequestAborted))
            return NotFound(new { message = "Véhicule non trouvé" });

        // Unassign from previous vehicle if any
        if (vehicle.GpsDeviceId.HasValue)
        {
            var previousDevice = await _context.GpsDevices.FindAsync(vehicle.GpsDeviceId);
            if (previousDevice != null)
            {
                previousDevice.Status = "unassigned";
            }
        }

        vehicle.GpsDeviceId = id;
        vehicle.HasGps = true;
        device.Status = "active";
        device.InstallationDate = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Appareil assigné avec succès" });
    }

    [HttpPost("{id}/unassign")]
    public async Task<ActionResult> UnassignFromVehicle(int id)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        // Débrancher le boîtier du véhicule d'un autre locataire, c'est le rendre muet.
        if (await BoitierHorsPorteeAsync(companyId, id))
            return NotFound();

        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.GpsDeviceId == id);

        if (vehicle != null)
        {
            vehicle.GpsDeviceId = null;
            vehicle.HasGps = false;
        }

        device.Status = "unassigned";
        await _context.SaveChangesAsync();

        return Ok(new { message = "Appareil désassigné avec succès" });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteDevice(int id)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        if (await BoitierHorsPorteeAsync(companyId, id))
            return NotFound();

        // Unassign from vehicle first
        var vehicle = await _context.Vehicles
            .FirstOrDefaultAsync(v => v.GpsDeviceId == id);

        if (vehicle != null)
        {
            vehicle.GpsDeviceId = null;
            vehicle.HasGps = false;
        }

        _context.GpsDevices.Remove(device);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id}/positions")]
    public async Task<ActionResult<List<GpsPosition>>> GetDevicePositions(
        int id,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int limit = 100)
    {
        var companyId = GetCompanyId();

        var device = await _context.GpsDevices
            .FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (device == null)
            return NotFound();

        // LA fuite de ce contrôleur : cette route rejoue la trajectoire complète du
        // boîtier, exactement comme « /api/gps/devices/{uid}/history », simplement
        // indexée par identifiant de boîtier au lieu de l'IMEI.
        if (await BoitierHorsPorteeAsync(companyId, id))
            return NotFound();

        var query = _context.GpsPositions
            .Where(p => p.DeviceId == id)
            .AsQueryable();

        if (startDate.HasValue)
            query = query.Where(p => p.RecordedAt >= startDate);

        if (endDate.HasValue)
            query = query.Where(p => p.RecordedAt <= endDate);

        var positions = await query
            .OrderByDescending(p => p.RecordedAt)
            .Take(limit)
            .ToListAsync();

        return Ok(positions);
    }
}
