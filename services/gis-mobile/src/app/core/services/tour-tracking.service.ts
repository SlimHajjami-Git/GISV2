import { Injectable, NgZone } from '@angular/core';
import { BehaviorSubject, firstValueFrom } from 'rxjs';
import { App } from '@capacitor/app';
import { Capacitor } from '@capacitor/core';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { KvStore } from './kv-store.service';
import { LocationStatus, PhoneFix, PhoneLocationService } from './phone-location.service';
import { PhonePoint, TrackingMode } from '../models/driver-app.types';
import { haversineM } from '../util/polyline';

/** Ce que l'application retient d'un suivi en cours (persisté pour survivre à un redémarrage). */
export interface TrackingState {
  tourId: number;
  /** Millisecondes epoch du « Je pars » (ou de la reprise côté serveur). */
  startedAt: number;
  mode: TrackingMode;
  /** Compte propriétaire : un autre compte ne reprend ni ce suivi ni ses points. */
  userId: string;
}

/** Ce que le serveur dit du suivi dans ses réponses (positions, depart, arrive). */
export interface TrackingVerdict {
  tracking: boolean;
  mode: TrackingMode;
  activeTourId?: number | null;
}

/**
 * Barrière des lots de positions (posée par DriverDeclarationsService) : tant que
 * `blocked()` est vrai, aucun lot ne part ; `unblock()` est appelé à chaque échéance
 * bloquée pour relancer ce qui la lèvera (le rejeu des déclarations).
 */
export interface FlushGate {
  blocked(): boolean;
  unblock(): void;
}

/**
 * Un point en file, avec la tournée à laquelle il appartient. La file peut porter les points
 * de DEUX tournées ou plus : au passage de X à Y hors ligne, ceux de X n'ont pas pu partir et
 * ne doivent ni être jetés ni partir au nom de Y (relecture du 22/09/2026). Chaque lot ne
 * contient qu'une tournée et la nomme dans activeTourId. `tourId` absent : point écrit par
 * une version antérieure (rattaché à la tournée reprise, comme avant).
 */
export type QueuedPoint = PhonePoint & { tourId?: number | null };

/** Tournée d'un point en file (null : inconnue, le serveur prend la plus récente en cours). */
function tourOf(p: QueuedPoint): number | null {
  return p.tourId ?? null;
}

/** Le point tel que le serveur l'attend (PhonePoint), sans la tournée qui ne sert qu'ici. */
function toPhonePoint(p: QueuedPoint): PhonePoint {
  return {
    recordedAt: p.recordedAt,
    latitude: p.latitude,
    longitude: p.longitude,
    accuracyM: p.accuracyM,
    speedKph: p.speedKph,
    heading: p.heading,
    isMocked: p.isMocked
  };
}

/** État et file persistés, PAR COMPTE (le téléphone de service peut changer de mains). */
export const trackingStateKey = (userId: string) => `driver_tracking_state_${userId}`;
export const trackingQueueKey = (userId: string) => `driver_tracking_queue_${userId}`;
/** Clés de la 1.2.0 de recette, sans compte : jamais relues, supprimées à la reprise. */
export const LEGACY_TRACKING_KEYS = ['driver_tracking_state', 'driver_tracking_queue'];

/** Capteur non démarré : la permission de localisation n'est pas accordée. */
export const SENSOR_NOT_AUTHORIZED = 'NOT_AUTHORIZED';
/** Seule la position « approximative » est accordée : inutilisable pour le suivi. */
export const SENSOR_COARSE_ONLY = 'COARSE_ONLY';
/** La localisation du TÉLÉPHONE est coupée (réglages rapides). */
export const SENSOR_LOCATION_DISABLED = 'LOCATION_DISABLED';
const PERMISSION_CODES: ReadonlySet<string> = new Set([SENSOR_NOT_AUTHORIZED, SENSOR_COARSE_ONLY, SENSOR_LOCATION_DISABLED]);

/** Au-delà, le serveur ignore un point du téléphone (TrackingSourceSelector.PhoneMaxAccuracyM). */
export const PHONE_MAX_ACCURACY_M = 100;

/** Message du bandeau selon le souci du capteur (fiche, liste, profil). */
export function sensorMessageFor(code: string | null): string {
  switch (code) {
    case SENSOR_NOT_AUTHORIZED:
      return 'Localisation non autorisée : votre position n\'est pas transmise. Touchez ici pour l\'autoriser.';
    case SENSOR_COARSE_ONLY:
      return 'Position approximative seulement : votre position n\'est pas exploitable. Touchez ici pour activer la position précise.';
    case SENSOR_LOCATION_DISABLED:
      return 'Localisation du téléphone désactivée : votre position n\'est pas transmise. Touchez ici pour l\'activer.';
    default:
      return 'Position du téléphone indisponible pour le moment (pas de signal GPS).';
  }
}

function sensorCodeFor(status: LocationStatus): string {
  return status === 'coarse' ? SENSOR_COARSE_ONLY
    : status === 'disabled' ? SENSOR_LOCATION_DISABLED
    : SENSOR_NOT_AUTHORIZED;
}

/**
 * Suivi de la tournée par le téléphone du chauffeur — relais du boîtier du véhicule.
 *
 * Cycle : démarre au « Je pars » (AVANT l'appel réseau, pendant que l'écran est visible),
 * s'arrête quand une réponse dit tracking:false (destination atteinte, tournée annulée),
 * à la déconnexion, ou de lui-même 12 h après le départ (DriverTourRules.MaxTrackingDuration).
 *
 * UN SEUL observateur de position pour toute la tournée, filtre fixe de 10 m. La cadence
 * (décidée par le serveur via `mode`) s'applique ICI, en filtrant les positions :
 *  - full (boîtier muet) : un point toutes les 15 s OU tous les 50 m ;
 *  - eco  (boîtier vivant) : un point toutes les 2 min.
 * Changer de cadence ne touche jamais au capteur : Android 12+ interdit de relancer le
 * service au premier plan depuis l'arrière-plan, et le suivi mourait en silence à chaque
 * bascule eco/full écran verrouillé (relecture du 21/09/2026, constats 2 et 4).
 *
 * Battement : sans point depuis 60 s, une mesure FRAÎCHE est demandée et part avec SON
 * heure ; pas de mesure, pas de point (constats 9 et 20).
 *
 * Envoi par lots toutes les 30 s (200 points au plus, DriverTourRules.MaxPositionsPerBatch)
 * avec sentAt (correction d'horloge côté serveur) et le niveau de batterie. File vide :
 * un lot vide part quand même toutes les 5 min, seul moyen d'apprendre du serveur que
 * la tournée est finie (un chauffeur n'a pas SignalR).
 *
 * Hors ligne : les points s'accumulent dans une file persistée PAR COMPTE, bornée à 2 000
 * points (les plus anciens tombent), écrite au plus toutes les 15 s et au passage en
 * arrière-plan (constat 26). Rien n'est jeté sur un échec passager ; à l'arrêt, un dernier
 * envoi est toujours tenté. Une session perdue sans le vouloir garde état et file pour le
 * retour du même chauffeur (constat 12).
 *
 * Reliquat : chaque point porte sa tournée. Ce qui n'a pas pu partir quand le suivi quitte
 * une tournée (fin de tournée, passage à la suivante) reste en file et part au premier envoi
 * réussi, AVANT les points de la tournée suivie, au nom de SA tournée (le serveur accepte
 * les points d'une tournée close jusqu'à sa fin + 2 min). Le verdict d'un tel lot ne pilote
 * pas le suivi. Seules la déconnexion volontaire et la limite des 12 h suppriment sans envoi.
 *
 * Le capteur est derrière PhoneLocationService (plugin background-geolocation : service
 * au premier plan Android de type « location », notification permanente « Tournée en
 * cours »). Il n'est démarré QUE si la position PRÉCISE est déjà accordée et la
 * localisation allumée : l'invite système relève de LocationConsentService.
 */
@Injectable({ providedIn: 'root' })
export class TourTrackingService {
  static readonly FULL_MIN_INTERVAL_MS = 15_000;
  static readonly FULL_MIN_DISTANCE_M = 50;
  static readonly ECO_MIN_INTERVAL_MS = 120_000;
  static readonly HEARTBEAT_MS = 60_000;
  /** Battement : délai pour obtenir une mesure, et âge maximal d'une mesure en cache. */
  static readonly HEARTBEAT_FIX_TIMEOUT_MS = 15_000;
  static readonly HEARTBEAT_FIX_MAX_AGE_MS = 10_000;
  static readonly FLUSH_MS = 30_000;
  static readonly EMPTY_POLL_MS = 5 * 60_000;
  static readonly PERSIST_DEBOUNCE_MS = 15_000;
  static readonly MAX_BATCH = 200;
  static readonly MAX_QUEUE = 2000;
  static readonly MAX_DURATION_MS = 12 * 60 * 60 * 1000;
  /**
   * Filtre de distance du capteur, le même pour toute la tournée : le plugin tourne à
   * 1 Hz et ne livre une position qu'après ce déplacement — assez fin pour la règle
   * 15 s / 50 m, sans réveiller la WebView chaque seconde. À l'arrêt, c'est le battement
   * qui mesure.
   */
  static readonly WATCH_DISTANCE_FILTER_M = 10;
  static readonly NOTIFICATION_TITLE = 'Tournée en cours';
  static readonly NOTIFICATION_MESSAGE = 'Calypso transmet votre position à votre gestionnaire pendant la tournée.';

  private state: TrackingState | null = null;
  private queue: QueuedPoint[] = [];
  /** Compte à qui appartient la file en mémoire (reste connu tant qu'un reliquat y attend). */
  private queueOwner: string | null = null;
  private lastFix: PhoneFix | null = null;
  private lastAccepted: { lat: number; lng: number; at: number } | null = null;
  /** Heure de MESURE du point le plus récent mis en file (jamais deux fois la même mesure). */
  private lastEnqueuedAt = 0;
  private lastSendAt = 0;
  private heartbeatTimer: any = null;
  private flushTimer: any = null;
  private maxDurationTimer: any = null;
  private persistTimer: any = null;
  /** Envoi en cours (lot de la minuterie, vidage) : un seul à la fois, stop() l'attend. */
  private sending: Promise<unknown> | null = null;
  /** Incrémenté à chaque départ/arrêt : la réponse d'un lot parti avant ne relance rien. */
  private epoch = 0;
  private gate: FlushGate | null = null;
  private appListenerArmed = false;

  /** État courant (null = pas de suivi) ; les pages s'y abonnent pour afficher « Suivi actif ». */
  readonly state$ = new BehaviorSubject<TrackingState | null>(null);
  /** Dernier souci du capteur (NOT_AUTHORIZED, COARSE_ONLY, LOCATION_DISABLED…), null quand tout va bien. */
  readonly sensorError$ = new BehaviorSubject<string | null>(null);

  constructor(
    private api: ApiService,
    private store: KvStore,
    private location: PhoneLocationService,
    private zone: NgZone,
    private auth: AuthService
  ) {}

  get isActive(): boolean {
    return this.state !== null;
  }

  get activeTourId(): number | null {
    return this.state?.tourId ?? null;
  }

  get mode(): TrackingMode | null {
    return this.state?.mode ?? null;
  }

  /** Points en attente d'envoi (reliquat d'une tournée précédente compris). */
  get pendingCount(): number {
    return this.queue.length;
  }

  /**
   * Un « Je pars » pas encore accepté par le serveur n'a pas démarré la tournée : un lot
   * envoyé avant lui recevrait tracking:false et arrêterait le suivi.
   * DriverDeclarationsService pose donc cette barrière tant qu'un tel départ attend.
   */
  setFlushGate(gate: FlushGate | null): void {
    this.gate = gate;
  }

  /**
   * Au démarrage de l'application (compte chauffeur) : reprend le suivi interrompu DE CE
   * COMPTE (redémarrage du téléphone, session perdue puis retrouvée), sauf au-delà de 12 h.
   */
  async resume(): Promise<void> {
    if (this.state) return;
    const userId = this.auth.currentUserId();
    if (!userId) return;
    for (const key of LEGACY_TRACKING_KEYS) await this.store.remove(key);

    const saved = await this.store.get<TrackingState>(trackingStateKey(userId));
    if (this.state) return;
    if (!saved) {
      // Aucun suivi à reprendre, mais peut-être le reliquat d'une tournée quittée sans
      // réseau (application tuée depuis) : il part au premier envoi possible.
      await this.loadLeftovers(userId);
      this.flush();
      return;
    }
    if (Date.now() - saved.startedAt >= TourTrackingService.MAX_DURATION_MS) {
      await this.store.remove(trackingStateKey(userId));
      await this.store.remove(trackingQueueKey(userId));
      if (this.queueOwner === userId) { this.queue = []; this.queueOwner = null; }
      return;
    }
    await this.begin({ ...saved, userId });
  }

  /** Démarre (ou ajuste) le suivi de cette tournée ; idempotent. */
  async start(tourId: number, mode: TrackingMode): Promise<void> {
    if (this.state && this.state.tourId === tourId) {
      await this.setMode(mode);
      return;
    }
    const userId = this.auth.currentUserId();
    if (!userId) return;
    if (this.state) {
      // Autre tournée (le serveur n'en suit qu'une par chauffeur) : dernier envoi des points
      // de l'ancienne, mais le capteur et son service au premier plan RESTENT — les
      // recréer depuis l'arrière-plan est interdit par Android. Ce qui ne part pas (hors
      // ligne, 5xx) reste en file au nom de l'ancienne tournée : c'était son dernier
      // tronçon, souvent fait sans réseau, et il était jeté ici.
      await this.halt({ flush: true, keepSensor: true });
    }
    await this.begin({ tourId, startedAt: Date.now(), mode, userId });
  }

  /**
   * Arrêt voulu par l'application.
   *  - `flush` : déconnexion VOLONTAIRE — tenter d'envoyer TOUS les points restants, puis
   *    tout supprimer, reliquat compris (le chauffeur a été prévenu de ce qui attendait).
   *  - `keep` : session perdue sans que le chauffeur l'ait voulu (jeton refusé) — on ne
   *    peut plus rien envoyer, mais état et file restent persistés, rattachés au compte,
   *    et repartent quand CE chauffeur se reconnecte.
   *  - ni l'un ni l'autre (« Je pars » refusé) : les points de la tournée suivie sont
   *    jetés — elle n'a pas démarré ; le reliquat d'une tournée précédente est gardé.
   */
  async stop(options: { flush?: boolean; keep?: boolean } = {}): Promise<void> {
    await this.halt({ ...options, discard: !!options.flush && !options.keep });
  }

  /**
   * Applique ce que le serveur vient de dire (réponse à un lot, à « Je pars », à
   * « Je suis arrivé ») : arrêt sur tracking:false, démarrage ou changement de cadence sinon.
   * `tourId` : la tournée de la DÉCLARATION (depart / arrive), absente pour un lot.
   */
  async applyVerdict(verdict: TrackingVerdict, tourId?: number | null): Promise<void> {
    // Déclaration faite sur une AUTRE tournée que celle suivie (arrivée ou « Je repars » sur
    // X pendant que le téléphone suit Y, en ligne ou au rejeu de la file) : la réponse ne
    // parle que de X. L'appliquer coupait le suivi de Y (X close : tracking:false) ou le
    // détournait vers X (arrivée intermédiaire : tracking:true), sans un mot — et au rejeu,
    // écran verrouillé, Android refuse ensuite de relancer le service au premier plan : Y
    // restait « Suivi actif » sans transmettre (relecture du 22/09/2026). Seuls les verdicts
    // des lots et ceux de la tournée suivie pilotent le suivi ; le « Je pars » qui démarre
    // une tournée est déjà passé par start() avant l'appel réseau.
    if (tourId != null && this.state && this.state.tourId !== tourId) return;
    if (!verdict.tracking) {
      // Dernier envoi AVANT d'arrêter (constat 8) : le serveur garde ce qui tombe encore
      // dans la fenêtre de la tournée. Un échec passager garde les points en reliquat.
      if (this.state) await this.halt({ flush: true });
      return;
    }
    const id = verdict.activeTourId ?? tourId ?? this.state?.tourId ?? null;
    if (id == null) return;
    await this.start(id, verdict.mode === 'eco' ? 'eco' : 'full');
  }

  /**
   * (Re)vérifie la localisation et démarre le capteur s'il ne tourne pas. Démarrer le
   * service au premier plan n'est permis que l'écran visible : au « Je pars », au retour
   * dans l'application, au toucher du bandeau. `foreground` (écran visible, certain) :
   * un observateur resté accroché à une localisation qui était coupée est recréé.
   */
  async ensureSensor(options: { foreground?: boolean } = {}): Promise<void> {
    if (!this.state) return;
    const status = await this.location.getLocationStatus();
    if (!this.state) return;
    if (status !== 'granted') {
      this.setSensorError(sensorCodeFor(status));
      return;
    }
    if (options.foreground && this.location.isWatching && this.sensorError$.value === SENSOR_LOCATION_DISABLED) {
      await this.location.stopWatch();
    }
    if (!this.location.isWatching) {
      try {
        await this.location.startWatch(
          (fix, error) => this.onSensor(fix, error),
          {
            title: TourTrackingService.NOTIFICATION_TITLE,
            message: TourTrackingService.NOTIFICATION_MESSAGE,
            distanceFilterM: TourTrackingService.WATCH_DISTANCE_FILTER_M
          }
        );
      } catch (e: any) {
        console.error('[TourTracking] impossible de démarrer le capteur', e);
        this.setSensorError(e?.code || e?.message || 'LOCATION_ERROR');
        return;
      }
    }
    // Position précise accordée et localisation allumée, vérifiées à l'instant.
    this.clearPermissionError();
  }

  /** Application revenue au premier plan : relance le capteur s'il est arrêté ou en souci (constat 19). */
  async onForeground(): Promise<void> {
    if (!this.state) return;
    if (this.sensorError$.value || !this.location.isWatching) await this.ensureSensor({ foreground: true });
  }

  /** Application passée en arrière-plan : la file est écrite tout de suite (le processus peut être tué). */
  async onBackground(): Promise<void> {
    await this.persistQueueNow();
  }

  /** Une position du capteur (aussi le point d'entrée des tests). Applique la cadence. */
  ingest(fix: PhoneFix): void {
    if (!this.state) return;
    this.lastFix = fix;

    const now = Date.now();
    let accept: boolean;
    if (!this.lastAccepted) {
      accept = true;
    } else {
      const since = now - this.lastAccepted.at;
      if (this.state.mode === 'eco') {
        accept = since >= TourTrackingService.ECO_MIN_INTERVAL_MS;
      } else {
        const dist = haversineM(this.lastAccepted.lat, this.lastAccepted.lng, fix.latitude, fix.longitude);
        accept = since >= TourTrackingService.FULL_MIN_INTERVAL_MS || dist >= TourTrackingService.FULL_MIN_DISTANCE_M;
      }
    }
    if (!accept) return;
    this.lastAccepted = { lat: fix.latitude, lng: fix.longitude, at: now };
    this.enqueue(fix);
  }

  /**
   * Envoi (minuterie de 30 s, ou à la demande après le rejeu des déclarations) : d'abord
   * le reliquat des tournées quittées, puis un lot de la tournée suivie.
   */
  async flush(): Promise<void> {
    if (this.sending) return;
    const epoch = this.epoch;

    // Reliquat d'une tournée précédente : il part en premier, barrière fermée ou non. Son
    // lot nomme SA tournée ; son verdict ne concerne qu'elle et n'est pas appliqué — il
    // arrêterait (tournée close) ou détournerait (encore en cours) le suivi de l'actuelle.
    // La tournée suivie est figée MAINTENANT : si le suivi s'arrête pendant l'envoi (« Je
    // pars » refusé), ses points ne doivent pas partir avec le reliquat.
    const leftover = this.leftoverFilter();
    if (this.ownsQueue() && this.queue.some(p => leftover(tourOf(p)))) {
      try {
        await this.exclusive(() => this.drainQueue(leftover));
      } catch (err: any) {
        if (err?.status === 403 && epoch === this.epoch) await this.halt({ discard: true });
        return;   // passager : tout reste en file, la tournée suivie attend son tour
      }
    }
    if (!this.state || epoch !== this.epoch) return;

    if (this.gate && this.gate.blocked()) {
      // Barrière fermée : ce qui la lèvera (le rejeu du « Je pars ») est relancé ici, à
      // chaque échéance. Sans cela, un départ en file sans coupure réseau visible
      // (événement « online » jamais émis, application en arrière-plan derrière le GPS)
      // bloquait toutes les positions de la tournée (constats 10 et 14).
      this.gate.unblock();
      return;
    }
    const tourId = this.state.tourId;
    const mine = this.queue.some(p => tourOf(p) === tourId);
    if (!mine && Date.now() - this.lastSendAt < TourTrackingService.EMPTY_POLL_MS) return;

    let verdict: TrackingVerdict | null;
    try {
      verdict = await this.exclusive(() => this.sendBatch(tourId));
    } catch (err: any) {
      // Réseau absent, 401 pendant un rafraîchissement, 5xx : les points restent en file.
      // 403 = plus de fiche chauffeur / compte hors périmètre : inutile d'insister.
      if (err?.status === 403 && epoch === this.epoch) await this.halt({ discard: true });
      return;
    }
    // Arrêté ou passé à une autre tournée pendant l'appel : ce verdict ne concerne plus
    // rien et ne doit pas relancer le suivi (constat 27).
    if (verdict && epoch === this.epoch) await this.applyVerdict(verdict);
  }

  /**
   * Envoie les points en attente, lot après lot, en ignorant la barrière. Appelé avant
   * « Je suis arrivé » à la DESTINATION de `closingTourId` : une fois la tournée close,
   * /positions refuse ses points au-delà de sa fin + 2 min, et le dernier tronçon (souvent
   * fait hors ligne) était perdu (constats 8 et 16). Le reliquat de cette tournée part donc
   * aussi. Seuls les points d'une AUTRE tournée suivie restent : son « Je pars » attend
   * peut-être encore dans la file, et envoyés maintenant ils seraient rangés sur la
   * tournée qu'on clôt. Sans `closingTourId`, tout part. Lève l'erreur d'un échec
   * passager : l'arrivée doit alors attendre.
   */
  async drain(closingTourId?: number | null): Promise<void> {
    if (this.queueOwner !== null && !this.ownsQueue()) return;
    const tracked = this.state?.tourId ?? null;
    const held = closingTourId != null && tracked != null && tracked !== closingTourId ? tracked : null;
    const accept = (t: number | null) => held == null || t !== held;
    if (!this.queue.some(p => accept(tourOf(p))) && !this.sending) return;
    await this.exclusive(() => this.drainQueue(accept));
  }

  // ────────────────── interne ──────────────────

  /** Reliquat = tout ce qui n'est pas la tournée suivie à cet instant (tout, si rien n'est suivi). */
  private leftoverFilter(): (tourId: number | null) => boolean {
    const tracked = this.state ? this.state.tourId : null;
    return t => tracked == null || t !== tracked;
  }

  /** La file en mémoire appartient au compte connecté : jamais envoyée avec le jeton d'un autre. */
  private ownsQueue(): boolean {
    return this.queueOwner !== null && this.queueOwner === this.auth.currentUserId();
  }

  /** Charge le reliquat persisté du compte quand aucun suivi ne tourne (après redémarrage). */
  private async loadLeftovers(userId: string): Promise<void> {
    if (this.queueOwner === userId) return;          // déjà en mémoire
    const saved = await this.store.get<QueuedPoint[]>(trackingQueueKey(userId));
    if (this.state || this.queueOwner === userId) return;   // un départ est passé entre-temps
    if (!Array.isArray(saved) || saved.length === 0) return;
    this.queue = saved.slice(-TourTrackingService.MAX_QUEUE);
    this.queueOwner = userId;
  }

  private async begin(state: TrackingState): Promise<void> {
    this.epoch++;
    // File déjà en mémoire pour CE compte (reliquat de la tournée qu'on vient de quitter) :
    // on la garde telle quelle — la copie persistée peut avoir jusqu'à 15 s de retard.
    // Sinon : celle persistée (déconnexion subie, redémarrage) ; la file d'un autre compte
    // est déjà écrite sous son nom et sort de la mémoire.
    const reuse = this.queueOwner === state.userId;
    if (!reuse) this.queue = [];
    this.state = state;
    this.queueOwner = state.userId;
    await this.store.set(trackingStateKey(state.userId), state);
    if (!reuse) {
      const saved = await this.store.get<QueuedPoint[]>(trackingQueueKey(state.userId));
      // En tête ; derrière, d'éventuels points mesurés pendant ces lectures.
      if (Array.isArray(saved)) this.queue = [...saved, ...this.queue];
    }
    // Points sans tournée (file écrite par une version antérieure) : ceux de la tournée
    // reprise, comme avant — ils partaient au nom de la tournée suivie.
    this.queue = this.queue
      .slice(-TourTrackingService.MAX_QUEUE)
      .map(p => (p.tourId == null ? { ...p, tourId: state.tourId } : p));
    this.lastEnqueuedAt = this.queue.reduce((m, p) => Math.max(m, new Date(p.recordedAt).getTime() || 0), 0);
    this.lastAccepted = null;
    this.lastFix = null;
    this.lastSendAt = Date.now();
    this.armAppListener();
    await this.ensureSensor();
    if (this.state !== state) return;   // arrêté pendant le démarrage
    this.armTimers();
    this.zone.run(() => this.state$.next(state));
  }

  /**
   * Arrêt du suivi.
   *  - `keep` : état et file persistés tels quels (session perdue, voir stop()).
   *  - `flush` : dernier envoi de TOUT ce qui attend, chaque lot au nom de sa tournée.
   *  - `discard` : ensuite, tout est supprimé, reliquat compris — réservé à la déconnexion
   *    volontaire, aux 12 h et au 403 (plus de fiche chauffeur : rien ne passera plus).
   *    Sans lui, ce qui n'a pas pu partir RESTE en file (fin de tournée ou passage à la
   *    suivante sans réseau) et part au premier envoi réussi.
   *  - ni `flush` ni `discard` (« Je pars » refusé) : seuls les points de la tournée
   *    arrêtée sont jetés.
   */
  private async halt(options: { flush?: boolean; keep?: boolean; keepSensor?: boolean; discard?: boolean }): Promise<void> {
    const wasActive = this.state !== null;
    const haltedTourId = this.state?.tourId ?? null;
    const owner = this.state?.userId ?? this.queueOwner;
    this.epoch++;
    this.clearTimers();
    this.state = null;
    if (!options.keepSensor) await this.location.stopWatch();

    if (options.keep && owner) {
      await this.settleSending();
      await this.persistQueueNow();       // l'état persisté reste tel quel pour resume()
      this.queue = [];
      this.queueOwner = null;
    } else {
      if (this.persistTimer) { clearTimeout(this.persistTimer); this.persistTimer = null; }
      if (owner) await this.store.remove(trackingStateKey(owner));
      // Jamais avec le jeton d'un autre compte : ses points iraient sur ses tournées.
      if (options.flush && this.queue.length > 0 && owner && owner === this.auth.currentUserId()) {
        try {
          await this.exclusive(() => this.drainQueue(() => true));
        } catch { /* serveur injoignable : voir ci-dessous ce qui reste */ }
      } else {
        await this.settleSending();
      }
      if (options.discard) {
        this.queue = [];
      } else if (!options.flush) {
        this.queue = this.queue.filter(p => tourOf(p) !== haltedTourId);
      }
      // Ce qui reste est un reliquat : écrit tout de suite (le processus peut être tué),
      // gardé en mémoire pour le prochain envoi.
      if (owner) {
        try {
          if (this.queue.length === 0) await this.store.remove(trackingQueueKey(owner));
          else await this.store.set(trackingQueueKey(owner), this.queue);
        } catch { /* la file reste en mémoire */ }
      }
      this.queueOwner = this.queue.length > 0 ? owner : null;
    }
    this.lastFix = null;
    this.lastAccepted = null;
    this.lastEnqueuedAt = 0;
    if (wasActive || this.state$.value !== null) {
      this.zone.run(() => { this.state$.next(null); this.sensorError$.next(null); });
    }
  }

  private async setMode(mode: TrackingMode): Promise<void> {
    if (!this.state || this.state.mode === mode) return;
    // Seule la cadence change, appliquée dans ingest() : l'observateur et le service au
    // premier plan restent tels quels.
    this.state = { ...this.state, mode };
    await this.store.set(trackingStateKey(this.state.userId), this.state);
    this.zone.run(() => this.state$.next(this.state));
  }

  private onSensor(fix: PhoneFix | null, error?: { code?: string; message?: string }): void {
    if (error) {
      console.warn('[TourTracking] capteur :', error);
      // Le plugin rend NOT_AUTHORIZED aussi quand la localisation du téléphone est coupée.
      const disabled = /disabled/i.test(error.message || '');
      this.setSensorError(disabled ? SENSOR_LOCATION_DISABLED : (error.code || error.message || 'LOCATION_ERROR'));
      return;
    }
    if (!fix) return;
    this.clearErrorOnGoodFix(fix);
    this.ingest(fix);
  }

  /** Un point précis lève le souci affiché ; un point flou (> 100 m, inutilisable par le serveur) non. */
  private clearErrorOnGoodFix(fix: PhoneFix): void {
    if (this.sensorError$.value && fix.accuracyM != null && fix.accuracyM <= PHONE_MAX_ACCURACY_M) {
      this.setSensorError(null);
    }
  }

  private clearPermissionError(): void {
    const current = this.sensorError$.value;
    if (current && PERMISSION_CODES.has(current) && this.location.isWatching) this.setSensorError(null);
  }

  private setSensorError(code: string | null): void {
    if (this.sensorError$.value === code) return;
    this.zone.run(() => this.sensorError$.next(code));
  }

  private armAppListener(): void {
    if (this.appListenerArmed || !Capacitor.isNativePlatform()) return;
    this.appListenerArmed = true;
    try {
      App.addListener('appStateChange', ({ isActive }) => {
        this.zone.run(() => { if (isActive) this.onForeground(); else this.onBackground(); });
      }).catch(() => { /* hors natif */ });
    } catch { /* hors natif */ }
  }

  private armTimers(): void {
    this.clearTimers();
    this.scheduleHeartbeat();
    this.flushTimer = setInterval(() => { this.flush(); }, TourTrackingService.FLUSH_MS);
    const remaining = this.state
      ? Math.max(0, this.state.startedAt + TourTrackingService.MAX_DURATION_MS - Date.now())
      : 0;
    // 12 h : le serveur n'accepte plus rien au-delà (DriverTourRules.MaxTrackingDuration),
    // ce qui ne part pas au dernier envoi est supprimé.
    this.maxDurationTimer = setTimeout(() => { this.halt({ flush: true, discard: true }); }, remaining);
  }

  private clearTimers(): void {
    if (this.heartbeatTimer) { clearTimeout(this.heartbeatTimer); this.heartbeatTimer = null; }
    if (this.flushTimer) { clearInterval(this.flushTimer); this.flushTimer = null; }
    if (this.maxDurationTimer) { clearTimeout(this.maxDurationTimer); this.maxDurationTimer = null; }
  }

  private scheduleHeartbeat(): void {
    if (this.heartbeatTimer) clearTimeout(this.heartbeatTimer);
    this.heartbeatTimer = setTimeout(() => {
      this.heartbeatTimer = null;
      this.heartbeat();
    }, TourTrackingService.HEARTBEAT_MS);
  }

  /**
   * Battement : jamais plus de 60 s sans point TANT QUE le téléphone mesure. On demande
   * une mesure fraîche et elle part avec l'heure de SA mesure. Plus jamais la dernière
   * position redatée de maintenant (constats 9 et 20) : un téléphone dans un tunnel, un
   * parking souterrain ou localisation coupée passait pour vivant, immobile au dernier
   * point ; l'alerte « suivi perdu » ne partait jamais, et deux battements dans le rayon
   * d'une étape longée validaient une fausse arrivée. Pas de mesure : pas de point — le
   * serveur voit alors le téléphone se taire, ce qui est la vérité. Le battement ne touche
   * pas à la référence de cadence (lastAccepted) : en « eco », le point des 2 min reste dû.
   */
  private async heartbeat(): Promise<void> {
    if (!this.state) return;
    const epoch = this.epoch;
    const status = await this.location.getLocationStatus();
    if (epoch !== this.epoch || !this.state) return;
    if (status !== 'granted') {
      this.setSensorError(sensorCodeFor(status));   // ex. localisation coupée en cours de route
      this.scheduleHeartbeat();
      return;
    }
    this.clearPermissionError();
    const fix = await this.location.getCurrentFix(
      TourTrackingService.HEARTBEAT_FIX_TIMEOUT_MS, TourTrackingService.HEARTBEAT_FIX_MAX_AGE_MS);
    if (epoch !== this.epoch || !this.state) return;
    if (fix && (fix.time || 0) > this.lastEnqueuedAt) {
      this.lastFix = fix;
      this.clearErrorOnGoodFix(fix);
      this.enqueue(fix);            // relance le battement
    } else {
      this.scheduleHeartbeat();
    }
  }

  /** Met un point en file (bornée), daté de SA mesure ; relance le battement ; persistance différée. */
  private enqueue(fix: PhoneFix): void {
    if (!this.state) return;
    const measuredAt = fix.time || Date.now();
    this.queue.push({
      recordedAt: new Date(measuredAt).toISOString(),
      latitude: fix.latitude,
      longitude: fix.longitude,
      accuracyM: fix.accuracyM,
      speedKph: fix.speedKph,
      heading: fix.heading,
      isMocked: fix.isMocked,
      tourId: this.state.tourId
    });
    this.lastEnqueuedAt = Math.max(this.lastEnqueuedAt, measuredAt);
    while (this.queue.length > TourTrackingService.MAX_QUEUE) this.queue.shift();
    this.scheduleHeartbeat();
    this.schedulePersist();
  }

  /**
   * Écriture différée de la file : au plus une fois toutes les 15 s. Réécrire toute la file
   * (jusqu'à ~400 Ko) dans Preferences à chaque point accepté, soit toutes les 2 s sur
   * route, coûtait ~0,5 Go d'écritures par heure (constat 26).
   */
  private schedulePersist(): void {
    if (this.persistTimer) return;
    this.persistTimer = setTimeout(() => {
      this.persistTimer = null;
      this.persistQueueNow();
    }, TourTrackingService.PERSIST_DEBOUNCE_MS);
  }

  private async persistQueueNow(): Promise<void> {
    if (this.persistTimer) { clearTimeout(this.persistTimer); this.persistTimer = null; }
    const owner = this.queueOwner;
    if (!owner) return;
    try {
      if (this.queue.length === 0) await this.store.remove(trackingQueueKey(owner));
      else await this.store.set(trackingQueueKey(owner), this.queue);
    } catch { /* la file reste en mémoire */ }
  }

  /** Un seul envoi à la fois : attend celui en cours, puis exécute `fn`. */
  private async exclusive<T>(fn: () => Promise<T>): Promise<T> {
    await this.settleSending();
    const run = fn();
    this.sending = run;
    try {
      return await run;
    } finally {
      if (this.sending === run) this.sending = null;
    }
  }

  private async settleSending(): Promise<void> {
    while (this.sending) {
      try { await this.sending; } catch { /* l'appelant d'origine gère l'erreur */ }
    }
  }

  /**
   * Envoie, lot après lot et dans l'ordre de la file, les points des tournées acceptées
   * par `accept` (verdicts non appliqués). Une tournée pour laquelle le serveur n'a plus
   * rien (tracking:false sans activeTourId : annulée, retirée, hors fenêtre, et aucune
   * autre en cours) refuserait tout le reste de ses points : ils sont retirés, et les
   * autres tournées continuent. Lève l'erreur d'un échec passager (rien n'est perdu).
   */
  private async drainQueue(accept: (tourId: number | null) => boolean): Promise<void> {
    const tours = new Set(this.queue.map(tourOf)).size;
    const maxRounds = Math.ceil(TourTrackingService.MAX_QUEUE / TourTrackingService.MAX_BATCH) + tours + 1;
    for (let i = 0; i < maxRounds; i++) {
      const next = this.queue.find(p => accept(tourOf(p)));
      if (!next) return;
      const tourId = tourOf(next);
      const verdict = await this.sendBatch(tourId);
      if (verdict && !verdict.tracking && verdict.activeTourId == null) {
        this.queue = this.queue.filter(p => tourOf(p) !== tourId);
        await this.persistQueueNow();
      }
    }
  }

  /**
   * Envoie au plus 200 points de `tourId` (les plus anciens) ; retire ceux que le serveur a
   * reçus ; rend son verdict. Un lot ne mélange jamais deux tournées.
   */
  private async sendBatch(tourId: number | null): Promise<TrackingVerdict | null> {
    const batch: QueuedPoint[] = [];
    for (const p of this.queue) {
      if (tourOf(p) !== tourId) continue;
      batch.push(p);
      if (batch.length >= TourTrackingService.MAX_BATCH) break;
    }
    const batteryLevel = await this.location.getBatteryLevel();
    const res = await firstValueFrom(this.api.postDriverPositions({
      points: batch.map(toPhonePoint),
      sentAt: new Date().toISOString(),
      batteryLevel,
      // La tournée de CES points (y compris au dernier envoi, après l'arrêt, et pour le
      // reliquat d'une tournée quittée) : un « Démarrer » du gestionnaire sur une autre
      // tournée ne doit pas détourner la trace.
      activeTourId: tourId
    }));
    // Cadence du lot vide des 5 min : seuls comptent les envois de la tournée suivie.
    if (this.state && this.state.tourId === tourId) this.lastSendAt = Date.now();
    // Retirer exactement les points envoyés : d'autres ont pu arriver pendant l'appel,
    // et la borne de 2 000 a pu en faire tomber en tête.
    const sent = new Set(batch);
    this.queue = this.queue.filter(p => !sent.has(p));
    await this.persistQueueNow();
    return res ?? null;
  }
}
