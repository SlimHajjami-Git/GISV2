namespace GisAPI.Application.Features.AlertEmails;

public record AlertEmailDto(int Id, string Email, string AlertType, DateTime CreatedAt);
