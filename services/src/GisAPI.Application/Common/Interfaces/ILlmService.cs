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
}

public record LlmMessage(string Role, string Content);

public record LlmResponse(string Content, int TokensUsed);

/// <summary>
/// One callable tool exposed to the LLM. <see cref="ParametersJsonSchema"/> is
/// a raw JSON Schema object string (OpenAI "function.parameters" format).
/// </summary>
public record LlmToolDefinition(string Name, string Description, string ParametersJsonSchema);
