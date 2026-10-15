using FluentValidation;

namespace GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;

/// <summary>
/// La modification écrasait la plaque et l'année avec la valeur reçue, vide ou absurde comprise
/// (erreur n°15 de la campagne GPA du 06/10/2026). Le carburant absent n'est pas modifié par le
/// handler : il n'est pas exigé ici.
/// </summary>
public class UpdateVehicleCommandValidator : AbstractValidator<UpdateVehicleCommand>
{
    public UpdateVehicleCommandValidator()
    {
        RuleFor(x => x.Plate)
            .Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage(VehicleInputRules.MessagePlaque)
            .MaximumLength(20).WithMessage(VehicleInputRules.MessagePlaqueLongue);

        RuleFor(x => x.Year)
            .Must(VehicleInputRules.AnneeValide).WithMessage(_ => VehicleInputRules.MessageAnnee);
    }
}
