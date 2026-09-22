import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
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
