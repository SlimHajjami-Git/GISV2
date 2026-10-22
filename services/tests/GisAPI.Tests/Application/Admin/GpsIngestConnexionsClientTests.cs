using System.Net;
using System.Text;
using FluentAssertions;
using GisAPI.Application.Common.Interfaces;
using GisAPI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GisAPI.Tests.Application.Admin;

/// <summary>
/// Contrat JSON avec l'ingestion Rust (attente.rs, transport.rs) : les réponses
/// ci-dessous sont celles du vrai binaire, relevées le 22/10/2026 sur un essai
/// local (boîtier simulé sur le port 6100).
/// </summary>
public class GpsIngestConnexionsClientTests
{
    // Réponse exacte du binaire : heures de réception à la NANOseconde (9 décimales).
    private const string ListeReelle = """
        [{"peer":"172.18.0.1:45534","protocole":"gps_type_1","ouverte":true,"premiere_reception":"2026-10-22T10:54:39.734878808Z","derniere_reception":"2026-10-22T10:54:41.230607536Z","trames":2,"trames_ecartees":0,"premiere_position":{"lat":35.618249999999996,"lon":9.927138333333334,"horodatage":"2026-10-19T09:18:04","vitesse_kmh":0.0},"derniere_position":{"lat":35.618249999999996,"lon":9.927138333333334,"horodatage":"2026-10-19T09:18:14","vitesse_kmh":0.0}}]
        """;

    [Fact]
    public async Task Liste_LitLeJsonDeLIngestion()
    {
        var client = Client(HttpStatusCode.OK, ListeReelle);

        var liste = await client.ListAsync();

        liste.Should().ContainSingle();
        var c = liste![0];
        c.Peer.Should().Be("172.18.0.1:45534");
        c.Protocol.Should().Be("gps_type_1");
        c.Open.Should().BeTrue();
        c.Frames.Should().Be(2);
        c.FirstReceivedAt.Should().BeCloseTo(new DateTime(2026, 10, 22, 10, 54, 39, 735, DateTimeKind.Utc), TimeSpan.FromMilliseconds(1));
        c.FirstReceivedAt.Kind.Should().Be(DateTimeKind.Utc);
        c.FirstPosition!.Lat.Should().BeApproximately(35.61825, 1e-7);
        c.FirstPosition.At.Should().Be(new DateTime(2026, 10, 19, 9, 18, 4, DateTimeKind.Utc));
        c.FirstPosition.At.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Liste_IngestionEnErreur_RendNull()
    {
        (await Client(HttpStatusCode.ServiceUnavailable, "[]").ListAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Attribution_LitLeRapport()
    {
        var client = Client(HttpStatusCode.OK,
            """{"connexion_ouverte":true,"imei":"860000000000101","peer":"172.18.0.1:45534","trames_en_echec":0,"trames_rejouees":2}""");

        var r = await client.AttributeAsync("172.18.0.1:45534", "860000000000101", "admin@exemple.test");

        r.Outcome.Should().Be(HeldAttributionOutcome.Done);
        r.ReplayedFrames.Should().Be(2);
        r.ConnectionOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Attribution_ConflitEtInconnue()
    {
        var conflit = await Client(HttpStatusCode.Conflict, """{"erreur":"deja_identifiee","imei":"860000000000209"}""")
            .AttributeAsync("p", "i", "x");
        conflit.Outcome.Should().Be(HeldAttributionOutcome.AlreadyIdentified);
        conflit.Detail.Should().Be("860000000000209");

        var inconnue = await Client(HttpStatusCode.NotFound, """{"erreur":"connexion_inconnue"}""")
            .AttributeAsync("p", "i", "x");
        inconnue.Outcome.Should().Be(HeldAttributionOutcome.UnknownConnection);
    }

    private static GpsIngestConnexions Client(HttpStatusCode status, string body)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GpsIngest:BaseUrl"] = "http://ingest.test" })
            .Build();
        return new GpsIngestConnexions(new FakeFactory(status, body), config, NullLogger<GpsIngestConnexions>.Instance);
    }

    private sealed class FakeFactory : IHttpClientFactory
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public FakeFactory(HttpStatusCode status, string body) { _status = status; _body = body; }
        public HttpClient CreateClient(string name) => new(new FakeHandler(_status, _body));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public FakeHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
    }
}
