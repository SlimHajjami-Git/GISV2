import { BatteryHistoryDto } from '../../services/api.service';
import { CHART, buildBatteryChart, nearestIndex } from './battery-history-chart.helpers';

/**
 * Géométrie du graphe « santé de la batterie » (Slim, 29/09/2026). Ce que ces cas
 * protègent : une échelle qui écraserait la chute, un seuil invisible, ou un graphe
 * tracé sur des données absentes.
 */
describe('buildBatteryChart', () => {
  const base = (points: { atUtc: string; minV: number; maxV: number }[],
                extra: Partial<BatteryHistoryDto> = {}): BatteryHistoryDto => ({
    vehicleId: 1,
    plate: '262 TU 9816',
    supported: true,
    days: 3,
    thresholdV: 11.5,
    medianV: null,
    points,
    starts: [],
    ...extra
  });

  // Relevé réel sur 262 TU 9816, batterie en fin de vie.
  const reel = [
    { atUtc: '2026-09-27T10:00:00Z', minV: 11.1, maxV: 13.6 },
    { atUtc: '2026-09-28T00:00:00Z', minV: 12.0, maxV: 12.0 },
    { atUtc: '2026-09-28T08:00:00Z', minV: 7.0, maxV: 7.7 },
    { atUtc: '2026-09-28T16:00:00Z', minV: 10.2, maxV: 14.1 }
  ];

  it('rend null sans aucune mesure — l’écran doit le dire, pas tracer un cadre vide', () => {
    expect(buildBatteryChart(base([]))).toBeNull();
    expect(buildBatteryChart(null)).toBeNull();
    expect(buildBatteryChart(undefined)).toBeNull();
  });

  it('ignore les mesures non numériques plutôt que de produire un chemin cassé', () => {
    const g = buildBatteryChart(base([
      { atUtc: '2026-09-28T00:00:00Z', minV: NaN, maxV: 12 },
      ...reel
    ]));
    expect(g!.hover.length).toBe(reel.length);
    expect(g!.minPath).not.toContain('NaN');
    expect(g!.bandPath).not.toContain('NaN');
  });

  it('cadre l’échelle sur les données : la chute à 7 V occupe la hauteur du graphe', () => {
    const g = buildBatteryChart(base(reel))!;

    const creux = g.hover.find(h => h.minV === 7.0)!;
    const haut = g.hover.find(h => h.maxV === 14.1)!;

    // Le creux doit être bien plus bas que le sommet — en SVG, y augmente vers le bas.
    expect(creux.minY).toBeGreaterThan(haut.maxY);
    expect(creux.minY - haut.maxY).toBeGreaterThan((CHART.bottom - CHART.top) * 0.6);
  });

  it('garde le tracé dans le cadre', () => {
    const g = buildBatteryChart(base(reel))!;
    g.hover.forEach(h => {
      expect(h.x).toBeGreaterThanOrEqual(CHART.left);
      expect(h.x).toBeLessThanOrEqual(CHART.right);
      expect(h.maxY).toBeGreaterThanOrEqual(CHART.top);
      expect(h.minY).toBeLessThanOrEqual(CHART.bottom);
    });
  });

  it('montre le seuil même quand tout le véhicule est en dessous', () => {
    // Sinon l'exploitant voit une courbe plate et ne comprend pas l'alerte.
    const g = buildBatteryChart(base([
      { atUtc: '2026-09-28T00:00:00Z', minV: 8.1, maxV: 8.3 },
      { atUtc: '2026-09-28T12:00:00Z', minV: 7.8, maxV: 8.1 }
    ]))!;

    expect(g.thresholdY).not.toBeNull();
    expect(g.thresholdY!).toBeGreaterThanOrEqual(CHART.top);
    expect(g.thresholdY!).toBeLessThanOrEqual(CHART.bottom);
  });

  it('reste lisible sur une courbe parfaitement plate', () => {
    const g = buildBatteryChart(base([
      { atUtc: '2026-09-28T00:00:00Z', minV: 12.5, maxV: 12.5 },
      { atUtc: '2026-09-28T12:00:00Z', minV: 12.5, maxV: 12.5 }
    ]))!;

    expect(g.minPath).not.toContain('NaN');
    expect(g.yTicks.length).toBeGreaterThan(1);
    expect(g.yTicks[0].label).not.toBe(g.yTicks[g.yTicks.length - 1].label);
  });

  it('pose les démarrages sur la courbe et marque ceux qui sont sous le seuil', () => {
    const g = buildBatteryChart(base(reel, {
      starts: [
        { atUtc: '2026-09-28T08:05:00Z', voltsV: 10.9, low: true },
        { atUtc: '2026-09-28T16:05:00Z', voltsV: 12.4, low: false }
      ]
    }))!;

    expect(g.starts.length).toBe(2);
    expect(g.starts[0].low).toBe(true);
    expect(g.starts[1].low).toBe(false);
    expect(g.starts[0].label).toContain('28/09');
  });

  it('écarte un démarrage hors de la période tracée', () => {
    const g = buildBatteryChart(base(reel, {
      starts: [{ atUtc: '2026-08-01T08:00:00Z', voltsV: 10.9, low: true }]
    }))!;

    expect(g.starts.length).toBe(0);
  });

  it('trace la médiane quand elle est connue, et rien sinon', () => {
    expect(buildBatteryChart(base(reel))!.medianY).toBeNull();
    expect(buildBatteryChart(base(reel, { medianV: 11.3 }))!.medianY).not.toBeNull();
  });

  it('date les tranches à l’heure sur une fenêtre courte, au jour sur une longue', () => {
    const court = buildBatteryChart(base([
      { atUtc: '2026-09-28T00:00:00Z', minV: 12, maxV: 12.5 },
      { atUtc: '2026-09-28T12:00:00Z', minV: 11, maxV: 13 }
    ]))!;
    expect(court.xTicks[0].label).toMatch(/\d{2}\/\d{2} \d{2}:\d{2}/);

    const long = buildBatteryChart(base([
      { atUtc: '2026-09-20T00:00:00Z', minV: 12, maxV: 12.5 },
      { atUtc: '2026-09-28T00:00:00Z', minV: 11, maxV: 13 }
    ]))!;
    expect(long.xTicks[0].label).toMatch(/^\d{2}\/\d{2}$/);
  });
});

describe('nearestIndex', () => {
  const g = buildBatteryChart({
    vehicleId: 1, plate: 'X', supported: true, days: 3, thresholdV: 11.5, medianV: null,
    points: [
      { atUtc: '2026-09-28T00:00:00Z', minV: 12, maxV: 12.5 },
      { atUtc: '2026-09-28T12:00:00Z', minV: 11, maxV: 13 },
      { atUtc: '2026-09-29T00:00:00Z', minV: 10, maxV: 13.5 }
    ],
    starts: []
  })!;

  it('trouve la tranche la plus proche du curseur', () => {
    expect(nearestIndex(g, CHART.left)).toBe(0);
    expect(nearestIndex(g, CHART.right)).toBe(2);
    // Sur le point lui-même, et un peu à côté : pas de test à mi-chemin exact entre
    // deux tranches, où le résultat dépend du dernier bit de l'arrondi flottant.
    expect(nearestIndex(g, g.hover[1].x)).toBe(1);
    expect(nearestIndex(g, g.hover[1].x + 5)).toBe(1);
  });

  it('ne plante pas sans géométrie', () => {
    expect(nearestIndex(null, 100)).toBe(-1);
  });
});
