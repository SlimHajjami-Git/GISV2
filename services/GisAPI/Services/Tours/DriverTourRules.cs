using GisAPI.Application.Common;
using GisAPI.Domain.Entities;

namespace GisAPI.Services.Tours;

/// <summary>
/// Règles des déclarations du chauffeur (« Je pars », « Je suis arrivé », « Je repars »)
/// et des positions de son téléphone. Fonctions pures, partagées par
/// <c>DriverAppController</c> et <c>TourMonitoringService</c>, testées seules.
///
/// Arbitrage déclaré / détecté (relecture du 18/09/2026, C3) : le PREMIER événement,
/// déclaration ou détection, change le statut de l'étape — c'est ce qui débloque le
/// parcours et arrête le contrôle de délai. L'heure qui fait foi pour les retards est
/// l'heure DÉTECTÉE (boîtier puis téléphone) : le moniteur continue d'analyser une
/// étape déclarée et remplace l'heure déclarée s'il détecte l'arrivée ; sans détection,
/// l'heure déclarée reste, marquée « non confirmée » (ArrivalSource = driver).
/// </summary>
public static class DriverTourRules
{
    public const string SourceDevice = "device";
    public const string SourcePhone = "phone";
    public const string SourceGeofence = "geofence";
    public const string SourceDriver = "driver";
    public const string SourceManager = "manager";

    /// <summary>Une déclaration peut précéder l'heure de référence d'autant (file hors ligne).</summary>
    public static readonly TimeSpan DeclaredTimeMinLead = TimeSpan.FromMinutes(5);
    /// <summary>…et être en avance sur le serveur d'au plus autant (horloge du téléphone).</summary>
    public static readonly TimeSpan DeclaredTimeMaxAhead = TimeSpan.FromMinutes(2);
    /// <summary>Déclaration d'arrivée refusée si boîtier ET téléphone sont vivants et d'accord
    /// pour dire que le chauffeur est plus loin que ceci.</summary>
    public const double DeclarationRefusedBeyondM = 2000;
    /// <summary>Au-delà, la déclaration passe mais l'écran du gestionnaire l'affiche avec sa distance.</summary>
    public const double DeclarationWarnBeyondM = 1000;
    /// <summary>Un lot de positions ne dépasse pas ceci.</summary>
    public const int MaxPositionsPerBatch = 200;
    /// <summary>Le suivi par téléphone s'arrête au plus tard ici après le départ.</summary>
    public static readonly TimeSpan MaxTrackingDuration = TimeSpan.FromHours(12);
    /// <summary>Tournée envoyée à un chauffeur et toujours pas partie autant après l'heure prévue : le gestionnaire est prévenu.</summary>
    public static readonly TimeSpan NotStartedAlertAfter = TimeSpan.FromMinutes(15);
    /// <summary>Une déclaration dont l'heure retenue précède la réception de plus que ceci
    /// a été REJOUÉE (file hors ligne) : la position actuelle du boîtier ne dit plus rien
    /// de l'endroit où était le chauffeur au moment du geste.</summary>
    public static readonly TimeSpan DeclarationReplayedAfter = TimeSpan.FromMinutes(2);
    /// <summary>Déclaration rejouée : trame du boîtier retenue si elle est à moins de ceci de l'heure déclarée.</summary>
    public static readonly TimeSpan ReplayedDeviceFrameWindow = TimeSpan.FromMinutes(3);
    /// <summary>Une arrivée déclarée n'est confirmée que par une détection proche de la
    /// déclaration : au plus autant AVANT (le véhicule s'est arrêté, le chauffeur a
    /// touché un peu plus tard)…</summary>
    public static readonly TimeSpan DeclaredConfirmationBefore = TimeSpan.FromMinutes(15);
    /// <summary>…et au plus autant APRÈS (trame ou point téléphone arrivé en retard).</summary>
    public static readonly TimeSpan DeclaredConfirmationAfter = TimeSpan.FromMinutes(30);
    /// <summary>Écart entre l'heure d'envoi lue sur le téléphone et la réception en deçà
    /// duquel on ne corrige rien : c'est le temps du réseau, pas une horloge fausse.</summary>
    public static readonly TimeSpan ClockSkewIgnoredBelow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Décalage de l'horloge du téléphone, mesuré sur l'heure d'envoi qu'il joint à sa
    /// requête (« sentAt ») : maintenant − sentAt, à AJOUTER à toute heure lue sur ce
    /// téléphone. Zéro sans sentAt, ou si l'écart est trop petit pour être autre chose que
    /// le réseau. Même règle pour les positions et pour les déclarations.
    /// </summary>
    public static TimeSpan ClockSkew(DateTime? sentAt, DateTime now)
    {
        if (!sentAt.HasValue) return TimeSpan.Zero;
        var mesure = now - DateTime.SpecifyKind(sentAt.Value, DateTimeKind.Utc);
        return mesure.Duration() > ClockSkewIgnoredBelow ? mesure : TimeSpan.Zero;
    }

    /// <summary>
    /// Heure d'une déclaration telle que le serveur la lit.
    /// <paramref name="DeclaredAt"/> : l'heure retenue (bornée par <see cref="BoundDeclaredTime"/>) ;
    /// <paramref name="GestureAt"/> : l'heure du geste corrigée de l'horloge du téléphone, où
    /// chercher la trame du boîtier ; <paramref name="Replayed"/> : la déclaration sort de la
    /// file hors ligne.
    /// </summary>
    public readonly record struct DeclarationTime(DateTime DeclaredAt, DateTime GestureAt, bool Replayed);

    /// <summary>
    /// Lit l'heure d'une déclaration du chauffeur (relecture du 21/09/2026, R10c).
    ///
    /// L'application 1.2 joint à CHAQUE envoi, rejeu compris, l'heure de son horloge au
    /// moment de l'envoi (<paramref name="sentAt"/>). Deux lectures de la MÊME horloge :
    /// - « rejouée » se décide sur sentAt − clientTime (> 2 min) : indépendant du décalage
    ///   de l'horloge et du bornage. On le décidait sur maintenant − heure retenue : une
    ///   horloge en retard de 4 min faisait passer un geste en direct pour un rejeu (le
    ///   boîtier était alors lu 4 min trop tôt, à 2,5 km de l'étape), et un rejeu dont
    ///   l'heure était bornée à « maintenant » passait pour un geste en direct (le boîtier
    ///   était lu à sa position actuelle, 15 km plus loin) ;
    /// - l'heure du geste est corrigée du décalage mesuré (maintenant − sentAt), comme les
    ///   positions, AVANT le bornage.
    /// Sans sentAt (application plus ancienne) : la règle d'avant, sur l'heure bornée.
    /// </summary>
    public static DeclarationTime ReadDeclarationTime(DateTime? clientTime, DateTime? sentAt, DateTime now, DateTime reference)
    {
        if (!sentAt.HasValue || !clientTime.HasValue)
        {
            var bornee = BoundDeclaredTime(clientTime, now, reference);
            return new DeclarationTime(bornee, bornee, IsReplayedDeclaration(bornee, now));
        }

        var client = DateTime.SpecifyKind(clientTime.Value, DateTimeKind.Utc);
        var envoi = DateTime.SpecifyKind(sentAt.Value, DateTimeKind.Utc);
        var geste = client + ClockSkew(sentAt, now);
        return new DeclarationTime(BoundDeclaredTime(geste, now, reference), geste, envoi - client > DeclarationReplayedAfter);
    }

    /// <summary>
    /// Borne basse de l'heure d'un « Je pars » qui DÉMARRE une tournée planifiée : le plus
    /// ancien de son PREMIER envoi (FirstSentAt), de son dernier envoi (SentAt) et de son
    /// ouverture sur le téléphone (OpenedAt) — le chauffeur ne peut pas avoir touché
    /// « Je pars » avant d'avoir reçu la tournée.
    ///
    /// FirstSentAt (relecture finale du 22/09/2026) : le renvoi remet OpenedAt à null (sans
    /// cela l'écran ne voyait jamais la réouverture) et avance SentAt. Sans le premier envoi,
    /// l'enchaînement que l'écran provoque — « Tournée non démarrée » puis « Renvoyer » —
    /// redatait au rejeu un « Je pars » fait hors ligne avant le renvoi, et rejetait toute la
    /// trace du téléphone jusque-là (seule source pour un véhicule sans boîtier).
    ///
    /// Relecture du 21/09/2026 (Wc18) : la borne était l'heure PRÉVUE. Un départ anticipé
    /// hors ligne (07:40 pour 08:00) rejoué à 08:30 était daté de 08:30 : estimations
    /// décalées comme pour un retard, points du téléphone d'avant 08:25 rejetés, arrivées
    /// en file redatées à leur tour.
    /// </summary>
    public static DateTime DepartureReference(Tour tour)
    {
        DateTime? reference = null;
        foreach (var t in new[] { tour.FirstSentAt, tour.SentAt, tour.OpenedAt })
            if (t is DateTime d && (reference == null || d < reference)) reference = d;
        return reference ?? tour.ScheduledStartTime;
    }

    /// <summary>
    /// Heure retenue pour une déclaration : celle du téléphone si elle est plausible
    /// (entre la référence − 5 min et maintenant + 2 min), sinon l'heure du serveur.
    /// Une file hors ligne rejoue des déclarations anciennes avec leur vraie heure ;
    /// une horloge fantaisiste ne doit pas dater une arrivée dans le futur.
    /// </summary>
    public static DateTime BoundDeclaredTime(DateTime? clientTime, DateTime now, DateTime reference)
    {
        if (!clientTime.HasValue) return now;
        var t = DateTime.SpecifyKind(clientTime.Value, DateTimeKind.Utc);
        if (t < reference - DeclaredTimeMinLead) return now;
        if (t > now + DeclaredTimeMaxAhead) return now;
        return t;
    }

    /// <summary>
    /// Une position du téléphone est acceptée si elle tombe dans la fenêtre de la
    /// tournée : [départ − 5 min, min(maintenant + 2 min, départ + 12 h)].
    /// </summary>
    public static bool IsPositionInWindow(DateTime recordedAt, DateTime tourStart, DateTime now) =>
        recordedAt >= tourStart - DeclaredTimeMinLead
        && recordedAt <= now + DeclaredTimeMaxAhead
        && recordedAt <= tourStart + MaxTrackingDuration;

    /// <summary>Points d'une tournée CLÔTURÉE encore acceptés : mesurés au plus autant après sa fin.</summary>
    public static readonly TimeSpan PositionsAcceptedAfterEnd = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Une position du téléphone est-elle encore acceptée pour une tournée CLÔTURÉE que le
    /// téléphone suivait ? Même fenêtre que pendant la tournée, bornée en plus à la fin
    /// réelle + 2 min ; sans fin connue, aucune.
    ///
    /// Pourquoi (relecture du 21/09/2026) : l'application vide sa file juste avant « Je suis
    /// arrivé » à destination, puis tente un dernier envoi à l'arrêt du suivi. Les points
    /// mesurés entre ce vidage et la réponse — et ceux d'une tournée clôturée par le
    /// moniteur pendant que le téléphone était hors ligne — étaient refusés : la fin de la
    /// trace manquait, précisément à destination.
    /// </summary>
    public static bool IsPositionInClosedTourWindow(DateTime recordedAt, DateTime tourStart, DateTime? tourEnd, DateTime now) =>
        tourEnd.HasValue
        && IsPositionInWindow(recordedAt, tourStart, now)
        && recordedAt <= tourEnd.Value + PositionsAcceptedAfterEnd;

    /// <summary>Distance (m) entre une position connue et une étape, null sans position.</summary>
    public static int? DistanceToStop(double? lat, double? lon, TourWaypoint wp) =>
        lat.HasValue && lon.HasValue
            ? (int)Math.Round(GeoMath.HaversineDistance(lat.Value, lon.Value, wp.Latitude, wp.Longitude))
            : null;

    /// <summary>
    /// Une déclaration d'arrivée est-elle refusée ? Seulement si les DEUX sources sont
    /// vivantes et d'accord pour dire que le chauffeur est à plus de 2 km de l'étape.
    /// Une seule source, ou une source muette, ne suffit jamais : le GPS ou le réseau
    /// peuvent être en défaut, et refuser à tort bloquerait le chauffeur.
    /// </summary>
    public static bool IsArrivalDeclarationRefused(int? deviceDistanceM, int? phoneDistanceM) =>
        deviceDistanceM.HasValue && phoneDistanceM.HasValue
        && deviceDistanceM.Value > DeclarationRefusedBeyondM
        && phoneDistanceM.Value > DeclarationRefusedBeyondM;

    /// <summary>
    /// Applique une déclaration d'arrivée : l'étape passe « completed » si elle était
    /// encore attendue, ArrivalSource = driver, heure déclarée conservée. Une étape
    /// déjà validée (détection, gestionnaire) garde son heure et sa source ; seule la
    /// déclaration est notée. Rend vrai si le statut a changé.
    /// </summary>
    public static bool DeclareArrival(TourWaypoint wp, DateTime declaredAt, int? distanceM)
    {
        wp.DriverArrivedAt = declaredAt;
        wp.DriverDeclarationDistanceM = distanceM;
        if (wp.IsCompleted || wp.WaypointStatus == "completed") return false;

        TourPlanning.MarkReached(wp, declaredAt);
        wp.ArrivalSource = SourceDriver;
        return true;
    }

    /// <summary>Le chauffeur repart de l'étape : heure déclarée, et départ retenu s'il n'est pas déjà détecté.</summary>
    public static void DeclareDeparture(TourWaypoint wp, DateTime declaredAt)
    {
        wp.DriverDepartedAt = declaredAt;
        wp.ActualDepartureTime ??= declaredAt;
    }

    /// <summary>
    /// Le moniteur a DÉTECTÉ l'arrivée à une étape que le chauffeur avait déclarée :
    /// l'heure détectée fait foi, la déclaration reste visible à part.
    /// </summary>
    public static void ConfirmDeclaredArrival(TourWaypoint wp, DateTime detectedAt, string source)
    {
        wp.ActualArrivalTime = detectedAt;
        wp.ArrivalSource = source;
    }

    /// <summary>Étape validée par le chauffeur seul, sans détection : « non confirmée » à l'écran.</summary>
    public static bool IsUnconfirmed(TourWaypoint wp) =>
        wp.IsCompleted && wp.ArrivalSource == SourceDriver;

    /// <summary>
    /// Fenêtre dans laquelle le moniteur peut CONFIRMER une arrivée déclarée par le
    /// chauffeur (points de trace entre DriverArrivedAt − 15 min et + 30 min), ou null
    /// s'il ne doit rien chercher.
    ///
    /// Relecture du 21/09/2026 (F20) : la recherche n'avait aucune borne de temps. Un
    /// véhicule qui repassait plus tard près d'une étape déclarée (aller-retour, dépôt
    /// sur le trajet) « confirmait » l'arrivée à l'heure du REPASSAGE, et l'origine —
    /// marquée « driver » par « Je pars », ce que l'écran affiche comme « Départ signalé
    /// par le chauffeur » — était re-cherchée à chaque cycle : sur un aller-retour, le
    /// retour au dépôt à 17:00 écrasait l'heure de départ de 08:00. L'origine n'est donc
    /// jamais re-cherchée : son heure est celle du départ déclaré.
    /// </summary>
    public static (DateTime From, DateTime To)? DeclaredArrivalConfirmationWindow(TourWaypoint wp)
    {
        if (!IsUnconfirmed(wp) || wp.Type == "origin" || wp.DriverArrivedAt is not DateTime declared) return null;
        return (declared - DeclaredConfirmationBefore, declared + DeclaredConfirmationAfter);
    }

    /// <summary>La déclaration a-t-elle été rejouée depuis la file hors ligne du téléphone ?</summary>
    public static bool IsReplayedDeclaration(DateTime declaredAt, DateTime now) =>
        now - declaredAt > DeclarationReplayedAfter;

    /// <summary>
    /// Quelque chose est-il CENSÉ suivre cette tournée ? Le boîtier du véhicule s'il en a
    /// un, le téléphone du chauffeur si elle lui a été envoyée. Sinon (offre sans GPS,
    /// tournée classique sans chauffeur), elle n'est pas suivie : ni source, ni
    /// « Suivi interrompu » — rien n'a jamais dû la suivre (relecture du 21/09/2026, F16).
    /// </summary>
    public static bool HasExpectedTrackingSource(Tour tour) =>
        tour.Vehicle?.GpsDeviceId != null || StartsOnDriverDeparture(tour);

    /// <summary>
    /// Une tournée envoyée à un chauffeur qui possède un compte ne démarre plus toute
    /// seule à l'heure prévue (décision D6) : « En cours » veut dire « parti ». Le
    /// démarrage automatique reste pour les tournées sans chauffeur.
    /// </summary>
    public static bool StartsOnDriverDeparture(Tour tour) =>
        tour.DriverId.HasValue && tour.SentAt.HasValue;

    public static bool IsNotStartedAlertDue(Tour tour, DateTime now) =>
        tour.Status == "planned" && StartsOnDriverDeparture(tour)
        && now >= tour.ScheduledStartTime + NotStartedAlertAfter;
}
