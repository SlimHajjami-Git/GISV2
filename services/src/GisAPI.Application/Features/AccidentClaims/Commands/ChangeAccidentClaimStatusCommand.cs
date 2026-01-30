using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public record SubmitAccidentClaimCommand(int Id) : ICommand<bool>;

public record ApproveAccidentClaimCommand(int Id, decimal ApprovedAmount) : ICommand<bool>;

public record RejectAccidentClaimCommand(int Id, string? Reason) : ICommand<bool>;

public record CloseAccidentClaimCommand(int Id) : ICommand<bool>;



