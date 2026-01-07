using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GisAPI.Application.Common.Interfaces;


namespace GisAPI.Application.Features.Vehicles.Queries.GetVehiclesWithPositions;

public record GetVehiclesWithPositionsQuery() : IQuery<List<VehicleWithPositionDto>>;
