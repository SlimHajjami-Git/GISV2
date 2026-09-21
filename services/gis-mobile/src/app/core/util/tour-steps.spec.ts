import { stepActionFor } from './tour-steps';
import { DriverWaypoint } from '../models/driver-app.types';

function wp(id: number, seq: number, type: string, over: Partial<DriverWaypoint> = {}): DriverWaypoint {
  return {
    id, sequenceOrder: seq, type, name: `E${id}`, address: null, latitude: 36.8, longitude: 10.1,
    estimatedArrivalTime: null, plannedPauseMinutes: null, isCompleted: false, waypointStatus: 'pending',
    actualArrivalTime: null, arrivalSource: null, driverArrivedAt: null, driverDepartedAt: null,
    ...over
  };
}

const reached = { isCompleted: true, waypointStatus: 'completed' };

describe('stepActionFor (boutons de la fiche chauffeur)', () => {
  it('tournée planifiée : « Je pars » sur l\'origine seulement', () => {
    const wps = [wp(1, 0, 'origin'), wp(2, 1, 'stop'), wp(3, 2, 'destination')];
    const tour = { status: 'planned', waypoints: wps };
    expect(wps.map(w => stepActionFor(tour, w))).toEqual(['depart_origin', null, null]);
  });

  it('origine trouvée par son type même si elle n\'est pas la première dans la liste', () => {
    const wps = [wp(2, 1, 'stop'), wp(1, 0, 'origin'), wp(3, 2, 'destination')];
    expect(stepActionFor({ status: 'planned', waypoints: wps }, wps[1])).toBe('depart_origin');
  });

  it('en cours, parti de l\'origine : « Je suis arrivé » sur la prochaine étape, rien ailleurs', () => {
    const wps = [
      wp(1, 0, 'origin', { ...reached, driverDepartedAt: '2026-09-21T07:00:00Z' }),
      wp(2, 1, 'stop'),
      wp(3, 2, 'destination')
    ];
    const tour = { status: 'in_progress', waypoints: wps };
    expect(wps.map(w => stepActionFor(tour, w))).toEqual([null, 'arrive', null]);
  });

  it('en cours, arrivé à une étape : « Je repars » dessus ET « Je suis arrivé » sur la suivante', () => {
    const wps = [
      wp(1, 0, 'origin', { ...reached, driverDepartedAt: '2026-09-21T07:00:00Z' }),
      wp(2, 1, 'stop', reached),
      wp(3, 2, 'destination')
    ];
    const tour = { status: 'in_progress', waypoints: wps };
    expect(wps.map(w => stepActionFor(tour, w))).toEqual([null, 'redepart', 'arrive']);
  });

  it('« Je repars » disparaît une fois le départ de l\'étape déclaré', () => {
    const wps = [
      wp(1, 0, 'origin', { ...reached, driverDepartedAt: '2026-09-21T07:00:00Z' }),
      wp(2, 1, 'stop', { ...reached, driverDepartedAt: '2026-09-21T07:30:00Z' }),
      wp(3, 2, 'destination')
    ];
    expect(stepActionFor({ status: 'in_progress', waypoints: wps }, wps[1])).toBeNull();
  });

  it('tournée démarrée par le gestionnaire : « Je repars » proposé sur l\'origine', () => {
    const wps = [wp(1, 0, 'origin', reached), wp(2, 1, 'stop'), wp(3, 2, 'destination')];
    const tour = { status: 'in_progress', waypoints: wps };
    expect(wps.map(w => stepActionFor(tour, w))).toEqual(['redepart', 'arrive', null]);
  });

  it('une étape non visitée (skipped) est sautée : la suivante attendue reçoit « Je suis arrivé »', () => {
    const wps = [
      wp(1, 0, 'origin', { ...reached, driverDepartedAt: '2026-09-21T07:00:00Z' }),
      wp(2, 1, 'stop', { waypointStatus: 'skipped' }),
      wp(3, 2, 'destination')
    ];
    expect(stepActionFor({ status: 'in_progress', waypoints: wps }, wps[2])).toBe('arrive');
    expect(stepActionFor({ status: 'in_progress', waypoints: wps }, wps[1])).toBeNull();
  });

  it('jamais « Je repars » sur la destination', () => {
    const wps = [wp(1, 0, 'origin', reached), wp(3, 1, 'destination', reached)];
    expect(stepActionFor({ status: 'in_progress', waypoints: wps }, wps[1])).toBeNull();
  });

  it('tournée terminée ou annulée : aucun bouton', () => {
    const wps = [wp(1, 0, 'origin'), wp(2, 1, 'destination')];
    for (const status of ['completed', 'cancelled']) {
      expect(wps.map(w => stepActionFor({ status, waypoints: wps }, w))).toEqual([null, null]);
    }
  });

  it('sans étapes : aucun bouton', () => {
    expect(stepActionFor({ status: 'planned', waypoints: [] }, wp(1, 0, 'origin'))).toBeNull();
  });
});
