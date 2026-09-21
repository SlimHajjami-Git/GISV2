import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Subject, firstValueFrom } from 'rxjs';
import { App } from '@capacitor/app';
import type { PluginListenerHandle } from '@capacitor/core';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { KvStore } from './kv-store.service';
import { PhoneLocationService } from './phone-location.service';
import { TourTrackingService } from './tour-tracking.service';
import { DriverEventRequest, DriverEventResponse } from '../models/driver-app.types';

export type DeclarationKind = 'depart' | 'arrive';

/** Une déclaration en attente de réseau, avec l'heure du geste (clientTime) : le serveur la date ainsi. */
export interface QueuedDeclaration {
  tourId: number;
  waypointId: number;
  kind: DeclarationKind;
  body: DriverEventRequest;
  /** Ordre d'insertion (rejeu dans l'ordre). */
  seq: number;
  /** Compte qui a fait le geste : lui seul la rejoue, jamais le compte suivant du téléphone. */
  userId: string;
  /** « Je pars » qui DÉMARRE une tournée planifiée : tant qu'il attend, aucun lot de positions ne part. */
  startsTour?: boolean;
  /** « Je suis arrivé » à la DESTINATION : les positions en attente partent AVANT (la tournée va se clore). */
  closesTour?: boolean;
}

/** Déclaration rejouée puis refusée par le serveur : conservée jusqu'à ce que le chauffeur l'ait vue. */
export interface RefusedDeclaration {
  tourId: number;
  waypointId: number;
  kind: DeclarationKind;
  message: string | null;
  /** Heure du refus (ISO). */
  at: string;
}

/** Résultat d'une déclaration : envoyée, ou mise en file (réseau absent, serveur indisponible). */
export type DeclarationOutcome =
  | { sent: true; response: DriverEventResponse }
  | { sent: false; queued: true };

/** File et refus persistés PAR COMPTE (téléphone de service partagé). */
export const declarationsQueueKey = (userId: string) => `driver_declarations_queue_${userId}`;
export const declarationsRefusedKey = (userId: string) => `driver_declarations_refused_${userId}`;
/** File de la 1.2.0 de recette, sans propriétaire : jamais rejouée (on ne sait pas de qui elle est). */
export const LEGACY_DECLARATIONS_QUEUE_KEY = 'driver_declarations_queue';

/**
 * Refus MÉTIER du serveur : la déclaration ne passera jamais telle quelle (400 tournée plus
 * en cours, 404 tournée retirée, 409 PENDING_STOPS / TOO_FAR, 403 NO_DRIVER_PROFILE).
 * Tout le reste est PASSAGER et se réessaie : 0 (réseau), 401 (jeton en cours de
 * renouvellement ou serveur d'authentification indisponible), 408, 429, 5xx (rollout de
 * l'API, Traefik 502/503/504). Relecture du 21/09/2026 (constats 6 et 13) : tout statut
 * non nul abandonnait la déclaration — un « Je pars » perdu sur un 503, puis toute la trace
 * hors ligne effacée par la réponse tracking:false qui suivait.
 */
export function isBusinessRefusal(err: any): boolean {
  switch (err?.status) {
    case 400:
    case 404:
    case 409:
    case 422:
      return true;
    case 403:
      return !!err?.error?.code;
    default:
      return false;
  }
}

/**
 * « Je pars » / « Je suis arrivé » / « Je repars », avec file HORS LIGNE.
 *
 * Un POST qui échoue de façon PASSAGÈRE (voir isBusinessRefusal) est mis en file
 * (Preferences, par compte) avec son clientTime, puis rejoué dans l'ordre : au retour du
 * réseau (événement `online`), au retour de l'application au premier plan, avant toute
 * nouvelle déclaration, à chaque échéance de lot bloquée par la barrière, et par une
 * minuterie à délai croissant (30 s → 5 min) tant que la file n'est pas vide. Un refus
 * métier lors du rejeu retire la déclaration de la file ; le chauffeur en est averti par
 * un bandeau qui reste affiché jusqu'à ce qu'il l'ait vu (liste et fiche).
 *
 * Les 409 d'une déclaration EN LIGNE (PENDING_STOPS, TOO_FAR) sont rendus à l'appelant,
 * qui demande confirmation ou affiche le message.
 */
@Injectable({ providedIn: 'root' })
export class DriverDeclarationsService implements OnDestroy {
  static readonly RETRY_MIN_MS = 30_000;
  static readonly RETRY_MAX_MS = 5 * 60_000;
  static readonly MAX_REFUSALS_KEPT = 20;

  private queue: QueuedDeclaration[] = [];
  /** Compte dont la file est en mémoire. */
  private loadedFor: string | null = null;
  private loading: Promise<void> | null = null;
  private replaying: Promise<void> | null = null;
  private seq = 0;
  private listeners: { online: () => void; app: Promise<PluginListenerHandle> | null } | null = null;
  private retryTimer: any = null;
  private retryDelay = DriverDeclarationsService.RETRY_MIN_MS;
  /** Un « Je pars » qui démarre une tournée est en cours d'envoi (pas encore en file). */
  private tourStartInFlight = false;

  /** Émis après chaque rejeu réussi (les pages rechargent la tournée). */
  readonly replayed$ = new Subject<QueuedDeclaration>();
  /** Déclaration rejouée puis REFUSÉE par le serveur (abandonnée) : la fiche ouverte prévient aussitôt. */
  readonly dropped$ = new Subject<{ item: QueuedDeclaration; message: string | null }>();
  /** Refus pas encore vus par le chauffeur (persistés : l'application était peut-être en arrière-plan). */
  readonly refused$ = new BehaviorSubject<RefusedDeclaration[]>([]);
  /** Nombre de déclarations en attente (affiché sur la fiche). */
  readonly pendingCount$ = new Subject<number>();

  constructor(
    private api: ApiService,
    private store: KvStore,
    private location: PhoneLocationService,
    private tracking: TourTrackingService,
    private auth: AuthService
  ) {
    // Seul un « Je pars » pas encore accepté retient les positions : envoyées avant lui,
    // le serveur répondrait tracking:false et couperait le suivi. Une simple arrivée en
    // file ne bloque rien — ces lots-là sont acceptés (constat 14). Barrière fermée :
    // chaque échéance de lot relance le rejeu (constat 10).
    this.tracking.setFlushGate({
      blocked: () => this.tourStartInFlight || this.queue.some(q => q.startsTour),
      unblock: () => this.nudge()
    });
  }

  ngOnDestroy(): void {
    this.disarmAutoReplay();
  }

  /** Branche le rejeu automatique (retour réseau, retour au premier plan). Idempotent. */
  armAutoReplay(): void {
    if (this.listeners) return;
    const online = () => { this.replay(); };
    this.listeners = { online, app: null };
    try {
      window.addEventListener('online', online);
    } catch { /* hors navigateur */ }
    try {
      const handle = App.addListener('appStateChange', ({ isActive }) => { if (isActive) this.replay(); });
      handle.catch(() => { /* hors natif */ });
      this.listeners.app = handle;
    } catch { /* hors natif */ }
  }

  /** Déconnexion : plus aucun rejeu automatique, ni minuterie (un autre compte va peut-être se connecter). */
  disarmAutoReplay(): void {
    this.clearRetry();
    const l = this.listeners;
    if (!l) return;
    this.listeners = null;
    try { window.removeEventListener('online', l.online); } catch { /* hors navigateur */ }
    l.app?.then(h => h.remove()).catch(() => { /* hors natif */ });
  }

  /** Déclarations du compte connecté encore en file. */
  get pendingCount(): number {
    return this.loadedFor && this.loadedFor === this.auth.currentUserId() ? this.queue.length : 0;
  }

  /** Déclarations de cette tournée encore en file (la fiche les affiche « en attente d'envoi »). */
  async pendingFor(tourId: number): Promise<QueuedDeclaration[]> {
    if (!(await this.ensureLoaded())) return [];
    return this.queue.filter(q => q.tourId === tourId);
  }

  /**
   * Déclare un geste. `withPosition` joint la position courante du téléphone (utile au
   * serveur pour « Je suis arrivé » : TOO_FAR ne se décide qu'avec elle). `startsTour` :
   * « Je pars » à l'origine d'une tournée planifiée. `closesTour` : arrivée à la destination.
   */
  async declare(
    tourId: number,
    waypointId: number,
    kind: DeclarationKind,
    options: { withPosition?: boolean; confirmSkipPending?: boolean; startsTour?: boolean; closesTour?: boolean } = {}
  ): Promise<DeclarationOutcome> {
    const me = await this.ensureLoaded();
    if (!me) {
      throw { status: 401, error: { message: 'Session expirée : reconnectez-vous pour envoyer cette déclaration.' } };
    }
    const startsTour = kind === 'depart' && !!options.startsTour;
    const closesTour = kind === 'arrive' && !!options.closesTour;
    const wasTrackingThisTour = this.tracking.activeTourId === tourId;

    if (startsTour) {
      this.tourStartInFlight = true;
      // Le capteur démarre MAINTENANT, écran allumé. À la réception de la réponse (position
      // à obtenir, réseau lent), le chauffeur a peut-être déjà verrouillé son téléphone, et
      // Android interdit alors de lancer le service au premier plan (constat 4).
      await this.tracking.start(tourId, 'full');
    }

    try {
      // D'abord ce qui attend, dans l'ordre : une arrivée ne doit pas doubler un départ.
      await this.replay();

      const body: DriverEventRequest = { clientTime: new Date().toISOString() };
      if (options.confirmSkipPending) body.confirmSkipPending = true;
      if (options.withPosition) {
        const fix = await this.location.getCurrentFix();
        if (fix) {
          body.latitude = fix.latitude;
          body.longitude = fix.longitude;
          body.accuracyM = fix.accuracyM ?? undefined;
        }
      }

      const item: QueuedDeclaration = { tourId, waypointId, kind, body, seq: ++this.seq, userId: me };
      if (startsTour) item.startsTour = true;
      if (closesTour) item.closesTour = true;

      // Des gestes plus anciens attendent encore : celui-ci passe DERRIÈRE eux, sans tenter
      // l'envoi (sinon il pourrait doubler un « Je pars » resté en file).
      if (this.queue.length > 0) return await this.queueOffline(item);

      try {
        if (closesTour) await this.drainBeforeClosing();
        const response = await this.send(item);
        this.tourStartInFlight = false;
        await this.tracking.applyVerdict(response, tourId);
        return { sent: true, response };
      } catch (err: any) {
        if (isBusinessRefusal(err)) {
          // Refus métier (PENDING_STOPS, TOO_FAR, tournée plus en cours…) : rendu à l'écran.
          // Le suivi démarré pour un départ refusé n'a plus lieu d'être.
          if (startsTour && !wasTrackingThisTour && this.tracking.activeTourId === tourId) {
            await this.tracking.stop();
          }
          throw err;
        }
        return await this.queueOffline(item);
      }
    } finally {
      if (startsTour) this.tourStartInFlight = false;
    }
  }

  private async queueOffline(item: QueuedDeclaration): Promise<DeclarationOutcome> {
    await this.enqueue(item);
    this.scheduleRetry();
    return { sent: false, queued: true };
  }

  /** Rejoue la file dans l'ordre ; s'arrête au premier échec passager. Un seul rejeu à la fois. */
  replay(): Promise<void> {
    if (!this.replaying) {
      this.replaying = this.doReplay().finally(() => { this.replaying = null; });
    }
    return this.replaying;
  }

  /** Refus vus par le chauffeur (bandeau fermé) : on les oublie. Sans `tourId`, tous. */
  async acknowledgeRefusals(tourId?: number): Promise<void> {
    const me = await this.ensureLoaded();
    if (!me) return;
    const rest = tourId == null ? [] : this.refused$.value.filter(r => r.tourId !== tourId);
    this.refused$.next(rest);
    if (rest.length === 0) await this.store.remove(declarationsRefusedKey(me));
    else await this.store.set(declarationsRefusedKey(me), rest);
  }

  /**
   * Déconnexion VOLONTAIRE : l'appelant a déjà tenté un dernier rejeu (et prévenu le
   * chauffeur de ce qui resterait) ; ce qui reste de la file de CE compte est abandonné.
   */
  async discardForCurrentUser(): Promise<void> {
    this.clearRetry();
    const me = await this.ensureLoaded();
    if (!me) return;
    this.queue = [];
    await this.store.remove(declarationsQueueKey(me));
    await this.store.remove(declarationsRefusedKey(me));
    this.refused$.next([]);
    this.pendingCount$.next(0);
  }

  // ────────────────── interne ──────────────────

  /**
   * Échéance de lot bloquée par la barrière (toutes les 30 s pendant le suivi) : rejouer.
   * La minuterie à délai croissant reste le filet quand aucun suivi ne tourne.
   */
  private nudge(): void {
    if (this.replaying) return;
    this.replay();
  }

  private async doReplay(): Promise<void> {
    const me = await this.ensureLoaded();
    if (!me) return;
    while (this.queue.length > 0) {
      if (this.auth.currentUserId() !== me) return;   // compte changé pendant le rejeu
      const item = this.queue[0];
      try {
        if (item.closesTour) await this.drainBeforeClosing();
        const response = await this.send(item);
        this.queue.shift();
        await this.persist(me);
        await this.tracking.applyVerdict(response, item.tourId);
        this.replayed$.next(item);
      } catch (err: any) {
        if (!isBusinessRefusal(err)) {
          // Passager : on garde tout, dans l'ordre, et on réessaie plus tard. Pas d'envoi
          // de positions ici : un « Je pars » en tête doit passer avant elles.
          this.scheduleRetry();
          return;
        }
        console.warn('[DriverDeclarations] déclaration rejouée refusée, abandonnée', item, err?.status, err?.error?.code);
        this.queue.shift();
        await this.persist(me);
        await this.recordRefusal(me, item, err?.error?.message || null);
      }
    }
    this.resetRetry();
    // File vidée : la barrière est levée, les positions accumulées peuvent partir.
    this.tracking.flush();
  }

  /**
   * Avant « Je suis arrivé » à la destination : toutes les positions en attente partent
   * d'abord — une fois la tournée close, /positions les refuse, et le dernier tronçon
   * (souvent fait hors ligne) était perdu (constats 8 et 16). Un échec PASSAGER remonte :
   * l'arrivée attend alors avec les points plutôt que de les condamner. Un refus métier
   * des positions (403) n'empêche pas l'arrivée : elle aura sa propre réponse.
   */
  private async drainBeforeClosing(): Promise<void> {
    try {
      await this.tracking.drain();
    } catch (err: any) {
      if (!isBusinessRefusal(err)) throw err;
    }
  }

  private send(item: QueuedDeclaration): Promise<DriverEventResponse> {
    // Heure d'ENVOI posée à chaque tentative, jamais stockée dans la file : avec l'heure du
    // geste (clientTime), elle dit au serveur si la déclaration est rejouée et de combien
    // l'horloge du téléphone dérive — il lit alors le boîtier à l'heure du geste.
    const body: DriverEventRequest = { ...item.body, sentAt: new Date().toISOString() };
    const call = item.kind === 'depart'
      ? this.api.driverDepart(item.tourId, item.waypointId, body)
      : this.api.driverArrive(item.tourId, item.waypointId, body);
    return firstValueFrom(call);
  }

  private async recordRefusal(me: string, item: QueuedDeclaration, message: string | null): Promise<void> {
    const entry: RefusedDeclaration = {
      tourId: item.tourId, waypointId: item.waypointId, kind: item.kind, message, at: new Date().toISOString()
    };
    const list = [...this.refused$.value, entry].slice(-DriverDeclarationsService.MAX_REFUSALS_KEPT);
    this.refused$.next(list);
    await this.store.set(declarationsRefusedKey(me), list);
    this.dropped$.next({ item, message });
  }

  private scheduleRetry(): void {
    if (this.retryTimer || !this.auth.currentUserId()) return;
    const delay = this.retryDelay;
    this.retryDelay = Math.min(this.retryDelay * 2, DriverDeclarationsService.RETRY_MAX_MS);
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null;
      this.replay();
    }, delay);
  }

  private resetRetry(): void {
    this.clearRetry();
    this.retryDelay = DriverDeclarationsService.RETRY_MIN_MS;
  }

  private clearRetry(): void {
    if (this.retryTimer) { clearTimeout(this.retryTimer); this.retryTimer = null; }
  }

  private async enqueue(item: QueuedDeclaration): Promise<void> {
    this.queue.push(item);
    await this.persist(item.userId);
  }

  private async persist(me: string): Promise<void> {
    if (this.queue.length === 0) await this.store.remove(declarationsQueueKey(me));
    else await this.store.set(declarationsQueueKey(me), this.queue);
    this.pendingCount$.next(this.queue.length);
  }

  /** Charge la file du compte connecté (une fois par compte) ; rend son identifiant, ou null sans session. */
  private async ensureLoaded(): Promise<string | null> {
    const me = this.auth.currentUserId();
    if (!me) return null;
    if (this.loadedFor === me) return me;
    if (!this.loading) {
      this.loading = (async () => {
        await this.store.remove(LEGACY_DECLARATIONS_QUEUE_KEY);
        const saved = await this.store.get<QueuedDeclaration[]>(declarationsQueueKey(me));
        const refused = await this.store.get<RefusedDeclaration[]>(declarationsRefusedKey(me));
        this.queue = Array.isArray(saved) ? saved.filter(q => q.userId === me) : [];
        this.seq = this.queue.reduce((m, q) => Math.max(m, q.seq), 0);
        this.loadedFor = me;
        this.refused$.next(Array.isArray(refused) ? refused : []);
        this.pendingCount$.next(this.queue.length);
      })().finally(() => { this.loading = null; });
    }
    await this.loading;
    return this.loadedFor === me ? me : this.ensureLoaded();
  }
}
