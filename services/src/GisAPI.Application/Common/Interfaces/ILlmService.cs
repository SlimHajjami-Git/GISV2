namespace GisAPI.Application.Common.Interfaces;

public interface ILlmService
{
    Task<LlmResponse> ChatAsync(string systemPrompt, List<LlmMessage> messages, CancellationToken ct = default);
    Task<LlmResponse> ChatAsync(string systemPrompt, List<LlmMessage> messages, int maxTokens, CancellationToken ct = default);

    /// <summary>
    /// Chat with OpenAI-style function calling (Groq tool use). The model may
    /// request tool calls; each one is executed via <paramref name="executeTool"/>
    /// (args: tool name, raw JSON arguments) and its JSON result is fed back,
    /// looping until the model produces a final text answer or
    /// <paramref name="maxToolRounds"/> is reached (the last round runs WITHOUT
    /// tools to force a plain answer). TokensUsed accumulates across all rounds.
    /// </summary>
    Task<LlmResponse> ChatWithToolsAsync(
        string systemPrompt,
        List<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        Func<string, string, CancellationToken, Task<string>> executeTool,
        int maxTokens,
        int maxToolRounds,
        CancellationToken ct = default);

    /// <summary>
    /// Single-shot STRUCTURED extraction. Sends one user message (text + an
    /// optional inline image as a <c>data:</c> URL) and forces a JSON-object
    /// response (Groq/OpenAI <c>response_format: json_object</c>). When an image
    /// is provided the multimodal vision model is used, otherwise the text
    /// model. Returns the raw JSON string in <see cref="LlmResponse.Content"/>.
    /// Used by the invoice-scan feature.
    /// </summary>
    Task<LlmResponse> ExtractJsonAsync(
        string systemPrompt,
        string userText,
        string? imageDataUrl,
        int maxTokens,
        CancellationToken ct = default);

    /// <summary>
    /// Same tool-calling loop as <see cref="ChatWithToolsAsync"/>, but STREAMED:
    /// text fragments of the answer are pushed to <paramref name="onDelta"/> as
    /// the model produces them (tool-invoking rounds stay silent — only rounds
    /// that turn out to be text answers are forwarded). Returns the full final
    /// text + accumulated tokens once the stream completes.
    /// </summary>
    Task<LlmResponse> ChatStreamWithToolsAsync(
        string systemPrompt,
        List<LlmMessage> messages,
        IReadOnlyList<LlmToolDefinition> tools,
        Func<string, string, CancellationToken, Task<string>> executeTool,
        Func<string, Task> onDelta,
        int maxTokens,
        int maxToolRounds,
        CancellationToken ct = default);
}

public record LlmMessage(string Role, string Content);

public record LlmResponse(string Content, int TokensUsed);

/// <summary>
/// One callable tool exposed to the LLM. <see cref="ParametersJsonSchema"/> is
/// a raw JSON Schema object string (OpenAI "function.parameters" format).
/// </summary>
public record LlmToolDefinition(string Name, string Description, string ParametersJsonSchema);
