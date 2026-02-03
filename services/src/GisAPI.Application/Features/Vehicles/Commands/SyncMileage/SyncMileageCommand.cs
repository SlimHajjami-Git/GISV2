using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Commands.SyncMileage;

public record SyncMileageCommand(int VehicleId) : ICommand<SyncMileageResult>;

public record SyncMileageResult(
    int VehicleId,
    int PreviousMileage,
    int NewMileage,
    string Source,
    bool Updated
);
