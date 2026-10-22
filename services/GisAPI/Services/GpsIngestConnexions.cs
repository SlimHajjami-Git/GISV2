using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Services;

/// <summary>
/// Client HTTP des connexions en attente tenues par l'ingestion Rust
/// (<c>GET /connexions/en-attente</c>, <c>POST /connexions/attribuer</c>).
/// Même adresse que <see cref="RustCommandPusher"/> : <c>GpsIngest:BaseUrl</c>
/// (défaut <c>http://gps-ingest:3000</c>).
/// </summary>
public class GpsIngestConnexions : IGpsIngestConnexions
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GpsIngestConnexions> _logger;
    private readonly string _baseUrl;

    public GpsIngestConnexions(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<GpsIngestConnexions> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = (configuration["GpsIngest:BaseUrl"] ?? "http://gps-ingest:3000").TrimEnd('/');
    }

    public async Task<IReadOnlyList<HeldConnection>?> ListAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        try
        {
            var response = await client.GetAsync($"{_baseUrl}/connexions/en-attente", ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Ingestion GPS : /connexions/en-attente a répondu {Status}", response.StatusCode);
                return null;
            }
            var body = await response.Content.ReadAsStringAsync(ct);
            var rows = JsonSerializer.Deserialize<List<ConnexionJson>>(body) ?? new List<ConnexionJson>();
            return rows.Select(r => new HeldConnection(
                r.Peer, r.Protocole, r.Ouverte,
                r.PremiereReception.UtcDateTime, r.DerniereReception.UtcDateTime,
                r.Trames, r.TramesEcartees,
                ToPosition(r.PremierePosition), ToPosition(r.DernierePosition))).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Ingestion GPS injoignable pour /connexions/en-attente");
            return null;
        }
    }

    public async Task<HeldConnectionAttribution> AttributeAsync(string peer, string imei, string by, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        // Le rejeu ingère jusqu'à quelques milliers de trames : laisser le temps.
        client.Timeout = TimeSpan.FromSeconds(120);
        var payload = JsonSerializer.Serialize(new { peer, imei, par = by });
        try
        {
            var response = await client.PostAsync($"{_baseUrl}/connexions/attribuer",
                new StringContent(payload, Encoding.UTF8, "application/json"), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    var rapport = JsonSerializer.Deserialize<RapportJson>(body);
                    return new HeldConnectionAttribution(HeldAttributionOutcome.Done,
                        rapport?.TramesRejouees ?? 0, rapport?.ConnexionOuverte ?? false, null);
                case HttpStatusCode.NotFound:
                    return new HeldConnectionAttribution(HeldAttributionOutcome.UnknownConnection, 0, false, null);
                case HttpStatusCode.Conflict:
                    var conflit = JsonSerializer.Deserialize<ConflitJson>(body);
                    return new HeldConnectionAttribution(HeldAttributionOutcome.AlreadyIdentified, 0, true, conflit?.Imei);
                default:
                    _logger.LogError("Ingestion GPS : /connexions/attribuer a répondu {Status} {Body}", response.StatusCode, body);
                    return new HeldConnectionAttribution(HeldAttributionOutcome.Error, 0, false, $"{(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Ingestion GPS injoignable pour /connexions/attribuer ({Peer})", peer);
            return new HeldConnectionAttribution(HeldAttributionOutcome.Error, 0, false, "ingestion injoignable");
        }
    }

    private static HeldPosition? ToPosition(PositionJson? p) =>
        p == null ? null : new HeldPosition(p.Lat, p.Lon, DateTime.SpecifyKind(p.Horodatage, DateTimeKind.Utc), p.VitesseKmh);

    // Contrat JSON de gps-ingest-rust/src/attente.rs (ConnexionEnAttente) et
    // transport.rs (RapportAttribution) : ne pas renommer d'un seul côté.
    private sealed class ConnexionJson
    {
        [JsonPropertyName("peer")] public string Peer { get; set; } = "";
        [JsonPropertyName("protocole")] public string Protocole { get; set; } = "";
        [JsonPropertyName("ouverte")] public bool Ouverte { get; set; }
        [JsonPropertyName("premiere_reception")] public DateTimeOffset PremiereReception { get; set; }
        [JsonPropertyName("derniere_reception")] public DateTimeOffset DerniereReception { get; set; }
        [JsonPropertyName("trames")] public int Trames { get; set; }
        [JsonPropertyName("trames_ecartees")] public long TramesEcartees { get; set; }
        [JsonPropertyName("premiere_position")] public PositionJson? PremierePosition { get; set; }
        [JsonPropertyName("derniere_position")] public PositionJson? DernierePosition { get; set; }
    }

    private sealed class PositionJson
    {
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("lon")] public double Lon { get; set; }
        /// <summary>Heure GPS sans fuseau (UTC) : « 2026-10-22T08:54:12 ».</summary>
        [JsonPropertyName("horodatage")] public DateTime Horodatage { get; set; }
        [JsonPropertyName("vitesse_kmh")] public double VitesseKmh { get; set; }
    }

    private sealed class RapportJson
    {
        [JsonPropertyName("connexion_ouverte")] public bool ConnexionOuverte { get; set; }
        [JsonPropertyName("trames_rejouees")] public int TramesRejouees { get; set; }
    }

    private sealed class ConflitJson
    {
        [JsonPropertyName("imei")] public string? Imei { get; set; }
    }
}
