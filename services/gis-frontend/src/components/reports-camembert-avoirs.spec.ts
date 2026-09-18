// marked n'est livré qu'en ESM, que jest ne transforme pas dans node_modules : le charger
// avec le composant fait échouer la suite avant le premier test. Le calcul n'en dépend pas.
jest.mock('marked', () => ({ marked: { parse: (s: string) => s } }));

import { partsCamembert, ReportsComponent } from './reports.component';

// Décision de Karim du 18/09/2026 : les avoirs et remboursements portent une ligne à
// part, en NÉGATIF. Chart.js dessine une valeur négative en valeur absolue : la part
// prendrait la place d'une dépense de plus, et les pourcentages des autres parts ne
// feraient plus 100.
describe('partsCamembert (camemberts de coûts)', () => {
  const sommePct = (parts: { percent: number }[]) => parts.reduce((s, p) => s + p.percent, 0);

  it('écarte la ligne des avoirs et laisse les parts restantes faire 100 %', () => {
    const parts = partsCamembert([
      { key: 'fuel', amount: 300 },
      { key: 'repair', amount: 300 },
      { key: 'other', amount: 600 },
      { key: 'credit', amount: -400 }
    ]);

    expect(parts.map(p => p.key)).toEqual(['fuel', 'repair', 'other']);
    expect(sommePct(parts)).toBeCloseTo(100, 6);
    expect(parts.find(p => p.key === 'other')!.percent).toBeCloseTo(50, 6);
  });

  it('écarte aussi un poste net négatif et une part à zéro', () => {
    const parts = partsCamembert([
      { key: 'fuel', amount: 200 },
      { key: 'repair', amount: -300 },
      { key: 'maintenance', amount: 0 }
    ]);

    expect(parts.map(p => p.key)).toEqual(['fuel']);
    expect(parts[0].percent).toBeCloseTo(100, 6);
  });

  it('ne rend aucune part quand il ne reste que des montants négatifs ou nuls', () => {
    expect(partsCamembert([{ key: 'credit', amount: -50 }, { key: 'fuel', amount: 0 }])).toEqual([]);
    expect(partsCamembert([])).toEqual([]);
  });

  it('garde les autres propriétés de chaque part', () => {
    const parts = partsCamembert([{ key: 'fuel', label: 'Carburant', color: '#3B82F6', amount: 80 }]);

    expect(parts[0]).toEqual({ key: 'fuel', label: 'Carburant', color: '#3B82F6', amount: 80, percent: 100 });
  });
});

/**
 * Légende du camembert « Répartition des coûts » du rapport mensuel flotte. Le
 * pourcentage rendu par le serveur rapporte chaque ligne au total NET : sur
 * l'exemple de la règle (réparation 300, remboursement 250, assurance 600, avoir
 * 150, total net 500) il annonçait « Assurance 600 (120,0 %) » au-dessus d'un
 * « Total (100 %) » écrit en dur, pour une part qui occupe les deux tiers de
 * l'anneau. Méthode appelée hors composant : elle ne lit que monthlyReport.
 */
describe('mfCategoriesLegende (rapport mensuel flotte)', () => {
  const legende = (byCategory: { category: string; amount: number; percentage: number }[]) =>
    (ReportsComponent.prototype as any).mfCategoriesLegende.call({ monthlyReport: { costAnalysis: { byCategory } } }) as
      { category: string; amount: number; percent: number | null }[];

  it("mesure les pourcentages sur les parts dessinées, qui font 100", () => {
    const lignes = legende([
      { category: 'Assurance', amount: 600, percentage: 120 },
      { category: 'Réparations', amount: 300, percentage: 60 },
      { category: 'Remboursement assurance', amount: -250, percentage: -50 },
      { category: 'Avoir fournisseur', amount: -150, percentage: -30 }
    ]);

    const dessinees = lignes.filter(l => l.percent !== null);
    expect(dessinees.map(l => l.category)).toEqual(['Assurance', 'Réparations']);
    expect(dessinees.reduce((s, l) => s + l.percent!, 0)).toBeCloseTo(100, 6);
    expect(dessinees[0].percent).toBeCloseTo(66.667, 2);
  });

  it('garde les lignes de crédit dans la légende, avec leur montant et sans part', () => {
    const lignes = legende([
      { category: 'Assurance', amount: 600, percentage: 120 },
      { category: 'Avoir fournisseur', amount: -150, percentage: -30 }
    ]);

    expect(lignes).toHaveLength(2);
    expect(lignes[1]).toEqual({ category: 'Avoir fournisseur', amount: -150, percent: null });
  });

  it('ne rend aucune part quand il n’y a pas de dépense', () => {
    expect(legende([])).toEqual([]);
    expect(legende([{ category: 'Avoir fournisseur', amount: -150, percentage: -100 }])[0].percent).toBeNull();
  });
});
