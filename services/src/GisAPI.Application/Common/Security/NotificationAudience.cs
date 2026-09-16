using GisAPI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace GisAPI.Application.Common.Security;

/// <summary>
/// Destinataires d'une notification rattachée à UN véhicule précis.
///
/// <para>Pendant du <see cref="VehicleScope"/> : celui-ci répond « quels
/// véhicules cet utilisateur voit-il ? » côté écran, celui-ci répond « qui doit
/// être prévenu pour ce véhicule ? » côté production d'alerte. Les deux doivent
/// raconter la même histoire, sinon un utilisateur reçoit des notifications sur
/// des véhicules qu'il ne peut même pas ouvrir.</para>
///
/// <para>C'est exactement ce qui est arrivé chez Hertz le 15/09/2026 : les
/// producteurs d'alertes faisaient « Notify all active users in the company »,
/// si bien qu'un opérateur affecté à 2 véhicules sur 279 a reçu 125 excès de
/// vitesse en 18 h, dont 100 % portaient sur des véhicules tiers. Les trois
/// opérateurs de la société recevaient rigoureusement les mêmes alertes alors
/// qu'ils avaient 2, 36 et 187 véhicules affectés.</para>
///
/// <para>Règle appliquée, identique à <see cref="VehicleScope"/> :</para>
/// <list type="bullet">
///   <item><description>un administrateur de société voit tout le parc, donc il
///     est notifié de tout — <b>y compris quand il n'a aucune affectation</b>
///     (cas réel : une administratrice Hertz a 0 ligne dans UserVehicles) ;</description></item>
///   <item><description>un utilisateur simple n'est notifié que des véhicules
///     qui lui sont explicitement affectés (table UserVehicles).</description></item>
/// </list>
///
/// <para>Sans véhicule identifiable, on se replie sur les administrateurs
/// seuls : on préfère une alerte vue par trop peu de monde qu'une alerte
/// diffusée à des gens qui n'ont pas à la voir (fail-closed).</para>
///
/// <para>À n'utiliser QUE pour une notification qui parle d'un véhicule. Les
/// notifications de niveau société — expiration d'abonnement, création d'un
/// utilisateur, permis d'un conducteur — gardent leur audience d'origine.</para>
/// </summary>
public static class NotificationAudience
{
    /// <summary>
    /// Identifiants des utilisateurs actifs à notifier pour <paramref name="vehicleId"/>.
    /// Liste vide = personne à prévenir, ce qui est un résultat valide (aucun
    /// admin et aucune affectation) et jamais une raison de retomber sur
    /// « toute la société ».
    /// </summary>
    public static async Task<List<int>> ForVehicleAsync(
        IGisDbContext context,
        int companyId,
        int? vehicleId,
        CancellationToken ct = default)
    {
        // Le véhicule peut manquer (boîtier non rattaché) : on ne sait alors pas
        // cloisonner, seuls les administrateurs sont prévenus.
        var scopedVehicleId = vehicleId.GetValueOrDefault();

        // IgnoreQueryFilters + CompanyId épinglé : ces requêtes tournent aussi
        // depuis les services de fond et les consommateurs RabbitMQ, où aucun
        // tenant n'est positionné. Le cloisonnement société reste garanti par le
        // Where explicite ci-dessous.
        return await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId
                        && u.Status == "active"
                        && u.Role != null
                        && (u.Role.IsCompanyAdmin
                            || u.Role.IsSystemRole
                            || (scopedVehicleId > 0
                                && context.UserVehicles.Any(uv => uv.UserId == u.Id
                                                                  && uv.VehicleId == scopedVehicleId))))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Administrateurs actifs de la société. Pour les notifications qui parlent
    /// de l'activité de la société sans porter sur un véhicule identifiable
    /// (écho d'audit « X a enregistré une dépense », par exemple) : elles n'ont
    /// jamais eu vocation à descendre jusqu'aux opérateurs.
    /// </summary>
    public static async Task<List<int>> CompanyAdminsAsync(
        IGisDbContext context,
        int companyId,
        CancellationToken ct = default)
    {
        return await context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId
                        && u.Status == "active"
                        && u.Role != null
                        && (u.Role.IsCompanyAdmin || u.Role.IsSystemRole))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }
}
