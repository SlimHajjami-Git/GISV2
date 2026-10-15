using FluentValidation;

namespace GisAPI.Application.Features.Auth.Commands.Login;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email est obligatoire")
            .EmailAddress().WithMessage("Format d'email invalide");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Le mot de passe est obligatoire");
    }
}



