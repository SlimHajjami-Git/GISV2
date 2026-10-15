using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using GisAPI.Application.Features.Vehicles;
using GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;
using GisAPI.Application.Features.Vehicles.Commands.PatchVehicle;
using GisAPI.Application.Features.Vehicles.Commands.UpdateVehicle;
using GisAPI.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Vehicles;

/// <summary>
/// L'API acceptait un véhicule sans plaque, sans carburant ou avec l'année 1800 (campagne de
/// tests GPA du 06/10/2026, erreur n°15) ; le refus revenait en « Validation failed ».
/// </summary>
public class VehicleInputRulesTests
{
    private static CreateVehicleCommand Creation(string? plate = "GE-768-HJ", string? fuel = "diesel", int? year = 2020) =>
        new("Clio", "car", "Renault", "Clio", plate, year, null, 10_000, fuel);

    // Les commandes de modification portent une trentaine de champs : on part d'une commande
    // vide et on ne renseigne que ceux que la règle regarde.
    private static T Vide<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

    private static UpdateVehicleCommand Modification(string? plate, int? year) =>
        Vide<UpdateVehicleCommand>() with { Name = "Clio", Type = "car", Status = "active", Plate = plate, Year = year };

    private static PatchVehicleCommand ModificationPartielle(string? plate, int? year) =>
        Vide<PatchVehicleCommand>() with { Id = 1, Plate = plate, Year = year };

    private static string[] Erreurs<T>(IValidator<T> validator, T cmd) =>
        validator.Validate(cmd).Errors.Select(e => e.ErrorMessage).ToArray();

    [Fact]
    public void Une_fiche_complete_est_acceptee()
    {
        Erreurs(new CreateVehicleCommandValidator(), Creation()).Should().BeEmpty();
        Erreurs(new CreateVehicleCommandValidator(), Creation(year: null)).Should().BeEmpty();
        Erreurs(new CreateVehicleCommandValidator(), Creation(year: DateTime.UtcNow.Year + 1)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void La_creation_sans_plaque_est_refusee(string? plate)
    {
        Erreurs(new CreateVehicleCommandValidator(), Creation(plate: plate))
            .Should().Equal("La plaque d'immatriculation est obligatoire.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void La_creation_sans_carburant_est_refusee(string? fuel)
    {
        Erreurs(new CreateVehicleCommandValidator(), Creation(fuel: fuel))
            .Should().Equal("Le type de carburant est obligatoire.");
    }

    [Theory]
    [InlineData(1800)]
    [InlineData(0)]
    [InlineData(2100)]
    public void Une_annee_absurde_est_refusee_partout(int year)
    {
        var attendu = $"L'année doit être comprise entre 1900 et {DateTime.UtcNow.Year + 1}.";

        Erreurs(new CreateVehicleCommandValidator(), Creation(year: year)).Should().Equal(attendu);
        Erreurs(new UpdateVehicleCommandValidator(), Modification("GE-768-HJ", year)).Should().Equal(attendu);
        Erreurs(new PatchVehicleCommandValidator(), ModificationPartielle(null, year)).Should().Equal(attendu);
    }

    [Fact]
    public void La_modification_ne_peut_pas_effacer_la_plaque()
    {
        Erreurs(new UpdateVehicleCommandValidator(), Modification("", 2020))
            .Should().Equal("La plaque d'immatriculation est obligatoire.");
        Erreurs(new PatchVehicleCommandValidator(), ModificationPartielle(" ", null))
            .Should().Equal("La plaque d'immatriculation est obligatoire.");
    }

    [Fact]
    public void La_modification_partielle_sans_plaque_ni_annee_ne_touche_a_rien()
    {
        // Plaque absente = non modifiée (le handler la garde) : pas d'erreur.
        Erreurs(new PatchVehicleCommandValidator(), ModificationPartielle(null, null)).Should().BeEmpty();
    }

    [Fact]
    public async Task L_api_renvoie_le_message_en_francais_et_non_Validation_failed()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new ValidationException(new CreateVehicleCommandValidator().Validate(Creation(plate: "")).Errors),
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(http);

        http.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        http.Response.Body.Position = 0;
        using var json = await JsonDocument.ParseAsync(http.Response.Body);
        json.RootElement.GetProperty("message").GetString().Should().Be(VehicleInputRules.MessagePlaque);
        json.RootElement.GetProperty("errors").GetArrayLength().Should().Be(1);
    }
}
