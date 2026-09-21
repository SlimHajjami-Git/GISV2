import { Injectable } from '@angular/core';
import { Subject, firstValueFrom } from 'rxjs';
import { App } from '@capacitor/app';
import { ApiService } from './api.service';
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
}

/** Résultat d'une déclaration : envoyée, ou mise en file faute de réseau. */
export type DeclarationOutcome =
  | { sent: true; response: DriverEventResponse }
  | { sent: false; queued: true };

export const DECLARATIONS_QUEUE_KEY = 'driver_declarations_queue';

/**
 * « Je pars » / « Je suis arrivé » / « Je repars », avec file HORS LIGNE.
 *
 * Un POST qui échoue faute de réseau (status 0) est mis en file (Preferences) avec son
 * clientTime, puis rejoué dans l'ordre au retour du réseau (événement `online`), au
 * retour de l'application au premier plan, et avant toute nouvelle déclaration (pour
 * que l'ordre des gestes soit respecté). Un refus définitif du serveur (400, 404, 409)
 * lors du rejeu retire la déclaration de la file : le chauffeur la refera à l'écran
 * avec le contexte (une confirmation PENDING_STOPS ne peut pas se donner hors ligne).
 *
 * Les 409 d'une déclaration EN LIGNE (PENDING_STOPS, TOO_FAR) sont rendus à l'appelant,
 * qui demande confirmation ou affiche le message.
 */
@Injectable({ providedIn: 'root' })
export class DriverDeclarationsService {
  private queue: QueuedDeclaration[] = [];
  private loaded: Promise<void> | null = null;
  private replaying: Promise<void> | null = null;
  private seq = 0;
  private listenersArmed = false;

  /** Émis après chaque rejeu réussi (les pages rechargent la tournée). */
  readonly replayed$ = new Subject<QueuedDeclaration>();
  /** Déclaration rejouée puis REFUSÉE par le serveur (abandonnée) : la fiche prévient le chauffeur. */
  readonly dropped$ = new Subject<{ item: QueuedDeclaration; message: string | null }>();
  /** Nombre de déclarations en attente (affiché sur la fiche). */
  readonly pendingCount$ = new Subject<number>();

  constructor(
    private api: ApiService,
    private store: KvStore,
    private location: PhoneLocationService,
    private tracking: TourTrackingService
  ) {
    // Tant qu'un geste attend le réseau, aucun lot de positions ne part : un « Je pars »
    // pas encore rejoué laisserait le serveur répondre tracking:false et couper le suivi.
    this.tracking.setFlushGate(() => this.queue.length > 0);
  }

  /** Branche le rejeu automatique (retour réseau, retour au premier plan). Idempotent. */
  armAutoReplay(): void {
    if (this.listenersArmed) return;
    this.listenersArmed = true;
    try {
      window.addEventListener('online', () => { this.replay(); });
    } catch { /* hors navigateur */ }
    try {
      App.addListener('appStateChange', ({ isActive }) => { if (isActive) this.replay(); });
    } catch { /* hors natif */ }
  }

  get pendingCount(): number {
    return this.queue.length;
  }

  /** Déclarations de cette tournée encore en file (la fiche les affiche « en attente d'envoi »). */
  async pendingFor(tourId: number): Promise<QueuedDeclaration[]> {
    await this.ensureLoaded();
    return this.queue.filter(q => q.tourId === tourId);
  }

  /**
   * Déclare un geste. `withPosition` joint la position courante du téléphone (utile
   * au serveur pour « Je suis arrivé » : TOO_FAR ne se décide qu'avec elle).
   */
  async declare(
    tourId: number,
    waypointId: number,
    kind: DeclarationKind,
    options: { withPosition?: boolean; confirmSkipPending?: boolean; startsTour?: boolean } = {}
  ): Promise<DeclarationOutcome> {
    await this.ensureLoaded();
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

    const item: QueuedDeclaration = { tourId, waypointId, kind, body, seq: ++this.seq };

    // Des gestes plus anciens attendent encore le réseau : celui-ci passe DERRIÈRE eux,
    // sans tenter l'envoi (sinon il pourrait doubler un « Je pars » resté en file).
    if (this.queue.length > 0) {
      return this.queueOffline(item, options.startsTour);
    }

    try {
      const response = await this.send(item);
      await this.tracking.applyVerdict(response, tourId);
      return { sent: true, response };
    } catch (err: any) {
      if (this.isNetworkError(err)) {
        return this.queueOffline(item, options.startsTour);
      }
      throw err;
    }
  }

  private async queueOffline(item: QueuedDeclaration, startsTour?: boolean): Promise<DeclarationOutcome> {
    await this.enqueue(item);
    // « Je pars » hors ligne : le suivi démarre quand même (cadence complète, le serveur
    // ajustera au premier lot) ; les points attendent derrière la déclaration.
    if (item.kind === 'depart' && startsTour) {
      await this.tracking.start(item.tourId, 'full');
    }
    return { sent: false, queued: true };
  }

  /** Rejoue la file dans l'ordre ; s'arrête au premier échec réseau. Un seul rejeu à la fois. */
  replay(): Promise<void> {
    if (!this.replaying) {
      this.replaying = this.doReplay().finally(() => { this.replaying = null; });
    }
    return this.replaying;
  }

  private async doReplay(): Promise<void> {
    await this.ensureLoaded();
    while (this.queue.length > 0) {
      const item = this.queue[0];
      try {
        const response = await this.send(item);
        this.queue.shift();
        await this.persist();
        await this.tracking.applyVerdict(response, item.tourId);
        this.replayed$.next(item);
      } catch (err: any) {
        if (this.isNetworkError(err)) return; // toujours hors ligne : on garde tout
        // Refus définitif : la déclaration ne passera jamais telle quelle.
        console.warn('[DriverDeclarations] déclaration rejouée refusée, abandonnée', item, err?.status, err?.error?.code);
        this.queue.shift();
        await this.persist();
        this.dropped$.next({ item, message: err?.error?.message || null });
      }
    }
    // File vidée : la barrière est levée, les positions accumulées peuvent partir.
    if (this.queue.length === 0) this.tracking.flush();
  }

  private send(item: QueuedDeclaration): Promise<DriverEventResponse> {
    const call = item.kind === 'depart'
      ? this.api.driverDepart(item.tourId, item.waypointId, item.body)
      : this.api.driverArrive(item.tourId, item.waypointId, item.body);
    return firstValueFrom(call);
  }

  /** Pas de réponse du serveur (status 0 : hors ligne, DNS, délai) — à distinguer d'un refus. */
  private isNetworkError(err: any): boolean {
    return !!err && err.status === 0;
  }

  private async enqueue(item: QueuedDeclaration): Promise<void> {
    this.queue.push(item);
    await this.persist();
  }

  private async persist(): Promise<void> {
    await this.store.set(DECLARATIONS_QUEUE_KEY, this.queue);
    this.pendingCount$.next(this.queue.length);
  }

  private ensureLoaded(): Promise<void> {
    if (!this.loaded) {
      this.loaded = (async () => {
        const saved = await this.store.get<QueuedDeclaration[]>(DECLARATIONS_QUEUE_KEY);
        this.queue = Array.isArray(saved) ? saved : [];
        this.seq = this.queue.reduce((m, q) => Math.max(m, q.seq), 0);
        this.pendingCount$.next(this.queue.length);
      })();
    }
    return this.loaded;
  }
}
