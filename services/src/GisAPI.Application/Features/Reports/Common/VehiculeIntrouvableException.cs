namespace GisAPI.Application.Features.Reports.Common;

/// <summary>
/// Libellé du 404 d'un rapport sur un véhicule inexistant, d'une autre société ou hors
/// de la portée de l'appelant : les rapports répondaient en anglais au milieu d'une API
/// en français (recette du 16/09/2026).
///
/// <para>Pas une exception malgré son nom : le 404 se lève par
/// <c>new NotFoundException(Libelle)</c>, NotFoundException acceptant un message libre.
/// Le nom est conservé parce que ReportsController lit <see cref="Libelle"/> ; une seule
/// constante évite que le handler et le contrôleur divergent.</para>
/// </summary>
public static class VehiculeIntrouvableException
{
    public const string Libelle = "Véhicule introuvable.";
}
