import { DriverTourDetail, DriverWaypoint } from '../models/driver-app.types';

/**
 * Ce que la fiche propose sur une étape, selon l'état de la tournée :
 *  - depart_origin : « Je pars » (origine d'une tournée planifiée) ;
 *  - arrive        : « Je suis arrivé » sur la PROCHAINE étape attendue (bouton principal) ;
 *  - arrive_ahead  : « Je suis arrivé ici » sur une étape attendue PLUS LOIN (bouton
 *                    secondaire, avec confirmation) — client fermé, arrêt sauté, étape mal
 *                    géocodée : le chauffeur n'est jamais bloqué derrière une étape où il
 *                    n'ira pas ;
 *  - redepart      : « Je repars » de la dernière étape atteinte.
 */
export type StepAction = 'depart_origin' | 'arrive' | 'arrive_ahead' | 'redepart' | null;

/**
 * Étape encore ATTENDUE : ni atteinte, ni « non visitée ». « temps_depasse » en fait
 * partie — le moniteur la passe ainsi quand l'échéance est dépassée, mais continue d'y
 * chercher l'arrivée, et le serveur accepte qu'on la déclare. La traiter comme réglée
 * déplaçait « Je suis arrivé » sur l'étape suivante et rendait une destination en retard
 * impossible à clôturer (relecture du 21/09/2026, constat 15).
 */
export function isExpected(wp: DriverWaypoint): boolean {
  return !wp.isCompleted && wp.waypointStatus !== 'completed' && wp.waypointStatus !== 'skipped';
}

/** Étape attendue dont l'échéance est dépassée (badge « En retard »). */
export function isOverdue(wp: DriverWaypoint): boolean {
  return isExpected(wp) && wp.waypointStatus === 'temps_depasse';
}

/**
 * Étapes encore « pending » AVANT `stop`, dans l'ordre — miroir de
 * TourPlanning.HasPendingStopBefore : ce sont elles qui font répondre PENDING_STOPS à une
 * arrivée à la destination sans confirmSkipPending, et elles qui passeront « non visitées ».
 * Une étape « temps_depasse » n'en fait pas partie : le serveur ne bloque plus sur elle.
 */
export function pendingStopsBefore(waypoints: DriverWaypoint[], stop: DriverWaypoint): DriverWaypoint[] {
  return [...waypoints]
    .sort((a, b) => a.sequenceOrder - b.sequenceOrder)
    .filter(w => w.sequenceOrder < stop.sequenceOrder && isExpected(w) && w.waypointStatus === 'pending');
}

/** Prochaine étape attendue, dans l'ordre de la tournée (null s'il n'y en a plus). */
export function nextExpected(waypoints: DriverWaypoint[]): DriverWaypoint | null {
  const ordered = [...waypoints].sort((a, b) => a.sequenceOrder - b.sequenceOrder);
  return ordered.find(isExpected) ?? null;
}

/**
 * Bouton à afficher sur une étape (fonction pure, testée seule) :
 *  - tournée planifiée : « Je pars » sur l'origine, rien ailleurs ;
 *  - tournée en cours : « Je suis arrivé » sur la prochaine étape attendue, et « Je suis
 *    arrivé ici » sur toute étape attendue plus loin — destination comprise : la fiche
 *    demande alors, AVANT l'envoi, de confirmer que les étapes sautées seront « non
 *    visitées » (constat 3 : ce dialogue ne s'ouvrait jamais, et TOO_FAR sur la seule
 *    étape proposée bloquait le chauffeur) ;
 *  - « Je repars » sur la DERNIÈRE étape atteinte (la plus avancée dans la tournée) tant
 *    que le départ n'en est pas déclaré — jamais sur la destination : y arriver clôt la
 *    tournée.
 */
export function stepActionFor(tour: Pick<DriverTourDetail, 'status' | 'waypoints'>, wp: DriverWaypoint): StepAction {
  const ordered = [...tour.waypoints].sort((a, b) => a.sequenceOrder - b.sequenceOrder);
  if (ordered.length === 0) return null;
  const origin = ordered.find(w => w.type === 'origin') ?? ordered[0];

  if (tour.status === 'planned') return wp.id === origin.id ? 'depart_origin' : null;
  if (tour.status !== 'in_progress') return null;

  const next = ordered.find(isExpected);
  if (next && next.id === wp.id) return 'arrive';
  if (next && isExpected(wp) && wp.sequenceOrder > next.sequenceOrder) return 'arrive_ahead';

  const reached = ordered.filter(w => w.isCompleted || w.waypointStatus === 'completed');
  const last = reached[reached.length - 1];
  if (last && last.id === wp.id && wp.type !== 'destination' && !wp.driverDepartedAt) return 'redepart';
  return null;
}
