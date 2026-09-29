using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Queries.GetVehicleBatteryHistory;

/// <summary>
/// Courbe de tension batterie d'un véhicule sur une période, pour la fenêtre qui
/// s'ouvre quand l'admin clique sur la notification « batterie en fin de vie »
/// (Slim, 29/09/2026 : « l'utilisateur verra la chute du voltage à chaque démarrage
/// et comprendra que sa batterie est en train de mourir »).
/// </summary>
public record GetVehicleBatteryHistoryQuery(int VehicleId, int Days) : IQuery<BatteryHistoryDto?>;

/// <param name="AtUtc">Début de la tranche, en UTC.</param>
/// <param name="MinV">Tension la plus basse de la tranche — le plancher de la batterie.</param>
/// <param name="MaxV">
/// Tension la plus haute. Moteur tournant c'est l'alternateur : un sommet qui
/// n'atteint plus 13,5 V dit que la batterie n'est plus rechargée, ce qui est un
/// diagnostic différent d'une batterie usée.
/// </param>
public record BatteryHistoryPointDto(DateTime AtUtc, double MinV, double MaxV);

/// <param name="AtUtc">Instant du démarrage.</param>
/// <param name="VoltsV">Tension retenue pour ce démarrage (médiane de ses premières trames).</param>
/// <param name="Low">Sous le seuil : ces points-là sont ceux qui comptent pour l'alerte.</param>
public record BatteryStartPointDto(DateTime AtUtc, double VoltsV, bool Low);

/// <param name="Supported">
/// Faux pour un véhicule sans boîtier NEMS : la courbe n'a alors aucun sens et
/// l'écran doit le dire plutôt que d'afficher un graphe vide.
/// </param>
/// <param name="ThresholdV">Seuil de l'alerte, tracé en rouge sur le graphe.</param>
/// <param name="MedianV">
/// Médiane des derniers démarrages — la valeur qui a déclenché l'alerte. Null tant
/// qu'on n'a pas assez de démarrages pour conclure.
/// </param>
public record BatteryHistoryDto(
    int VehicleId,
    string? Plate,
    bool Supported,
    int Days,
    double ThresholdV,
    double? MedianV,
    IReadOnlyList<BatteryHistoryPointDto> Points,
    IReadOnlyList<BatteryStartPointDto> Starts);
