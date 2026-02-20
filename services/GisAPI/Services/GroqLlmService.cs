using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Services;

public class GroqLlmService : ILlmService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<GroqLlmService> _logger;

    public GroqLlmService(IConfiguration configuration, ILogger<GroqLlmService> logger)
    {
        _logger = logger;
        _model = configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(configuration["Groq:ApiUrl"] ?? "https://api.groq.com/openai/v1/chat/completions"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        var apiKey = configuration["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning("Groq:ApiKey is not configured. AI chat will not work until the key is set via env var Groq__ApiKey.");
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<LlmResponse> ChatAsync(string systemPrompt, List<LlmMessage> messages, CancellationToken ct = default)
    {
        if (_httpClient.DefaultRequestHeaders.Authorization == null)
            throw new Exception("Clé API Groq non configurée. Définissez la variable d'environnement Groq__ApiKey.");

        var requestMessages = new List<object>
        {
            new { role = "system", content = systemPrompt }
        };

        foreach (var msg in messages)
        {
            requestMessages.Add(new { role = msg.Role, content = msg.Content });
        }

        var requestBody = new
        {
            model = _model,
            messages = requestMessages,
            temperature = 0.3,
            max_tokens = 2048,
            top_p = 0.9
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Groq API error {StatusCode}: {Body}", response.StatusCode, responseBody);
                throw new Exception($"Groq API returned {response.StatusCode}");
            }

            var result = JsonSerializer.Deserialize<GroqChatResponse>(responseBody);
            var reply = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "Pas de réponse disponible.";
            var tokens = result?.Usage?.TotalTokens ?? 0;

            return new LlmResponse(reply, tokens);
        }
        catch (TaskCanceledException)
        {
            throw new Exception("Le service IA est temporairement indisponible (timeout).");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Groq API");
            throw new Exception("Impossible de se connecter au service IA.");
        }
    }
}

// Groq API response models
public class GroqChatResponse
{
    [JsonPropertyName("choices")]
    public List<GroqChoice>? Choices { get; set; }

    [JsonPropertyName("usage")]
    public GroqUsage? Usage { get; set; }
}

public class GroqChoice
{
    [JsonPropertyName("message")]
    public GroqMessage? Message { get; set; }
}

public class GroqMessage
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

public class GroqUsage
{
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
