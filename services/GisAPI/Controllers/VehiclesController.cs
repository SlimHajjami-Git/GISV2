using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicles;
using GisAPI.Application.Features.Vehicles.Queries.GetVehicleDetails;
using GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;
using GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;
using GisAPI.Application.Features.Vehicles.Commands.DeleteVehicle;
using GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;
using GisAPI.Application.Features.Vehicles.Commands.SyncMileage;
using GisAPI.Application.Features.Vehicles.Commands.SetVehicleImmobilization;
using GisAPI.Services;

namespace GisAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class VehiclesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IRedisCacheService _redisCache;
    private readonly ILogger<VehiclesController> _logger;

    public VehiclesController(IMediator mediator, IRedisCacheService redisCache, ILogger<VehiclesController> logger)
    {
        _mediator = mediator;
        _redisCache = redisCache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<VehicleDto>>> GetVehicles(
        [FromQuery] string? searchTerm = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 500)
    {
        var result = await _mediator.Send(new GetVehiclesQuery(searchTerm, status, page, pageSize));
        return Ok(result.Items);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VehicleDetailsDto>> GetVehicle(int id)
    {
        var vehicle = await _mediator.Send(new GetVehicleDetailsQuery(id));
        
        if (vehicle == null)
            return NotFound();

        return Ok(vehicle);
    }

    [HttpPost]
    public async Task<ActionResult<int>> CreateVehicle([FromBody] CreateVehicleCommand command)
    {
        var vehicleId = await _mediator.Send(command);
        return CreatedAtAction(nameof(GetVehicle), new { id = vehicleId }, vehicleId);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateVehicle(int id, [FromBody] UpdateVehicleCommand command)
    {
        if (id != command.Id)
            return BadRequest("ID mismatch");

        await _mediator.Send(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteVehicle(int id)
    {
        await _mediator.Send(new DeleteVehicleCommand(id));
        return NoContent();
    }

    [HttpGet("with-positions")]
    public async Task<ActionResult<List<VehicleWithPositionDto>>> GetVehiclesWithPositions()
    {
        try
        {
            // Get base vehicle data from DB (always needed for vehicle info)
            var vehicles = await _mediator.Send(new GetVehiclesWithPositionsQuery());
            
            // Try to enhance positions from Redis cache (faster, more recent)
            try
            {
                var companyIdClaim = User.FindFirst("companyId")?.Value;
                if (int.TryParse(companyIdClaim, out var companyId))
                {
                    var cachedPositions = await _redisCache.GetAllPositionsForCompanyAsync(companyId);
                    if (cachedPositions.Any())
                    {
                        _logger.LogDebug("Found {Count} cached positions in Redis for company {CompanyId}", 
                            cachedPositions.Count, companyId);
                        
                        // Create lookup by device UID
                        var cacheByDevice = cachedPositions
                            .GroupBy(p => p.DeviceUid)
                            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.CachedAt).First());
                        
                        // Update vehicles with cached positions (more recent data)
                        foreach (var vehicle in vehicles.Where(v => v.DeviceUid != null))
                        {
                            if (cacheByDevice.TryGetValue(vehicle.DeviceUid!, out var cached))
                            {
                                // Only use cache if it's more recent than DB position
                                if (vehicle.LastPosition == null || cached.CachedAt > vehicle.LastPosition.RecordedAt)
                                {
                                    // Preserve DB values for fields Redis doesn't store
                                    var dbPos = vehicle.LastPosition;
                                    // BatteryVoltage: Redis already publishes the volts directly
                                    // (computed in redis_cache.rs with the same 0.3 factor used
                                    // server-side), so we trust the cache value first; fall back
                                    // to whatever the DB query returned earlier.
                                    //
                                    // Apply the same 14.4 V ceiling as the DB query path
                                    // (alternator regulator max on a 12 V system). Anything
                                    // higher is a firmware calibration artefact, not a real
                                    // reading — capping here keeps the UI consistent whether
                                    // a vehicle gets its volts from Redis or from DB.
                                    var rawCachedV = cached.BatteryVoltage ?? dbPos?.BatteryVoltage;
                                    double? cachedVoltage = rawCachedV.HasValue
                                        ? Math.Min(rawCachedV.Value, 14.4)
                                        : (double?)null;

                                    var updatedPosition = new GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions.PositionDto(
                                        dbPos?.Id ?? 0,
                                        cached.Latitude,
                                        cached.Longitude,
                                        cached.IgnitionOn ? Math.Round(cached.SpeedKph) : 0,
                                        cached.HeadingDeg,
                                        cached.IgnitionOn,
                                        cached.RecordedAt,
                                        cached.FuelRaw != 0 ? cached.FuelRaw : dbPos?.FuelRaw,
                                        (short?)(cached.TemperatureC ?? dbPos?.TemperatureC),
                                        cached.BatteryPercent ?? dbPos?.BatteryLevel,
                                        dbPos?.Address,
                                        cached.OdometerKm ?? dbPos?.OdometerKm,
                                        cachedVoltage
                                    );
                                    
                                    // Create updated vehicle with cached position
                                    var idx = vehicles.IndexOf(vehicle);
                                    if (vehicle.Stats != null)
                                    {
                                        // Update fuel level from Redis cache if available
                                        var cachedFuel = updatedPosition.FuelRaw;
                                        int? updatedFuelLevel = vehicle.Stats.FuelLevel;
                                        if (cachedFuel.HasValue && cachedFuel.Value > 0)
                                        {
                                            updatedFuelLevel = cachedFuel.Value; // Already raw %, will be refined by sensor mode if needed
                                        }

                                        vehicles[idx] = vehicle with {
                                            LastPosition = updatedPosition,
                                            Stats = vehicle.Stats with {
                                                CurrentSpeed = cached.IgnitionOn ? Math.Round(cached.SpeedKph) : 0,
                                                FuelLevel = updatedFuelLevel,
                                                Temperature = (short?)(cached.TemperatureC ?? vehicle.Stats.Temperature),
                                                BatteryLevel = cached.BatteryPercent ?? vehicle.Stats.BatteryLevel,
                                                BatteryVoltage = cachedVoltage ?? vehicle.Stats.BatteryVoltage,
                                                IsMoving = cached.IgnitionOn && cached.SpeedKph > 5,
                                                IsStopped = !cached.IgnitionOn || cached.SpeedKph <= 5,
                                                // Fresh frame is ignition-on → engine is currently
                                                // running, no "off since" to show. Otherwise keep
                                                // whatever the handler computed from the DB scan.
                                                EngineOffSince = cached.IgnitionOn
                                                    ? null
                                                    : (vehicle.Stats.EngineOffSince ?? cached.RecordedAt)
                                            }
                                        };
                                    }
                                    else
                                    {
                                        vehicles[idx] = vehicle with { LastPosition = updatedPosition };
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception redisEx)
            {
                _logger.LogWarning(redisEx, "Redis cache enhancement failed, returning DB-only positions");
            }
            
            return Ok(vehicles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get vehicles with positions");
            return StatusCode(500, new { error = "An internal error occurred. Please try again later." });
        }
    }

    [HttpPost("{id}/sync-mileage")]
    public async Task<ActionResult<SyncMileageResult>> SyncMileageFromGps(int id)
    {
        var result = await _mediator.Send(new SyncMileageCommand(id));
        return Ok(result);
    }

    /// <summary>
    /// Activate the "immobilisation" flag on a vehicle. While the flag
    /// is on, all automatic alert services skip this vehicle: accident
    /// detection, voltage health, speed limit, geofence. The vehicle
    /// stays visible on the monitoring page with a clear badge.
    ///
    /// <para>Idempotent — re-posting just refreshes the reason field.</para>
    /// </summary>
    /// <param name="id">Vehicle ID.</param>
    /// <param name="request">Free-text reason (e.g. "Garage Mahmoud — courroie").</param>
    [HttpPost("{id}/immobilize")]
    public async Task<ActionResult<SetVehicleImmobilizationResult>> Immobilize(
        int id, [FromBody] ImmobilizeRequest request)
    {
        var result = await _mediator.Send(
            new SetVehicleImmobilizationCommand(id, Activate: true, Reason: request?.Reason));
        return Ok(result);
    }

    /// <summary>
    /// Clear the immobilisation flag. Re-enables every automatic alert
    /// service for the vehicle as soon as the next monitoring cycle / next
    /// GPS frame arrives.
    /// </summary>
    [HttpDelete("{id}/immobilize")]
    public async Task<ActionResult<SetVehicleImmobilizationResult>> ClearImmobilization(int id)
    {
        var result = await _mediator.Send(
            new SetVehicleImmobilizationCommand(id, Activate: false, Reason: null));
        return Ok(result);
    }
}

public record ImmobilizeRequest(string? Reason);

