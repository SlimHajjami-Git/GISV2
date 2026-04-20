using System.Net;
using System.Text;
using System.Text.Json;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Services;

/// <summary>
/// HTTP client for the Rust gps-ingest service's <c>POST /commands/push</c> endpoint.
///
/// Base URL is <c>GpsIngest:BaseUrl</c> (default <c>http://gps-ingest:3000</c>,
/// matching the docker-compose service name on the internal network).
/// </summary>
public class RustCommandPusher : IRustCommandPusher
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RustCommandPusher> _logger;
    private readonly string _baseUrl;

    public RustCommandPusher(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<RustCommandPusher> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseUrl = (configuration["GpsIngest:BaseUrl"] ?? "http://gps-ingest:3000").TrimEnd('/');
    }

    public async Task<RustPushResult> PushAsync(int deviceId, string command, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);

        var url = $"{_baseUrl}/commands/push";
        var payload = JsonSerializer.Serialize(new { device_id = deviceId, command });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            var response = await client.PostAsync(url, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Rust push OK for device {DeviceId} ({Command})",
                    deviceId, command.TrimEnd('\n'));
                return new RustPushResult(RustPushOutcome.Pushed, body);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    "Rust push: device {DeviceId} not connected; DB-poll fallback will deliver on next frame",
                    deviceId);
                return new RustPushResult(RustPushOutcome.DeviceNotConnected, body);
            }

            _logger.LogError(
                "Rust push failed for device {DeviceId}: {Status} {Body}",
                deviceId, response.StatusCode, body);
            return new RustPushResult(RustPushOutcome.Error, body);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Rust push timeout for device {DeviceId}", deviceId);
            return new RustPushResult(RustPushOutcome.Error, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Rust push HTTP exception for device {DeviceId}", deviceId);
            return new RustPushResult(RustPushOutcome.Error, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rust push unexpected exception for device {DeviceId}", deviceId);
            return new RustPushResult(RustPushOutcome.Error, ex.Message);
        }
    }
}
