import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, tick } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { AlertController, ToastController } from '@ionic/angular';
import { Observable, Subject, of, throwError } from 'rxjs';
import { VehiclesPage } from './vehicles.page';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { SignalRService } from '../../core/services/signalr.service';
import { PositionShareService } from '../../core/services/position-share.service';
import { PositionShareBarComponent } from '../../shared/position-share-bar.component';

describe('VehiclesPage (partage de la position depuis la fiche véhicule)', () => {
  let fixture: ComponentFixture<VehiclesPage>;
  let page: VehiclesPage;
  let share: jasmine.Spy;
  let toasts: string[];
  /** Réponses successives de getLastPositions (chargement, puis repli au toucher). */
  let positionsResponses: (() => Observable<any[]>)[];
  let batch$: Subject<any[]>;

  // GET /api/vehicles ne renvoie PAS d'adresse (VehicleDto) : l'adresse vient de la
  // dernière position REST. `lastAddress` sur la Clio simule une adresse sans point associé.
  const vehicles = [
    { id: 1, name: 'Camion Sfax', plate: '239 TU 6235', gpsDevice: { id: 3 } },
    { id: 2, name: 'Clio', plate: '101 TU 2020', lastAddress: 'Ancienne adresse, Bizerte', gpsDevice: null }
  ];
  const latest = [
    {
      vehicleId: 1,
      lastPosition: {
        latitude: 34.74, longitude: 10.76, speedKph: 0, ignitionOn: false,
        recordedAt: '2026-09-22T07:00:00Z', address: 'Route de Gabès, Sfax'
      }
    }
  ];

  const buttons = (): HTMLButtonElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll('app-position-share-bar button.psb-btn'));

  async function open(vehicleId: number) {
    await TestBed.configureTestingModule({
      declarations: [VehiclesPage],
      imports: [PositionShareBarComponent],
      providers: [
        {
          provide: ApiService,
          useValue: {
            getVehicles: () => of(vehicles.map(v => ({ ...v }))),
            getLastPositions: () => (positionsResponses.shift() ?? (() => of(latest)))(),
            getImmobilizationState: () => of(null)
          }
        },
        { provide: AuthService, useValue: { getCurrentUser: () => of(null) } },
        { provide: SignalRService, useValue: { positionBatch$: batch$ } },
        { provide: Router, useValue: { navigate: () => Promise.resolve(true) } },
        { provide: ActivatedRoute, useValue: {} },
        { provide: AlertController, useValue: { create: async () => ({ present: async () => {} }) } },
        {
          provide: ToastController,
          useValue: { create: async (o: any) => { toasts.push(o.message); return { present: async () => {} }; } }
        },
        { provide: PositionShareService, useValue: { share } }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(VehiclesPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    page.selectVehicle(page.vehicles.find(v => String(v.id) === String(vehicleId))!);
    fixture.detectChanges();
  }

  beforeEach(() => {
    share = jasmine.createSpy('share').and.resolveTo('opened');
    toasts = [];
    positionsResponses = [];
    batch$ = new Subject<any[]>();
  });

  afterEach(() => fixture?.destroy());

  it('la fiche propose les quatre canaux, actifs quand la position est connue ; l\'ancien bouton a disparu', async () => {
    await open(1);
    const b = buttons();
    expect(b.map(x => x.getAttribute('data-channel'))).toEqual(['whatsapp', 'messenger', 'sms', 'more']);
    expect(b.map(x => x.textContent!.trim())).toEqual(['WhatsApp', 'Messenger', 'SMS', 'Plus']);
    expect(b.every(x => !x.disabled && !!x.getAttribute('aria-label'))).toBeTrue();
    expect(fixture.nativeElement.querySelector('.share-position-btn')).toBeNull();
  });

  it('toucher WhatsApp partage la dernière position connue avec sa date', async () => {
    await open(1);
    buttons()[0].click();
    await fixture.whenStable();
    expect(share).toHaveBeenCalledOnceWith('whatsapp', {
      label: '239 TU 6235', latitude: 34.74, longitude: 10.76,
      address: 'Route de Gabès, Sfax', recordedAt: '2026-09-22T07:00:00Z'
    });
  });

  it('dernières positions chargées et aucune pour ce véhicule : boutons désactivés avec un libellé', async () => {
    await open(2);
    const b = buttons();
    expect(b.length).toBe(4);
    expect(b.every(x => x.disabled)).toBeTrue();
    expect(fixture.nativeElement.querySelector('.psb-hint').textContent.trim()).toBe('Aucune position connue pour ce véhicule');
  });

  it('dernières positions pas encore obtenues : boutons actifs, le repli REST trouve la position au toucher', async () => {
    positionsResponses = [
      () => throwError(() => new Error('réseau')),     // chargement de la liste : échec
      () => of([{ vehicleId: 2, lastPosition: { latitude: 36.8, longitude: 10.18, recordedAt: '2026-09-21T18:30:00Z', address: 'Lac 2, Tunis' } }])
    ];
    await open(2);
    const b = buttons();
    expect(b.every(x => !x.disabled)).toBeTrue();
    b[2].click();
    await fixture.whenStable();
    expect(share).toHaveBeenCalledOnceWith('sms', {
      label: '101 TU 2020', latitude: 36.8, longitude: 10.18, address: 'Lac 2, Tunis', recordedAt: '2026-09-21T18:30:00Z'
    });
  });

  it('une trame SignalR a déplacé le véhicule : lien et date de la trame, SANS l\'adresse d\'avant', async () => {
    await open(1);
    batch$.next([{ vehicleId: 1, latitude: 34.81, longitude: 10.70, speedKph: 62, ignitionOn: true, recordedAt: '2026-09-22T13:10:00Z' }]);
    buttons()[0].click();
    await fixture.whenStable();
    expect(share).toHaveBeenCalledOnceWith('whatsapp', {
      label: '239 TU 6235', latitude: 34.81, longitude: 10.70, address: null, recordedAt: '2026-09-22T13:10:00Z'
    });
  });

  it('trame SignalR au même point (véhicule à l\'arrêt) : l\'adresse de ce point reste jointe', async () => {
    await open(1);
    batch$.next([{ vehicleId: 1, latitude: 34.74, longitude: 10.76, speedKph: 0, ignitionOn: false, recordedAt: '2026-09-22T13:10:00Z' }]);
    buttons()[1].click();
    await fixture.whenStable();
    expect(share).toHaveBeenCalledOnceWith('messenger', {
      label: '239 TU 6235', latitude: 34.74, longitude: 10.76, address: 'Route de Gabès, Sfax', recordedAt: '2026-09-22T13:10:00Z'
    });
  });

  it('repli REST : une position sans adresse n\'hérite pas d\'une adresse gardée sur le véhicule', async () => {
    positionsResponses = [
      () => throwError(() => new Error('réseau')),
      () => of([{ vehicleId: 2, lastPosition: { latitude: 37.27, longitude: 9.87, recordedAt: '2026-09-22T09:00:00Z' } }])
    ];
    await open(2);
    buttons()[0].click();
    await fixture.whenStable();
    expect(share).toHaveBeenCalledOnceWith('whatsapp', {
      label: '101 TU 2020', latitude: 37.27, longitude: 9.87, address: null, recordedAt: '2026-09-22T09:00:00Z'
    });
  });

  it('repli REST sans position pour ce véhicule : message, aucun partage', async () => {
    positionsResponses = [() => throwError(() => new Error('réseau')), () => of(latest)];
    await open(2);
    buttons()[3].click();
    await fixture.whenStable();
    expect(share).not.toHaveBeenCalled();
    expect(toasts).toEqual(['Aucune position connue pour Clio.']);
  });
});

describe('VehiclesPage (code couleur des états : vert en route, orange au ralenti, rouge à l\'arrêt, gris déconnecté)', () => {
  let fixture: ComponentFixture<VehiclesPage>;
  let page: VehiclesPage;
  let batch$: Subject<any[]>;

  /** Le navigateur normalise les couleurs de style en rgb(). */
  const rgb = (hex: string) => `rgb(${[1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)).join(', ')})`;
  const ago = (minutes: number) => new Date(Date.now() - minutes * 60 * 1000).toISOString();

  const vehicles = [
    { id: 1, name: 'Camion Sfax', plate: '239 TU 6235', gpsDevice: { id: 3 } },
    { id: 2, name: 'Clio', plate: '101 TU 2020', gpsDevice: { id: 4 } },
    { id: 3, name: 'Partner', plate: '55 TU 1111', gpsDevice: { id: 5 } },
    { id: 4, name: 'Boxer', plate: '77 TU 7777', gpsDevice: { id: 6 } },
    { id: 5, name: 'Sans boîtier', plate: '88 TU 8888', gpsDevice: null }
  ];
  const latest = () => [
    { vehicleId: 1, lastPosition: { latitude: 34.7, longitude: 10.7, speedKph: 62, ignitionOn: true, recordedAt: ago(1) } },
    // À l'arrêt MOTEUR TOURNANT : c'est le cas que la liste confondait avec « contact coupé ».
    { vehicleId: 2, lastPosition: { latitude: 36.8, longitude: 10.1, speedKph: 0, ignitionOn: true, recordedAt: ago(2) } },
    { vehicleId: 3, lastPosition: { latitude: 35.8, longitude: 10.6, speedKph: 0, ignitionOn: false, recordedAt: ago(5) } },
    // Dernière trame figée à 40 km/h il y a deux jours : déconnecté, pas « en route ».
    { vehicleId: 4, lastPosition: { latitude: 33.8, longitude: 10.1, speedKph: 40, ignitionOn: true, recordedAt: ago(48 * 60) } }
  ];

  const item = (name: string): HTMLElement =>
    Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('ion-item'))
      .find(el => el.querySelector('.vehicle-name')!.textContent!.trim() === name)!;
  const chips = (): HTMLElement[] => Array.from(fixture.nativeElement.querySelectorAll('.filter-chips ion-chip'));
  /** Texte réel des noeuds : quand une autre spec a enregistré les composants Ionic,
   *  ion-label remplace son textContent (correctif de slots Stencil) et rend ''. */
  const textOf = (el: Element) => {
    const walker = document.createTreeWalker(el, NodeFilter.SHOW_TEXT);
    let out = '';
    while (walker.nextNode()) out += walker.currentNode.nodeValue;
    return out.trim();
  };
  const listedNames = () => Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('ion-item .vehicle-name'))
    .map(el => el.textContent!.trim());

  beforeEach(async () => {
    batch$ = new Subject<any[]>();
    await TestBed.configureTestingModule({
      declarations: [VehiclesPage],
      imports: [PositionShareBarComponent],
      providers: [
        {
          provide: ApiService,
          useValue: {
            getVehicles: () => of(vehicles.map(v => ({ ...v }))),
            getLastPositions: () => of(latest()),
            getImmobilizationState: () => of(null)
          }
        },
        { provide: AuthService, useValue: { getCurrentUser: () => of(null) } },
        { provide: SignalRService, useValue: { positionBatch$: batch$ } },
        { provide: Router, useValue: { navigate: () => Promise.resolve(true) } },
        { provide: ActivatedRoute, useValue: {} },
        { provide: AlertController, useValue: { create: async () => ({ present: async () => {} }) } },
        { provide: ToastController, useValue: { create: async () => ({ present: async () => {} }) } },
        { provide: PositionShareService, useValue: { share: async () => 'opened' } }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(VehiclesPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('chaque ligne porte la couleur, l\'icône et le libellé de son état — dont le ralenti et le gris déconnecté', () => {
    const expected: [string, string, string, string, string][] = [
      ['Camion Sfax', 'moving', 'En route', '#10b981', 'navigate'],
      ['Clio', 'idling', 'Au ralenti', '#f59e0b', 'pause-circle'],
      ['Partner', 'parked', 'À l\'arrêt', '#ef4444', 'stop-circle'],
      ['Boxer', 'offline', 'Déconnecté', '#9ca3af', 'cloud-offline']
    ];
    for (const [name, state, label, color, icon] of expected) {
      const row = item(name);
      const dot = row.querySelector('.vehicle-status-dot') as HTMLElement;
      expect(dot.classList).withContext(name).toContain(state);
      expect(dot.style.color).withContext(name).toBe(rgb(color));
      expect((dot.querySelector('ion-icon') as any).name).withContext(name).toBe(icon);
      expect(row.querySelector('.vehicle-state-label')!.textContent!.trim()).withContext(name).toBe(label);
    }
  });

  it('les puces de filtre suivent les quatre états, chacune avec sa pastille de couleur et son compteur', () => {
    page.toggleFilter();
    fixture.detectChanges();
    const c = chips();
    expect(c.map(x => x.getAttribute('data-filter'))).toEqual(['all', 'moving', 'idling', 'parked', 'offline']);
    // Le véhicule sans boîtier n'entre que dans « Tous » : « Déconnecté » ne compte que la flotte équipée.
    expect(c.map(x => textOf(x.querySelector('ion-label')!)))
      .toEqual(['Tous (5)', 'En route (1)', 'Au ralenti (1)', 'À l\'arrêt (1)', 'Déconnecté (1)']);
    expect(c.slice(1).map(x => (x.querySelector('.chip-dot') as HTMLElement).style.backgroundColor))
      .toEqual([rgb('#10b981'), rgb('#f59e0b'), rgb('#ef4444'), rgb('#9ca3af')]);
  });

  it('toucher « Au ralenti » ne garde que le véhicule arrêté moteur tournant ; « Déconnecté » le seul boîtier muet', () => {
    page.toggleFilter();
    fixture.detectChanges();
    chips()[2].click();
    fixture.detectChanges();
    expect(page.activeFilter).toBe('idling');
    expect(listedNames()).toEqual(['Clio']);
    expect((chips()[2] as any).color).toBe('primary');

    chips()[4].click();
    fixture.detectChanges();
    expect(listedNames()).toEqual(['Boxer']);

    chips()[0].click();
    fixture.detectChanges();
    expect(listedNames().length).toBe(5);
  });

  it('une trame SignalR contact mis à l\'arrêt fait passer un véhicule garé au ralenti, compteurs et filtre compris', () => {
    page.setFilter('idling');
    fixture.detectChanges();
    batch$.next([{ vehicleId: 3, latitude: 35.8, longitude: 10.6, speedKph: 0, ignitionOn: true, recordedAt: ago(0) }]);
    fixture.detectChanges();
    expect(page.counts).toEqual({ moving: 1, idling: 2, parked: 0, offline: 1 });
    expect(listedNames()).toEqual(['Clio', 'Partner']);
    expect(item('Partner').querySelector('.vehicle-state-label')!.textContent!.trim()).toBe('Au ralenti');
  });

  it('la fiche reprend la même pastille : couleur de l\'état, libellé, texte lisible', () => {
    page.selectVehicle(page.vehicles.find(v => String(v.id) === '2')!);
    fixture.detectChanges();
    const pill = fixture.nativeElement.querySelector('.detail-card .status-pill') as HTMLElement;
    expect(pill.textContent!.trim()).toBe('Au ralenti');
    expect(pill.style.backgroundColor).toBe(rgb('#f59e0b'));
    expect(pill.style.color).toBe(rgb('#111827'));
    const avatar = fixture.nativeElement.querySelector('.detail-card .detail-status') as HTMLElement;
    expect(avatar.classList).toContain('idling');
    expect(avatar.style.color).toBe(rgb('#f59e0b'));
    // Plus de vert hors « En route » dans la fiche : ni l'éclair du contact mis, ni le kilométrage.
    const gridColors = Array.from<any>(fixture.nativeElement.querySelectorAll('.detail-grid ion-icon')).map(i => i.color);
    expect(gridColors).not.toContain('success');
    expect(gridColors).toContain('primary');

    page.selectVehicle(page.vehicles.find(v => String(v.id) === '4')!);
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('.detail-card .status-pill') as HTMLElement).textContent!.trim()).toBe('Déconnecté');
  });

  it('un véhicule SANS boîtier a une pastille neutre « Sans boîtier » : ni gris déconnecté, ni compté dans un état', () => {
    const row = item('Sans boîtier');
    const dot = row.querySelector('.vehicle-status-dot') as HTMLElement;
    expect(dot.classList).toContain('no-device');
    expect(dot.classList).not.toContain('offline');
    expect((dot.querySelector('ion-icon') as any).name).toBe('hardware-chip-outline');
    expect(row.querySelector('.vehicle-state-label')!.textContent!.trim()).toBe('Sans boîtier');
    for (const hex of ['#10b981', '#f59e0b', '#ef4444', '#9ca3af']) expect(dot.style.color).not.toBe(rgb(hex));
    expect(row.querySelector('.vehicle-speed')).toBeNull();
    // Comptes alignés sur le tableau de bord (flotte équipée seulement) : 4 équipés, 1 muet.
    expect(page.counts).toEqual({ moving: 1, idling: 1, parked: 1, offline: 1 });

    page.selectVehicle(page.vehicles.find(v => String(v.id) === '5')!);
    fixture.detectChanges();
    const pill = fixture.nativeElement.querySelector('.detail-card .status-pill') as HTMLElement;
    expect(pill.textContent!.trim()).toBe('Sans boîtier');
    expect(pill.classList).toContain('no-device');
    expect(pill.style.backgroundColor).toBe('transparent');
    expect(fixture.nativeElement.querySelector('.detail-card .detail-value.no-speed').textContent.trim()).toBe('—');
  });

  it('pas de vitesse fantôme sous « Déconnecté » : ni dans la liste, ni dans la fiche', () => {
    expect(item('Boxer').querySelector('.vehicle-speed')).toBeNull();
    expect(item('Camion Sfax').querySelector('.vehicle-speed .speed-value')!.textContent!.trim()).toBe('62');
    expect(item('Partner').querySelector('.vehicle-speed .speed-value')!.textContent!.trim()).toBe('0');

    page.selectVehicle(page.vehicles.find(v => String(v.id) === '4')!);
    fixture.detectChanges();
    const speed = () => (fixture.nativeElement.querySelector('.detail-grid .detail-value') as HTMLElement).textContent!.trim();
    expect(speed()).toBe('—');

    page.selectVehicle(page.vehicles.find(v => String(v.id) === '1')!);
    fixture.detectChanges();
    expect(speed()).toBe('62 km/h');
  });

  it('un boîtier qui se tait passe au gris « Déconnecté » tout seul, sans attendre la trame d\'un autre véhicule', fakeAsync(() => {
    // Nouvelle page créée DANS la zone fakeAsync : son horloge de recalcul suit tick().
    fixture.destroy();
    fixture = TestBed.createComponent(VehiclesPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
    tick();
    fixture.detectChanges();
    page.setFilter('moving');
    fixture.detectChanges();
    expect(listedNames()).toEqual(['Camion Sfax']);

    // 30 min sans aucune trame : la dernière du Camion (il y a 1 min, à 62 km/h) date de 31 min.
    tick(30 * 60 * 1000);
    fixture.detectChanges();
    expect(page.counts).toEqual({ moving: 0, idling: 0, parked: 0, offline: 4 });
    expect(listedNames()).toEqual([]);
    page.setFilter('all');
    fixture.detectChanges();
    const dot = item('Camion Sfax').querySelector('.vehicle-status-dot') as HTMLElement;
    expect(dot.classList).toContain('offline');
    expect(dot.style.color).toBe(rgb('#9ca3af'));
    expect(item('Camion Sfax').querySelector('.vehicle-speed')).toBeNull();

    fixture.destroy();
    discardPeriodicTasks();
    flush();
  }));
});
