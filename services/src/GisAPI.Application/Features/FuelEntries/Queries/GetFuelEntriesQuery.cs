using GisAPI.Application.Common.Models;
using MediatR;

namespace GisAPI.Application.Features.FuelEntries.Queries;

public record GetFuelEntriesQuery(
    int? FuelTypeId,
    string? VehiclePlate,
    DateTime? StartDate,
    DateTime? EndDate,
    int Page = 1,
    int PageSize = 50,
    // Filtre par véhicule de l'onglet Historique (recette client 04/09/2026).
    // Ajouté en fin de record : les appels positionnels existants restent valides.
    int? VehicleId = null
) : IRequest<PaginatedList<FuelEntryDto>>;
