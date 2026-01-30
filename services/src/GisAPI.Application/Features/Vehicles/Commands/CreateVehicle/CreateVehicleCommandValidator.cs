using FluentValidation;

namespace GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;

public class CreateVehicleCommandValidator : AbstractValidator<CreateVehicleCommand>
{
    public CreateVehicleCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Vehicle name is required")
            .MaximumLength(100).WithMessage("Vehicle name must not exceed 100 characters");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Vehicle type is required");

        RuleFor(x => x.Plate)
            .MaximumLength(20).WithMessage("Plate number must not exceed 20 characters");

        RuleFor(x => x.Mileage)
            .GreaterThanOrEqualTo(0).WithMessage("Mileage cannot be negative");
    }
}



