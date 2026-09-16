using GisAPI.Application.Common.Interfaces;
using GisAPI.Application.Features.FuelEntries;
using GisAPI.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Features.Vehicles;

/// <summary>
/// Règles d'écriture de la fiche véhicule, communes à la création, à la
/// modification (PUT) et au PATCH — recette Calypso GPA du 13/09/2026.
///
/// <para>Jour de paiement (DEF-034) : l'échéancier le plafonne au 28
/// (<see cref="AcquisitionSchedule"/>). Un 31 était accepté et affiché tel quel
/// alors que les mensualités tombaient le 28.</para>
///
/// <para>Matricule (DEF-037) : aucun contrôle, deux « GA-214-RK » coexistaient
/// dans la même société. La comparaison reprend <see cref="VehiclePlateKey"/>,
/// la règle unique qui rattache un plein ou une ligne d'import à un véhicule :
/// deux matricules qu'elle confondrait ne peuvent donc pas coexister.</para>
///
/// <para>Limite de la formule (DEF-036) : seul max_users était contrôlé, le
/// 51e véhicule d'un Plan Standard à 50 passait. Contrôlée à la création unitaire
/// et à l'import Excel ; jamais pour une formule facturée au véhicule.</para>
/// </summary>
public static class VehicleWriteRules
{
    public const int MaxPaymentDay = 28;

    /// <summary>
    /// Refuse un jour de paiement hors 1..28. <paramref name="current"/> : valeur
    /// déjà enregistrée — une valeur ancienne renvoyée telle quelle par le
    /// formulaire reste acceptée, sinon la fiche ne serait plus modifiable.
    /// </summary>
    public static void EnsurePaymentDay(int? requested, int? current = null)
    {
        if (requested is not { } day || day is >= 1 and <= MaxPaymentDay || day == current)
            return;

        throw new DomainException(
            $"Jour de paiement invalide ({day}) : choisissez un jour entre 1 et {MaxPaymentDay}. " +
            "L'échéancier ne va pas au-delà du 28 pour rester valable en février.");
    }

    /// <summary>
    /// Refuse (409) un matricule déjà porté par un autre véhicule de la société.
    /// <paramref name="currentPlate"/> : matricule actuel du véhicule modifié —
    /// inchangé, il n'est pas contrôlé, pour que les doublons déjà en base
    /// restent modifiables.
    /// </summary>
    public static async Task EnsurePlateAvailableAsync(
        IGisDbContext context, int companyId, string? plate,
        int? vehicleId, string? currentPlate, CancellationToken ct)
    {
        // Le message cite le matricule enregistré, jamais le nom du véhicule : un
        // utilisateur limité à ses véhicules affectés n'a pas à le connaître.
        var clash = await FindPlateClashAsync(context, companyId, plate, vehicleId, currentPlate, ct);
        if (clash != null)
            throw new ConflictException(
                $"Le matricule {clash.Plate} est déjà utilisé dans votre société.");
    }

    /// <summary>Véhicule de la société qui porte déjà le matricule demandé.</summary>
    public sealed record PlateClash(int VehicleId, string Plate, string Name);

    /// <summary>
    /// Refus de matricule pour l'administration système, qui voit tout le parc : le
    /// véhicule en conflit est nommé pour être retrouvé sans recherche.
    /// </summary>
    public static string AdminPlateClashMessage(PlateClash clash) =>
        $"Doublon refusé : le matricule {clash.Plate} est déjà porté par le véhicule « {clash.Name} » (#{clash.VehicleId}) de cette société.";

    /// <summary>
    /// Même contrôle que <see cref="EnsurePlateAvailableAsync"/>, sans lever : pour les
    /// écrans d'administration dont les handlers renvoient un résultat en échec.
    /// </summary>
    public static async Task<PlateClash?> FindPlateClashAsync(
        IGisDbContext context, int companyId, string? plate,
        int? vehicleId, string? currentPlate, CancellationToken ct)
    {
        var key = VehiclePlateKey.Normalize(plate);
        if (key.Length == 0 || (vehicleId.HasValue && key == VehiclePlateKey.Normalize(currentPlate)))
            return null;

        // Filtrage en mémoire : la clé ne doit porter que sur le matricule, alors
        // que le filtre SQL de VehiclePlateKey accepte aussi le nom. Le parc d'une
        // société reste de taille modeste.
        var vehicles = await context.Vehicles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => v.CompanyId == companyId && v.Plate != null && v.Id != (vehicleId ?? 0))
            .OrderBy(v => v.Id)
            .Select(v => new { v.Id, v.Plate, v.Name })
            .ToListAsync(ct);

        var clash = vehicles.FirstOrDefault(v => VehiclePlateKey.Normalize(v.Plate) == key);
        return clash == null ? null : new PlateClash(clash.Id, clash.Plate!.Trim(), clash.Name);
    }

    /// <summary>Limite de véhicules applicable à la société et taille actuelle du parc.</summary>
    public sealed record VehicleQuota(int Max, int Count)
    {
        public int Remaining => Math.Max(0, Max - Count);
    }

    public static string VehicleQuotaReachedMessage(int max) =>
        $"Limite de véhicules de votre abonnement atteinte ({max} au maximum).";

    /// <summary>
    /// Limite max_vehicles de la formule et parc actuel, ou null quand aucune limite
    /// ne s'applique. Même décompte que l'écran Abonnement (tous les véhicules de la
    /// société).
    ///
    /// <para>Pas de formule, ou limite à 0 ou moins : aucune limite — une formule de
    /// gestion de parc sans véhicule n'a pas de sens, 0 y signale un plan non
    /// paramétré. Écart volontaire avec max_users (0 bloque) : un plan mal saisi ne
    /// doit pas interdire tout ajout de véhicule en production.</para>
    ///
    /// <para>Formule facturée au véhicule (price_per_vehicle) : aucune limite. Le
    /// nombre de véhicules y est la base de facturation et l'écran Abonnement annonce
    /// « Véhicules illimités » ; le plan-basique de l'offre GPA européenne porte
    /// pourtant max_vehicles = 15, qui aurait bloqué un client au 16e véhicule.</para>
    /// </summary>
    public static async Task<VehicleQuota?> GetVehicleQuotaAsync(IGisDbContext context, int companyId, CancellationToken ct)
    {
        var plan = await context.Societes
            .AsNoTracking()
            .Where(s => s.Id == companyId && s.SubscriptionType != null)
            .Select(s => new { s.SubscriptionType!.MaxVehicles, s.SubscriptionType.PricePerVehicle })
            .FirstOrDefaultAsync(ct);

        if (plan == null || plan.PricePerVehicle || plan.MaxVehicles <= 0)
            return null;

        var count = await context.Vehicles
            .IgnoreQueryFilters()
            .CountAsync(v => v.CompanyId == companyId, ct);

        return new VehicleQuota(plan.MaxVehicles, count);
    }

    /// <summary>Refuse une création au-delà de la limite de <see cref="GetVehicleQuotaAsync"/>.</summary>
    public static async Task EnsureVehicleQuotaAsync(IGisDbContext context, int companyId, CancellationToken ct)
    {
        var quota = await GetVehicleQuotaAsync(context, companyId, ct);
        if (quota is { Remaining: 0 })
            throw new DomainException(VehicleQuotaReachedMessage(quota.Max));
    }
}
