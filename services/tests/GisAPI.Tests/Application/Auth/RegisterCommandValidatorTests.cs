using FluentAssertions;
using GisAPI.Application.Features.Auth.Commands.Register;
using Xunit;

namespace GisAPI.Tests.Application.Auth;

/// <summary>
/// Règles de saisie de l'inscription libre.
///
/// Ces tests sont SÉPARÉS de ceux du handler pour une raison précise : le
/// validateur FluentValidation n'est pas exécuté quand on appelle le handler
/// directement — il passe par le pipeline MediatR. Tester le handler ne prouve
/// donc rien sur la politique de mot de passe, et réciproquement.
/// </summary>
public class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator = new();

    private static RegisterCommand Valid(string password = "MotDePasse#2026", string email = "sonia@exemple.tn") =>
        new("Sonia", "Ben Salah", email, password, "Transports Sonia", "+216 20 000 000");

    [Fact]
    public void Une_saisie_complete_est_acceptee()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Le_nom_de_societe_est_facultatif()
    {
        // Beaucoup d'indépendants n'ont pas encore de société : l'exiger les
        // bloquait. À défaut, la société prendra le nom de la personne.
        var cmd = new RegisterCommand("Sonia", "Ben Salah", "sonia@exemple.tn", "MotDePasse#2026", "", null);

        _validator.Validate(cmd).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Court#1")]          // moins de 10 caractères
    [InlineData("motdepasselong")]   // une seule classe de caractères
    [InlineData("motdepasse2026")]   // deux classes seulement
    public void Un_mot_de_passe_faible_est_refuse(string password)
    {
        var result = _validator.Validate(Valid(password: password));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCommand.Password));
    }

    [Fact]
    public void Un_mot_de_passe_contenant_l_email_est_refuse()
    {
        // Il serait deviné dès que l'adresse fuite.
        var result = _validator.Validate(Valid(password: "Sonia#2026Sonia", email: "sonia@exemple.tn"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("email"));
    }

    [Fact]
    public void Un_mot_de_passe_de_dix_caracteres_avec_trois_classes_est_accepte()
    {
        _validator.Validate(Valid(password: "Abcdefg#12")).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas-un-email")]
    [InlineData("manque@")]
    public void Un_email_invalide_est_refuse(string email)
    {
        var result = _validator.Validate(Valid(email: email));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCommand.Email));
    }

    [Fact]
    public void Le_prenom_et_le_nom_sont_obligatoires()
    {
        var cmd = new RegisterCommand("", "", "sonia@exemple.tn", "MotDePasse#2026", "Transports Sonia", null);

        var result = _validator.Validate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCommand.FirstName));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RegisterCommand.LastName));
    }

    [Fact]
    public void Les_messages_sont_en_francais()
    {
        // L'interface est francophone ; les messages étaient restés en anglais.
        var result = _validator.Validate(Valid(password: "court"));

        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().OnlyContain(e => !e.ErrorMessage.Contains("must") && !e.ErrorMessage.Contains("required"));
    }
}
