using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Societes.Commands.DeleteSociete;

public record DeleteSocieteCommand(int Id) : ICommand;



