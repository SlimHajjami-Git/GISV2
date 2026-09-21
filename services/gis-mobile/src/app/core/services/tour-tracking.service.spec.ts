import { TestBed, fakeAsync, tick, flushMicrotasks, discardPeriodicTasks } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { TourTrackingService, TRACKING_QUEUE_KEY, TRACKING_STATE_KEY, SENSOR_NOT_AUTHORIZED } from './tour-tracking.service';
import { ApiService } from './api.service';
import { KvStore } from './kv-store.service';
import { PhoneLocationService } from './phone-location.service';
import { PhonePoint, PhonePositionsRequest, PhonePositionsResponse } from '../models/driver-app.types';
import { FakePhoneLocation, MemoryKvStore, fixAt } from '../testing/driver-test-doubles';

describe('TourTrackingService', () => {
  const S = 1000;
  const MIN = 60 * S;
  const H = 60 * MIN;

  let service: TourTrackingService;
  let store: MemoryKvStore;
  let loc: FakePhoneLocation;
  let posts: PhonePositionsRequest[];
  let respond: () => Observable<PhonePositionsResponse>;

  const ok = (overrides: Partial<PhonePositionsResponse> = {}) =>
    () => of({ tracking: true, mode: 'full', activeTourId: 7, accepted: 1, ...overrides } as PhonePositionsResponse);
  const offline = () => throwError(() => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' }));

  beforeEach(() => {
    store = new MemoryKvStore();
    loc = new FakePhoneLocation();
    posts = [];
    respond = ok();
    const api = {
      postDriverPositions: (body: PhonePositionsRequest) => {
        posts.push(JSON.parse(JSON.stringify(body)));
        return respond();
      }
    };
    TestBed.configureTestingModule({
      providers: [
        TourTrackingService,
        { provide: ApiService, useValue: api },
        { provide: KvStore, useValue: store },
        { provide: PhoneLocationService, useValue: loc }
      ]
    });
    service = TestBed.inject(TourTrackingService);
  });

  /** Arrête le service et purge ce qui reste dans la zone fakeAsync. */
  function teardown() {
    service.stop();
    flushMicrotasks();
    discardPeriodicTasks();
  }

  const queued = () => store.peek<PhonePoint[]>(TRACKING_QUEUE_KEY) ?? [];

  // ─────────────── Cycle de vie ───────────────

  it('démarre le capteur en service au premier plan « Tournée en cours » et persiste l\'état', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();

    expect(service.isActive).toBeTrue();
    expect(service.activeTourId).toBe(7);
    expect(loc.startWatchCalls).toBe(1);
    expect(loc.watchOptions?.title).toBe('Tournée en cours');
    expect(loc.watchOptions?.message).toContain('position');
    expect(store.peek<any>(TRACKING_STATE_KEY)).toEqual(jasmine.objectContaining({ tourId: 7, mode: 'full' }));
    teardown();
  }));

  it('ne démarre pas le capteur sans permission (pas d\'invite système) et le signale', fakeAsync(() => {
    loc.permissionGranted = false;
    service.start(7, 'full');
    flushMicrotasks();

    expect(service.isActive).toBeTrue();
    expect(loc.startWatchCalls).toBe(0);
    expect(service.sensorError$.value).toBe(SENSOR_NOT_AUTHORIZED);
    teardown();
  }));

  // ─────────────── Cadence ───────────────

  it('mode full : un point toutes les 15 s OU tous les 50 m, puis envoi du lot à 30 s', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();

    service.ingest(fixAt(0));                 // t=0 : premier point, toujours pris
    expect(service.pendingCount).toBe(1);

    tick(5 * S);
    service.ingest(fixAt(0.0001));            // t=5 s, ~11 m : ni 15 s ni 50 m → écarté
    expect(service.pendingCount).toBe(1);

    service.ingest(fixAt(0.001));             // t=5 s, ~111 m : distance → pris
    expect(service.pendingCount).toBe(2);

    tick(14 * S);
    service.ingest(fixAt(0.001));             // t=19 s, immobile, 14 s après → écarté
    expect(service.pendingCount).toBe(2);

    tick(1 * S);
    service.ingest(fixAt(0.001));             // t=20 s, 15 s après → pris
    expect(service.pendingCount).toBe(3);
    expect(posts.length).toBe(0);             // pas encore d'envoi

    tick(10 * S);                             // t=30 s : minuterie des lots
    expect(posts.length).toBe(1);
    expect(posts[0].points.length).toBe(3);
    expect(posts[0].batteryLevel).toBe(80);
    expect(new Date(posts[0].sentAt).getTime()).toBe(Date.now());
    expect(service.pendingCount).toBe(0);
    expect(queued().length).toBe(0);
    teardown();
  }));

  it('mode eco : un point toutes les 2 min, et le battement de 60 s entre les deux', fakeAsync(() => {
    service.setFlushGate(() => true);         // isole la cadence de l'envoi
    service.start(7, 'eco');
    flushMicrotasks();
    expect(loc.watchOptions?.distanceFilterM).toBe(100);

    service.ingest(fixAt(0));                 // t=0 : pris
    for (let t = 10; t <= 50; t += 10) {      // t=10..50 s, en mouvement → écartés
      tick(10 * S);
      service.ingest(fixAt(t / 1000));
    }
    expect(service.pendingCount).toBe(1);

    tick(10 * S);                             // t=60 s : battement (dernière position connue)
    expect(service.pendingCount).toBe(2);
    const beat = queued()[1];
    expect(beat.latitude).toBeCloseTo(36.8 + 0.05, 6);
    expect(new Date(beat.recordedAt).getTime()).toBe(Date.now());

    for (let t = 70; t <= 110; t += 10) {     // t=70..110 s → écartés (eco)
      tick(10 * S);
      service.ingest(fixAt(t / 1000));
    }
    expect(service.pendingCount).toBe(2);

    tick(9 * S);                              // t=119 s
    service.ingest(fixAt(0.119));             // 119 s après le dernier point de cadence → écarté
    expect(service.pendingCount).toBe(2);
    tick(1 * S);                              // t=120 s : battement (2e) puis point de cadence dû
    expect(service.pendingCount).toBe(3);
    service.ingest(fixAt(0.12));
    expect(service.pendingCount).toBe(4);
    teardown();
  }));

  it('battement : au moins un point toutes les 60 s même immobile, daté de maintenant', fakeAsync(() => {
    service.setFlushGate(() => true);
    service.start(7, 'full');
    flushMicrotasks();
    const t0 = Date.now();

    service.ingest(fixAt(0, t0));
    tick(59 * S);
    expect(service.pendingCount).toBe(1);
    tick(1 * S);
    expect(service.pendingCount).toBe(2);
    tick(60 * S);
    expect(service.pendingCount).toBe(3);

    const pts = queued();
    expect(new Date(pts[1].recordedAt).getTime()).toBe(t0 + 60 * S);
    expect(new Date(pts[2].recordedAt).getTime()).toBe(t0 + 120 * S);
    expect(pts[2].latitude).toBe(pts[0].latitude);
    teardown();
  }));

  it('battement : rien n\'est inventé tant que le capteur n\'a donné aucune position', fakeAsync(() => {
    service.setFlushGate(() => true);
    service.start(7, 'full');
    flushMicrotasks();
    tick(5 * MIN);
    expect(service.pendingCount).toBe(0);
    teardown();
  }));

  // ─────────────── Envoi et file hors ligne ───────────────

  it('hors ligne : les points restent en file (persistée) puis partent au retour du réseau', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    tick(1 * S);                              // décale le battement des échéances de lot
    service.ingest(fixAt(0));
    service.ingest(fixAt(0.001));
    service.ingest(fixAt(0.002));
    respond = offline;

    tick(29 * S);                             // t=30 s : tentative, échec réseau
    expect(posts.length).toBe(1);
    expect(service.pendingCount).toBe(3);
    expect(queued().length).toBe(3);
    expect(service.isActive).toBeTrue();

    respond = ok();
    tick(30 * S);                             // t=60 s : réseau revenu
    expect(posts.length).toBe(2);
    expect(posts[1].points.length).toBe(3);
    expect(posts[1].points.map(p => p.latitude)).toEqual(posts[0].points.map(p => p.latitude));
    expect(service.pendingCount).toBe(0);
    teardown();
  }));

  it('file hors ligne bornée à 2 000 points (les plus anciens tombent) et lots de 200 au plus', fakeAsync(() => {
    let gate = true;
    service.setFlushGate(() => gate);
    service.start(7, 'full');
    flushMicrotasks();
    tick(1 * S);

    for (let i = 0; i < 2100; i++) service.ingest(fixAt(i * 0.001));
    flushMicrotasks();
    expect(service.pendingCount).toBe(2000);
    expect(queued().length).toBe(2000);
    expect(queued()[0].latitude).toBeCloseTo(36.8 + 0.1, 6);   // le 101e point est désormais le plus ancien

    gate = false;
    tick(29 * S);                             // t=30 s
    expect(posts.length).toBe(1);
    expect(posts[0].points.length).toBe(200);
    expect(posts[0].points[0].latitude).toBeCloseTo(36.8 + 0.1, 6);
    expect(service.pendingCount).toBe(1800);
    teardown();
  }));

  it('la barrière des déclarations en attente retient les lots', fakeAsync(() => {
    let gate = true;
    service.setFlushGate(() => gate);
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));

    tick(30 * S);
    expect(posts.length).toBe(0);
    gate = false;
    tick(30 * S);
    expect(posts.length).toBe(1);
    teardown();
  }));

  it('file vide : un lot vide part toutes les 5 min pour apprendre la fin de tournée', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    tick(4 * MIN + 30 * S);
    expect(posts.length).toBe(0);
    tick(30 * S);                             // t=5 min
    expect(posts.length).toBe(1);
    expect(posts[0].points).toEqual([]);
    teardown();
  }));

  // ─────────────── Arrêts ───────────────

  it('s\'arrête quand le serveur répond tracking:false (capteur coupé, état effacé)', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    respond = ok({ tracking: false, activeTourId: null, accepted: 1 });

    tick(30 * S);
    expect(posts.length).toBe(1);
    expect(service.isActive).toBeFalse();
    expect(service.state$.value).toBeNull();
    expect(loc.stopWatchCalls).toBeGreaterThan(0);
    expect(store.peek(TRACKING_STATE_KEY)).toBeNull();
    expect(store.peek(TRACKING_QUEUE_KEY)).toBeNull();

    tick(5 * MIN);                            // plus aucune minuterie
    expect(posts.length).toBe(1);
    teardown();
  }));

  it('s\'arrête sur un 403 (plus de fiche chauffeur)', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    respond = () => throwError(() => new HttpErrorResponse({ status: 403, error: { code: 'NO_DRIVER_PROFILE' } }));
    tick(30 * S);
    expect(service.isActive).toBeFalse();
    teardown();
  }));

  it('s\'arrête de lui-même 12 h après le départ', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    tick(12 * H - S);
    expect(service.isActive).toBeTrue();
    tick(S);
    flushMicrotasks();
    expect(service.isActive).toBeFalse();
    teardown();
  }));

  it('stop({ flush: true }) tente un dernier envoi puis vide tout (déconnexion)', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    service.ingest(fixAt(0.001));
    service.stop({ flush: true });
    flushMicrotasks();
    expect(posts.length).toBe(1);
    expect(posts[0].points.length).toBe(2);
    expect(service.isActive).toBeFalse();
    expect(service.pendingCount).toBe(0);
    teardown();
  }));

  // ─────────────── Verdicts du serveur ───────────────

  it('applyVerdict : démarre, change de cadence (capteur relancé), puis arrête', fakeAsync(() => {
    service.applyVerdict({ tracking: true, mode: 'eco' }, 7);
    flushMicrotasks();
    expect(service.activeTourId).toBe(7);
    expect(service.mode).toBe('eco');
    expect(loc.watchOptions?.distanceFilterM).toBe(100);

    service.applyVerdict({ tracking: true, mode: 'full', activeTourId: 7 });
    flushMicrotasks();
    expect(service.mode).toBe('full');
    expect(loc.startWatchCalls).toBe(2);
    expect(loc.watchOptions?.distanceFilterM).toBe(10);

    service.applyVerdict({ tracking: false, mode: 'full' });
    flushMicrotasks();
    expect(service.isActive).toBeFalse();
    teardown();
  }));

  it('un lot qui annonce mode eco fait passer la cadence en eco', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    respond = ok({ mode: 'eco' });
    tick(30 * S);
    expect(service.mode).toBe('eco');
    teardown();
  }));

  // ─────────────── Reprise après redémarrage ───────────────

  it('resume() reprend un suivi de moins de 12 h avec sa file', fakeAsync(() => {
    const pending: PhonePoint[] = [{ recordedAt: new Date().toISOString(), latitude: 36.8, longitude: 10.1, isMocked: false }];
    store.set(TRACKING_STATE_KEY, { tourId: 9, startedAt: Date.now() - 2 * H, mode: 'eco' });
    store.set(TRACKING_QUEUE_KEY, pending);
    flushMicrotasks();
    respond = ok({ activeTourId: 9, mode: 'eco' });

    service.resume();
    flushMicrotasks();
    expect(service.activeTourId).toBe(9);
    expect(service.mode).toBe('eco');
    expect(service.pendingCount).toBe(1);

    tick(10 * H - S);
    expect(service.isActive).toBeTrue();
    tick(S);
    flushMicrotasks();
    expect(service.isActive).toBeFalse();     // 12 h comptées depuis le départ d'origine
    teardown();
  }));

  it('resume() abandonne un suivi de plus de 12 h', fakeAsync(() => {
    store.set(TRACKING_STATE_KEY, { tourId: 9, startedAt: Date.now() - 13 * H, mode: 'full' });
    store.set(TRACKING_QUEUE_KEY, []);
    flushMicrotasks();

    service.resume();
    flushMicrotasks();
    expect(service.isActive).toBeFalse();
    expect(store.peek(TRACKING_STATE_KEY)).toBeNull();
    expect(loc.startWatchCalls).toBe(0);
    teardown();
  }));
});
