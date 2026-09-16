using GisAPI.Domain.Exceptions;

namespace GisAPI.Application.Features.Documents;

/// <summary>
/// 404 « Véhicule introuvable. » du renouvellement et de la correction d'échéance
/// (véhicule inexistant ou d'une autre société). NotFoundException compose un
/// message anglais (« Entity "Véhicule" (20) was not found. ») que l'écran
/// Échéances affiche tel quel (err.error.message).
/// </summary>
public sealed class DocumentVehiculeIntrouvableException : NotFoundException
{
    public const string Libelle = "Véhicule introuvable.";

    public DocumentVehiculeIntrouvableException(int vehicleId) : base("Véhicule", vehicleId) { }

    public override string Message => Libelle;
}
