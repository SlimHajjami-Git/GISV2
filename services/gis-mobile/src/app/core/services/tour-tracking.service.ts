import { Injectable, NgZone } from '@angular/core';
import { BehaviorSubject, firstValueFrom } from 'rxjs';
import { ApiService } from './api.service';
import { KvStore } from './kv-store.service';
import { PhoneFix, PhoneLocationService } from './phone-location.service';
import { PhonePoint, TrackingMode } from '../models/driver-app.types';
import { haversineM } from '../util/polyline';

/** Ce que l'application retient d'un suivi en cours (persisté pour survivre à un redémarrage). */
export interface TrackingState {
  tourId: number;
  /** Millisecondes epoch du « Je pars » (ou de la reprise côté serveur). */
  startedAt: number;
  mode: TrackingMode;
}

/** Ce que le serveur dit du suivi dans ses réponses (positions, depart, arrive). */
export interface TrackingVerdict {
  tracking: boolean;
  mode: TrackingMode;
  activeTourId?: number | null;
}

export const TRACKING_STATE_KEY = 'driver_tracking_state';
export const TRACKING_QUEUE_KEY = 'driver_tracking_queue';
/** Capteur non démarré : la permission de localisation n'est pas accordée. */
export const SENSOR_NOT_AUTHORIZED = 'NOT_AUTHORIZED';

/**
 * Suivi de la tournée par le téléphone du chauffeur — relais du boîtier du véhicule.
 *
 * Cycle : démarre au « Je pars » (réponse serveur tracking:true), s'arrête quand une
 * réponse dit tracking:false (destination atteinte, tournée annulée), à la
 * déconnexion, ou de lui-même 12 h après le départ (DriverTourRules.MaxTrackingDuration).
 *
 * Cadence (décidée par le serveur via `mode`) :
 *  - full (boîtier muet) : un point toutes les 15 s OU tous les 50 m ;
 *  - eco  (boîtier vivant) : un point toutes les 2 min ;
 *  - dans les deux cas un battement AU MOINS toutes les 60 s, même immobile (dernière
 *    position connue, datée de maintenant : « je suis toujours là »).
 * Envoi par lots toutes les 30 s (200 points au plus, DriverTourRules.MaxPositionsPerBatch)
 * avec sentAt (correction d'horloge côté serveur) et le niveau de batterie. File vide :
 * un lot vide part quand même toutes les 5 min, seul moyen d'apprendre du serveur que
 * la tournée est finie (un chauffeur n'a pas SignalR).
 *
 * Hors ligne : les points s'accumulent dans une file persistée (Preferences), bornée à
 * 2 000 points (les plus anciens sont abandonnés) ; le serveur écarte lui-même ce qui
 * tombe hors de la fenêtre de la tournée.
 *
 * Le capteur est derrière PhoneLocationService (plugin background-geolocation : service
 * au premier plan Android de type « location », notification permanente « Tournée en
 * cours »). Il n'est démarré QUE si la permission est déjà accordée : l'invite système
 * relève de LocationConsentService (écran d'explication d'abord).
 */
@Injectable({ providedIn: 'root' })
export class TourTrackingService {
  static readonly FULL_MIN_INTERVAL_MS = 15_000;
  static readonly FULL_MIN_DISTANCE_M = 50;
  static readonly ECO_MIN_INTERVAL_MS = 120_000;
  static readonly HEARTBEAT_MS = 60_000;
  static readonly FLUSH_MS = 30_000;
  static readonly EMPTY_POLL_MS = 5 * 60_000;
  static readonly MAX_BATCH = 200;
  static readonly MAX_QUEUE = 2000;
  static readonly MAX_DURATION_MS = 12 * 60 * 60 * 1000;
  static readonly NOTIFICATION_TITLE = 'Tournée en cours';
  static readonly NOTIFICATION_MESSAGE = 'Calypso transmet votre position à votre gestionnaire pendant la tournée.';

  /**
   * Filtre de distance demandé au capteur selon le mode. Le plugin natif tourne à 1 Hz
   * et ne livre une position qu'après ce déplacement : 10 m en « full » (assez fin pour
   * la règle 15 s / 50 m appliquée ici, sans réveiller la WebView chaque seconde),
   * 100 m en « eco ». À l'arrêt le capteur se tait : c'est le battement qui parle.
   */
  private static distanceFilterFor(mode: TrackingMode): number {
    return mode === 'eco' ? 100 : 10;
  }

  private state: TrackingState | null = null;
  private queue: PhonePoint[] = [];
  private lastFix: PhoneFix | null = null;
  private lastAccepted: { lat: number; lng: number; at: number } | null = null;
  private lastSendAt = 0;
  private heartbeatTimer: any = null;
  private flushTimer: any = null;
  private maxDurationTimer: any = null;
  private flushing = false;
  /** Tant que ceci rend vrai, aucun lot ne part (déclarations hors ligne à rejouer d'abord). */
  private flushGate: (() => boolean) | null = null;

  /** État courant (null = pas de suivi) ; les pages s'y abonnent pour afficher « Suivi actif ». */
  readonly state$ = new BehaviorSubject<TrackingState | null>(null);
  /** Dernier souci du capteur (NOT_AUTHORIZED…), null quand tout va bien. */
  readonly sensorError$ = new BehaviorSubject<string | null>(null);

  constructor(
    private api: ApiService,
    private store: KvStore,
    private location: PhoneLocationService,
    private zone: NgZone
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

  /** Points en attente d'envoi. */
  get pendingCount(): number {
    return this.queue.length;
  }

  /**
   * Un « Je pars » resté en file hors ligne n'a pas encore démarré la tournée côté
   * serveur : un lot envoyé avant lui recevrait tracking:false et arrêterait le suivi.
   * DriverDeclarationsService pose donc cette barrière tant que sa file n'est pas vide.
   */
  setFlushGate(gate: (() => boolean) | null): void {
    this.flushGate = gate;
  }

  /**
   * Au démarrage de l'application (compte chauffeur) : reprend un suivi interrompu
   * par un redémarrage du téléphone ou de l'application, sauf s'il a dépassé 12 h.
   */
  async resume(): Promise<void> {
    if (this.state) return;
    const saved = await this.store.get<TrackingState>(TRACKING_STATE_KEY);
    if (!saved) return;
    if (Date.now() - saved.startedAt >= TourTrackingService.MAX_DURATION_MS) {
      await this.store.remove(TRACKING_STATE_KEY);
      await this.store.remove(TRACKING_QUEUE_KEY);
      return;
    }
    await this.begin(saved);
  }

  /** Démarre (ou ajuste) le suivi de cette tournée ; idempotent. */
  async start(tourId: number, mode: TrackingMode): Promise<void> {
    if (this.state && this.state.tourId === tourId) {
      await this.setMode(mode);
      return;
    }
    if (this.state) {
      // Autre tournée : on repart de zéro (le serveur n'en suit qu'une par chauffeur).
      await this.stop({ flush: true });
    }
    await this.begin({ tourId, startedAt: Date.now(), mode });
  }

  /** Arrête tout ; `flush` = tenter un dernier envoi des points restants (avant déconnexion). */
  async stop(options: { flush?: boolean } = {}): Promise<void> {
    const wasActive = this.state !== null;
    this.clearTimers();
    this.state = null;
    await this.location.stopWatch();
    await this.store.remove(TRACKING_STATE_KEY);

    if (wasActive && options.flush && this.queue.length > 0) {
      try {
        await this.sendBatch();
      } catch { /* meilleur effort */ }
    }
    this.queue = [];
    this.lastFix = null;
    this.lastAccepted = null;
    await this.store.remove(TRACKING_QUEUE_KEY);
    if (wasActive || this.state$.value !== null) {
      this.zone.run(() => { this.state$.next(null); this.sensorError$.next(null); });
    }
  }

  /**
   * Applique ce que le serveur vient de dire (réponse à un lot, à « Je pars », à
   * « Je suis arrivé ») : arrêt sur tracking:false, démarrage ou changement de cadence sinon.
   * `tourId` : la tournée concernée quand la réponse ne la nomme pas (depart / arrive).
   */
  async applyVerdict(verdict: TrackingVerdict, tourId?: number | null): Promise<void> {
    if (!verdict.tracking) {
      if (this.state) await this.stop();
      return;
    }
    const id = verdict.activeTourId ?? tourId ?? this.state?.tourId ?? null;
    if (id == null) return;
    await this.start(id, verdict.mode === 'eco' ? 'eco' : 'full');
  }

  /** Après que le chauffeur a accordé la permission (écran d'explication) : (re)lance le capteur. */
  async restartSensor(): Promise<void> {
    await this.startWatcher();
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
    this.enqueue(fix, fix.time || now);
  }

  /** Envoi du lot en attente (minuterie de 30 s, ou à la demande après une déclaration). */
  async flush(): Promise<void> {
    if (!this.state || this.flushing) return;
    if (this.flushGate && this.flushGate()) return;
    if (this.queue.length === 0 && Date.now() - this.lastSendAt < TourTrackingService.EMPTY_POLL_MS) return;

    this.flushing = true;
    try {
      const verdict = await this.sendBatch();
      if (verdict) await this.applyVerdict(verdict);
    } catch (err: any) {
      // Réseau absent (status 0) ou erreur passagère : les points restent en file.
      // 403 = plus de fiche chauffeur / compte hors périmètre : inutile d'insister.
      if (err?.status === 403) await this.stop();
    } finally {
      this.flushing = false;
    }
  }

  // ────────────────── interne ──────────────────

  private async begin(state: TrackingState): Promise<void> {
    this.state = state;
    await this.store.set(TRACKING_STATE_KEY, state);
    const saved = await this.store.get<PhonePoint[]>(TRACKING_QUEUE_KEY);
    this.queue = Array.isArray(saved) ? saved.slice(-TourTrackingService.MAX_QUEUE) : [];
    this.lastAccepted = null;
    this.lastFix = null;
    this.lastSendAt = Date.now();
    await this.startWatcher();
    this.armTimers();
    this.zone.run(() => this.state$.next(state));
  }

  private async setMode(mode: TrackingMode): Promise<void> {
    if (!this.state || this.state.mode === mode) return;
    this.state = { ...this.state, mode };
    await this.store.set(TRACKING_STATE_KEY, this.state);
    // Le filtre de distance du capteur dépend du mode : on relance le capteur.
    await this.startWatcher();
    this.zone.run(() => this.state$.next(this.state));
  }

  private async startWatcher(): Promise<void> {
    if (!this.state) return;
    if (!(await this.location.isPermissionGranted())) {
      this.setSensorError(SENSOR_NOT_AUTHORIZED);
      return;
    }
    try {
      await this.location.startWatch(
        (fix, error) => {
          if (error) {
            console.warn('[TourTracking] capteur :', error);
            this.setSensorError(error.code || error.message || 'LOCATION_ERROR');
            return;
          }
          if (fix) {
            if (this.sensorError$.value) this.setSensorError(null);
            this.ingest(fix);
          }
        },
        {
          title: TourTrackingService.NOTIFICATION_TITLE,
          message: TourTrackingService.NOTIFICATION_MESSAGE,
          distanceFilterM: TourTrackingService.distanceFilterFor(this.state.mode)
        }
      );
      this.setSensorError(null);
    } catch (e: any) {
      console.error('[TourTracking] impossible de démarrer le capteur', e);
      this.setSensorError(e?.code || e?.message || 'LOCATION_ERROR');
    }
  }

  private setSensorError(code: string | null): void {
    if (this.sensorError$.value === code) return;
    this.zone.run(() => this.sensorError$.next(code));
  }

  private armTimers(): void {
    this.clearTimers();
    this.scheduleHeartbeat();
    this.flushTimer = setInterval(() => { this.flush(); }, TourTrackingService.FLUSH_MS);
    const remaining = this.state
      ? Math.max(0, this.state.startedAt + TourTrackingService.MAX_DURATION_MS - Date.now())
      : 0;
    this.maxDurationTimer = setTimeout(() => { this.stop({ flush: true }); }, remaining);
  }

  private clearTimers(): void {
    if (this.heartbeatTimer) { clearTimeout(this.heartbeatTimer); this.heartbeatTimer = null; }
    if (this.flushTimer) { clearInterval(this.flushTimer); this.flushTimer = null; }
    if (this.maxDurationTimer) { clearTimeout(this.maxDurationTimer); this.maxDurationTimer = null; }
  }

  /**
   * Battement : jamais plus de 60 s sans point. À l'échéance, la dernière position
   * connue du capteur repart, datée de maintenant (téléphone immobile : le capteur se
   * tait, la position reste vraie). Le battement ne touche PAS à la référence de
   * cadence (lastAccepted) : en « eco », le vrai point des 2 min reste dû.
   */
  private scheduleHeartbeat(): void {
    if (this.heartbeatTimer) clearTimeout(this.heartbeatTimer);
    this.heartbeatTimer = setTimeout(() => {
      this.heartbeatTimer = null;
      if (!this.state) return;
      if (this.lastFix) {
        this.enqueue(this.lastFix, Date.now());
      } else {
        this.scheduleHeartbeat();
      }
    }, TourTrackingService.HEARTBEAT_MS);
  }

  /** Met un point en file (bornée), relance le battement, persiste. */
  private enqueue(fix: PhoneFix, recordedAtMs: number): void {
    this.queue.push({
      recordedAt: new Date(recordedAtMs).toISOString(),
      latitude: fix.latitude,
      longitude: fix.longitude,
      accuracyM: fix.accuracyM,
      speedKph: fix.speedKph,
      heading: fix.heading,
      isMocked: fix.isMocked
    });
    while (this.queue.length > TourTrackingService.MAX_QUEUE) this.queue.shift();
    this.scheduleHeartbeat();
    this.store.set(TRACKING_QUEUE_KEY, this.queue).catch(() => { /* la file reste en mémoire */ });
  }

  /** Envoie au plus 200 points ; retire ceux que le serveur a reçus ; rend son verdict. */
  private async sendBatch(): Promise<TrackingVerdict | null> {
    const batch = this.queue.slice(0, TourTrackingService.MAX_BATCH);
    const batteryLevel = await this.location.getBatteryLevel();
    const res = await firstValueFrom(this.api.postDriverPositions({
      points: batch,
      sentAt: new Date().toISOString(),
      batteryLevel
    }));
    this.lastSendAt = Date.now();
    // Retirer exactement les points envoyés : d'autres ont pu arriver pendant l'appel,
    // et la borne de 2 000 a pu en faire tomber en tête.
    const sent = new Set(batch);
    this.queue = this.queue.filter(p => !sent.has(p));
    await this.store.set(TRACKING_QUEUE_KEY, this.queue);
    return res ?? null;
  }
}
