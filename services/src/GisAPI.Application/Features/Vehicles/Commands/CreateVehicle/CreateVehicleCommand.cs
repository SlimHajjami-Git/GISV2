using GisAPI.Application.Common.Interfaces;

namespace GisAPI.Application.Features.Vehicles.Commands.CreateVehicle;

public record CreateVehicleCommand(
    string Name,
    string Type,
    string? Brand,
    string? Model,
    string? Plate,
    int? Year,
    string? Color,
    int Mileage = 0,
    string? FuelType = null,
    int? FuelTankCapacity = null,
    int? GpsDeviceId = null,
    GpsDeviceInfo? NewGpsDevice = null,
    // Acquisition & financement — le formulaire « Ajouter un véhicule » affiche
    // la section et l'envoie déjà : sans ces champs ici, le contrat saisi était
    // jeté sans message et l'utilisateur devait le ressaisir en modification
    // (recette GPA du 11/09/2026).
    string? AcquisitionType = null,
    decimal? PurchasePrice = null,
    DateTime? PurchaseDate = null,
    decimal? LeasingMonthlyPayment = null,
    int? LeasingDurationMonths = null,
    DateTime? LeasingStartDate = null,
    int? LeasingPaymentDay = null,
    DateTime? RegistrationDate = null
) : ICommand<int>;

public record GpsDeviceInfo(
    string DeviceUid,
    string? SimNumber,
    string? SimOperator,
    string? Brand,
    string? Model,
    DateTime? InstallationDate
);



