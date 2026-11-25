using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GisAPI.Application.Common.Security;
using GisAPI.Application.Features.Reports.Queries.GetStopsReport;
using GisAPI.Domain.Interfaces;
using GisAPI.Services;
using Xunit;

namespace GisAPI.Tests.Application.Reports;

/// <summary>
/// Adresse et code postal des arrêts pour un client de l'API (plainte du 25/11/2026 : « la liste
/// donne des adresses null, il n'y a pas de code postal »). L'écran n'est pas concerné : il calcule
/// l'adresse dans le navigateur.
/// </summary>
public class AdresseArretsApiTests
{
    private sealed class FauxGeocodeur : IGeocodingService
    {
        public readonly ConcurrentBag<(double, double)> Appels = new();
        public Func<double, double, CancellationToken, Task<GeocodedAddress?>> Reponse { get; set; } =
            (lat, lon, _) => Task.FromResult<GeocodedAddress?>(new GeocodedAddress($"Rue {lat:F4}", "1029"));

        public Task<GeocodedAddress?> ReverseGeocodeWithPostcodeAsync(double latitude, double longitude, CancellationToken ct = default)
        {
            Appels.Add((latitude, longitude));
            return Reponse(latitude, longitude, ct);
        }

        public Task<string?> ReverseGeocodeAsync(double latitude, double longitude) => throw new NotSupportedException();
        public Task<GeocodedLocation?> ReverseGeocodeStructuredAsync(double latitude, double longitude) => throw new NotSupportedException();
        public (int CacheHits, int CacheMisses, int CacheSize) GetCacheStats() => (0, 0, 0);
    }

    private static StopEntryDto Arret(double lat, double lon, string? adresse = null) =>
        new() { Latitude = lat, Longitude = lon, Address = adresse };

    private static List<StopsReportDto> Rapport(params StopEntryDto[] arrets) => new()
    {
        new StopsReportDto { Days = { new StopDayDto { Stops = arrets.ToList() } } }
    };

    [Fact]
    public async Task Chaque_arret_recoit_adresse_et_code_postal()
    {
        var geo = new FauxGeocodeur();
        var rapport = Rapport(Arret(36.80587, 10.16099), Arret(35.82643, 10.63335));

        var remplis = await StopAddressEnricher.EnrichAsync(rapport, geo, CancellationToken.None);

        remplis.Should().Be(2);
        rapport[0].Days[0].Stops.Should().OnlyContain(s => s.Address != null && s.PostalCode == "1029");
    }

    [Fact]
    public async Task Un_meme_lieu_ne_fait_qu_une_recherche()
    {
        var geo = new FauxGeocodeur();
        // 36.80587 et 36.80590 tombent dans le même point arrondi à ~11 m.
        var rapport = Rapport(Arret(36.80587, 10.16099), Arret(36.80590, 10.16101), Arret(36.80587, 10.16099));

        var remplis = await StopAddressEnricher.EnrichAsync(rapport, geo, CancellationToken.None);

        remplis.Should().Be(3);
        geo.Appels.Should().HaveCount(1);
    }

    [Fact]
    public async Task Au_dela_de_la_borne_l_adresse_reste_vide_sans_erreur()
    {
        var geo = new FauxGeocodeur();
        var arrets = Enumerable.Range(0, 10).Select(i => Arret(36 + i * 0.01, 10)).ToArray();
        var rapport = Rapport(arrets);

        var remplis = await StopAddressEnricher.EnrichAsync(rapport, geo, CancellationToken.None, maxLookups: 3);

        remplis.Should().Be(3);
        geo.Appels.Should().HaveCount(3);
        arrets.Count(a => a.Address is null).Should().Be(7);
    }

    [Fact]
    public async Task Le_budget_de_temps_coupe_les_recherches_trop_lentes()
    {
        var geo = new FauxGeocodeur
        {
            Reponse = async (_, _, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return null; }
        };
        var rapport = Rapport(Arret(36.8, 10.1), Arret(36.9, 10.2));

        var chrono = System.Diagnostics.Stopwatch.StartNew();
        var remplis = await StopAddressEnricher.EnrichAsync(rapport, geo, CancellationToken.None,
            budget: TimeSpan.FromMilliseconds(200));

        remplis.Should().Be(0);
        chrono.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
        rapport[0].Days[0].Stops.Should().OnlyContain(s => s.Address == null);
    }

    [Fact]
    public async Task Une_recherche_en_echec_n_empeche_pas_les_autres()
    {
        var geo = new FauxGeocodeur
        {
            Reponse = (lat, _, _) => lat < 36.85
                ? throw new HttpRequestException("Nominatim indisponible")
                : Task.FromResult<GeocodedAddress?>(new GeocodedAddress("Bardo", "2017"))
        };
        var echec = Arret(36.8, 10.1);
        var ok = Arret(36.9, 10.1);

        var remplis = await StopAddressEnricher.EnrichAsync(Rapport(echec, ok), geo, CancellationToken.None);

        remplis.Should().Be(1);
        echec.Address.Should().BeNull();
        ok.Address.Should().Be("Bardo");
        ok.PostalCode.Should().Be("2017");
    }

    [Fact]
    public async Task Position_absente_ou_adresse_deja_connue_pas_de_recherche()
    {
        var geo = new FauxGeocodeur();
        var sansPosition = Arret(0, 0);
        var dejaConnue = Arret(36.8, 10.1, "Déjà là");

        var remplis = await StopAddressEnricher.EnrichAsync(Rapport(sansPosition, dejaConnue), geo, CancellationToken.None);

        remplis.Should().Be(0);
        geo.Appels.Should().BeEmpty();
        sansPosition.Address.Should().BeNull();
        dejaConnue.Address.Should().Be("Déjà là");
    }

    [Fact]
    public async Task Le_client_qui_part_annule_l_appel()
    {
        var geo = new FauxGeocodeur
        {
            Reponse = async (_, _, ct) => { await Task.Delay(TimeSpan.FromSeconds(30), ct); return null; }
        };
        using var parti = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var appel = () => StopAddressEnricher.EnrichAsync(Rapport(Arret(36.8, 10.1)), geo, parti.Token);

        await appel.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void Seul_un_appel_par_cle_ou_jeton_est_reconnu()
    {
        ClaimsPrincipal Identite(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

        ApiTokenAccess.IsApiCall(Identite(new Claim(ApiTokenAccess.AuthMethodClaim, ApiTokenAccess.AuthMethodValue)))
            .Should().BeTrue();
        ApiTokenAccess.IsApiCall(Identite(new Claim(ClaimTypes.Name, "écran"))).Should().BeFalse();
        ApiTokenAccess.IsApiCall(new ClaimsPrincipal(new ClaimsIdentity())).Should().BeFalse();
        ApiTokenAccess.IsApiCall(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"address\":{\"road\":\"Boulevard du 9 Avril 1938\",\"postcode\":\"1029\"}}", "1029")]
    [InlineData("{\"address\":{\"road\":\"RR42\"}}", null)]
    [InlineData("{\"address\":{\"postcode\":\"  \"}}", null)]
    [InlineData("{\"error\":\"Unable to geocode\"}", null)]
    public void Code_postal_lu_dans_la_reponse_Nominatim(string json, string? attendu)
    {
        using var doc = JsonDocument.Parse(json);
        GeocodingService.ExtractPostcode(doc).Should().Be(attendu);
    }
}
