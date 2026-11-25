using System.Security.Claims;
using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Reports.Queries.GetStopsReport;
using GisAPI.Domain.Entities;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using GisAPI.Tests.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Adresse et code postal des arrêts pour les appels par API (24/11/2026) : un client
/// recevait « address: null » sur /api/reports/stops, le géocodage n'existant que dans
/// l'écran, et demandait le code postal, absent du DTO.
/// </summary>
public class StopAddressesTests
{
    private static readonly ReverseAddress Tunis = new("Rue Taïeb Mhiri, Tunis", "2015");

    private static StopEntryDto Arret(double lat, double lon) => new() { Latitude = lat, Longitude = lon };

    // ── L'utilitaire ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deux_arrets_au_meme_endroit_ne_coutent_qu_un_appel()
    {
        var geo = new Mock<IGeocodingService>();
        geo.Setup(g => g.ReverseGeocodeAddressAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tunis);
        // Même dépôt à quelques mètres près (bruit GPS), puis un autre lieu.
        var arrets = new[] { Arret(36.80612, 10.18114), Arret(36.80614, 10.18112), Arret(35.0, 10.5) };

        await StopAddresses.EnrichStopsAsync(geo.Object, arrets, TimeSpan.FromSeconds(5), CancellationToken.None);

        geo.Verify(g => g.ReverseGeocodeAddressAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        arrets.Should().OnlyContain(a => a.Address == "Rue Taïeb Mhiri, Tunis" && a.PostalCode == "2015");
    }

    [Fact]
    public async Task Un_geocodage_trop_lent_laisse_l_adresse_nulle_sans_bloquer_la_reponse()
    {
        var geo = new Mock<IGeocodingService>();
        geo.Setup(g => g.ReverseGeocodeAddressAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .Returns(async (double _, double _, CancellationToken ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return Tunis;
            });
        var arrets = Enumerable.Range(0, 10).Select(i => Arret(36 + i * 0.01, 10)).ToList();

        var chrono = System.Diagnostics.Stopwatch.StartNew();
        await StopAddresses.EnrichStopsAsync(geo.Object, arrets, TimeSpan.FromMilliseconds(300), CancellationToken.None);

        chrono.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        arrets.Should().OnlyContain(a => a.Address == null && a.PostalCode == null);
    }

    [Fact]
    public async Task Sans_reponse_du_fournisseur_l_arret_reste_sans_adresse()
    {
        var geo = new Mock<IGeocodingService>();
        geo.Setup(g => g.ReverseGeocodeAddressAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReverseAddress?)null);
        var arret = Arret(36.8, 10.18);

        await StopAddresses.EnrichStopsAsync(geo.Object, new[] { arret }, TimeSpan.FromSeconds(5), CancellationToken.None);

        arret.Address.Should().BeNull();
    }

    [Fact]
    public void Seuls_le_jeton_et_la_cle_d_api_sont_des_appels_par_api()
    {
        var api = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ApiTokenAccess.AuthMethodClaim, ApiTokenAccess.AuthMethodValue) }, "ApiToken"));
        var session = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("companyId", "1") }, "Bearer"));

        ApiTokenAccess.IsApiCall(api).Should().BeTrue();
        ApiTokenAccess.IsApiCall(session).Should().BeFalse();
        ApiTokenAccess.IsApiCall(null).Should().BeFalse();
    }

    // ── Le rapport d'arrêts ─────────────────────────────────────────────────────

    private static readonly DateTime Jour = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<TestGisDbContext> UnArretAsync()
    {
        var ctx = TestDbContextFactory.Create();
        ctx.Vehicles.Add(new Vehicle { Id = 1, Name = "Partner", Plate = "200 TU 1", CompanyId = 1, GpsDeviceId = 11 });
        GpsPosition P(long id, int minute, bool contact, double lat, double lon) => new()
        {
            Id = id, DeviceId = 11, RecordedAt = Jour.AddHours(8).AddMinutes(minute), IgnitionOn = contact,
            Latitude = lat, Longitude = lon, CreatedAt = Jour
        };
        ctx.GpsPositions.AddRange(
            P(1, 0, true, 36.80, 10.18),
            P(2, 10, false, 36.80612, 10.18114),
            P(3, 40, false, 36.80612, 10.18114),
            P(4, 60, true, 36.81, 10.19));
        await ctx.SaveChangesAsync();
        return ctx;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Le_rapport_n_est_geocode_que_pour_un_appel_par_api(bool parApi)
    {
        using var ctx = await UnArretAsync();
        var geo = new Mock<IGeocodingService>();
        geo.Setup(g => g.ReverseGeocodeAddressAsync(It.IsAny<double>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tunis);
        var handler = new GetStopsReportQueryHandler(ctx, TestDbContextFactory.CreateMockTenantService(1).Object, geo.Object);

        var rapport = await handler.Handle(new GetStopsReportQuery(1, Jour, Jour, 60, WithAddresses: parApi), CancellationToken.None);

        var arret = rapport.Days.Should().ContainSingle().Which.Stops.Should().ContainSingle().Which;
        if (parApi)
        {
            arret.Address.Should().Be("Rue Taïeb Mhiri, Tunis");
            arret.PostalCode.Should().Be("2015");
        }
        else
        {
            // L'écran géocode lui-même : rien ne change pour lui, aucun appel au fournisseur.
            arret.Address.Should().BeNull();
            geo.VerifyNoOtherCalls();
        }
    }

    // ── Le service : code postal lu dans la réponse Nominatim ───────────────────

    [Fact]
    public async Task Le_service_lit_l_adresse_et_le_code_postal_et_les_met_en_cache()
    {
        var appels = 0;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var faux = builder.Build();
        faux.MapGet("/reverse", (HttpContext c) =>
        {
            appels++;
            var lat = c.Request.Query["lat"].ToString();
            return lat.StartsWith("36.8")
                ? Results.Text("""{"address":{"road":"Rue Taïeb Mhiri","city":"Tunis","postcode":"2015"},"display_name":"x"}""", "application/json")
                : Results.Text("""{"error":"Unable to geocode"}""", "application/json");
        });
        await faux.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Nominatim:Url"] = faux.Urls.First() })
            .Build();
        var service = new GeocodingService(config);

        var premier = await service.ReverseGeocodeAddressAsync(36.80612, 10.18114);
        var second = await service.ReverseGeocodeAddressAsync(36.80613, 10.18113);   // même case de ~11 m
        var nulle = await service.ReverseGeocodeAddressAsync(20.0, 20.0);              // point sans adresse

        premier.Should().Be(new ReverseAddress("Rue Taïeb Mhiri, Tunis", "2015"));
        second.Should().Be(premier);
        appels.Should().Be(2, "le second point est servi par le cache");
        nulle.Should().Be(new ReverseAddress(null, null), "jamais le texte « Adresse inconnue » dans l'API");
        // Les appelants historiques gardent leur libellé de repli.
        (await service.ReverseGeocodeAsync(20.0, 20.0)).Should().Be("Adresse inconnue");

        await faux.StopAsync();
    }
}
