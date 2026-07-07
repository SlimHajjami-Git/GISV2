using System.Collections.Generic;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesStatus;

/// <summary>
/// Cheap online/offline snapshot of the caller's vehicles — feeds the header
/// offline-bell. See <see cref="VehicleStatusDto"/> for why this exists separately
/// from GetVehiclesWithPositions.
/// </summary>
public record GetVehiclesStatusQuery() : IQuery<List<VehicleStatusDto>>;
