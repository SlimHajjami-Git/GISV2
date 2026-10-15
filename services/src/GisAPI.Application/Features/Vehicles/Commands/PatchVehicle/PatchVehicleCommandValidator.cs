using FluentValidation;

namespace GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;

/// <summary>
/// Modification partielle : un champ absent n'est pas touché, mais une plaque ENVOYÉE vide
/// effaçait la plaque et une année absurde était acceptée (erreur n°15 de la campagne GPA du
/// 06/10/2026).
/// </summary>
public class PatchVehicleCommandValidator : AbstractValidator<PatchVehicleCommand>
{
    public PatchVehicleCommandValidator()
    {
        RuleFor(x => x.Plate)
            .Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage(VehicleInputRules.MessagePlaque)
            .MaximumLength(20).WithMessage(VehicleInputRules.MessagePlaqueLongue)
            .When(x => x.Plate != null);

        RuleFor(x => x.Year)
            .Must(VehicleInputRules.AnneeValide).WithMessage(_ => VehicleInputRules.MessageAnnee);
    }
}
