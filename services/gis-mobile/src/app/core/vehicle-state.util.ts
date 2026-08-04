/**
 * Classification de l'état d'un véhicule à partir de sa dernière position.
 *
 * LA RÈGLE QUI COMPTE : la fraîcheur de la donnée passe AVANT tout le reste.
 * Une position est une photographie — sa vitesse et son contact décrivent
 * l'instant où elle a été prise, pas l'instant présent. Un boîtier muet laisse
 * derrière lui une dernière trame figée (vitesse 8 km/h, contact mis…) : tant
 * que la classification lisait la vitesse d'abord, un véhicule silencieux
 * depuis 11 jours restait affiché « En mouvement » en vert, avec sa vitesse
 * fantôme. Constaté en production sur l'application mobile — l'écran affichait
 * lui-même « Dernière comm. : il y a 11 j » sous la pastille verte.
 *
 * Chaque page (carte, tableau de bord, liste des véhicules) recodait sa propre
 * variante de cette logique, chacune avec le même défaut. Cet utilitaire est
 * désormais la seule source.
 */

/** Au-delà de ce silence, la donnée n'est plus une mesure : le véhicule est hors ligne. */
export const ONLINE_WINDOW_MS = 30 * 60 * 1000;

export type VehicleMotionState = 'moving' | 'idling' | 'parked' | 'offline';

/** La position est-elle assez récente pour décrire le présent ? */
export function isFresh(recordedAt: string | null | undefined, nowMs: number = Date.now()): boolean {
  if (!recordedAt) return false;
  const t = Date.parse(recordedAt);
  if (isNaN(t)) return false;
  return nowMs - t < ONLINE_WINDOW_MS;
}

/**
 * État d'affichage du véhicule :
 *   offline = silence > 30 min — quoi que dise la dernière trame ;
 *   moving  = donnée fraîche et vitesse > 3 km/h ;
 *   idling  = donnée fraîche, contact mis, à l'arrêt (moteur qui tourne) ;
 *   parked  = donnée fraîche, contact coupé.
 */
export function motionState(pos: {
  speedKph?: number | null;
  ignitionOn?: boolean | null;
  recordedAt?: string | null;
}, nowMs: number = Date.now()): VehicleMotionState {
  if (!isFresh(pos.recordedAt, nowMs)) return 'offline';
  if ((pos.speedKph ?? 0) > 3) return 'moving';
  if (pos.ignitionOn) return 'idling';
  return 'parked';
}
