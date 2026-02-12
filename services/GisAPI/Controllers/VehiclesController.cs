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
        [FromQuery] int pageSize = 50)
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
        // Get base vehicle data from DB (always needed for vehicle info)
        var vehicles = await _mediator.Send(new GetVehiclesWithPositionsQuery());
        
        // Try to enhance positions from Redis cache (faster, more recent)
        var companyIdClaim = User.FindFirst("company_id")?.Value;
        if (int.TryParse(companyIdClaim, out var companyId))
        {
            var cachedPositions = await _redisCache.GetAllPositionsForCompanyAsync(companyId);
            if (cachedPositions.Any())
            {
                _logger.LogDebug("Found {Count} cached positions in Redis for company {CompanyId}", 
                    cachedPositions.Count, companyId);
                
                // Create lookup by device UID
                var cacheByDevice = cachedPositions.ToDictionary(p => p.DeviceUid, p => p);
                
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
                            var updatedPosition = new GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions.PositionDto(
                                dbPos?.Id ?? 0,
                                cached.Latitude,
                                cached.Longitude,
                                cached.IgnitionOn ? Math.Round(cached.SpeedKph) : 0,
                                cached.HeadingDeg,
                                cached.IgnitionOn,
                                cached.RecordedAt,
                                cached.FuelRaw != 0 ? cached.FuelRaw : dbPos?.FuelRaw,
                                dbPos?.TemperatureC,
                                dbPos?.BatteryLevel,
                                dbPos?.Address,
                                dbPos?.OdometerKm
                            );
                            
                            // Create updated vehicle with cached position
                            var idx = vehicles.IndexOf(vehicle);
                            if (vehicle.Stats != null)
                            {
                                vehicles[idx] = vehicle with { 
                                    LastPosition = updatedPosition,
                                    Stats = vehicle.Stats with {
                                        CurrentSpeed = cached.IgnitionOn ? Math.Round(cached.SpeedKph) : 0,
                                        IsMoving = cached.IgnitionOn && cached.SpeedKph > 5,
                                        IsStopped = !cached.IgnitionOn || cached.SpeedKph <= 5
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
        
        return Ok(vehicles);
    }

    [HttpPost("{id}/sync-mileage")]
    public async Task<ActionResult<SyncMileageResult>> SyncMileageFromGps(int id)
    {
        var result = await _mediator.Send(new SyncMileageCommand(id));
        return Ok(result);
    }
}
