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
