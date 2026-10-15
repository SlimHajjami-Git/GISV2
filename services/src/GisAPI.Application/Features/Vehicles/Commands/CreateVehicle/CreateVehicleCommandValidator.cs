using FluentValidation;

namespace GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;

public class CreateVehicleCommandValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Le nom du véhicule est obligatoire.")
            .MaximumLength(100).WithMessage("Le nom du véhicule ne doit pas dépasser 100 caractères.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Le type de véhicule est obligatoire.");

        // Plaque, carburant et année : l'écran les exige ou les borne, l'API les acceptait vides
        // ou absurdes (erreur n°15 de la campagne GPA du 06/10/2026).
        RuleFor(x => x.Plate)
            .Must(p => !string.IsNullOrWhiteSpace(p)).WithMessage(VehicleInputRules.MessagePlaque)
            .MaximumLength(20).WithMessage(VehicleInputRules.MessagePlaqueLongue);

        RuleFor(x => x.FuelType)
            .Must(f => !string.IsNullOrWhiteSpace(f)).WithMessage(VehicleInputRules.MessageCarburant);

        RuleFor(x => x.Year)
            .Must(VehicleInputRules.AnneeValide).WithMessage(_ => VehicleInputRules.MessageAnnee);

        RuleFor(x => x.Mileage)
            .GreaterThanOrEqualTo(0).WithMessage("Le kilométrage ne peut pas être négatif.");
    }
}
