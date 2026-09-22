import { TestBed, fakeAsync, flushMicrotasks, tick as fakeTick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import {
  DriverDeclarationsService, QueuedDeclaration, RefusedDeclaration, declarationsQueueKey, declarationsRefusedKey,
  LEGACY_DECLARATIONS_QUEUE_KEY, isBusinessRefusal
} from './driver-declarations.service';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { KvStore } from './kv-store.service';
import { PhoneLocationService } from './phone-location.service';
import { FlushGate, TourTrackingService, TrackingVerdict } from './tour-tracking.service';
import { DriverEventRequest, DriverEventResponse } from '../models/driver-app.types';
import { FakeAuth, FakePhoneLocation, MemoryKvStore, fixAt } from '../testing/driver-test-doubles';

interface ApiCall { kind: 'depart' | 'arrive'; tourId: number; waypointId: number; body: DriverEventRequest; }

/** Journal partagé : l'ORDRE des appels (suivi, API) est ce qu'on vérifie. */
let log: string[];

/** Faux ApiService : enregistre les appels ; en ligne / hors ligne / réponse scriptée. */
class FakeDriverApi {
  calls: ApiCall[] = [];
  online = true;
  /** Réponse imposée pour l'appel n° i (0-based) ; sinon selon `online`. */
  script: Record<number, () => Observable<any>> = {};

  driverDepart(tourId: number, waypointId: number, body: DriverEventRequest) { return this.handle('depart', tourId, waypointId, body); }
  driverArrive(tourId: number, waypointId: number, body: DriverEventRequest) { return this.handle('arrive', tourId, waypointId, body); }

  private handle(kind: 'depart' | 'arrive', tourId: number, waypointId: number, body: DriverEventRequest): Observable<DriverEventResponse> {
    const index = this.calls.length;
    this.calls.push({ kind, tourId, waypointId, body: JSON.parse(JSON.stringify(body)) });
    log.push(`api:${kind}:${waypointId}`);
    if (this.script[index]) return this.script[index]();
    if (!this.online) return throwError(() => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' }));
    return of({
      tourStatus: 'in_progress', tracking: true, mode: 'full',
      waypoint: { id: waypointId } as any
    } as DriverEventResponse);
  }
}

/** Faux suivi : garde la tournée suivie et note chaque appel dans le journal. */
class FakeTracking {
  activeTourId: number | null = null;
  gate: FlushGate | null = null;
  drainError: any = null;
  /** Tournée passée à chaque drain() (celle que l'arrivée va clore). */
  drainedFor: (number | null | undefined)[] = [];
  verdicts: { verdict: TrackingVerdict; tourId?: number | null }[] = [];

  setFlushGate(g: FlushGate | null) { this.gate = g; }
  async start(tourId: number, mode: string) { log.push(`start:${tourId}:${mode}`); this.activeTourId = tourId; }
  async stop() { log.push('stop'); this.activeTourId = null; }
  async applyVerdict(verdict: TrackingVerdict, tourId?: number | null) { log.push(`verdict:${tourId}`); this.verdicts.push({ verdict, tourId }); }
  async flush() { log.push('flush'); }
  async drain(tourId?: number | null) {
    log.push('drain');
    this.drainedFor.push(tourId);
    if (this.drainError) throw this.drainError;
  }
}

describe('DriverDeclarationsService (file hors ligne des déclarations)', () => {
  let api: FakeDriverApi;
  let store: MemoryKvStore;
  let loc: FakePhoneLocation;
  let tracking: FakeTracking;
  let auth: FakeAuth;

  function create(): DriverDeclarationsService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        DriverDeclarationsService,
        { provide: ApiService, useValue: api },
        { provide: KvStore, useValue: store },
        { provide: PhoneLocationService, useValue: loc },
        { provide: TourTrackingService, useValue: tracking },
        { provide: AuthService, useValue: auth }
      ]
    });
    return TestBed.inject(DriverDeclarationsService);
  }

  beforeEach(() => {
    log = [];
    api = new FakeDriverApi();
    store = new MemoryKvStore();
    loc = new FakePhoneLocation();
    tracking = new FakeTracking();
    auth = new FakeAuth();
  });

  afterEach(() => TestBed.resetTestingModule());   // ngOnDestroy : plus de minuterie en suspens

  const queued = (userId = '42') => store.peek<QueuedDeclaration[]>(declarationsQueueKey(userId)) ?? [];
  const tick = (ms: number) => new Promise(r => setTimeout(r, ms));
  const httpError = (status: number, error: any = null) => () => throwError(() => new HttpErrorResponse({ status, error }));

  it('en ligne : envoie tout de suite, avec l\'heure du geste et la position du téléphone', async () => {
    loc.currentFix = fixAt(0);
    const service = create();

    const outcome = await service.declare(7, 11, 'arrive', { withPosition: true });

    expect(outcome.sent).toBeTrue();
    expect(api.calls.length).toBe(1);
    const call = api.calls[0];
    expect(call.kind).toBe('arrive');
    expect(call.tourId).toBe(7);
    expect(call.waypointId).toBe(11);
    expect(call.body.clientTime).toEqual(jasmine.any(String));
    expect(Math.abs(new Date(call.body.clientTime!).getTime() - Date.now())).toBeLessThan(5000);
    expect(call.body.latitude).toBe(36.8);
    expect(call.body.longitude).toBe(10.18);
    expect(call.body.accuracyM).toBe(8);
    expect(call.body.confirmSkipPending).toBeUndefined();
    expect(tracking.verdicts[0]).toEqual({ verdict: jasmine.objectContaining({ tracking: true, mode: 'full' }), tourId: 7 });
    expect(service.pendingCount).toBe(0);
  });

  it('sans position disponible, la déclaration part quand même (sans coordonnées)', async () => {
    loc.currentFix = null;
    const service = create();
    await service.declare(7, 11, 'arrive', { withPosition: true });
    expect(api.calls[0].body.latitude).toBeUndefined();
  });

  it('confirmSkipPending est transmis quand le chauffeur confirme (PENDING_STOPS)', async () => {
    const service = create();
    await service.declare(7, 99, 'arrive', { confirmSkipPending: true });
    expect(api.calls[0].body.confirmSkipPending).toBeTrue();
  });

  it('un 409 en ligne (PENDING_STOPS, TOO_FAR) remonte à l\'écran et n\'est PAS mis en file', async () => {
    api.script[0] = httpError(409, { code: 'PENDING_STOPS', pending: [{ id: 3, name: 'Client A' }], message: 'Des étapes…' });
    const service = create();

    await expectAsync(service.declare(7, 99, 'arrive')).toBeRejectedWith(jasmine.objectContaining({ status: 409 }));
    expect(service.pendingCount).toBe(0);
    expect(queued().length).toBe(0);
  });

  it('en ligne, un 503 (API en cours de déploiement) met la déclaration en file au lieu de la perdre', async () => {
    api.script[0] = httpError(503);
    const service = create();
    const outcome = await service.declare(7, 11, 'arrive');
    expect(outcome).toEqual({ sent: false, queued: true });
    expect(queued().map(q => q.waypointId)).toEqual([11]);
  });

  it('hors ligne : met la déclaration en file persistée du compte, avec son clientTime', async () => {
    api.online = false;
    const service = create();

    const outcome = await service.declare(7, 11, 'arrive');

    expect(outcome).toEqual({ sent: false, queued: true });
    expect(service.pendingCount).toBe(1);
    const q = queued();
    expect(q.length).toBe(1);
    expect(q[0]).toEqual(jasmine.objectContaining({ tourId: 7, waypointId: 11, kind: 'arrive', userId: '42' }));
    expect(q[0].body.clientTime).toBe(api.calls[0].body.clientTime);
    expect(tracking.verdicts.length).toBe(0);
  });

  it('« Je pars » : le suivi démarre AVANT l\'appel réseau (écran encore allumé), même hors ligne', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart', { startsTour: true, withPosition: true });
    expect(log.indexOf('start:7:full')).toBeGreaterThanOrEqual(0);
    expect(log.indexOf('start:7:full')).toBeLessThan(log.indexOf('api:depart:10'));
  });

  it('« Je pars » refusé par le serveur (400) : le suivi démarré pour lui est arrêté', async () => {
    api.script[0] = httpError(400, { message: 'Cette tournée n\'est plus en cours.' });
    const service = create();
    await expectAsync(service.declare(7, 10, 'depart', { startsTour: true })).toBeRejected();
    expect(log).toContain('stop');
    expect(tracking.activeTourId).toBeNull();
  });

  it('rejoue la file DANS L\'ORDRE, avec les heures d\'origine, au retour du réseau', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart', { startsTour: true });   // Je pars (origine)
    await tick(5);
    await service.declare(7, 11, 'arrive');                         // Je suis arrivé (étape 1)
    await tick(5);
    await service.declare(7, 11, 'depart');                         // Je repars
    expect(service.pendingCount).toBe(3);
    const before = queued();
    const attemptsOffline = api.calls.length;

    const replayed: QueuedDeclaration[] = [];
    service.replayed$.subscribe(i => replayed.push(i));
    api.online = true;
    await service.replay();

    const sent = api.calls.slice(attemptsOffline);
    expect(sent.map(c => `${c.kind}:${c.waypointId}`)).toEqual(['depart:10', 'arrive:11', 'depart:11']);
    expect(sent.map(c => c.body.clientTime)).toEqual(before.map(q => q.body.clientTime));
    // Les heures restent croissantes : l'ordre des gestes est celui du chauffeur.
    const times = sent.map(c => new Date(c.body.clientTime!).getTime());
    expect(times[0]).toBeLessThan(times[1]);
    expect(times[1]).toBeLessThan(times[2]);

    expect(service.pendingCount).toBe(0);
    expect(queued().length).toBe(0);
    expect(replayed.map(r => r.waypointId)).toEqual([10, 11, 11]);
    expect(tracking.verdicts.length).toBe(3);
    expect(log[log.length - 1]).toBe('flush');                      // barrière levée : les positions repartent
  });

  it('heure d\'ENVOI (sentAt) posée à chaque tentative, jamais stockée : le serveur reconnaît un rejeu', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 11, 'arrive');
    expect(api.calls[0].body.sentAt).toEqual(jasmine.any(String));
    expect(queued()[0].body.sentAt).toBeUndefined();

    await tick(30);
    api.online = true;
    const offlineCalls = api.calls.length;
    await service.replay();
    const replayed = api.calls[offlineCalls].body;
    expect(replayed.clientTime).toBe(api.calls[0].body.clientTime);   // l'heure du geste, intacte
    expect(new Date(replayed.sentAt!).getTime()).toBeGreaterThan(new Date(replayed.clientTime!).getTime());
  });

  it('une coupure au milieu du rejeu garde le reste de la file, dans l\'ordre', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart');
    await service.declare(7, 11, 'arrive');
    await service.declare(7, 12, 'arrive');
    const offlineCalls = api.calls.length;

    api.online = true;
    api.script[offlineCalls + 1] = httpError(0);                    // 2e rejeu : réseau perdu
    await service.replay();

    expect(service.pendingCount).toBe(2);
    expect(queued().map(q => q.waypointId)).toEqual([11, 12]);

    await service.replay();                                         // réseau revenu
    expect(service.pendingCount).toBe(0);
    const replayedIds = api.calls.slice(offlineCalls).map(c => c.waypointId);
    expect(replayedIds).toEqual([10, 11, 11, 12]);
  });

  for (const status of [401, 408, 429, 500, 502, 503, 504]) {
    it(`rejeu : un ${status} est PASSAGER — la déclaration reste en file, rien n'est abandonné`, async () => {
      api.online = false;
      const service = create();
      await service.declare(7, 10, 'depart', { startsTour: true });
      const offlineCalls = api.calls.length;
      const dropped: any[] = [];
      service.dropped$.subscribe(d => dropped.push(d));
      log = [];

      api.online = true;
      api.script[offlineCalls] = httpError(status);
      await service.replay();

      expect(dropped.length).toBe(0);
      expect(queued().map(q => q.waypointId)).toEqual([10]);
      expect(log).not.toContain('flush');                          // le « Je pars » doit passer avant les positions

      await service.replay();                                       // le serveur répond de nouveau
      expect(service.pendingCount).toBe(0);
    });
  }

  it('un geste fait pendant que la file attend passe DERRIÈRE elle, sans doubler', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart');
    const callsBefore = api.calls.length;

    await service.declare(7, 11, 'arrive');   // replay() échoue (1 appel), le nouveau geste n'est pas tenté

    expect(api.calls.length).toBe(callsBefore + 1);
    expect(api.calls[callsBefore].waypointId).toBe(10);
    expect(queued().map(q => q.waypointId)).toEqual([10, 11]);
  });

  it('un refus MÉTIER au rejeu (409) retire la déclaration, prévient durablement, et continue', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 99, 'arrive');   // arrivée destination sans confirmation
    await service.declare(8, 20, 'depart');
    const offlineCalls = api.calls.length;

    const dropped: any[] = [];
    service.dropped$.subscribe(d => dropped.push(d));
    api.online = true;
    api.script[offlineCalls] = httpError(409, { code: 'PENDING_STOPS', message: 'Des étapes n\'ont pas été signalées.' });
    await service.replay();

    expect(dropped.length).toBe(1);
    expect(dropped[0].item.waypointId).toBe(99);
    expect(dropped[0].message).toContain('étapes');
    expect(api.calls.slice(offlineCalls).map(c => c.waypointId)).toEqual([99, 20]);
    expect(service.pendingCount).toBe(0);
    // Le chauffeur n'avait peut-être pas la fiche ouverte : le refus reste affiché jusqu'à ce qu'il le voie.
    expect(service.refused$.value.map(r => r.waypointId)).toEqual([99]);
    expect(store.peek<RefusedDeclaration[]>(declarationsRefusedKey('42'))!.length).toBe(1);
    await service.acknowledgeRefusals(7);
    expect(service.refused$.value).toEqual([]);
  });

  it('rejeu : 403 NO_DRIVER_PROFILE est un refus métier (abandonné, averti)', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 11, 'arrive');
    const offlineCalls = api.calls.length;
    api.online = true;
    api.script[offlineCalls] = httpError(403, { code: 'NO_DRIVER_PROFILE', message: 'Aucune fiche' });
    await service.replay();
    expect(service.pendingCount).toBe(0);
    expect(service.refused$.value.length).toBe(1);
  });

  it('isBusinessRefusal : seuls 400/404/409/422 et un 403 avec code sont des refus', () => {
    for (const s of [400, 404, 409, 422]) expect(isBusinessRefusal({ status: s })).toBeTrue();
    expect(isBusinessRefusal({ status: 403, error: { code: 'NO_DRIVER_PROFILE' } })).toBeTrue();
    for (const s of [0, 401, 403, 408, 429, 500, 502, 503, 504]) expect(isBusinessRefusal({ status: s })).toBeFalse();
  });

  it('la file survit à un redémarrage de l\'application et se rejoue dans l\'ordre', async () => {
    api.online = false;
    const first = create();
    await first.declare(7, 10, 'depart');
    await first.declare(7, 11, 'arrive');

    api.online = true;
    const offlineCalls = api.calls.length;
    const second = create();                  // nouvelle instance, même stockage
    expect(await second.pendingFor(7)).toEqual([
      jasmine.objectContaining({ waypointId: 10 }),
      jasmine.objectContaining({ waypointId: 11 })
    ]);
    await second.replay();
    expect(api.calls.slice(offlineCalls).map(c => c.waypointId)).toEqual([10, 11]);
    expect(second.pendingCount).toBe(0);
  });

  // ─────────────── Comptes ───────────────

  it('la file est rattachée au compte : le compte suivant du téléphone ne la rejoue JAMAIS', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 11, 'arrive');   // chauffeur A (42), hors réseau
    const offlineCalls = api.calls.length;

    auth.userId = '99';                       // chauffeur B se connecte sur le même téléphone
    api.online = true;
    await service.replay();
    expect(api.calls.length).toBe(offlineCalls);
    expect(service.pendingCount).toBe(0);
    expect(await service.pendingFor(7)).toEqual([]);
    expect(queued('42').length).toBe(1);      // intacte pour A

    auth.userId = '42';                       // A revient
    await service.replay();
    expect(api.calls.slice(offlineCalls).map(c => c.waypointId)).toEqual([11]);
  });

  it('déconnexion volontaire : discardForCurrentUser vide la file de CE compte seulement', async () => {
    store.set(declarationsQueueKey('99'), [{ tourId: 1, waypointId: 2, kind: 'arrive', body: {}, seq: 1, userId: '99' }]);
    api.online = false;
    const service = create();
    await service.declare(7, 11, 'arrive');
    await service.discardForCurrentUser();
    expect(store.peek(declarationsQueueKey('42'))).toBeNull();
    expect(service.pendingCount).toBe(0);
    expect(queued('99').length).toBe(1);
  });

  it('la file sans propriétaire de la 1.2.0 de recette n\'est jamais rejouée', async () => {
    await store.set(LEGACY_DECLARATIONS_QUEUE_KEY, [{ tourId: 7, waypointId: 11, kind: 'arrive', body: {}, seq: 1 }]);
    const service = create();
    await service.replay();
    expect(api.calls.length).toBe(0);
    expect(store.peek(LEGACY_DECLARATIONS_QUEUE_KEY)).toBeNull();
  });

  it('sans session, une déclaration est refusée à l\'écran (jamais mise en file sans propriétaire)', async () => {
    auth.userId = null;
    const service = create();
    await expectAsync(service.declare(7, 11, 'arrive')).toBeRejectedWith(jasmine.objectContaining({ status: 401 }));
    expect(api.calls.length).toBe(0);
  });

  // ─────────────── Barrière des positions ───────────────

  it('barrière : seul un « Je pars » qui démarre la tournée retient les positions, pas une arrivée', async () => {
    api.online = false;
    const service = create();
    expect(tracking.gate).not.toBeNull();
    expect(tracking.gate!.blocked()).toBeFalse();
    await service.declare(7, 11, 'arrive');
    expect(tracking.gate!.blocked()).toBeFalse();           // arrivée en file : les lots sont acceptés
    await service.declare(8, 20, 'depart', { startsTour: true });
    expect(tracking.gate!.blocked()).toBeTrue();
    api.online = true;
    await service.replay();
    expect(tracking.gate!.blocked()).toBeFalse();
  });

  it('barrière fermée : chaque échéance de lot relance le rejeu (aucun événement « online » nécessaire)', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart', { startsTour: true });
    const offlineCalls = api.calls.length;

    api.online = true;                        // le réseau répond, sans passage visible hors ligne
    tracking.gate!.unblock();
    await tick(10);
    expect(api.calls.slice(offlineCalls).map(c => c.waypointId)).toEqual([10]);
    expect(service.pendingCount).toBe(0);
  });

  it('minuterie de nouvelle tentative : la file se rejoue seule, à délai croissant', fakeAsync(() => {
    api.online = false;
    const service = create();
    service.declare(7, 11, 'arrive');
    flushMicrotasks();
    const offlineCalls = api.calls.length;

    fakeTick(DriverDeclarationsService.RETRY_MIN_MS);          // 1re nouvelle tentative (toujours hors ligne)
    expect(api.calls.length).toBe(offlineCalls + 1);
    api.online = true;
    fakeTick(DriverDeclarationsService.RETRY_MIN_MS);          // le délai a doublé : rien encore
    expect(api.calls.length).toBe(offlineCalls + 1);
    fakeTick(DriverDeclarationsService.RETRY_MIN_MS);          // 60 s après : rejouée
    expect(api.calls.length).toBe(offlineCalls + 2);
    expect(service.pendingCount).toBe(0);
    service.disarmAutoReplay();
  }));

  // ─────────────── Arrivée à destination ───────────────

  it('arrivée à destination : TOUTES les positions partent AVANT la déclaration (sinon refusées une fois close)', async () => {
    const service = create();
    await service.declare(7, 99, 'arrive', { closesTour: true });
    expect(log.indexOf('drain')).toBeGreaterThanOrEqual(0);
    expect(log.indexOf('drain')).toBeLessThan(log.indexOf('api:arrive:99'));
  });

  it('arrivée à destination : si les positions ne peuvent pas partir, l\'arrivée attend en file avec elles', async () => {
    tracking.drainError = new HttpErrorResponse({ status: 0 });
    const service = create();
    const outcome = await service.declare(7, 99, 'arrive', { closesTour: true });
    expect(outcome).toEqual({ sent: false, queued: true });
    expect(api.calls.length).toBe(0);
    expect(queued()[0]).toEqual(jasmine.objectContaining({ waypointId: 99, closesTour: true }));

    tracking.drainError = null;               // réseau revenu : vidage PUIS arrivée
    log = [];
    await service.replay();
    expect(log.slice(0, 2)).toEqual(['drain', 'api:arrive:99']);
    expect(service.pendingCount).toBe(0);
  });

  it('arrivée à destination : le vidage nomme la tournée qu\'on clôt (son reliquat part, pas les points d\'une autre)', async () => {
    tracking.activeTourId = 8;                // le téléphone suit déjà Y
    tracking.drainError = new HttpErrorResponse({ status: 0 });
    const service = create();
    await service.declare(7, 99, 'arrive', { closesTour: true });   // X (7), hors ligne
    tracking.drainError = null;
    await service.replay();
    expect(tracking.drainedFor).toEqual([7, 7]);   // en ligne, puis au rejeu
  });

  it('arrivée à destination confirmée hors ligne : la déclaration en file porte le consentement, rejoué tel quel', async () => {
    api.online = false;
    const service = create();
    const outcome = await service.declare(7, 99, 'arrive', { closesTour: true, confirmSkipPending: true });
    expect(outcome).toEqual({ sent: false, queued: true });
    expect(queued()[0].body.confirmSkipPending).toBeTrue();

    api.online = true;
    const offlineCalls = api.calls.length;
    await service.replay();
    expect(api.calls[offlineCalls].body.confirmSkipPending).toBeTrue();   // pas de PENDING_STOPS au rejeu
    expect(service.pendingCount).toBe(0);
  });

  it('une arrivée intermédiaire ne vide pas la file de positions', async () => {
    const service = create();
    await service.declare(7, 11, 'arrive');
    expect(log).not.toContain('drain');
  });

  it('un seul rejeu à la fois (deux appels simultanés n\'envoient pas deux fois)', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart');
    const offlineCalls = api.calls.length;
    api.online = true;
    await Promise.all([service.replay(), service.replay()]);
    expect(api.calls.length - offlineCalls).toBe(1);
  });
});
