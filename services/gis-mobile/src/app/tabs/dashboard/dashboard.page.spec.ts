import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, tick } from '@angular/core/testing';
import { Router } from '@angular/router';
import { BehaviorSubject, Subject, of } from 'rxjs';
import { DashboardPage } from './dashboard.page';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { SignalRService } from '../../core/services/signalr.service';
import { FleetStateSummaryComponent } from '../../shared/fleet-state-summary.component';

describe('DashboardPage (état de la flotte aux couleurs de la carte)', () => {
  let fixture: ComponentFixture<DashboardPage>;
  let page: DashboardPage;
  let batch$: Subject<any[]>;
  let conn$: BehaviorSubject<string>;

  const rgb = (hex: string) => `rgb(${[1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)).join(', ')})`;
  const ago = (minutes: number) => new Date(Date.now() - minutes * 60 * 1000).toISOString();
  const row = (vehicleId: number, vehicleName: string, lastPosition: any) =>
    ({ vehicleId, vehicleName, plate: `${vehicleId} TU`, deviceId: vehicleId, lastPosition });

  const latest = () => [
    row(1, 'Camion', { latitude: 34, longitude: 10, speedKph: 70, ignitionOn: true, recordedAt: ago(1) }),
    row(2, 'Clio', { latitude: 36, longitude: 10, speedKph: 0, ignitionOn: true, recordedAt: ago(1) }),
    row(3, 'Partner', { latitude: 35, longitude: 10, speedKph: 0, ignitionOn: false, recordedAt: ago(1) }),
    row(4, 'Boxer', { latitude: 33, longitude: 10, speedKph: 30, ignitionOn: true, recordedAt: ago(5 * 24 * 60) }),
    // Boîtier posé mais jamais entendu : compte « Déconnecté ».
    row(5, 'Neuf', null)
  ];

  const stateCount = (state: string) =>
    (fixture.nativeElement.querySelector(`.fleet-state[data-state="${state}"] .fleet-count`) as HTMLElement).textContent!.trim();
  const recent = (name: string): HTMLElement =>
    Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('ion-item'))
      .find(el => el.querySelector('h2')!.textContent!.trim() === name)!;

  beforeEach(async () => {
    batch$ = new Subject<any[]>();
    conn$ = new BehaviorSubject('Connected');
    await TestBed.configureTestingModule({
      declarations: [DashboardPage],
      imports: [FleetStateSummaryComponent],
      providers: [
        {
          provide: ApiService,
          useValue: {
            getDashboardStats: () => of({ vehicles: { total: 6, online: 3, withGps: 5 }, maintenance: { upcoming: 1 }, alerts: { unresolved: 2 }, trips: { distanceToday: 120 } }),
            getLastPositions: () => of(latest())
          }
        },
        { provide: AuthService, useValue: { getCurrentUserSync: () => ({ name: 'Slim Test', companyName: 'Belive' }) } },
        {
          provide: SignalRService,
          useValue: { connectionState$: conn$, positionBatch$: batch$, notification$: new Subject() }
        },
        { provide: Router, useValue: { navigate: () => Promise.resolve(true) } }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(DashboardPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('compte les quatre états sur toute la flotte équipée, chacun avec sa couleur, son icône et son libellé', () => {
    expect(page.stateCounts).toEqual({ moving: 1, idling: 1, parked: 1, offline: 2 });
    const states = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.fleet-state'));
    expect(states.map(s => s.querySelector('.fleet-label')!.textContent!.trim()))
      .toEqual(['En route', 'Au ralenti', 'À l\'arrêt', 'Déconnecté']);
    expect(states.map(s => (s.querySelector('.fleet-icon') as HTMLElement).style.color))
      .toEqual([rgb('#10b981'), rgb('#f59e0b'), rgb('#ef4444'), rgb('#9ca3af')]);
    expect(states.map(s => (s.querySelector('ion-icon') as any).name))
      .toEqual(['navigate', 'pause-circle', 'stop-circle', 'cloud-offline']);
    expect(['moving', 'idling', 'parked', 'offline'].map(stateCount)).toEqual(['1', '1', '1', '2']);
    // Barre proportionnelle : un segment par état, à sa couleur, à la mesure de son compteur.
    const segs = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.fleet-seg'));
    expect(segs.map(s => [s.getAttribute('data-state'), s.style.backgroundColor, s.style.flexGrow])).toEqual([
      ['moving', rgb('#10b981'), '1'], ['idling', rgb('#f59e0b'), '1'],
      ['parked', rgb('#ef4444'), '1'], ['offline', rgb('#9ca3af'), '2']
    ]);
    // La barre est doublée d'un résumé texte pour les lecteurs d'écran.
    expect(fixture.nativeElement.querySelector('.fleet-bar').getAttribute('aria-label'))
      .toBe('En route : 1, Au ralenti : 1, À l\'arrêt : 1, Déconnecté : 2');
  });

  it('« Activité récente » : avatar et libellé de l\'état de chaque véhicule', () => {
    const expected: [string, string, string, string][] = [
      ['Camion', 'moving', 'En route', '#10b981'],
      ['Clio', 'idling', 'Au ralenti', '#f59e0b'],
      ['Partner', 'parked', 'À l\'arrêt', '#ef4444'],
      ['Boxer', 'offline', 'Déconnecté', '#9ca3af']
    ];
    for (const [name, state, label, color] of expected) {
      const item = recent(name);
      const avatar = item.querySelector('.vehicle-avatar') as HTMLElement;
      expect(avatar.classList).withContext(name).toContain(state);
      expect(avatar.style.color).withContext(name).toBe(rgb(color));
      expect(item.querySelector('.state-label')!.textContent!.trim()).withContext(name).toBe(label);
    }
  });

  it('une trame SignalR met les compteurs à jour (le véhicule garé repart)', () => {
    batch$.next([{ vehicleId: 3, vehicleName: 'Partner', plate: '3 TU', latitude: 35, longitude: 10, speedKph: 45, ignitionOn: true, recordedAt: ago(0) }]);
    fixture.detectChanges();
    expect(page.stateCounts).toEqual({ moving: 2, idling: 1, parked: 0, offline: 2 });
    expect(stateCount('moving')).toBe('2');
    expect(recent('Partner').querySelector('.state-label')!.textContent!.trim()).toBe('En route');
  });

  it('les tuiles qui ne sont pas des états n\'empruntent plus l\'orange (maintenance) ni le vert', () => {
    const icons = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.stat-card .stat-icon ion-icon'));
    const colors = icons.map(i => i.style.color);
    expect(colors).not.toContain(rgb('#f59e0b'));
    expect(colors).not.toContain(rgb('#10b981'));
    expect(colors).not.toContain(rgb('#9ca3af'));
  });

  it('le badge de connexion de l\'application n\'emprunte ni le vert « En route » ni le rouge « À l\'arrêt »', () => {
    const badge = () => fixture.nativeElement.querySelector('.connection-badge') as HTMLElement;
    const forbidden = ['rgba(16, 185, 129, 0.3)', 'rgba(239, 68, 68, 0.3)', rgb('#10b981'), rgb('#ef4444'), rgb('#9ca3af')];
    expect(badge().classList).toContain('connected');
    expect(getComputedStyle(badge()).backgroundColor).toBe('rgba(255, 255, 255, 0.2)');

    conn$.next('Disconnected');
    fixture.detectChanges();
    expect(badge().classList).toContain('disconnected');
    expect(getComputedStyle(badge()).backgroundColor).toBe(rgb('#4b5563'));
    expect(forbidden).not.toContain(getComputedStyle(badge()).backgroundColor);
  });

  it('« Activité récente » : pas de vitesse fantôme à côté de « Déconnecté »', () => {
    expect(recent('Boxer').querySelector('.recent-speed')).toBeNull();
    expect(recent('Camion').querySelector('.recent-speed')!.textContent!.trim()).toBe('· 70 km/h');
  });

  it('un boîtier qui se tait passe au compteur « Déconnecté » sans attendre la trame d\'un autre véhicule', fakeAsync(() => {
    fixture.destroy();
    fixture = TestBed.createComponent(DashboardPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
    expect(page.stateCounts).toEqual({ moving: 1, idling: 1, parked: 1, offline: 2 });
    tick(30 * 60 * 1000);
    fixture.detectChanges();
    expect(page.stateCounts).toEqual({ moving: 0, idling: 0, parked: 0, offline: 5 });
    expect(stateCount('offline')).toBe('5');
    fixture.destroy();
    discardPeriodicTasks();
    flush();
  }));
});
