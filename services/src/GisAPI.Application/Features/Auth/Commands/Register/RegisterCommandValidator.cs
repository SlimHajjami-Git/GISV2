using FluentValidation;

namespace GisAPI.Application.Features.Auth.Commands.Register;

/// <summary>
/// Règles de l'inscription libre.
///
/// Elles étaient calibrées pour un endpoint que personne ne pouvait appeler : six
/// caractères sans exigence de complexité, et des messages en anglais dans une
/// interface française. L'inscription devenant la seule porte d'écriture ouverte
/// sans jeton, la politique est durcie ici — et seulement ici : celle des comptes
/// créés par un administrateur est délibérément laissée telle quelle, la changer
/// dans le même geste modifierait le comportement de clients existants.
/// </summary>
public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Le prénom est obligatoire")
            .MaximumLength(100).WithMessage("Le prénom ne doit pas dépasser 100 caractères");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Le nom est obligatoire")
            .MaximumLength(100).WithMessage("Le nom ne doit pas dépasser 100 caractères");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("L'email est obligatoire")
            .EmailAddress().WithMessage("Format d'email invalide")
            .MaximumLength(255).WithMessage("L'email ne doit pas dépasser 255 caractères");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Le mot de passe est obligatoire")
            .MinimumLength(10).WithMessage("Le mot de passe doit contenir au moins 10 caractères")
            .MaximumLength(128).WithMessage("Le mot de passe ne doit pas dépasser 128 caractères")
            .Must(HasThreeCharacterClasses)
            .WithMessage("Le mot de passe doit mêler au moins trois types de caractères : minuscules, majuscules, chiffres, symboles");

        // Un mot de passe qui contient l'email est deviné dès que l'email fuite.
        RuleFor(x => x)
            .Must(cmd => !ContainsEmailLocalPart(cmd.Password, cmd.Email))
            .WithMessage("Le mot de passe ne doit pas contenir votre adresse email")
            .OverridePropertyName(nameof(RegisterCommand.Password));

        RuleFor(x => x.AccountType)
            .Must(AccountTypes.IsValid)
            .WithMessage("Type de compte invalide");

        // Le nom de société n'est exigé QUE d'un professionnel. Un particulier n'a
        // pas d'entreprise à déclarer : son espace prendra son nom, sans qu'il ait
        // à inventer quoi que ce soit.
        RuleFor(x => x.CompanyName)
            .NotEmpty().WithMessage("Le nom de la société est obligatoire")
            .When(x => x.AccountType == AccountTypes.Company);

        RuleFor(x => x.CompanyName)
            .MaximumLength(200).WithMessage("Le nom de la société ne doit pas dépasser 200 caractères");

        RuleFor(x => x.Phone)
            .MaximumLength(30).WithMessage("Le téléphone ne doit pas dépasser 30 caractères")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }

    private static bool HasThreeCharacterClasses(string? password)
    {
        if (string.IsNullOrEmpty(password)) return false;

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;
        return classes >= 3;
    }

    private static bool ContainsEmailLocalPart(string? password, string? email)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(email)) return false;

        var localPart = email.Split('@')[0].Trim();
        // En dessous de 4 caractères la comparaison rejetterait des mots de passe
        // corrects par pure coïncidence.
        if (localPart.Length < 4) return false;

        return password.Contains(localPart, StringComparison.OrdinalIgnoreCase);
    }
}
