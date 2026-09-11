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
    private readonly IReadOnlyList<string> _visionModels;
    private readonly string _completionsPath;
    private readonly ILogger<GroqLlmService> _logger;

    /// <summary>
    /// Modèles multimodaux essayés DANS L'ORDRE pour un scan d'image (facture).
    ///
    /// Pourquoi une liste : Groq a retiré meta-llama/llama-4-scout début août 2026 —
    /// chaque scan d'image répondait 404 « model_not_found » et le client lisait
    /// « service IA momentanément indisponible ». Aucun scan n'a abouti du 06/08 au
    /// 11/09. Un modèle retiré, saturé (429) ou en panne fait désormais passer au
    /// suivant au lieu de casser la fonctionnalité en silence.
    /// Configurable par Groq:VisionModels (séparés par des virgules) ou Groq:VisionModel.
    /// </summary>
    public static readonly string[] DefaultVisionModels = { "qwen/qwen3.8-27b", "qwen/qwen3.6-27b" };

    public GroqLlmService(IConfiguration configuration, ILogger<GroqLlmService> logger)
        : this(configuration, logger, null) { }

    /// <summary>Handler HTTP injectable pour les tests ; null = réseau réel.</summary>
    internal GroqLlmService(IConfiguration configuration, ILogger<GroqLlmService> logger, HttpMessageHandler? handler)
    {
        _logger = logger;
        _model = configuration["Groq:Model"] ?? "llama-3.3-70b-versatile";
        var configured = configuration["Groq:VisionModels"] ?? configuration["Groq:VisionModel"];
        var models = (configured ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        // Les modèles par défaut restent en repli derrière ceux de la configuration :
        // une valeur périmée dans appsettings ne peut plus, à elle seule, couper le scan.
        foreach (var m in DefaultVisionModels)
            if (!models.Contains(m, StringComparer.OrdinalIgnoreCase)) models.Add(m);
        _visionModels = models;

        var apiUrl = configuration["Groq:ApiUrl"] ?? "https://api.groq.com/openai/v1/chat/completions";
        var uri = new Uri(apiUrl);
        // Separate base address (scheme+host) from path so HttpClient resolves correctly
        _httpClient = new HttpClient(handler ?? new HttpClientHandler())
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

            GroqChatResponse? result;
            try
            {
                result = await SendAsync(requestBody, ct);
            }
            catch (Exception ex) when (offerTools && ex is not OperationCanceledException)
            {
                // llama sometimes emits malformed tool syntax on conversational /
                // meta questions and Groq rejects the WHOLE completion with 400
                // tool_use_failed. The question itself is fine — retry the same
                // round without tools instead of failing the user.
                _logger.LogWarning(ex, "Tools round failed — retrying without tools");
                var retry = await SendAsync(new { model = _model, messages = requestMessages, temperature = 0.3, max_tokens = maxTokens, top_p = 0.9 }, ct);
                totalTokens += retry?.Usage?.TotalTokens ?? 0;
                return new LlmResponse(retry?.Choices?.FirstOrDefault()?.Message?.Content ?? "Pas de réponse disponible.", totalTokens);
            }

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

    public async Task<LlmResponse> ExtractJsonAsync(
        string systemPrompt, string userText, string? imageDataUrl, int maxTokens, CancellationToken ct = default)
    {
        // The user message content is an array (OpenAI multimodal format): the
        // instruction text plus, optionally, the image as a data: URL.
        var userContent = new List<object> { new { type = "text", text = userText } };
        if (!string.IsNullOrWhiteSpace(imageDataUrl))
            userContent.Add(new { type = "image_url", image_url = new { url = imageDataUrl } });

        var messages = new object[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userContent }
        };

        // Texte seul (PDF avec couche texte) : un modèle, comme avant.
        if (string.IsNullOrWhiteSpace(imageDataUrl))
        {
            var textResult = await SendAsync(JsonRequest(_model, messages, maxTokens), ct);
            return new LlmResponse(textResult?.Choices?.FirstOrDefault()?.Message?.Content ?? "{}", textResult?.Usage?.TotalTokens ?? 0);
        }

        // Image (photo, PDF scanné) : les modèles vision dans l'ordre.
        Exception? last = null;
        foreach (var model in _visionModels)
        {
            try
            {
                var result = await SendAsync(JsonRequest(model, messages, maxTokens), ct);
                var reply = result?.Choices?.FirstOrDefault()?.Message?.Content ?? "{}";
                return new LlmResponse(reply, result?.Usage?.TotalTokens ?? 0);
            }
            catch (GroqApiException ex) when (IsWorthNextModel(ex.StatusCode))
            {
                _logger.LogWarning("Modèle vision {Model} indisponible ({Status}) — repli sur le suivant", model, (int)ex.StatusCode);
                last = ex;
            }
            catch (Exception ex) when (ex is not GroqApiException && !ct.IsCancellationRequested)
            {
                // Timeout ou connexion : le modèle suivant a sa chance aussi.
                _logger.LogWarning(ex, "Modèle vision {Model} en échec — repli sur le suivant", model);
                last = ex;
            }
        }
        throw last ?? new Exception("Aucun modèle vision configuré.");
    }

    /// <summary>
    /// Requête d'extraction JSON (mode json_object : ni prose, ni balises de code).
    /// Les Qwen 3 sont des modèles « à raisonnement » : leur réflexion par défaut casse
    /// le mode JSON (400 « Failed to validate JSON » mesuré sur qwen3.6 le 11/09/2026).
    /// On la coupe, ce qui rend aussi la réponse plus rapide.
    /// </summary>
    internal static object JsonRequest(string model, object[] messages, int maxTokens) =>
        model.Contains("qwen", StringComparison.OrdinalIgnoreCase)
            ? new { model, messages, temperature = 0.1, max_tokens = maxTokens, response_format = new { type = "json_object" }, reasoning_effort = "none" }
            : new { model, messages, temperature = 0.1, max_tokens = maxTokens, response_format = new { type = "json_object" } };

    /// <summary>
    /// Un autre modèle peut réussir là où celui-ci échoue : retiré (404/410), requête
    /// refusée par CE modèle (400 — format, JSON invalide), saturé (429) ou en panne (5xx).
    /// Une clé invalide (401/403) échouerait partout : inutile d'insister.
    /// </summary>
    internal static bool IsWorthNextModel(System.Net.HttpStatusCode status) =>
        status is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone
            or System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.TooManyRequests
        || (int)status >= 500;

    public async Task<LlmResponse> ChatStreamWithToolsAsync(
        string systemPrompt,
        List<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        Func<string, string, CancellationToken, Task<string>> executeTool,
        Func<string, Task> onDelta,
        int maxTokens,
        int maxToolRounds,
        CancellationToken ct = default)
    {
        var requestMessages = BuildInitialMessages(systemPrompt, messages);

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

        for (var round = 0; round <= maxToolRounds; round++)
        {
            var offerTools = round < maxToolRounds && toolsPayload.Count > 0;
            object requestBody = offerTools
                ? new { model = _model, messages = requestMessages, temperature = 0.3, max_tokens = maxTokens, top_p = 0.9, stream = true, tools = toolsPayload, tool_choice = "auto" }
                : new { model = _model, messages = requestMessages, temperature = 0.3, max_tokens = maxTokens, top_p = 0.9, stream = true };

            string content; List<GroqToolCall> toolCalls; int tokens;
            try
            {
                (content, toolCalls, tokens) = await StreamRoundAsync(requestBody, onDelta, ct);
            }
            catch (Exception ex) when (offerTools && ex is not OperationCanceledException)
            {
                // Same tool_use_failed recovery as the non-streamed path: Groq
                // rejects the round up-front (nothing streamed yet), so we can
                // safely re-run it without tools and stream that answer.
                _logger.LogWarning(ex, "Streamed tools round failed — retrying without tools");
                (content, toolCalls, tokens) = await StreamRoundAsync(
                    new { model = _model, messages = requestMessages, temperature = 0.3, max_tokens = maxTokens, top_p = 0.9, stream = true }, onDelta, ct);
                totalTokens += tokens;
                return new LlmResponse(string.IsNullOrEmpty(content) ? "Pas de réponse disponible." : content, totalTokens);
            }
            totalTokens += tokens;

            if (offerTools && toolCalls.Count > 0)
            {
                requestMessages.Add(new { role = "assistant", content = string.IsNullOrEmpty(content) ? null : content, tool_calls = toolCalls });

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

            return new LlmResponse(string.IsNullOrEmpty(content) ? "Pas de réponse disponible." : content, totalTokens);
        }

        throw new Exception("Le service IA n'a pas produit de réponse finale.");
    }

    /// <summary>
    /// One streamed completion round. Reads Groq's SSE ("data: {chunk}" lines,
    /// terminated by "data: [DONE]"), returning the assembled text, the
    /// re-assembled tool calls (delta.tool_calls arrive as index-keyed
    /// fragments: id/name first, arguments concatenated across chunks) and the
    /// usage from the final chunk (x_groq.usage). Content deltas are forwarded
    /// to <paramref name="onDelta"/> ONLY while no tool fragment has been seen:
    /// a tool round must stay silent for the end user.
    /// </summary>
    private async Task<(string Content, List<GroqToolCall> ToolCalls, int Tokens)> StreamRoundAsync(
        object requestBody, Func<string, Task> onDelta, CancellationToken ct)
    {
        if (_httpClient.DefaultRequestHeaders.Authorization == null)
            throw new Exception("Clé API Groq non configurée. Créez une clé sur https://console.groq.com/keys puis définissez la variable Groq__ApiKey.");

        var json = JsonSerializer.Serialize(requestBody);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _completionsPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new Exception("Le service IA est temporairement indisponible (timeout).");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Groq API (stream)");
            throw new Exception("Impossible de se connecter au service IA.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Groq API stream error {StatusCode}: {Body}", response.StatusCode, body);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new Exception("Clé API Groq invalide ou expirée. Vérifiez votre clé sur https://console.groq.com/keys");
                throw new Exception($"Erreur Groq API: {response.StatusCode}");
            }

            var content = new StringBuilder();
            // index → in-progress tool call (arguments concatenate across chunks)
            var toolAcc = new SortedDictionary<int, (string? Id, string? Name, StringBuilder Args)>();
            var tokens = 0;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var payload = line["data:".Length..].Trim();
                if (payload.Length == 0) continue;
                if (payload == "[DONE]") break;

                GroqStreamChunk? chunk;
                try { chunk = JsonSerializer.Deserialize<GroqStreamChunk>(payload); }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Unparseable Groq stream chunk: {Chunk}", payload.Length > 200 ? payload[..200] : payload);
                    continue;
                }

                tokens = Math.Max(tokens, chunk?.XGroq?.Usage?.TotalTokens ?? 0);

                var delta = chunk?.Choices?.FirstOrDefault()?.Delta;
                if (delta == null) continue;

                if (delta.ToolCalls is { Count: > 0 })
                {
                    foreach (var frag in delta.ToolCalls)
                    {
                        if (!toolAcc.TryGetValue(frag.Index, out var acc))
                            acc = (null, null, new StringBuilder());
                        toolAcc[frag.Index] = (
                            frag.Id ?? acc.Id,
                            frag.Function?.Name ?? acc.Name,
                            acc.Args.Append(frag.Function?.Arguments));
                    }
                }

                if (!string.IsNullOrEmpty(delta.Content))
                {
                    content.Append(delta.Content);
                    // Only a text-answer round streams to the user; a stray
                    // preamble before tool fragments is dropped from the UI
                    // (it stays in `content` for the assistant echo message).
                    if (toolAcc.Count == 0)
                        await onDelta(delta.Content);
                }
            }

            var toolCalls = toolAcc.Values
                .Where(a => !string.IsNullOrEmpty(a.Name))
                .Select(a => new GroqToolCall
                {
                    Id = a.Id,
                    Type = "function",
                    Function = new GroqToolFunction { Name = a.Name, Arguments = a.Args.ToString() }
                })
                .ToList();

            return (content.ToString(), toolCalls, tokens);
        }
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
                    throw new GroqApiException(response.StatusCode, "Clé API Groq invalide ou expirée. Vérifiez votre clé sur https://console.groq.com/keys");
                throw new GroqApiException(response.StatusCode, $"Erreur Groq API: {response.StatusCode}");
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

/// <summary>Réponse HTTP d'erreur de Groq, avec son statut (sert au repli entre modèles).</summary>
public class GroqApiException : Exception
{
    public System.Net.HttpStatusCode StatusCode { get; }
    public GroqApiException(System.Net.HttpStatusCode statusCode, string message) : base(message) => StatusCode = statusCode;
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

// ── Streaming (SSE) chunk models ─────────────────────────────────────────────

public class GroqStreamChunk
{
    [JsonPropertyName("choices")]
    public List<GroqStreamChoice>? Choices { get; set; }

    /// <summary>Groq puts the usage of a streamed completion on the last chunk.</summary>
    [JsonPropertyName("x_groq")]
    public GroqXGroq? XGroq { get; set; }
}

public class GroqXGroq
{
    [JsonPropertyName("usage")]
    public GroqUsage? Usage { get; set; }
}

public class GroqStreamChoice
{
    [JsonPropertyName("delta")]
    public GroqStreamDelta? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public class GroqStreamDelta
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    public List<GroqStreamToolCallFragment>? ToolCalls { get; set; }
}

/// <summary>
/// A fragment of a streamed tool call: the first fragment of an index carries
/// id + function.name, subsequent ones append to function.arguments.
/// </summary>
public class GroqStreamToolCallFragment
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("function")]
    public GroqToolFunction? Function { get; set; }
}
