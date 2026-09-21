namespace GisAPI.Services.Tours;

/// <summary>
/// Quelle source suit la tournée en ce moment : le boîtier du véhicule d'abord, le
/// téléphone du chauffeur en relais, sinon aucune (décision de Slim du 18/09/2026 :
/// « les deux, si l'un n'est pas disponible on bascule sur l'autre »).
///
/// Fonction PURE, sans horloge ni base : le moniteur lui passe ce qu'il sait et
/// enregistre ce qu'elle rend. C'est ce qui la rend testable ligne à ligne.
///
/// Seuils, d'après les trames réelles de TN (relecture du 18/09/2026) :
/// - boîtier vivant = trame reçue il y a moins de 3 min contact mis, moins de 35 min
///   contact coupé — HERTZ envoie une trame toutes les 30 min à l'arrêt (p90 = 1 800 s),
///   un seuil unique de 5 min faisait osciller la source à chaque livraison ;
/// - téléphone vivant = point ou battement reçu il y a moins de 3 min, précision ≤ 100 m,
///   position non simulée ;
/// - hystérésis : le boîtier ne reprend la main au téléphone qu'après deux cycles
///   consécutifs où il est vivant, pour ne pas faire clignoter la pastille.
/// </summary>
public static class TrackingSourceSelector
{
    public const string Device = "device";
    public const string Phone = "phone";
    public const string None = "none";

    public static readonly TimeSpan DeviceAliveIgnitionOn = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan DeviceAliveIgnitionOff = TimeSpan.FromMinutes(35);
    public static readonly TimeSpan PhoneAlive = TimeSpan.FromMinutes(3);
    public const double PhoneMaxAccuracyM = 100;
    /// <summary>Cycles consécutifs « boîtier vivant » avant de quitter le téléphone.</summary>
    public const int DeviceRecoveryCycles = 2;
    /// <summary>Au-delà, sans aucune source, le gestionnaire est prévenu (une fois par coupure).</summary>
    public static readonly TimeSpan LostAlertAfter = TimeSpan.FromMinutes(10);
    /// <summary>Boîtier et téléphone vivants mais éloignés d'au moins ceci pendant
    /// <see cref="DivergenceAfter"/> : le téléphone n'est pas dans le véhicule.</summary>
    public const double DivergenceMeters = 1000;
    public static readonly TimeSpan DivergenceAfter = TimeSpan.FromMinutes(5);

    public sealed record DeviceState(DateTime? LastAt, bool IgnitionOn);
    public sealed record PhoneState(DateTime? LastAt, double? AccuracyM, bool IsMocked);

    /// <summary>
    /// Résultat d'un cycle : la source retenue et le compteur d'hystérésis à conserver.
    /// <paramref name="DeviceAlive"/> / <paramref name="PhoneAlive"/> sont exposés pour
    /// l'écran (« boîtier muet depuis… », « téléphone disponible ») et la cadence.
    /// </summary>
    public sealed record Choice(string Source, int RecoveryCount, bool DeviceAlive, bool PhoneAlive);

    /// <summary>
    /// Pas d'écriture de l'ingest pour un boîtier à l'arrêt contact coupé : une trame stockée
    /// toutes les 30 min (gps-ingest-rust, transport.rs, STOPPED_MIN_INTERVAL_SECS) ; les
    /// autres ne font que rafraîchir gps_devices.last_communication.
    /// </summary>
    public static readonly TimeSpan DeviceStoppedStoreInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// État du boîtier d'après sa dernière trame STOCKÉE (heure, contact) et sa dernière
    /// communication (gps_devices.last_communication, heure du serveur).
    ///
    /// Relecture du 21/09/2026 (R2c) : l'état ne venait que de la dernière trame stockée.
    /// Or l'ingest écrème un boîtier à l'arrêt contact coupé : un battement arrivé moins de
    /// 30 min après la dernière trame stockée n'est pas écrit, seule last_communication
    /// avance. Un battement horodaté 1 s trop tôt portait l'écart entre trames stockées à
    /// 60 min, au-delà des 35 min « vivant » : « Suivi interrompu » en pleine livraison
    /// (HERTZ), alors que le boîtier parlait.
    ///
    /// Une communication plus récente que la dernière trame stockée n'est donc tenue pour
    /// une trame écrémée — vivante, contact coupé — que si cette trame est contact COUPÉ
    /// et que rien n'a été stocké depuis contact mis (<paramref name="ignitionOnSince"/>) :
    /// l'ingest ne retient jamais un changement de contact ni une trame contact mis. Dans
    /// tous les autres cas (boîtier qui roule sans position valide, battements sans GPS),
    /// last_communication ne prouve pas que le véhicule est suivi : elle est ignorée, et le
    /// téléphone peut prendre le relais.
    /// </summary>
    public static DeviceState DeviceStateOf(DateTime? lastFrameAt, bool lastFrameIgnitionOn,
        DateTime? lastCommunication, bool ignitionOnSince)
    {
        if (!lastFrameAt.HasValue) return new DeviceState(null, false);
        var at = lastFrameAt.Value;
        if (!lastFrameIgnitionOn && !ignitionOnSince && lastCommunication is DateTime comm && comm > at)
            at = comm;
        return new DeviceState(at, lastFrameIgnitionOn);
    }

    public static bool IsDeviceAlive(DateTime now, DeviceState device) =>
        device.LastAt.HasValue
        && now - device.LastAt.Value <= (device.IgnitionOn ? DeviceAliveIgnitionOn : DeviceAliveIgnitionOff);

    public static bool IsPhoneAlive(DateTime now, PhoneState phone) =>
        phone.LastAt.HasValue
        && now - phone.LastAt.Value <= PhoneAlive
        && (phone.AccuracyM ?? 0) <= PhoneMaxAccuracyM
        && !phone.IsMocked;

    /// <param name="previous">Source du cycle précédent (null au premier cycle).</param>
    /// <param name="recoveryCount">Compteur d'hystérésis rendu par le cycle précédent.</param>
    public static Choice Choose(DateTime now, DeviceState device, PhoneState phone, string? previous, int recoveryCount)
    {
        var deviceAlive = IsDeviceAlive(now, device);
        var phoneAlive = IsPhoneAlive(now, phone);

        if (deviceAlive)
        {
            // Retour du boîtier alors que le téléphone assurait le relais : on attend
            // DeviceRecoveryCycles cycles vivants d'affilée avant de rebasculer.
            if (previous == Phone && phoneAlive)
            {
                var count = recoveryCount + 1;
                return count >= DeviceRecoveryCycles
                    ? new Choice(Device, 0, true, true)
                    : new Choice(Phone, count, true, true);
            }
            return new Choice(Device, 0, true, phoneAlive);
        }

        if (phoneAlive) return new Choice(Phone, 0, false, true);
        return new Choice(None, 0, false, false);
    }

    /// <summary>
    /// Cadence demandée au téléphone : « eco » (un point toutes les 2 min environ) tant
    /// que le boîtier suit, pour ménager la batterie et la vie privée ; « full » dès que
    /// le boîtier se tait ou que le véhicule n'en a pas.
    /// </summary>
    public static string PhoneMode(bool deviceAlive) => deviceAlive ? "eco" : "full";
}
