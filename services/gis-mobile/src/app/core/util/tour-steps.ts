import { DriverTourDetail, DriverWaypoint } from '../models/driver-app.types';

/** Ce que la fiche propose sur une étape, selon l'état de la tournée. */
export type StepAction = 'depart_origin' | 'arrive' | 'redepart' | null;

/**
 * Bouton à afficher sur une étape (fonction pure, testée seule) :
 *  - tournée planifiée : « Je pars » sur l'origine, rien ailleurs ;
 *  - tournée en cours : « Je suis arrivé » sur la PROCHAINE étape non atteinte,
 *    « Je repars » sur la DERNIÈRE étape atteinte tant que le départ n'en est pas déclaré
 *    (jamais sur la destination : y arriver clôt la tournée).
 */
export function stepActionFor(tour: Pick<DriverTourDetail, 'status' | 'waypoints'>, wp: DriverWaypoint): StepAction {
  const ordered = [...tour.waypoints].sort((a, b) => a.sequenceOrder - b.sequenceOrder);
  if (ordered.length === 0) return null;
  const origin = ordered.find(w => w.type === 'origin') ?? ordered[0];

  if (tour.status === 'planned') return wp.id === origin.id ? 'depart_origin' : null;
  if (tour.status !== 'in_progress') return null;

  const next = ordered.find(w => !w.isCompleted && w.waypointStatus === 'pending');
  if (next && next.id === wp.id) return 'arrive';

  const reached = ordered.filter(w => w.isCompleted && (!next || w.sequenceOrder < next.sequenceOrder));
  const last = reached[reached.length - 1];
  if (last && last.id === wp.id && wp.type !== 'destination' && !wp.driverDepartedAt) return 'redepart';
  return null;
}
