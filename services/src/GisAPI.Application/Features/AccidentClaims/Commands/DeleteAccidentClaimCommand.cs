using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.AccidentClaims.Commands;

public record DeleteAccidentClaimCommand(int Id) : ICommand<bool>;
