namespace GisAPI.Application.Common.Interfaces;

public interface ILlmService
{
    Task<LlmResponse> ChatAsync(string systemPrompt, List<LlmMessage> messages, CancellationToken ct = default);
}

public record LlmMessage(string Role, string Content);

public record LlmResponse(string Content, int TokensUsed);
