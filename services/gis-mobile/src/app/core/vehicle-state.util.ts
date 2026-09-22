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
 *
 * CODE COULEUR (demande client du 22/09) : chaque écran avait aussi son propre
 * code — l'arrêt était orange dans la liste, rouge sur la carte ; le hors-ligne
 * rouge dans la liste, gris sur la carte ; le replay peignait la conduite en
 * bleu. Le même véhicule changeait de couleur d'un onglet à l'autre. Les
 * couleurs, libellés et icônes d'état vivent donc ICI et nulle part ailleurs :
 *   vert   = en route      orange = au ralenti (contact mis, à l'arrêt)
 *   rouge  = à l'arrêt (contact coupé)       gris = déconnecté
 */

/** Au-delà de ce silence, la donnée n'est plus une mesure : le véhicule est hors ligne. */
export const ONLINE_WINDOW_MS = 30 * 60 * 1000;

/**
 * Au-dessus de cette vitesse, le véhicule roule. Même seuil que le rapport
 * d'activité de l'API (StopSpeedThresholdKph = 3) : un arrêt du rapport et un
 * point rouge/orange du replay désignent le même moment.
 */
export const MOVING_SPEED_KPH = 3;

export type VehicleMotionState = 'moving' | 'idling' | 'parked' | 'offline';

/**
 * État d'une trame prise isolément (replay, historique) : la notion de
 * fraîcheur n'y a pas de sens — un point d'hier n'est pas « déconnecté ».
 */
export type FrameMotionState = Exclude<VehicleMotionState, 'offline'>;

export interface VehicleStateStyle {
  readonly state: VehicleMotionState;
  /** Libellé affiché À CÔTÉ de la couleur : la couleur n'est jamais le seul indice. */
  readonly label: string;
  /** Couleur pleine (marqueurs, pastilles, puces). */
  readonly color: string;
  /** Fond teinté (avatars) — lisible en thème clair comme sombre. */
  readonly tint: string;
  /** Icône ionicons : une FORME différente par état, pour qui ne distingue pas les couleurs. */
  readonly icon: string;
  /** Même forme en SVG (fond de 24×24, tracé blanc) pour les marqueurs Leaflet. */
  readonly glyph: string;
}

/**
 * Texte posé SUR la couleur d'un état (pastilles). Le blanc n'atteint pas 3:1
 * sur le vert, l'orange ou le gris ; ce gris très foncé dépasse 4,5:1 sur les
 * quatre (vérifié par le test de contraste).
 */
export const STATE_TEXT_ON_COLOR = '#111827';

const OFFLINE_COLOR = '#9ca3af';

export const VEHICLE_STATES: Readonly<Record<VehicleMotionState, VehicleStateStyle>> = {
  moving: {
    state: 'moving',
    label: 'En route',
    color: '#10b981',
    tint: 'rgba(16,185,129,0.15)',
    icon: 'navigate',
    // Flèche de navigation (orientable selon le cap).
    glyph: '<path d="M12 2L4.5 20.29l.71.71L12 18l6.79 3 .71-.71z"/>'
  },
  idling: {
    state: 'idling',
    label: 'Au ralenti',
    color: '#f59e0b',
    tint: 'rgba(245,158,11,0.15)',
    icon: 'pause-circle',
    // « Pause » : arrêté, moteur qui tourne — à distinguer du carré « stop ».
    glyph: '<rect x="6" y="5" width="4" height="14" rx="1"/><rect x="14" y="5" width="4" height="14" rx="1"/>'
  },
  parked: {
    state: 'parked',
    label: 'À l\'arrêt',
    color: '#ef4444',
    tint: 'rgba(239,68,68,0.15)',
    icon: 'stop-circle',
    glyph: '<rect x="6" y="6" width="12" height="12" rx="2"/>'
  },
  offline: {
    state: 'offline',
    label: 'Déconnecté',
    color: OFFLINE_COLOR,
    tint: 'rgba(156,163,175,0.15)',
    icon: 'cloud-offline',
    // Nuage barré : la barre est découpée dans la couleur de fond du marqueur.
    glyph: '<path d="M17.5 19H7a5 5 0 0 1-.9-9.92A6.5 6.5 0 0 1 18.3 9.05 5 5 0 0 1 17.5 19z"/>'
      + `<path d="M3.5 3.5l17 17" stroke="${OFFLINE_COLOR}" stroke-width="4" stroke-linecap="round" fill="none"/>`
      + '<path d="M3.5 3.5l17 17" stroke="#fff" stroke-width="1.8" stroke-linecap="round" fill="none"/>'
  }
};

/** Ordre d'affichage (légende, puces, compteurs) : du plus actif au plus muet. */
export const VEHICLE_STATE_ORDER: readonly VehicleMotionState[] = ['moving', 'idling', 'parked', 'offline'];

/**
 * Véhicule SANS boîtier (offre gestion sans GPS, boîtier pas encore posé) : ce n'est PAS
 * un état. Le peindre « Déconnecté » (gris, nuage barré) mettait en panne apparente toute
 * la liste d'une société sans GPS, et gonflait le compteur « Déconnecté » des Véhicules
 * par rapport au tableau de bord, qui ne compte que la flotte équipée. Pastille neutre :
 * contour pointillé à la couleur de l'application, aucune des quatre couleurs d'état.
 */
export interface NoDeviceStyle {
  readonly state: 'no-device';
  readonly label: string;
  readonly color: string;
  readonly tint: string;
  readonly icon: string;
}

export const NO_DEVICE_STYLE: NoDeviceStyle = {
  state: 'no-device',
  label: 'Sans boîtier',
  color: 'var(--ion-color-primary, #1a56db)',
  tint: 'transparent',
  icon: 'hardware-chip-outline'
};

export function stateStyle(state: VehicleMotionState): VehicleStateStyle {
  return VEHICLE_STATES[state] ?? VEHICLE_STATES.offline;
}

/** La position est-elle assez récente pour décrire le présent ? */
export function isFresh(recordedAt: string | null | undefined, nowMs: number = Date.now()): boolean {
  if (!recordedAt) return false;
  const t = Date.parse(recordedAt);
  if (isNaN(t)) return false;
  return nowMs - t < ONLINE_WINDOW_MS;
}

/**
 * État décrit par UNE trame, sans juger de sa fraîcheur (replay, rapports) :
 *   moving = vitesse > 3 km/h ;
 *   idling = contact mis, à l'arrêt (moteur qui tourne) ;
 *   parked = contact coupé.
 */
export function frameState(pos: {
  speedKph?: number | null;
  ignitionOn?: boolean | null;
}): FrameMotionState {
  if ((pos.speedKph ?? 0) > MOVING_SPEED_KPH) return 'moving';
  if (pos.ignitionOn) return 'idling';
  return 'parked';
}

/**
 * État d'affichage du véhicule :
 *   offline = silence > 30 min — quoi que dise la dernière trame ;
 *   sinon, l'état de la dernière trame (frameState).
 */
export function motionState(pos: {
  speedKph?: number | null;
  ignitionOn?: boolean | null;
  recordedAt?: string | null;
}, nowMs: number = Date.now()): VehicleMotionState {
  if (!isFresh(pos.recordedAt, nowMs)) return 'offline';
  return frameState(pos);
}

/** Compteurs par état, toujours avec les quatre clés (un état absent vaut 0). */
export function countByState(states: Iterable<VehicleMotionState>): Record<VehicleMotionState, number> {
  const counts: Record<VehicleMotionState, number> = { moving: 0, idling: 0, parked: 0, offline: 0 };
  for (const s of states) counts[s] = (counts[s] ?? 0) + 1;
  return counts;
}

/**
 * Pastille ronde d'un état (marqueur de carte, légende) : fond à la couleur de
 * l'état, glyphe blanc de sa forme. Une seule fabrique pour que la légende
 * montre EXACTEMENT ce que la carte dessine. `rotateDeg` n'oriente que la
 * flèche « en route » : un pictogramme pause ou stop tourné ne voudrait rien dire.
 */
export function stateMarkerHtml(state: VehicleMotionState, opts: {
  size?: number;
  border?: number;
  rotateDeg?: number;
  shadow?: boolean;
} = {}): string {
  const s = stateStyle(state);
  const size = opts.size ?? 28;
  const border = opts.border ?? 3;
  const glyph = Math.round(size * 0.45);
  const rotate = state === 'moving' && opts.rotateDeg ? `transform:rotate(${Math.round(opts.rotateDeg)}deg);` : '';
  const shadow = opts.shadow === false ? '' : 'box-shadow:0 2px 8px rgba(0,0,0,0.3);';
  return `<div class="vs-badge vs-${state}" style="box-sizing:border-box;background:${s.color};width:${size}px;height:${size}px;`
    + `border-radius:50%;display:flex;align-items:center;justify-content:center;border:${border}px solid #fff;${shadow}">`
    + `<svg width="${glyph}" height="${glyph}" viewBox="0 0 24 24" fill="#fff" aria-hidden="true" style="${rotate}">${s.glyph}</svg></div>`;
}
