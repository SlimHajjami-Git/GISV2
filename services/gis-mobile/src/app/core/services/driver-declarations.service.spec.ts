import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { DriverDeclarationsService, DECLARATIONS_QUEUE_KEY, QueuedDeclaration } from './driver-declarations.service';
import { ApiService } from './api.service';
import { KvStore } from './kv-store.service';
import { PhoneLocationService } from './phone-location.service';
import { TourTrackingService } from './tour-tracking.service';
import { DriverEventRequest, DriverEventResponse } from '../models/driver-app.types';
import { FakePhoneLocation, MemoryKvStore, fixAt } from '../testing/driver-test-doubles';

interface ApiCall { kind: 'depart' | 'arrive'; tourId: number; waypointId: number; body: DriverEventRequest; }

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
    if (this.script[index]) return this.script[index]();
    if (!this.online) return throwError(() => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' }));
    return of({
      tourStatus: 'in_progress', tracking: true, mode: 'full',
      waypoint: { id: waypointId } as any
    } as DriverEventResponse);
  }
}

describe('DriverDeclarationsService (file hors ligne des déclarations)', () => {
  let api: FakeDriverApi;
  let store: MemoryKvStore;
  let loc: FakePhoneLocation;
  let tracking: jasmine.SpyObj<TourTrackingService>;
  let gate: (() => boolean) | null;

  function create(): DriverDeclarationsService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        DriverDeclarationsService,
        { provide: ApiService, useValue: api },
        { provide: KvStore, useValue: store },
        { provide: PhoneLocationService, useValue: loc },
        { provide: TourTrackingService, useValue: tracking }
      ]
    });
    return TestBed.inject(DriverDeclarationsService);
  }

  beforeEach(() => {
    api = new FakeDriverApi();
    store = new MemoryKvStore();
    loc = new FakePhoneLocation();
    gate = null;
    tracking = jasmine.createSpyObj<TourTrackingService>('TourTrackingService',
      ['applyVerdict', 'start', 'flush', 'setFlushGate']);
    tracking.applyVerdict.and.resolveTo();
    tracking.start.and.resolveTo();
    tracking.flush.and.resolveTo();
    tracking.setFlushGate.and.callFake((g: (() => boolean) | null) => { gate = g; });
  });

  const queued = () => store.peek<QueuedDeclaration[]>(DECLARATIONS_QUEUE_KEY) ?? [];
  const tick = (ms: number) => new Promise(r => setTimeout(r, ms));

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
    expect(tracking.applyVerdict).toHaveBeenCalledWith(jasmine.objectContaining({ tracking: true, mode: 'full' }), 7);
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
    api.script[0] = () => throwError(() => new HttpErrorResponse({
      status: 409, error: { code: 'PENDING_STOPS', pending: [{ id: 3, name: 'Client A' }], message: 'Des étapes…' }
    }));
    const service = create();

    await expectAsync(service.declare(7, 99, 'arrive')).toBeRejectedWith(jasmine.objectContaining({ status: 409 }));
    expect(service.pendingCount).toBe(0);
    expect(queued().length).toBe(0);
  });

  it('hors ligne : met la déclaration en file persistée, avec son clientTime', async () => {
    api.online = false;
    const service = create();

    const outcome = await service.declare(7, 11, 'arrive');

    expect(outcome).toEqual({ sent: false, queued: true });
    expect(service.pendingCount).toBe(1);
    const q = queued();
    expect(q.length).toBe(1);
    expect(q[0]).toEqual(jasmine.objectContaining({ tourId: 7, waypointId: 11, kind: 'arrive' }));
    expect(q[0].body.clientTime).toBe(api.calls[0].body.clientTime);
    expect(tracking.applyVerdict).not.toHaveBeenCalled();
  });

  it('« Je pars » hors ligne : le suivi démarre quand même (cadence complète)', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart', { startsTour: true });
    expect(tracking.start).toHaveBeenCalledWith(7, 'full');
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
    expect(tracking.applyVerdict).toHaveBeenCalledTimes(3);
    expect(tracking.flush).toHaveBeenCalled();                    // barrière levée : les positions repartent
  });

  it('une coupure au milieu du rejeu garde le reste de la file, dans l\'ordre', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 10, 'depart');
    await service.declare(7, 11, 'arrive');
    await service.declare(7, 12, 'arrive');
    const offlineCalls = api.calls.length;

    api.online = true;
    api.script[offlineCalls + 1] = () => throwError(() => new HttpErrorResponse({ status: 0 }));  // 2e rejeu : réseau perdu
    await service.replay();

    expect(service.pendingCount).toBe(2);
    expect(queued().map(q => q.waypointId)).toEqual([11, 12]);

    await service.replay();                                         // réseau revenu
    expect(service.pendingCount).toBe(0);
    const replayedIds = api.calls.slice(offlineCalls).map(c => c.waypointId);
    expect(replayedIds).toEqual([10, 11, 11, 12]);
  });

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

  it('un refus définitif au rejeu (409) retire la déclaration, prévient, et continue', async () => {
    api.online = false;
    const service = create();
    await service.declare(7, 99, 'arrive');   // arrivée destination sans confirmation
    await service.declare(8, 20, 'depart');
    const offlineCalls = api.calls.length;

    const dropped: any[] = [];
    service.dropped$.subscribe(d => dropped.push(d));
    api.online = true;
    api.script[offlineCalls] = () => throwError(() => new HttpErrorResponse({
      status: 409, error: { code: 'PENDING_STOPS', message: 'Des étapes n\'ont pas été signalées.' }
    }));
    await service.replay();

    expect(dropped.length).toBe(1);
    expect(dropped[0].item.waypointId).toBe(99);
    expect(dropped[0].message).toContain('étapes');
    expect(api.calls.slice(offlineCalls).map(c => c.waypointId)).toEqual([99, 20]);
    expect(service.pendingCount).toBe(0);
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

  it('pose une barrière sur les lots de positions tant que la file n\'est pas vide', async () => {
    api.online = false;
    const service = create();
    expect(gate).not.toBeNull();
    expect(gate!()).toBeFalse();
    await service.declare(7, 10, 'depart', { startsTour: true });
    expect(gate!()).toBeTrue();
    api.online = true;
    await service.replay();
    expect(gate!()).toBeFalse();
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
