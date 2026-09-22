import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ActionSheetController } from '@ionic/angular';
import { of } from 'rxjs';
import {
  PlaybackPage, PlaybackPoint, TRACK_END_HTML, TRACK_START_HTML, findStops, playbackMarkerHtml, pointState
} from './playback.page';
import { ApiService } from '../../core/services/api.service';

const STATE_COLORS = ['#10b981', '#f59e0b', '#ef4444', '#9ca3af'];

/** Point du trajet à t0 + `min` minutes. */
function pt(min: number, speed: number, ignition: boolean, heading = 0): PlaybackPoint {
  return { lat: 36.8 + min / 1000, lng: 10.1, speed, heading, ignition, time: new Date(Date.UTC(2026, 8, 22, 8, min)) };
}

describe('Replay (code couleur des points du trajet)', () => {
  it('point en route vert, ralenti orange, contact coupé rouge — jamais bleu ni gris', () => {
    expect(pointState(pt(0, 60, true))).toBe('moving');
    expect(pointState(pt(0, 0, true))).toBe('idling');
    expect(pointState(pt(0, 0, false))).toBe('parked');

    expect(playbackMarkerHtml(pt(0, 60, true))).toContain('background:#10b981');
    expect(playbackMarkerHtml(pt(0, 0, true))).toContain('background:#f59e0b');
    expect(playbackMarkerHtml(pt(0, 0, false))).toContain('background:#ef4444');
    for (const html of [playbackMarkerHtml(pt(0, 60, true)), playbackMarkerHtml(pt(0, 0, false))]) {
      expect(html).not.toContain('#1a56db');
      expect(html).not.toContain('#6b7280');
    }
  });

  it('seule la flèche « en route » suit le cap', () => {
    expect(playbackMarkerHtml(pt(0, 60, true, 135))).toContain('rotate(135deg)');
    expect(playbackMarkerHtml(pt(0, 0, true, 135))).not.toContain('rotate(');
  });

  it('les marqueurs de départ et d\'arrivée ne ressemblent à aucun état (drapeau bleu, damier sombre)', () => {
    for (const html of [TRACK_START_HTML, TRACK_END_HTML]) {
      for (const c of STATE_COLORS) expect(html).not.toContain(c);
    }
    expect(TRACK_START_HTML).toContain('track-start');
    expect(TRACK_START_HTML).toContain('#2563eb');
    expect(TRACK_END_HTML).toContain('track-end');
    expect(TRACK_END_HTML).toContain('#111827');
  });

  describe('findStops', () => {
    it('arrêt contact coupé = « à l\'arrêt » (rouge), moteur tournant tout du long = « au ralenti » (orange)', () => {
      const points = [
        pt(0, 50, true),
        pt(1, 0, true), pt(2, 0, false), pt(8, 0, false),   // garé contact coupé 9 min
        pt(10, 40, true),
        pt(11, 0, true), pt(14, 1, true),                   // feu / attente moteur tournant 4 min
        pt(15, 30, true)
      ];
      expect(findStops(points)).toEqual([
        { index: 1, durationMin: 9, state: 'parked' },
        { index: 5, durationMin: 4, state: 'idling' }
      ]);
    });

    it('même seuil que le rapport d\'activité : 3 km/h ne roule pas encore ; moins de 2 min n\'est pas un arrêt', () => {
      const points = [pt(0, 50, true), pt(1, 3, true), pt(4, 3, true), pt(5, 50, true), pt(6, 0, false), pt(7, 50, true)];
      expect(findStops(points)).toEqual([{ index: 1, durationMin: 4, state: 'idling' }]);
    });
  });
});

describe('PlaybackPage (pastille d\'état du point courant)', () => {
  let fixture: ComponentFixture<PlaybackPage>;
  let page: PlaybackPage;

  const rgb = (hex: string) => `rgb(${[1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)).join(', ')})`;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [PlaybackPage],
      imports: [FormsModule],
      providers: [
        { provide: ApiService, useValue: { getVehicles: () => of([]) } },
        { provide: ActivatedRoute, useValue: { queryParams: of({}) } },
        { provide: ActionSheetController, useValue: { create: async () => ({ present: async () => {} }) } }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(PlaybackPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('affiche couleur, icône et libellé de l\'état du point courant, texte lisible', () => {
    page.points = [pt(0, 60, true), pt(1, 0, true), pt(2, 0, false)];
    const expected: [string, string, string, string][] = [
      ['moving', 'En route', '#10b981', 'navigate'],
      ['idling', 'Au ralenti', '#f59e0b', 'pause-circle'],
      ['parked', 'À l\'arrêt', '#ef4444', 'stop-circle']
    ];
    expected.forEach(([state, label, color, icon], i) => {
      page.currentIndex = i;
      fixture.detectChanges();
      const item = fixture.nativeElement.querySelector('.state-item') as HTMLElement;
      const chip = item.querySelector('.state-chip') as HTMLElement;
      expect(item.getAttribute('data-state')).toBe(state);
      expect(chip.textContent!.trim()).toBe(label);
      expect(chip.style.backgroundColor).toBe(rgb(color));
      expect(chip.style.color).toBe(rgb('#111827'));
      expect((chip.querySelector('ion-icon') as any).name).toBe(icon);
    });
  });

  it('le contact reste affiché à côté de l\'état (véhicule remorqué contact coupé), sans vert ni rouge', () => {
    // 20 km/h contact coupé : « En route » — seul l'éclair dit que le contact est OFF.
    page.points = [pt(0, 20, false), pt(1, 60, true)];
    const contact = () => fixture.nativeElement.querySelector('.contact-item') as HTMLElement;

    page.currentIndex = 0;
    fixture.detectChanges();
    expect((fixture.nativeElement.querySelector('.state-item') as HTMLElement).getAttribute('data-state')).toBe('moving');
    expect(contact().getAttribute('data-ignition')).toBe('off');
    expect((contact().querySelector('ion-icon') as any).name).toBe('flash-off');
    expect((contact().querySelector('ion-icon') as any).color).toBe('medium');
    expect(contact().querySelector('.info-val')!.textContent!.trim()).toBe('Contact OFF');

    page.currentIndex = 1;
    fixture.detectChanges();
    expect(contact().getAttribute('data-ignition')).toBe('on');
    expect((contact().querySelector('ion-icon') as any).name).toBe('flash');
    expect((contact().querySelector('ion-icon') as any).color).toBe('primary');
    expect(contact().querySelector('.info-val')!.textContent!.trim()).toBe('Contact ON');
  });
});
