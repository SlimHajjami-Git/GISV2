/**
 * Envoi d'une tournée au chauffeur et suivi boîtier / téléphone. Fonctions
 * pures, testées dans tours-tracking.helpers.spec.ts.
 *
 * Contrat serveur (ToursController.cs, 21/09/2026) :
 * - POST /api/tours/{id}/send → { sentAt, push, resent } ;
 *   400 { code: 'DRIVER_NO_APP_ACCOUNT' } si le chauffeur n'a pas de compte actif.
 * - GET /api/tours/{id}/tracking → source ('device'|'phone'|'none'), sourceSince,
 *   deviceAvailable, phoneAvailable, positionAgeSeconds,
 *   phone { …, recordedAt, batteryLevel (entier 0-100) } | null — dernier point, sans borne d'âge.
 * - Chaque étape porte arrivalSource ('device'|'phone'|'geofence'|'driver'|'manager'),
 *   driverDeclarationDistanceM et unconfirmed (validée par le chauffeur seul).
 */

// ─────────────────────────── Compte application du chauffeur ───────────────────────────

export interface DriverAccountInfo {
  id: number;
  /** Compte utilisateur relié à la fiche (null = pas d'accès à l'application). */
  userId?: number | null;
  /** Statut de ce compte : 'active' | 'inactive' | null. */
  accountStatus?: string | null;
}

/** Vrai si la fiche est reliée à un compte application ACTIF. */
export function driverHasAppAccount(d: DriverAccountInfo | null | undefined): boolean {
  return !!d && d.userId != null && (d.accountStatus ?? '').toLowerCase() === 'active';
}

export const APP_BADGE = '📱 application';

/** Libellé d'option enrichi du badge « application » quand le chauffeur a un compte actif. */
export function withAppBadge(label: string, d: DriverAccountInfo | null | undefined): string {
  return driverHasAppAccount(d) ? `${label} · ${APP_BADGE}` : label;
}

// ─────────────────────────── Bouton « Envoyer au chauffeur » ───────────────────────────

export const NO_APP_ACCOUNT_TOOLTIP = "Ce chauffeur n'a pas de compte application — créez-le dans Utilisateurs";
export const NO_DRIVER_TOOLTIP = "Choisissez d'abord un chauffeur";

export interface SendButtonState {
  /** Le bouton n'a de sens que pour une tournée planifiée ou en cours. */
  visible: boolean;
  enabled: boolean;
  label: string;
  /** Motif du verrou, affiché en infobulle ; null quand le bouton est actif. */
  tooltip: string | null;
}

export function sendButtonState(
  tour: { status?: string | null; driverId?: number | null; sentAt?: string | null } | null | undefined,
  drivers: DriverAccountInfo[]
): SendButtonState {
  const visible = !!tour && (tour.status === 'planned' || tour.status === 'in_progress');
  const label = tour?.sentAt ? 'Renvoyer au chauffeur' : 'Envoyer au chauffeur';
  if (!visible) return { visible, enabled: false, label, tooltip: null };
  if (tour!.driverId == null) return { visible, enabled: false, label, tooltip: NO_DRIVER_TOOLTIP };
  const driver = drivers.find(d => Number(d.id) === Number(tour!.driverId));
  if (!driverHasAppAccount(driver)) return { visible, enabled: false, label, tooltip: NO_APP_ACCOUNT_TOOLTIP };
  return { visible, enabled: true, label, tooltip: null };
}

// ─────────────────────────── Issue de l'envoi → toast ───────────────────────────

export type TourPushOutcome = 'delivered_to_fcm' | 'no_device' | 'firebase_off' | 'failed' | 'quiet_hours';

export interface TourSendResult {
  sentAt: string;
  push: TourPushOutcome | string;
  resent: boolean;
}

export interface SendToast {
  type: 'success' | 'warning' | 'info';
  title: string;
  message: string;
}

export function sendToast(result: Pick<TourSendResult, 'push' | 'resent'>): SendToast {
  const title = result.resent ? 'Tournée renvoyée' : 'Tournée envoyée';
  switch (result.push) {
    case 'delivered_to_fcm':
      return { type: 'success', title, message: 'Envoyée au téléphone du chauffeur' };
    case 'no_device':
      return { type: 'warning', title, message: "Envoyée, mais le chauffeur n'a pas encore ouvert l'application sur son téléphone" };
    case 'firebase_off':
    case 'failed':
      return { type: 'warning', title, message: 'Enregistrée, notification non délivrée' };
    case 'quiet_hours':
      return { type: 'info', title, message: 'Enregistrée, le chauffeur est en heures silencieuses' };
    default:
      return { type: 'info', title, message: 'Enregistrée' };
  }
}

/** Message d'erreur d'un refus du serveur (400 avec code ou message), sinon repli en français. */
export function sendErrorMessage(err: any): string {
  const body = err?.error;
  if (body?.code === 'DRIVER_NO_APP_ACCOUNT') return body.message || NO_APP_ACCOUNT_TOOLTIP;
  return body?.message || "La tournée n'a pas pu être envoyée.";
}

// ─────────────────────────── Étapes : source d'arrivée ───────────────────────────

export interface WaypointArrivalInfo {
  arrivalSource?: string | null;
  unconfirmed?: boolean | null;
  driverDeclarationDistanceM?: number | null;
}

/** Libellé de la source qui a validé l'étape ; vide si inconnue. */
export function waypointSourceLabel(wp: WaypointArrivalInfo | null | undefined): string {
  switch (wp?.arrivalSource) {
    case 'device': return 'boîtier';
    case 'phone': return 'téléphone';
    case 'geofence': return 'zone';
    case 'driver': return wp!.unconfirmed ? 'chauffeur (non confirmée)' : 'chauffeur';
    case 'manager': return 'gestionnaire';
    default: return '';
  }
}

/** Seuil au-delà duquel une arrivée déclarée par le chauffeur est signalée (DriverTourRules.DeclarationWarnBeyondM). */
export const DECLARATION_WARN_BEYOND_M = 1000;

/** « Arrivée déclarée à X km de l'étape » au-delà du seuil, sinon null. */
export function declarationDistanceWarning(wp: WaypointArrivalInfo | null | undefined): string | null {
  const m = wp?.driverDeclarationDistanceM;
  if (m == null || m <= DECLARATION_WARN_BEYOND_M) return null;
  const km = (m / 1000).toFixed(1).replace('.', ',');
  return `Arrivée déclarée à ${km} km de l'étape`;
}

// ─────────────────────────── Pastille de suivi ───────────────────────────

export interface TrackingSnapshot {
  source?: string | null;
  sourceSince?: string | null;
  positionAgeSeconds?: number | null;
  deviceAvailable?: boolean | null;
  phoneAvailable?: boolean | null;
}

export interface TrackingBadge {
  kind: 'device' | 'phone' | 'lost' | 'none';
  label: string;
  /** « il y a N min », d'après positionAgeSeconds ; null si aucune position. */
  ageLabel: string | null;
}

/** Durée courte en français : « 12 s », « 3 min », « 1 h 05 ». */
export function shortDuration(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  if (s < 60) return `${s} s`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  return `${h} h ${String(m % 60).padStart(2, '0')}`;
}

export function positionAgeLabel(ageSeconds: number | null | undefined): string | null {
  if (ageSeconds == null || ageSeconds < 0) return null;
  return `il y a ${shortDuration(ageSeconds)}`;
}

/**
 * Pastille du bandeau : la source qui suit (boîtier / téléphone), ou depuis
 * combien de temps le suivi est interrompu — d'après sourceSince quand le
 * moniteur l'a fixé, sinon d'après l'âge de la dernière position.
 *
 * « Suivi en attente » plutôt qu'« interrompu » quand aucune source n'est
 * retenue mais qu'une source émet (deviceAvailable / phoneAvailable), ou que
 * la bascule date de moins d'une minute : au « Je pars », le serveur force la
 * source à 'none' (DriverAppController) jusqu'au cycle suivant du moniteur
 * (30 s). Le gestionnaire qui ouvrait le détail depuis la notification
 * « Départ » voyait alors un bandeau orange « interrompu depuis 0 min »,
 * boîtier vivant.
 */
export function trackingBadge(t: TrackingSnapshot | null | undefined, nowMs: number = Date.now()): TrackingBadge {
  if (!t) return { kind: 'none', label: 'Suivi en attente', ageLabel: null };
  const ageLabel = positionAgeLabel(t.positionAgeSeconds);
  if (t.source === 'device') return { kind: 'device', label: 'Suivi par boîtier', ageLabel };
  if (t.source === 'phone') return { kind: 'phone', label: 'Suivi par téléphone', ageLabel };
  if (t.deviceAvailable || t.phoneAvailable) return { kind: 'none', label: 'Suivi en attente', ageLabel };

  let lostSeconds: number | null = null;
  if (t.sourceSince) {
    const since = new Date(t.sourceSince).getTime();
    if (!isNaN(since)) {
      lostSeconds = Math.max(0, (nowMs - since) / 1000);
      if (lostSeconds < TRACKING_SWITCH_GRACE_SECONDS) return { kind: 'none', label: 'Suivi en attente', ageLabel };
    }
  }
  if (lostSeconds == null && t.positionAgeSeconds != null) lostSeconds = t.positionAgeSeconds;
  if (lostSeconds == null) return { kind: 'none', label: 'Aucune position reçue', ageLabel };
  const minutes = Math.max(0, Math.round(lostSeconds / 60));
  const since = minutes < 60 ? `${minutes} min` : shortDuration(lostSeconds);
  return { kind: 'lost', label: `Suivi interrompu depuis ${since}`, ageLabel };
}

/**
 * Délai de grâce après une bascule de source à 'none' : deux cycles du moniteur
 * (TourMonitoringService, 30 s), le temps qu'il retienne la source qui émet.
 */
export const TRACKING_SWITCH_GRACE_SECONDS = 60;

/**
 * Niveau de batterie du téléphone → « 63 % », null si absent.
 *
 * Le contrat est un ENTIER 0-100 de bout en bout : le mobile envoie
 * Math.round(batteryLevel × 100), le serveur le stocke en short
 * (DriverAppPosition.BatteryLevel). L'ancienne branche « ≤ 1 = fraction »
 * affichait « 100 % » pour un téléphone à 1 %, au moment exact où il allait
 * s'éteindre et le relais de suivi tomber.
 */
export function phoneBatteryLabel(level: number | null | undefined): string | null {
  if (level == null || isNaN(Number(level))) return null;
  const pct = Math.round(Number(level));
  return `${Math.min(100, Math.max(0, pct))} %`;
}

// ─────────────────────────── Carte : marqueur du téléphone ───────────────────────────

/** Au-delà, le serveur ne tient plus le téléphone pour vivant (TrackingSourceSelector.PhoneAlive). */
export const PHONE_ALIVE_SECONDS = 180;

export interface PhoneTrackingSnapshot {
  source?: string | null;
  phoneAvailable?: boolean | null;
  phone?: { latitude?: number | null; longitude?: number | null; recordedAt?: string | null } | null;
}

export interface PhoneMarkerState {
  /** Faux : pas de marqueur (aucun point, ou point périmé pendant que le boîtier suit). */
  visible: boolean;
  /** Vrai seulement quand le téléphone est LA source qui suit : marqueur plein, sinon grisé. */
  live: boolean;
  /** « il y a 4 min », d'après phone.recordedAt ; null si inconnu. */
  ageLabel: string | null;
  /** Précision affichée dans l'infobulle quand le marqueur n'est pas en direct. */
  note: string | null;
}

/**
 * Comment montrer le téléphone du chauffeur sur la carte du détail.
 *
 * GET /tracking renvoie le DERNIER point du téléphone pour la tournée, sans borne
 * d'âge : en mode « eco » (boîtier vivant) le téléphone n'en garde qu'un toutes les
 * 2 min, et un téléphone éteint laisse un point figé pour toujours. Affiché plein et
 * sans âge, ce point passait pour une position en direct — 2 à 3 km derrière le
 * camion à 80 km/h, ou immobile depuis des heures : le gestionnaire croyait le
 * chauffeur séparé de son véhicule. Donc : plein seulement quand le téléphone suit
 * la tournée, grisé et daté sinon, retiré quand le boîtier suit et que le point a
 * plus de 3 min.
 */
export function phoneMarkerState(t: PhoneTrackingSnapshot | null | undefined, nowMs: number = Date.now()): PhoneMarkerState {
  const phone = t?.phone;
  if (!phone || phone.latitude == null || phone.longitude == null) {
    return { visible: false, live: false, ageLabel: null, note: null };
  }
  const at = phone.recordedAt ? new Date(phone.recordedAt).getTime() : NaN;
  const ageSeconds = isNaN(at) ? null : Math.max(0, (nowMs - at) / 1000);
  const ageLabel = positionAgeLabel(ageSeconds);

  // phoneAvailable est recalculé à chaque appel, la source par le moniteur toutes
  // les 30 s : un téléphone muet depuis plus de 3 min n'est plus « en direct ».
  if (t!.source === 'phone' && t!.phoneAvailable !== false) {
    return { visible: true, live: true, ageLabel, note: null };
  }
  if (t!.source === 'device') {
    const fresh = ageSeconds != null && ageSeconds <= PHONE_ALIVE_SECONDS;
    return { visible: fresh, live: false, ageLabel, note: 'Le boîtier suit la tournée' };
  }
  // Aucune source : la dernière position connue du téléphone reste utile, datée.
  return { visible: true, live: false, ageLabel, note: 'Position non actualisée' };
}

// ─────────────────────────── Liste : état d'envoi ───────────────────────────

export type TourSendStatus = 'not_sent' | 'sent' | 'opened' | 'departed';

export function tourSendStatus(
  t: { sentAt?: string | null; openedAt?: string | null; actualStartTime?: string | null } | null | undefined
): TourSendStatus {
  if (!t?.sentAt) return 'not_sent';
  if (t.actualStartTime) return 'departed';
  if (openedSinceLastSend(t)) return 'opened';
  return 'sent';
}

/**
 * Heure d'ouverture sur le téléphone si elle SUIT le dernier envoi, sinon null.
 *
 * Un renvoi (« Tournée mise à jour ») ne remet pas OpenedAt à zéro côté serveur,
 * et /opened n'écrit OpenedAt que s'il est vide : une tournée ouverte à 08:05 puis
 * renvoyée à 10:45 restait « Ouverte » — le gestionnaire ne pouvait pas savoir si
 * la mise à jour avait été vue. Une ouverture antérieure à l'envoi concerne
 * l'ancienne version. Les deux heures viennent de l'horloge du serveur.
 */
export function openedSinceLastSend(
  t: { sentAt?: string | null; openedAt?: string | null } | null | undefined
): string | null {
  if (!t?.openedAt) return null;
  if (!t.sentAt) return t.openedAt;
  const opened = new Date(t.openedAt).getTime();
  const sent = new Date(t.sentAt).getTime();
  if (isNaN(opened) || isNaN(sent)) return t.openedAt;
  return opened >= sent ? t.openedAt : null;
}

export function sendStatusLabel(s: TourSendStatus): string {
  return ({ not_sent: 'Non envoyée', sent: 'Envoyée', opened: 'Ouverte', departed: 'Partie' } as const)[s];
}

// ─────────────────────────── Détail : départ signalé par le chauffeur ───────────────────────────

/**
 * Heure du « Je pars » du chauffeur (DriverDepartedAt de l'origine) ; null si le
 * départ vient du boîtier ou du gestionnaire, ou n'a pas eu lieu.
 *
 * Seul DriverAppController écrit DriverDepartedAt (DriverTourRules.DeclareDeparture).
 * On ne se fie plus à arrivalSource = driver : dès le cycle suivant (30 s), le
 * moniteur CONFIRME l'origine déclarée par le boîtier ou le téléphone
 * (ConfirmDeclaredArrival remplace ArrivalSource), et la ligne « Départ signalé
 * par le chauffeur » disparaissait du détail alors que le chauffeur l'avait bien
 * signalé.
 */
export function driverDepartureSignaledAt(
  t: { waypoints?: { type?: string | null; driverDepartedAt?: string | null }[] | null } | null | undefined
): string | null {
  if (!t?.waypoints?.length) return null;
  const origin = t.waypoints.find(w => w.type === 'origin') ?? t.waypoints[0];
  return origin?.driverDepartedAt ?? null;
}

// ─────────────────────────── Adresse /tournees/:id ───────────────────────────

/**
 * Id de tournée d'une adresse /tournees/:id ou /tours/:id ; 0 pour la liste
 * (/tournees, /tours) ; null pour toute autre page.
 */
export function tourIdFromUrl(url: string | null | undefined): number | null {
  const path = (url || '').split(/[?#]/)[0];
  const m = /^\/(?:tournees|tours)(?:\/(\d+))?\/?$/.exec(path);
  if (!m) return null;
  return m[1] ? Number(m[1]) : 0;
}
