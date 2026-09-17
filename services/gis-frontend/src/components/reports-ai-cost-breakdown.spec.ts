// marked n'est livré qu'en ESM, que jest ne transforme pas dans node_modules : le charger
// avec le composant fait échouer la suite avant le premier test. Le calcul n'en dépend pas.
jest.mock('marked', () => ({ marked: { parse: (s: string) => s } }));

import { buildAiCostBreakdown } from './reports.component';

// M9-AFFICHAGES : la barre « Répartition des coûts » du rapport IA se mesurait sur
// fleetSummary.totalCosts, NET des avoirs et remboursements : ses parts dépassaient 100 %
// dès que les crédits dépassaient les autres frais, et la déduction n'apparaissait pas.
describe('buildAiCostBreakdown (rapport de flotte IA)', () => {
  const sumPct = (items: { pct: number }[]) => items.reduce((s, i) => s + i.pct, 0);

  it('mesure la barre sur les parts positives quand les crédits dépassent les autres frais', () => {
    const view = buildAiCostBreakdown({ fuel: 1000, maintenance: 500, repairs: 0, other: 0, credits: 2000 });

    expect(sumPct(view.items)).toBeCloseTo(100, 6);
    expect(view.items.find(i => i.label === 'Carburant')!.pct).toBeCloseTo(66.667, 2);
    expect(view.credits).toBe(2000);
  });

  it('prend credits absent (API antérieure) pour 0', () => {
    const view = buildAiCostBreakdown({ fuel: 300, maintenance: 100, repairs: 100, other: 0 });

    expect(view.credits).toBe(0);
    expect(sumPct(view.items)).toBeCloseTo(100, 6);
  });

  it('ne donne aucune largeur à une part négative et ne dépasse jamais 100 %', () => {
    const view = buildAiCostBreakdown({ fuel: 400, maintenance: 100, repairs: 0, other: -250, credits: -10 });

    expect(view.items.find(i => i.label === 'Autres')!.pct).toBe(0);
    expect(view.items.find(i => i.label === 'Autres')!.value).toBe(-250);
    expect(sumPct(view.items)).toBeCloseTo(100, 6);
    expect(view.credits).toBe(0);
  });

  it('ne plante pas sans costBreakdown', () => {
    const view = buildAiCostBreakdown(undefined);

    expect(view.items).toHaveLength(4);
    expect(sumPct(view.items)).toBe(0);
    expect(view.credits).toBe(0);
  });
});
