import { BatteryHistoryDto } from '../../services/api.service';

/**
 * Géométrie du graphe « tension batterie » de la fenêtre d'alerte (Slim, 29/09/2026).
 *
 * Le calcul est séparé du composant pour être testable : c'est lui qui décide ce que
 * l'exploitant voit, et une échelle mal choisie peut faire passer une batterie mourante
 * pour une courbe plate. Relevé sur 262 TU 9816 en fin de vie : 11,1 → 13,6 V en
 * roulage, puis 7,0 → 7,7 V à l'arrêt le lendemain. C'est cet écart qu'il faut rendre
 * évident — d'où une échelle Y cadrée sur les données, jamais figée à 0-15 V où tout
 * s'écraserait au milieu.
 */

/** Cadre de tracé, en unités du viewBox. */
export const CHART = { width: 720, height: 300, left: 52, right: 704, top: 24, bottom: 236 };

export interface ChartGeometry {
  /** Ligne du CREUX de chaque tranche — le plancher de la batterie, ce qui compte. */
  minPath: string;
  /** Aire entre creux et sommet : l'écart alternateur / repos, d'un coup d'œil. */
  bandPath: string;
  /** Position du seuil d'alerte, ou null s'il sort de l'échelle affichée. */
  thresholdY: number | null;
  /** Médiane des derniers démarrages, la valeur qui a déclenché l'alerte. */
  medianY: number | null;
  yTicks: { y: number; label: string }[];
  xTicks: { x: number; label: string }[];
  /** Un point par démarrage, à poser sur la courbe. */
  starts: { x: number; y: number; low: boolean; label: string; volts: number }[];
  /** Points de survol : une entrée par tranche. */
  hover: { x: number; minY: number; maxY: number; minV: number; maxV: number; label: string }[];
}

/**
 * Prépare tout le tracé, ou <c>null</c> si la période ne contient aucune mesure —
 * l'écran affiche alors un message plutôt qu'un cadre vide.
 */
export function buildBatteryChart(data: BatteryHistoryDto | null | undefined): ChartGeometry | null {
  const points = (data?.points ?? []).filter(p => Number.isFinite(p.minV) && Number.isFinite(p.maxV));
  if (!data || points.length === 0) return null;

  const times = points.map(p => Date.parse(p.atUtc)).filter(t => !Number.isNaN(t));
  if (times.length === 0) return null;

  const t0 = Math.min(...times);
  const t1 = Math.max(...times);

  // Une tranche est étiquetée par son DÉBUT : la dernière couvre encore une largeur de
  // tranche après son horodatage. Sans cette extension, un démarrage survenu dans cette
  // dernière tranche — donc après l'étiquette — sortait du graphe et disparaissait,
  // typiquement le démarrage le plus récent, celui qu'on veut le plus voir.
  const gaps = times.slice(1).map((t, i) => t - times[i]).filter(g => g > 0);
  const step = gaps.length ? Math.min(...gaps) : 0;
  const tEnd = t1 + step;

  // Échelle Y cadrée sur les données ET sur le seuil : un véhicule entièrement sous le
  // seuil doit quand même montrer la ligne rouge, sinon on ne comprend pas l'alerte.
  const lows = points.map(p => p.minV);
  const highs = points.map(p => p.maxV);
  const candidates = [...lows, ...highs, data.thresholdV];
  if (data.medianV != null) candidates.push(data.medianV);

  let lo = Math.floor((Math.min(...candidates) - 0.4) * 2) / 2;
  let hi = Math.ceil((Math.max(...candidates) + 0.4) * 2) / 2;
  if (hi - lo < 1) { hi = lo + 1; }   // une courbe parfaitement plate reste lisible

  const x = (t: number) => tEnd === t0
    ? (CHART.left + CHART.right) / 2
    : CHART.left + ((t - t0) / (tEnd - t0)) * (CHART.right - CHART.left);
  const y = (v: number) =>
    CHART.bottom - ((v - lo) / (hi - lo)) * (CHART.bottom - CHART.top);

  const xs = points.map((p, i) => x(times[i]));

  const minPath = points
    .map((p, i) => `${i === 0 ? 'M' : 'L'}${xs[i].toFixed(1)},${y(p.minV).toFixed(1)}`)
    .join(' ');

  // L'aire se ferme en revenant par les sommets, à l'envers.
  const upper = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${xs[i].toFixed(1)},${y(p.maxV).toFixed(1)}`).join(' ');
  const backDown = points
    .map((p, i) => ({ i, p }))
    .reverse()
    .map(({ i, p }) => `L${xs[i].toFixed(1)},${y(p.minV).toFixed(1)}`)
    .join(' ');
  const bandPath = `${upper} ${backDown} Z`;

  const spanMs = tEnd - t0;
  const withHour = spanMs <= 2 * 24 * 3600 * 1000;

  return {
    minPath,
    bandPath,
    thresholdY: inScale(data.thresholdV, lo, hi) ? y(data.thresholdV) : null,
    medianY: data.medianV != null && inScale(data.medianV, lo, hi) ? y(data.medianV) : null,
    yTicks: buildYTicks(lo, hi, y),
    xTicks: buildXTicks(t0, tEnd, x, withHour),
    starts: (data.starts ?? [])
      .map(s => ({ s, t: Date.parse(s.atUtc) }))
      .filter(({ t }) => !Number.isNaN(t) && t >= t0 && t <= tEnd)
      .map(({ s, t }) => ({
        x: x(t),
        y: y(clamp(s.voltsV, lo, hi)),
        low: s.low,
        volts: s.voltsV,
        label: formatMoment(t, true)
      })),
    hover: points.map((p, i) => ({
      x: xs[i],
      minY: y(p.minV),
      maxY: y(p.maxV),
      minV: p.minV,
      maxV: p.maxV,
      label: formatMoment(times[i], withHour)
    }))
  };
}

/** Tranche la plus proche d'une abscisse — pour le survol. */
export function nearestIndex(geometry: ChartGeometry | null, x: number): number {
  if (!geometry || geometry.hover.length === 0) return -1;
  let best = 0;
  let bestDist = Infinity;
  geometry.hover.forEach((h, i) => {
    const d = Math.abs(h.x - x);
    if (d < bestDist) { bestDist = d; best = i; }
  });
  return best;
}

function inScale(v: number, lo: number, hi: number): boolean {
  return v >= lo && v <= hi;
}

function clamp(v: number, lo: number, hi: number): number {
  return Math.min(Math.max(v, lo), hi);
}

function buildYTicks(lo: number, hi: number, y: (v: number) => number) {
  const ticks: { y: number; label: string }[] = [];
  const steps = 4;
  for (let i = 0; i <= steps; i++) {
    const v = lo + ((hi - lo) * i) / steps;
    ticks.push({ y: y(v), label: `${v.toFixed(1)} V` });
  }
  return ticks;
}

function buildXTicks(t0: number, t1: number, x: (t: number) => number, withHour: boolean) {
  const ticks: { x: number; label: string }[] = [];
  const steps = 5;
  for (let i = 0; i <= steps; i++) {
    const t = t0 + ((t1 - t0) * i) / steps;
    ticks.push({ x: x(t), label: formatMoment(t, withHour) });
  }
  return ticks;
}

function formatMoment(ms: number, withHour: boolean): string {
  const d = new Date(ms);
  const jour = d.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' });
  if (!withHour) return jour;
  return `${jour} ${d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`;
}
