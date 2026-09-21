import { NgZone } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { BehaviorSubject, Observable, Subject, of, throwError } from 'rxjs';
import { DRIVER_TOURS_CACHE_KEY, DriverToursPage } from './driver-tours.page';
import { DriverTourSummary } from '../../core/models/driver-app.types';
import { FakeAuth, MemoryKvStore } from '../../core/testing/driver-test-doubles';

describe('DriverToursPage (liste « Mes tournées »)', () => {
  let store: MemoryKvStore;
  let auth: FakeAuth;
  let respond: () => Observable<DriverTourSummary[]>;

  const tour = { id: 7, name: 'Tournée Sfax', status: 'planned', waypointCount: 3, completedCount: 0 } as DriverTourSummary;

  function page(): DriverToursPage {
    const api = { getDriverTours: () => respond() };
    const tracking = { state$: new BehaviorSubject(null), sensorError$: new BehaviorSubject(null) };
    const declarations = {
      pendingCount$: new Subject<number>(), refused$: new BehaviorSubject([]), replayed$: new Subject(),
      pendingCount: 0, replay: async () => {}, acknowledgeRefusals: async () => {}
    };
    const push = { tourPush$: new Subject() };
    const p = new DriverToursPage(api as any, { navigate: () => {} } as any, push as any, tracking as any,
      declarations as any, TestBed.inject(NgZone), store as any, auth as any);
    p.ngOnInit();
    return p;
  }

  beforeEach(() => {
    store = new MemoryKvStore();
    auth = new FakeAuth();
    respond = () => of([tour]);
  });

  it('démarrage à froid SANS réseau : la dernière liste « En cours » du compte s\'affiche (accès aux fiches en cache)', async () => {
    await page().load();                      // la veille, en ligne : liste mise en cache
    expect(store.peek<any>(DRIVER_TOURS_CACHE_KEY)).toEqual({ userId: '42', tours: [tour] });

    respond = () => throwError(() => new HttpErrorResponse({ status: 0 }));
    const offline = page();                   // le matin, application redémarrée au dépôt sans réseau
    await offline.load();
    expect(offline.tours.map(t => t.id)).toEqual([7]);
    expect(offline.fromCache).toBeTrue();
    expect(offline.error).toBeNull();
  });

  it('la liste en cache d\'un AUTRE compte n\'est jamais montrée', async () => {
    await page().load();
    auth.userId = '99';
    respond = () => throwError(() => new HttpErrorResponse({ status: 0 }));
    const other = page();
    await other.load();
    expect(other.tours).toEqual([]);
    expect(other.error).toContain('Pas de réseau');
  });

  it('une erreur du serveur (pas le réseau) n\'affiche pas le cache', async () => {
    await page().load();
    respond = () => throwError(() => new HttpErrorResponse({ status: 500, error: { message: 'Erreur interne' } }));
    const p = page();
    await p.load();
    expect(p.fromCache).toBeFalse();
    expect(p.error).toBe('Erreur interne');
  });
});
