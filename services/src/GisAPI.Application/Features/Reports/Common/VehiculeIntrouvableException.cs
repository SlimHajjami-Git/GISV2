using GisAPI.Domain.Exceptions;

namespace GisAPI.Application.Features.Reports.Common;

/// <summary>
/// 404 d'un rapport sur un véhicule inexistant, d'une autre société ou hors de la
/// portée de l'appelant, avec un libellé français.
///
/// <para><see cref="NotFoundException"/> impose son format anglais
/// (« Entity "Vehicle" (20) was not found. »), dont d'autres écrans et tests
/// dépendent : on ne remplace le message que pour les rapports, qui répondaient en
/// anglais au milieu d'une API en français (recette du 16/09/2026).</para>
/// </summary>
public sealed class VehiculeIntrouvableException : NotFoundException
{
    public const string Libelle = "Véhicule introuvable.";

    public VehiculeIntrouvableException(int vehicleId) : base("Vehicle", vehicleId) { }

    public override string Message => Libelle;
}
