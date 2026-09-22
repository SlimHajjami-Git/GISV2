import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActionSheetController, LoadingController } from '@ionic/angular';
import { of } from 'rxjs';
import { ReportsPage } from './reports.page';
import { ApiService } from '../../core/services/api.service';

describe('ReportsPage (chronologie aux couleurs des états)', () => {
  let fixture: ComponentFixture<ReportsPage>;
  let page: ReportsPage;

  const rgb = (hex: string) => `rgb(${[1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16)).join(', ')})`;

  // Forme réelle de GET /api/reports/daily (DailyActivityReportDto) : `activities`, durées en secondes.
  const report = {
    vehicleId: 7, vehicleName: 'Camion Sfax', plate: '239 TU 6235', reportDate: '2026-09-22T00:00:00Z',
    activities: [
      { type: 'drive', startTime: '2026-09-22T07:00:00Z', durationSeconds: 1800, distanceKm: 23.4 },
      { type: 'stop', startTime: '2026-09-22T07:30:00Z', durationSeconds: 1500 },
      { type: 'idle', startTime: '2026-09-22T08:00:00Z', durationSeconds: 300 },
      { type: 'ignition_on', startTime: '2026-09-22T08:05:00Z' }
    ]
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      declarations: [ReportsPage],
      providers: [
        {
          provide: ApiService,
          useValue: { getVehicles: () => of([]), getDailyReports: () => of([report]) }
        },
        { provide: ActionSheetController, useValue: { create: async () => ({ present: async () => {} }) } },
        { provide: LoadingController, useValue: { create: async () => ({ present: async () => {}, dismiss: async () => {} }) } }
      ],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(ReportsPage);
    page = fixture.componentInstance;
    fixture.detectChanges();
  });

  afterEach(() => fixture.destroy());

  it('la chronologie lit `activities` : conduite vert, arrêt rouge, ralenti orange, autre événement en anneau neutre', () => {
    const events = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.timeline-event'));
    expect(events.map(e => e.getAttribute('data-kind'))).toEqual(['moving', 'parked', 'idling', 'default']);
    expect(events.map(e => (e.querySelector('.event-dot') as HTMLElement).style.backgroundColor))
      .toEqual([rgb('#10b981'), rgb('#ef4444'), rgb('#f59e0b'), '']);
    expect((events[3].querySelector('.event-dot') as HTMLElement).classList).toContain('default');
    // La couleur n'est jamais seule : chaque pastille a son libellé.
    expect(events.map(e => e.querySelector('.event-label')!.textContent!.trim()))
      .toEqual(['Conduite — 23.4 km', 'Arrêt — 25m', 'Ralenti — 5m', 'Contact ON']);
  });

  it('le ralenti est reconnu avant l\'arrêt, et un événement inconnu n\'emprunte aucune couleur d\'état', () => {
    expect(page.getEventClass({ type: 'idle_stop' })).toBe('idling');
    expect(page.getEventClass({ eventType: 'moving' })).toBe('moving');
    expect(page.eventColor({ type: 'ignition_off' })).toBeNull();
    expect(page.eventColor({ type: 'stop' })).toBe('#ef4444');
  });

  it('un arrêt moteur tournant (hasIgnitionOff === false) est orange « Ralenti », comme dans le Replay', () => {
    const waiting = { type: 'stop', startTime: '2026-09-22T09:00:00Z', durationSeconds: 1200, hasIgnitionOff: false };
    expect(page.getEventClass(waiting)).toBe('idling');
    expect(page.eventColor(waiting)).toBe('#f59e0b');
    expect(page.getEventLabel(waiting)).toBe('Ralenti — 20m');
    // Contact coupé pendant l'arrêt : rouge « Arrêt ».
    expect(page.getEventClass({ ...waiting, hasIgnitionOff: true })).toBe('parked');
    expect(page.getEventLabel({ ...waiting, hasIgnitionOff: true })).toBe('Arrêt — 20m');
    // L'API actuelle n'envoie pas le champ ([JsonIgnore]) : l'arrêt reste rouge.
    const withoutField: any = { ...waiting };
    delete withoutField.hasIgnitionOff;
    expect(page.getEventClass(withoutField)).toBe('parked');
    expect(page.getEventLabel(withoutField)).toBe('Arrêt — 20m');
  });

  it('les indicateurs conduite / arrêt portent la pastille de leur état', () => {
    const labels = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.report-card .kpi-label'))
      .filter(l => !!l.querySelector('.kpi-dot'));
    expect(labels.map(l => l.textContent!.trim())).toEqual(['conduite', 'arrêt']);
    expect(labels.map(l => (l.querySelector('.kpi-dot') as HTMLElement).style.backgroundColor))
      .toEqual([rgb('#10b981'), rgb('#ef4444')]);
  });
});
