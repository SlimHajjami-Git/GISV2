using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.VehicleAssignments.Commands.AssignVehicle;

namespace GisAPI.Application.Features.VehicleAssignments.Queries.GetVehicleAssignments;

public record GetVehicleAssignmentsQuery(int? VehicleId = null, int? UserId = null) : IQuery<List<VehicleAssignmentDto>>;



