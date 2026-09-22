import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject, of } from 'rxjs';
import { MonitoringPage } from './monitoring.page';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import { PositionShareService } from '../../core/services/position-share.service';
import { PositionShareBarComponent } from '../../shared/position-share-bar.component';

describe('MonitoringPage (partage de la position depuis la fiche de la carte)', () => {
  let fixture: ComponentFixture<MonitoringPage>;
  let page: MonitoringPage;
  let share: jasmine.Spy;

  const selected: PositionUpdate = {
    deviceId: 3, deviceUid: '860000', vehicleId: 12, vehicleName: 'Camion Sfax', plate: '239 TU 6235',
    latitude: 34.74, longitude: 10.76, speedKph: 0, courseDeg: 0, ignitionOn: false, isMoving: false,
    recordedAt: '2026-09-22T07:00:00Z', timestamp: '2026-09-22T07:00:00Z', address: 'Route de Gabès, Sfax'
  };

  const buttons = (): HTMLButtonElement[] =>
    Array.from(fixture.nativeElement.querySelectorAll('app-position-share-bar button.psb-btn'));

  beforeEach(async () => {
    share = jasmine.createSpy('share').and.resolveTo('opened');
    await TestBed.configureTestingModule({
      declarations: [MonitoringPage],
      imports: [PositionShareBarComponent],
      providers: [
        { provide: ApiService, useValue: { getLastPositions: () => of([]) } },
        {
          provide: SignalRService,
          useValue: { startConnection: () => {}, positionBatch$: new Subject(), connectionState$: new BehaviorSubject('Disconnected') }
        },
        { provide: ActivatedRoute, useValue: { queryParams: of({}), snapshot: { queryParamMap: convertToParamMap({}) } } },
        { provide: Router, useValue: { navigate: () => Promise.resolve(true) } },
        { provide: PositionShareService, useValue: { share } }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(MonitoringPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('la fiche propose WhatsApp, Messenger, SMS et Plus, actifs, avec des libellés d\'accessibilité', () => {
    page.selectedVehicle = selected;
    fixture.detectChanges();
    const b = buttons();
    expect(b.map(x => x.getAttribute('data-channel'))).toEqual(['whatsapp', 'messenger', 'sms', 'more']);
    expect(b.map(x => x.textContent!.trim())).toEqual(['WhatsApp', 'Messenger', 'SMS', 'Plus']);
    expect(b.map(x => x.getAttribute('aria-label'))).toEqual([
      'Partager la position sur WhatsApp', 'Partager la position sur Messenger',
      'Partager la position par SMS', 'Partager la position avec une autre application'
    ]);
    expect(b.every(x => !x.disabled)).toBeTrue();
    expect(fixture.nativeElement.querySelector('.psb-hint')).toBeNull();
    // L'ancien bouton unique a disparu.
    expect(fixture.nativeElement.querySelector('.share-btn')).toBeNull();
  });

  it('toucher Messenger partage la position du véhicule sélectionné (plaque, adresse, date)', async () => {
    page.selectedVehicle = selected;
    fixture.detectChanges();
    buttons()[1].click();
    await fixture.whenStable();
    expect(share).toHaveBeenCalledOnceWith('messenger', {
      label: '239 TU 6235', latitude: 34.74, longitude: 10.76,
      address: 'Route de Gabès, Sfax', recordedAt: '2026-09-22T07:00:00Z'
    });
  });

  it('sans position exploitable : boutons désactivés, libellé explicite, aucun partage', () => {
    page.selectedVehicle = { ...selected, latitude: null as any, longitude: null as any };
    fixture.detectChanges();
    const b = buttons();
    expect(b.length).toBe(4);
    expect(b.every(x => x.disabled)).toBeTrue();
    expect(fixture.nativeElement.querySelector('.psb-hint').textContent.trim()).toBe('Aucune position connue pour ce véhicule');
    b[0].click();
    expect(share).not.toHaveBeenCalled();
  });
});
