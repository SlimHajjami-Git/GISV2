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
    private readonly string _completionsPath;
    private readonly ILogger<GroqLlmService> _logger;

    public GroqLlmService(IConfiguration configuration, ILogger<GroqLlmService> logger)
    {
        _logger = logger;
        _model = configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";

        var apiUrl = configuration["Groq:ApiUrl"] ?? "https://api.groq.com/openai/v1/chat/completions";
        var uri = new Uri(apiUrl);
        // Separate base address (scheme+host) from path so HttpClient resolves correctly
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{uri.Scheme}://{uri.Authority}"),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _completionsPath = uri.PathAndQuery;

        var apiKey = configuration["Groq:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "your-groq-api-key")
        {
            _logger.LogWarning("Groq:ApiKey is not configured or is a placeholder. AI chat will not work. Set env var Groq__ApiKey with a valid key from https://console.groq.com/keys");
        }
        else
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public Task<LlmResponse> ChatAsync(string systemPrompt, List<LlmMessage> messages, CancellationToken ct = default)
        => ChatAsync(systemPrompt, messages, 2048, ct);

    public async Task<LlmResponse> ChatAsync(string systemPrompt, List<LlmMessage> messages, int maxTokens, CancellationToken ct = default)
    {
        var requestMessages = BuildInitialMessages(systemPrompt, messages);

        var requestBody = new
        {
            model = _model,
            messages = requestMessages,
            temperature = 0.3,
            max_tokens = maxTokens,
            top_p = 0.9
        };

        var result = await SendAsync(requestBody, ct);
        var reply = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "Pas de réponse disponible.";
        var tokens = result?.Usage?.TotalTokens ?? 0;

        return new LlmResponse(reply, tokens);
    }

    public async Task<LlmResponse> ChatWithToolsAsync(
        string systemPrompt,
        List<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        Func<string, string, CancellationToken, Task<string>> executeTool,
        int maxTokens,
        int maxToolRounds,
        CancellationToken ct = default)
    {
        var requestMessages = BuildInitialMessages(systemPrompt, messages);

        // OpenAI-compatible tools payload. Schemas are parsed to JsonElement so
        // they serialize as raw JSON objects, not escaped strings.
        var toolsPayload = tools.Select(t => new
        {
            type = "function",
            function = new
            {
                name = t.Name,
                description = t.Description,
                parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
            }
        }).ToList();

        var totalTokens = 0;

        // maxToolRounds tool-invoking rounds + 1 final forced-answer round (no
        // tools offered) so we always return text, never a dangling tool call.
        for (var round = 0; round <= maxToolRounds; round++)
        {
            var offerTools = round < maxToolRounds && toolsPayload.Count > 0;
            object requestBody = offerTools
                ? new { model = _model, messages = requestMessages, temperature = 0.3, max_tokens = maxTokens, top_p = 0.9, tools = toolsPayload, tool_choice = "auto" }
                : new { model = _model, messages = requestMessages, temperature = 0.3, max_tokens = maxTokens, top_p = 0.9 };

            var result = await SendAsync(requestBody, ct);
            totalTokens += result?.Usage?.TotalTokens ?? 0;
            var message = result?.Choices?.FirstOrDefault()?.Message;

            if (offerTools && message?.ToolCalls is { Count: > 0 } toolCalls)
            {
                // Echo the assistant tool-call message back verbatim (DTOs carry
                // the exact JSON property names), then append one result message
                // per call. A failing tool returns an error payload the model can
                // recover from instead of aborting the whole conversation.
                requestMessages.Add(new { role = "assistant", content = message.Content, tool_calls = toolCalls });

                foreach (var call in toolCalls)
                {
                    var name = call.Function?.Name ?? "";
                    var args = call.Function?.Arguments ?? "{}";
                    string toolResult;
                    try
                    {
                        toolResult = await executeTool(name, args, ct);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Tool {Tool} failed for args {Args}", name, args);
                        toolResult = JsonSerializer.Serialize(new { error = $"L'outil {name} a échoué. Réponds sans cette donnée et signale l'indisponibilité." });
                    }

                    requestMessages.Add(new { role = "tool", tool_call_id = call.Id, content = toolResult });
                }

                continue;
            }

            return new LlmResponse(message?.Content ?? "Pas de réponse disponible.", totalTokens);
        }

        // Unreachable: the final round never offers tools, so it always returns.
        throw new Exception("Le service IA n'a pas produit de réponse finale.");
    }

    private static List<object> BuildInitialMessages(string systemPrompt, List<LlmMessage> messages)
    {
        var requestMessages = new List<object> { new { role = "system", content = systemPrompt } };
        foreach (var msg in messages)
            requestMessages.Add(new { role = msg.Role, content = msg.Content });
        return requestMessages;
    }

    private async Task<GroqChatResponse?> SendAsync(object requestBody, CancellationToken ct)
    {
        if (_httpClient.DefaultRequestHeaders.Authorization == null)
            throw new Exception("Clé API Groq non configurée. Créez une clé sur https://console.groq.com/keys puis définissez la variable Groq__ApiKey.");

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_completionsPath, content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Groq API error {StatusCode}: {Body}", response.StatusCode, responseBody);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new Exception("Clé API Groq invalide ou expirée. Vérifiez votre clé sur https://console.groq.com/keys");
                throw new Exception($"Erreur Groq API: {response.StatusCode}");
            }

            return JsonSerializer.Deserialize<GroqChatResponse>(responseBody);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
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

    [JsonPropertyName("tool_calls")]
    public List<GroqToolCall>? ToolCalls { get; set; }
}

public class GroqToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public GroqToolFunction? Function { get; set; }
}

public class GroqToolFunction
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Raw JSON string of the arguments, exactly as the model sent them.</summary>
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }
}

public class GroqUsage
{
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
