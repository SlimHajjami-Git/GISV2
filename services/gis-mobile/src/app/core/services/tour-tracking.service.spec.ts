import { TestBed, fakeAsync, tick, flushMicrotasks, discardPeriodicTasks } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, Subject, of, throwError } from 'rxjs';
import {
  TourTrackingService, trackingQueueKey, trackingStateKey, SENSOR_NOT_AUTHORIZED, SENSOR_COARSE_ONLY,
  SENSOR_LOCATION_DISABLED, LEGACY_TRACKING_KEYS
} from './tour-tracking.service';
import { ApiService } from './api.service';
import { AuthService } from './auth.service';
import { KvStore } from './kv-store.service';
import { PhoneLocationService } from './phone-location.service';
import { PhonePoint, PhonePositionsRequest, PhonePositionsResponse } from '../models/driver-app.types';
import { FakeAuth, FakePhoneLocation, MemoryKvStore, fixAt } from '../testing/driver-test-doubles';

describe('TourTrackingService', () => {
  const S = 1000;
  const MIN = 60 * S;
  const H = 60 * MIN;

  let service: TourTrackingService;
  let store: MemoryKvStore;
  let loc: FakePhoneLocation;
  let auth: FakeAuth;
  let posts: PhonePositionsRequest[];
  let respond: () => Observable<PhonePositionsResponse>;

  const ok = (overrides: Partial<PhonePositionsResponse> = {}) =>
    () => of({ tracking: true, mode: 'full', activeTourId: 7, accepted: 1, ...overrides } as PhonePositionsResponse);
  const offline = () => throwError(() => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' }));

  function create(): TourTrackingService {
    TestBed.resetTestingModule();
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
        { provide: PhoneLocationService, useValue: loc },
        { provide: AuthService, useValue: auth }
      ]
    });
    return TestBed.inject(TourTrackingService);
  }

  beforeEach(() => {
    store = new MemoryKvStore();
    loc = new FakePhoneLocation();
    auth = new FakeAuth();
    posts = [];
    respond = ok();
    service = create();
  });

  /** Arrête le service et purge ce qui reste dans la zone fakeAsync. */
  function teardown() {
    service.stop();
    flushMicrotasks();
    discardPeriodicTasks();
  }

  /** File PERSISTÉE du compte 42 (écriture différée : voir PERSIST_DEBOUNCE_MS). */
  const queued = () => store.peek<PhonePoint[]>(trackingQueueKey('42')) ?? [];

  // ─────────────── Cycle de vie ───────────────

  it('démarre le capteur en service au premier plan « Tournée en cours » et persiste l\'état du compte', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();

    expect(service.isActive).toBeTrue();
    expect(service.activeTourId).toBe(7);
    expect(loc.startWatchCalls).toBe(1);
    expect(loc.watchOptions?.title).toBe('Tournée en cours');
    expect(loc.watchOptions?.message).toContain('position');
    expect(loc.watchOptions?.distanceFilterM).toBe(10);
    expect(store.peek<any>(trackingStateKey('42'))).toEqual(jasmine.objectContaining({ tourId: 7, mode: 'full', userId: '42' }));
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

  it('position « approximative » seulement : capteur NON démarré, souci COARSE_ONLY (jamais « Suivi actif »)', fakeAsync(() => {
    loc.status = 'coarse';
    service.start(7, 'full');
    flushMicrotasks();

    expect(loc.startWatchCalls).toBe(0);
    expect(service.sensorError$.value).toBe(SENSOR_COARSE_ONLY);
    teardown();
  }));

  it('localisation du téléphone coupée : souci LOCATION_DISABLED, capteur démarré au retour au premier plan', fakeAsync(() => {
    loc.status = 'disabled';
    service.start(7, 'full');
    flushMicrotasks();
    expect(loc.startWatchCalls).toBe(0);
    expect(service.sensorError$.value).toBe(SENSOR_LOCATION_DISABLED);

    loc.status = 'granted';                   // le chauffeur a rallumé la localisation
    service.onForeground();
    flushMicrotasks();
    expect(loc.startWatchCalls).toBe(1);
    expect(service.sensorError$.value).toBeNull();
    teardown();
  }));

  it('erreur du plugin « Location services disabled » : c\'est la localisation coupée, pas la permission', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    loc.watchCallback!(null, { code: 'NOT_AUTHORIZED', message: 'Location services disabled.' });
    expect(service.sensorError$.value).toBe(SENSOR_LOCATION_DISABLED);
    teardown();
  }));

  it('localisation rallumée : au retour au premier plan (écran visible), l\'observateur est recréé', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    loc.watchCallback!(null, { code: 'NOT_AUTHORIZED', message: 'Location services disabled.' });
    service.onForeground();                   // le chauffeur a rallumé la localisation et revient
    flushMicrotasks();
    expect(loc.stopWatchCalls).toBe(1);
    expect(loc.startWatchCalls).toBe(2);
    expect(service.sensorError$.value).toBeNull();
    teardown();
  }));

  it('un point flou (> 100 m, écarté par le serveur) n\'efface pas le souci ; un point précis si', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    loc.watchCallback!(null, { code: 'LOCATION_ERROR', message: 'pas de signal' });
    expect(service.sensorError$.value).toBe('LOCATION_ERROR');

    loc.watchCallback!({ ...fixAt(0), accuracyM: 2000 }, undefined);
    expect(service.sensorError$.value).toBe('LOCATION_ERROR');

    loc.watchCallback!(fixAt(0.001), undefined);
    expect(service.sensorError$.value).toBeNull();
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

  it('mode eco : un point toutes les 2 min seulement', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });   // isole la cadence de l'envoi
    loc.currentFix = null;                    // battement muet : seule la cadence parle
    service.start(7, 'eco');
    flushMicrotasks();

    service.ingest(fixAt(0));                 // t=0 : pris
    for (let t = 10; t <= 110; t += 10) {     // t=10..110 s, en mouvement → écartés
      tick(10 * S);
      service.ingest(fixAt(t / 1000));
    }
    expect(service.pendingCount).toBe(1);

    tick(10 * S);                             // t=120 s : point de cadence dû
    service.ingest(fixAt(0.12));
    expect(service.pendingCount).toBe(2);
    teardown();
  }));

  it('changer de cadence ne retire JAMAIS l\'observateur (service au premier plan intact, écran verrouillé)', fakeAsync(() => {
    service.start(7, 'eco');
    flushMicrotasks();
    for (const mode of ['full', 'eco', 'full'] as const) {
      service.applyVerdict({ tracking: true, mode, activeTourId: 7 });
      flushMicrotasks();
      expect(service.mode).toBe(mode);
    }
    expect(loc.startWatchCalls).toBe(1);
    expect(loc.stopWatchCalls).toBe(0);
    expect(loc.watchOptions?.distanceFilterM).toBe(10);
    expect(store.peek<any>(trackingStateKey('42')).mode).toBe('full');
    teardown();
  }));

  it('passer à une autre tournée garde le capteur (pas de nouveau service au premier plan)', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    service.start(8, 'full');
    flushMicrotasks();
    expect(service.activeTourId).toBe(8);
    expect(loc.startWatchCalls).toBe(1);
    expect(loc.stopWatchCalls).toBe(0);
    teardown();
  }));

  // ─────────────── Battement ───────────────

  it('battement : sans point depuis 60 s, une mesure FRAÎCHE part avec l\'heure de SA mesure', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    const t0 = Date.now();
    service.ingest(fixAt(0, t0));
    loc.currentFix = fixAt(0, t0 + 45 * S);   // mesure faite 45 s après le dernier point

    tick(60 * S);
    expect(service.pendingCount).toBe(2);
    expect(loc.fixRequests[0]).toEqual({ timeoutMs: 15000, maximumAgeMs: 10000 });
    expect(service['queue'][1].recordedAt).toBe(new Date(t0 + 45 * S).toISOString());

    tick(60 * S);                              // même mesure rendue : pas de doublon
    expect(service.pendingCount).toBe(2);
    teardown();
  }));

  it('battement : une position figée n\'est JAMAIS redatée (tunnel, parking, capteur muet)', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    loc.currentFix = null;                    // plus aucune mesure possible

    tick(5 * MIN);
    expect(service.pendingCount).toBe(1);     // rien d'inventé : le serveur voit le téléphone se taire
    teardown();
  }));

  it('battement : localisation coupée en route → souci LOCATION_DISABLED, aucun point', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    loc.currentFix = fixAt(0.001, Date.now() + 30 * S);
    loc.status = 'disabled';

    tick(60 * S);
    expect(service.sensorError$.value).toBe(SENSOR_LOCATION_DISABLED);
    expect(service.pendingCount).toBe(1);
    teardown();
  }));

  it('battement : rien n\'est inventé tant que le capteur n\'a donné aucune position', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
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

  it('chaque lot nomme la tournée de ses points (activeTourId), y compris au dernier envoi en passant à une autre', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    tick(30 * S);
    expect(posts[0].activeTourId).toBe(7);

    service.ingest(fixAt(0.001));
    service.start(8, 'full');                 // les points de la 7 partent avec la 7
    flushMicrotasks();
    expect(posts[1].activeTourId).toBe(7);
    service.ingest(fixAt(0.002));
    tick(30 * S);
    expect(posts[2].activeTourId).toBe(8);
    teardown();
  }));

  it('un 503 (déploiement de l\'API) ne jette rien : les points repartent au lot suivant', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    respond = () => throwError(() => new HttpErrorResponse({ status: 503 }));
    tick(30 * S);
    expect(service.isActive).toBeTrue();
    expect(service.pendingCount).toBe(1);
    respond = ok();
    tick(30 * S);
    expect(posts.length).toBe(2);
    expect(service.pendingCount).toBe(0);
    teardown();
  }));

  it('file hors ligne bornée à 2 000 points (les plus anciens tombent) et lots de 200 au plus', fakeAsync(() => {
    let gate = true;
    service.setFlushGate({ blocked: () => gate, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    tick(1 * S);

    for (let i = 0; i < 2100; i++) service.ingest(fixAt(i * 0.001));
    flushMicrotasks();
    expect(service.pendingCount).toBe(2000);
    tick(TourTrackingService.PERSIST_DEBOUNCE_MS);
    expect(queued().length).toBe(2000);
    expect(queued()[0].latitude).toBeCloseTo(36.8 + 0.1, 6);   // le 101e point est désormais le plus ancien

    gate = false;
    tick(14 * S);                             // t=30 s
    expect(posts.length).toBe(1);
    expect(posts[0].points.length).toBe(200);
    expect(posts[0].points[0].latitude).toBeCloseTo(36.8 + 0.1, 6);
    expect(service.pendingCount).toBe(1800);
    teardown();
  }));

  it('la file n\'est pas réécrite à chaque point : une écriture par 15 s au plus, et tout de suite en arrière-plan', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    for (let i = 0; i < 20; i++) {            // 20 points acceptés en 20 s (111 m chacun)
      service.ingest(fixAt(i * 0.001));
      tick(1 * S);
    }
    expect(service.pendingCount).toBe(20);
    expect(store.writes.get(trackingQueueKey('42')) ?? 0).toBeLessThanOrEqual(2);

    service.ingest(fixAt(0.05));
    service.onBackground();
    flushMicrotasks();
    expect(queued().length).toBe(21);
    teardown();
  }));

  it('la barrière retient les lots, et chaque échéance bloquée relance ce qui la lèvera', fakeAsync(() => {
    let gate = true;
    let unblocks = 0;
    service.setFlushGate({ blocked: () => gate, unblock: () => { unblocks++; } });
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));

    tick(30 * S);
    expect(posts.length).toBe(0);
    expect(unblocks).toBe(1);
    tick(30 * S);
    expect(unblocks).toBe(2);
    gate = false;
    tick(30 * S);
    expect(posts.length).toBe(1);
    teardown();
  }));

  it('file vide : un lot vide part toutes les 5 min pour apprendre la fin de tournée', fakeAsync(() => {
    loc.currentFix = null;
    service.start(7, 'full');
    flushMicrotasks();
    tick(4 * MIN + 30 * S);
    expect(posts.length).toBe(0);
    tick(30 * S);                             // t=5 min
    expect(posts.length).toBe(1);
    expect(posts[0].points).toEqual([]);
    teardown();
  }));

  // ─────────────── Vidage avant la destination ───────────────

  it('drain() envoie TOUTE la file, lot après lot, même barrière fermée', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    for (let i = 0; i < 450; i++) service.ingest(fixAt(i * 0.001));

    let done = false;
    service.drain().then(() => { done = true; });
    flushMicrotasks();
    expect(done).toBeTrue();
    expect(posts.map(p => p.points.length)).toEqual([200, 200, 50]);
    expect(service.pendingCount).toBe(0);
    teardown();
  }));

  it('drain() rend l\'échec passager et garde les points', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    respond = offline;

    let failed: any = null;
    service.drain().catch(e => { failed = e; });
    flushMicrotasks();
    expect(failed?.status).toBe(0);
    expect(service.pendingCount).toBe(1);
    teardown();
  }));

  // ─────────────── Arrêts ───────────────

  it('tracking:false : dernier envoi de TOUT ce qui reste avant de vider', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    for (let i = 0; i < 450; i++) service.ingest(fixAt(i * 0.001));
    // Fin des 12 h côté serveur : la tournée existe encore, ses points restent acceptés.
    respond = ok({ tracking: false, activeTourId: 7, accepted: 200 });

    tick(30 * S);
    expect(posts.map(p => p.points.length)).toEqual([200, 200, 50]);
    expect(service.isActive).toBeFalse();
    teardown();
  }));

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
    expect(store.peek(trackingStateKey('42'))).toBeNull();
    expect(store.peek(trackingQueueKey('42'))).toBeNull();

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
    loc.currentFix = null;
    service.start(7, 'full');
    flushMicrotasks();
    tick(12 * H - S);
    expect(service.isActive).toBeTrue();
    tick(S);
    flushMicrotasks();
    expect(service.isActive).toBeFalse();
    teardown();
  }));

  it('stop({ flush: true }) envoie TOUTE la file puis vide tout (déconnexion volontaire)', fakeAsync(() => {
    service.start(7, 'full');
    flushMicrotasks();
    for (let i = 0; i < 250; i++) service.ingest(fixAt(i * 0.001));
    service.stop({ flush: true });
    flushMicrotasks();
    expect(posts.map(p => p.points.length)).toEqual([200, 50]);
    expect(service.isActive).toBeFalse();
    expect(service.pendingCount).toBe(0);
    expect(store.peek(trackingQueueKey('42'))).toBeNull();
    teardown();
  }));

  it('stop() pendant un lot en vol : pas de double envoi, et la réponse tardive ne relance pas le suivi', fakeAsync(() => {
    const late = new Subject<PhonePositionsResponse>();
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    service.ingest(fixAt(0.001));
    respond = () => late;
    tick(30 * S);                             // lot parti, réponse pas encore arrivée
    expect(posts.length).toBe(1);

    service.stop({ flush: true });
    flushMicrotasks();
    late.next({ tracking: true, mode: 'full', activeTourId: 7, accepted: 2 });
    late.complete();
    flushMicrotasks();

    expect(posts.length).toBe(1);             // les 2 points ne repartent pas une seconde fois
    expect(service.isActive).toBeFalse();     // le verdict tardif ne redémarre rien
    expect(loc.startWatchCalls).toBe(1);
    teardown();
  }));

  // ─────────────── Verdicts du serveur ───────────────

  it('applyVerdict : démarre, change de cadence, puis arrête', fakeAsync(() => {
    service.applyVerdict({ tracking: true, mode: 'eco' }, 7);
    flushMicrotasks();
    expect(service.activeTourId).toBe(7);
    expect(service.mode).toBe('eco');

    service.applyVerdict({ tracking: true, mode: 'full', activeTourId: 7 });
    flushMicrotasks();
    expect(service.mode).toBe('full');
    expect(loc.startWatchCalls).toBe(1);

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

  // ─────────────── Reprise, comptes ───────────────

  it('resume() reprend un suivi de moins de 12 h avec sa file', fakeAsync(() => {
    const pending: PhonePoint[] = [{ recordedAt: new Date().toISOString(), latitude: 36.8, longitude: 10.1, isMocked: false }];
    store.set(trackingStateKey('42'), { tourId: 9, startedAt: Date.now() - 2 * H, mode: 'eco', userId: '42' });
    store.set(trackingQueueKey('42'), pending);
    flushMicrotasks();
    respond = ok({ activeTourId: 9, mode: 'eco' });
    loc.currentFix = null;

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
    store.set(trackingStateKey('42'), { tourId: 9, startedAt: Date.now() - 13 * H, mode: 'full', userId: '42' });
    store.set(trackingQueueKey('42'), []);
    flushMicrotasks();

    service.resume();
    flushMicrotasks();
    expect(service.isActive).toBeFalse();
    expect(store.peek(trackingStateKey('42'))).toBeNull();
    expect(loc.startWatchCalls).toBe(0);
    teardown();
  }));

  it('session refusée (stop keep) : état et points gardés pour CE compte, repris à sa reconnexion', fakeAsync(() => {
    service.setFlushGate({ blocked: () => true, unblock: () => {} });
    service.start(7, 'full');
    flushMicrotasks();
    service.ingest(fixAt(0));
    service.ingest(fixAt(0.001));
    service.stop({ keep: true });
    flushMicrotasks();
    expect(service.isActive).toBeFalse();
    expect(store.peek<any>(trackingStateKey('42'))?.tourId).toBe(7);
    expect(queued().length).toBe(2);

    service = create();                       // le chauffeur se reconnecte (même compte)
    service.resume();
    flushMicrotasks();
    expect(service.activeTourId).toBe(7);
    expect(service.pendingCount).toBe(2);
    teardown();
  }));

  it('un autre compte ne reprend jamais le suivi ni les points du précédent', fakeAsync(() => {
    store.set(trackingStateKey('42'), { tourId: 7, startedAt: Date.now() - H, mode: 'full', userId: '42' });
    store.set(trackingQueueKey('42'), [{ recordedAt: new Date().toISOString(), latitude: 1, longitude: 2, isMocked: false }]);
    flushMicrotasks();
    auth.userId = '99';

    service.resume();
    flushMicrotasks();
    expect(service.isActive).toBeFalse();

    service.start(8, 'full');
    flushMicrotasks();
    expect(service.pendingCount).toBe(0);
    service.stop({ flush: true });
    flushMicrotasks();
    expect(posts.every(p => p.points.every(pt => pt.latitude !== 1))).toBeTrue();
    expect(store.peek(trackingQueueKey('42'))).not.toBeNull();   // intact pour son propriétaire
    teardown();
  }));

  it('les clés sans compte de la 1.2.0 de recette ne sont jamais relues', fakeAsync(() => {
    store.set(LEGACY_TRACKING_KEYS[0], { tourId: 3, startedAt: Date.now(), mode: 'full' });
    store.set(LEGACY_TRACKING_KEYS[1], [{ recordedAt: new Date().toISOString(), latitude: 1, longitude: 2, isMocked: false }]);
    flushMicrotasks();
    service.resume();
    flushMicrotasks();
    expect(service.isActive).toBeFalse();
    expect(store.peek(LEGACY_TRACKING_KEYS[0])).toBeNull();
    expect(store.peek(LEGACY_TRACKING_KEYS[1])).toBeNull();
    teardown();
  }));
});
