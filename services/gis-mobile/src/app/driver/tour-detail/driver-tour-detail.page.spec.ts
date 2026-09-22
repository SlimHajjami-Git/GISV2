import { NgZone } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject, Subject, of } from 'rxjs';
import { DriverTourDetailPage } from './driver-tour-detail.page';
import { DeclarationOutcome } from '../../core/services/driver-declarations.service';
import { DriverTourDetail, DriverWaypoint } from '../../core/models/driver-app.types';
import { FakeAuth, MemoryKvStore } from '../../core/testing/driver-test-doubles';

function wp(id: number, seq: number, type: string, over: Partial<DriverWaypoint> = {}): DriverWaypoint {
  return {
    id, sequenceOrder: seq, type, name: `E${id}`, address: null, latitude: 36.8, longitude: 10.1,
    estimatedArrivalTime: null, plannedPauseMinutes: null, isCompleted: false, waypointStatus: 'pending',
    actualArrivalTime: null, arrivalSource: null, driverArrivedAt: null, driverDepartedAt: null,
    ...over
  };
}

const departed = { isCompleted: true, waypointStatus: 'completed', driverDepartedAt: '2026-09-22T07:00:00Z' };

describe('DriverTourDetailPage (arrivée à destination en sautant une étape)', () => {
  let alerts: any[];
  /** Arguments de chaque appel à DriverDeclarationsService.declare. */
  let declared: any[][];
  /** Réponse de declare() pour chaque appel (fonction : peut rejeter). */
  let outcomes: (() => Promise<DeclarationOutcome>)[];

  const queuedOutcome = () => Promise.resolve<DeclarationOutcome>({ sent: false, queued: true });
  /** Laisse s'exécuter les promesses lancées par un bouton de dialogue (handler non attendu). */
  const settle = () => new Promise(r => setTimeout(r, 0));

  function tourWith(waypoints: DriverWaypoint[]): DriverTourDetail {
    return {
      id: 7, name: 'Tournée Sfax', description: null, status: 'in_progress', scheduledStartTime: '2026-09-22T07:00:00Z',
      scheduledEndTime: null, actualStartTime: '2026-09-22T07:00:00Z', actualEndTime: null, sentAt: null, openedAt: null,
      notes: null, vehicleName: null, vehiclePlate: null, vehicleHasGps: true, estimatedDistanceKm: null,
      estimatedDurationMinutes: null, estimatedRoutePolyline: null, tracking: true, waypoints
    };
  }

  function page(tour: DriverTourDetail): DriverTourDetailPage {
    const overlay = { present: async () => {}, dismiss: async () => {} };
    const declarations = {
      declare: (...args: any[]) => { declared.push(args); return (outcomes.shift() ?? queuedOutcome)(); },
      pendingFor: async () => [],
      replayed$: new Subject(), dropped$: new Subject(), refused$: new BehaviorSubject([]),
      replay: async () => {}, acknowledgeRefusals: async () => {}
    };
    const p = new DriverTourDetailPage(
      { snapshot: { paramMap: { get: () => String(tour.id) } } } as any,
      { getDriverTour: () => of(tour), markDriverTourOpened: () => of(null) } as any,
      new MemoryKvStore() as any,
      declarations as any,
      { state$: new BehaviorSubject(null), sensorError$: new BehaviorSubject(null), isActive: true } as any,
      { hasAccepted: async () => true, ensure: async () => true } as any,
      { tourPush$: new Subject() } as any,
      { is: () => true } as any,
      { create: async (opts: any) => { alerts.push(opts); return overlay; } } as any,
      { create: async () => overlay } as any,
      { create: async () => overlay } as any,
      { create: async () => overlay } as any,
      TestBed.inject(NgZone),
      new FakeAuth() as any
    );
    p.tourId = tour.id;
    p.tour = tour;
    return p;
  }

  beforeEach(() => {
    alerts = [];
    declared = [];
    outcomes = [];
  });

  it('étape encore attendue : la confirmation s\'ouvre AVANT tout envoi, puis l\'arrivée part avec confirmSkipPending', async () => {
    const tour = tourWith([wp(1, 0, 'origin', departed), wp(2, 1, 'stop', { name: 'Client A' }), wp(3, 2, 'destination')]);
    const p = page(tour);

    await p.arriveAhead(tour.waypoints[2]);

    expect(declared.length).toBe(0);         // rien d'envoyé ni mis en file sans le consentement
    expect(alerts.length).toBe(1);
    expect(alerts[0].header).toBe('Étapes non signalées');
    expect(alerts[0].message).toContain('non visitées');
    expect(alerts[0].message).toContain('• Client A');
    expect(alerts[0].buttons[0].role).toBe('cancel');

    // Hors ligne : la déclaration part en file AVEC le consentement (rejouée sans PENDING_STOPS).
    alerts[0].buttons[1].handler();
    await settle();
    expect(declared.length).toBe(1);
    expect(declared[0]).toEqual([7, 3, 'arrive', jasmine.objectContaining({
      closesTour: true, confirmSkipPending: true, withPosition: true
    })]);
    expect(p.tour!.status).toBe('completed');   // l'écran avance, la tournée est close localement
  });

  it('seules des étapes « en retard » la précèdent : pas de dialogue, l\'arrivée part sans confirmSkipPending', async () => {
    const tour = tourWith([
      wp(1, 0, 'origin', departed), wp(2, 1, 'stop', { waypointStatus: 'temps_depasse' }), wp(3, 2, 'destination')
    ]);
    const p = page(tour);

    await p.arriveAhead(tour.waypoints[2]);

    expect(alerts.length).toBe(0);
    expect(declared.length).toBe(1);
    expect(declared[0][3].confirmSkipPending).toBeFalse();
    expect(declared[0][3].closesTour).toBeTrue();
  });

  it('PENDING_STOPS reçu en ligne (fiche périmée) : même dialogue, avec les étapes du serveur', async () => {
    const tour = tourWith([wp(1, 0, 'origin', departed), wp(3, 1, 'destination')]);
    outcomes.push(() => Promise.reject({
      status: 409, error: { code: 'PENDING_STOPS', pending: [{ id: 2, name: 'Client B' }], message: 'Des étapes n\'ont pas été signalées.' }
    }));
    const p = page(tour);

    await p.arrive(tour.waypoints[1]);
    expect(alerts.length).toBe(1);
    expect(alerts[0].header).toBe('Étapes non signalées');
    expect(alerts[0].message).toContain('• Client B');

    alerts[0].buttons[1].handler();
    await settle();
    expect(declared.length).toBe(2);
    expect(declared[1][3].confirmSkipPending).toBeTrue();
  });
});
