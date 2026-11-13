using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Commands.SyncMileage;

public record SyncMileageCommand(int VehicleId) : ICommand<SyncMileageResult>;

/// <param name="Message">Pourquoi la fiche n'a pas été mise à jour (compteur refusé), sinon null.</param>
public record SyncMileageResult(
    int VehicleId,
    int PreviousMileage,
    int NewMileage,
    string Source,
    bool Updated,
    string? Message = null
);
