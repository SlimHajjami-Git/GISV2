using MediatR;

namespace GisAPI.Application.Features.FuelEntries.Commands;

public record DeleteFuelEntryCommand(int Id) : IRequest<bool>;
