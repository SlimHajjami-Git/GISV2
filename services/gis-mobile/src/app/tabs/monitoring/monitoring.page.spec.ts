import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject, Subject, of } from 'rxjs';
import { MonitoringPage } from './monitoring.page';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import { PositionShareService } from '../../core/services/position-share.service';
import { PositionShareBarComponent } from '../../shared/position-share-bar.component';
import { LEGEND_COLLAPSED_KEY, VehicleStateLegendComponent } from '../../shared/vehicle-state-legend.component';
import { ONLINE_WINDOW_MS } from '../../core/vehicle-state.util';
import * as L from 'leaflet';

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
    localStorage.removeItem(LEGEND_COLLAPSED_KEY);
    await TestBed.configureTestingModule({
      declarations: [MonitoringPage],
      imports: [PositionShareBarComponent, VehicleStateLegendComponent],
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

  describe('code couleur des états (carte)', () => {
    const rgb = (hex: string) => `rgb(${[1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)).join(', ')})`;
    const ago = (minutes: number) => new Date(Date.now() - minutes * 60 * 1000).toISOString();
    const at = (speedKph: number, ignitionOn: boolean, recordedAt: string): PositionUpdate =>
      ({ ...selected, speedKph, ignitionOn, recordedAt, timestamp: recordedAt });

    const cases: [string, PositionUpdate, string, string, string][] = [
      ['moving', at(55, true, ago(1)), 'En route', '#10b981', 'navigate'],
      ['idling', at(0, true, ago(1)), 'Au ralenti', '#f59e0b', 'pause-circle'],
      ['parked', at(0, false, ago(1)), 'À l\'arrêt', '#ef4444', 'stop-circle'],
      ['offline', at(40, true, ago(3 * 24 * 60)), 'Déconnecté', '#9ca3af', 'cloud-offline']
    ];

    it('le marqueur de chaque état porte sa couleur (vert, orange, rouge, gris)', () => {
      for (const [state, pos, , color] of cases) {
        const icon = (page as any).pickIcon(pos);
        expect(icon.options.className).withContext(state).toContain(`${state}-marker`);
        expect(icon.options.html).withContext(state).toContain(`background:${color}`);
      }
    });

    it('la fiche affiche la même couleur, le même libellé et l\'icône de l\'état, texte lisible', () => {
      for (const [state, pos, label, color, icon] of cases) {
        page.selectedVehicle = pos;
        fixture.detectChanges();
        const pill = fixture.nativeElement.querySelector('.status-pill') as HTMLElement;
        const avatar = fixture.nativeElement.querySelector('.sheet-avatar') as HTMLElement;
        expect(pill.textContent!.trim()).withContext(state).toBe(label);
        expect(pill.style.backgroundColor).withContext(state).toBe(rgb(color));
        expect(pill.style.color).withContext(state).toBe(rgb('#111827'));
        expect(avatar.classList).withContext(state).toContain(state);
        expect(avatar.style.color).withContext(state).toBe(rgb(color));
        expect((avatar.querySelector('ion-icon') as any).name).withContext(state).toBe(icon);
        // Le vert reste réservé à « En route » : l'éclair du contact mis n'en est plus.
        const flash = Array.from<any>(fixture.nativeElement.querySelectorAll('.sheet-stat ion-icon'))
          .find(i => i.name === 'flash' || i.name === 'flash-off');
        expect(flash.color).withContext(state).toBe(pos.ignitionOn ? 'primary' : 'medium');
      }
    });

    it('la légende des quatre couleurs est posée sur la carte et s\'efface quand la fiche s\'ouvre', () => {
      const legend = () => fixture.nativeElement.querySelector('app-vehicle-state-legend') as HTMLElement | null;
      expect(legend()).not.toBeNull();
      const rows = Array.from<HTMLElement>(legend()!.querySelectorAll('.vsl-row'));
      expect(rows.map(r => r.querySelector('.vsl-label')!.textContent!.trim()))
        .toEqual(['En route', 'Au ralenti', 'À l\'arrêt', 'Déconnecté']);
      // Mêmes pastilles que les marqueurs (même fabrique), glyphe SVG compris.
      expect(rows.map(r => /background:(#[0-9a-f]{6})/.exec(r.querySelector('.vs-badge')!.getAttribute('style')!)![1]))
        .toEqual(['#10b981', '#f59e0b', '#ef4444', '#9ca3af']);
      expect(rows.every(r => !!r.querySelector('.vs-badge svg'))).toBeTrue();

      page.selectedVehicle = selected;
      fixture.detectChanges();
      expect(legend()).toBeNull();
    });

    it('pas de vitesse fantôme sous la pastille grise « Déconnecté »', () => {
      const speed = () => (fixture.nativeElement.querySelector('.speed-stat span') as HTMLElement).textContent!.trim();
      page.selectedVehicle = at(38, true, ago(11 * 24 * 60));
      fixture.detectChanges();
      expect(speed()).toBe('—');
      page.selectedVehicle = at(55, true, ago(1));
      fixture.detectChanges();
      expect(speed()).toBe('55 km/h');
    });

    describe('un marqueur vieillit sans trame (SignalR connecté, aucun rechargement)', () => {
      let host: HTMLDivElement;
      const markerClass = (vehicleId: number) =>
        ((page as any).markers.get(vehicleId) as L.Marker).options.icon!.options.className!;

      beforeEach(() => {
        host = document.createElement('div');
        host.style.width = '400px';
        host.style.height = '300px';
        document.body.appendChild(host);
        (page as any).map = L.map(host, { center: [34, 9], zoom: 7 });
      });

      afterEach(() => {
        (page as any).map?.remove();
        (page as any).map = null;
        host.remove();
      });

      it('refreshMarkerStates repasse au gris un véhicule muet depuis plus de 30 min, sans retoucher les autres', () => {
        (page as any).updateMarker({ ...at(50, true, ago(1)), vehicleId: 12 });
        (page as any).updateMarker({ ...at(0, false, ago(1)), vehicleId: 13, vehicleName: 'Partner' });
        expect(markerClass(12)).toContain('moving-marker');
        expect(markerClass(13)).toContain('parked-marker');

        const in31min = Date.now() + 31 * 60 * 1000;
        page.refreshMarkerStates(in31min);
        expect(markerClass(12)).toContain('offline-marker');
        expect(markerClass(13)).toContain('offline-marker');

        // État inchangé : aucune retouche du DOM.
        const spy = spyOn((page as any).markers.get(12), 'setIcon').and.callThrough();
        page.refreshMarkerStates(in31min + 1000);
        expect(spy).not.toHaveBeenCalled();
      });

      it('l\'horloge de la carte fait passer le marqueur au gris à la seconde où la fiche le dit « Déconnecté »', fakeAsync(() => {
        // Dernière trame à 50 km/h, 30 min moins 1,5 s avant maintenant.
        const recordedAt = new Date(Date.now() - ONLINE_WINDOW_MS + 1500).toISOString();
        (page as any).updateMarker({ ...at(50, true, recordedAt), vehicleId: 12 });
        page.ionViewDidEnter();
        tick(1000);
        expect(markerClass(12)).toContain('moving-marker');
        tick(1000);
        expect(markerClass(12)).toContain('offline-marker');
        page.selectedVehicle = (page as any).markers.get(12)._posData;
        fixture.detectChanges();
        expect((fixture.nativeElement.querySelector('.status-pill') as HTMLElement).textContent!.trim()).toBe('Déconnecté');

        page.ionViewWillLeave();
        discardPeriodicTasks();
        flush();
      }));
    });
  });
});
