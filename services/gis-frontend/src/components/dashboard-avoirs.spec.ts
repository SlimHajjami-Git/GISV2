import { partDuTotal } from './dashboard.component';

/**
 * Règle du 18/09/2026 : au tableau de bord, le poste « Réparations » est NET des
 * avoirs et remboursements — il peut donc être négatif quand le mois rend plus
 * qu'il ne répare. La carte affichait alors « −100,00 € » et, juste à côté,
 * « 0 % » : le montant disait la vérité, le pourcentage non, et la colonne ne
 * faisait plus 100.
 */
describe('partDuTotal (cartes de coûts du tableau de bord)', () => {
  it('un poste négatif n’a pas de part : « — » plutôt qu’un 0 % faux', () => {
    expect(partDuTotal(-100, 500)).toBeNull();
  });

  it('un poste nul garde sa part à 0 %', () => {
    expect(partDuTotal(0, 500)).toBe(0);
  });

  it('les parts positives font 100 sur l’exemple de la règle', () => {
    // Carburant 200, Entretiens 0, Réparations nettes 50, Autres 250, total 500.
    const parts = [200, 0, 50, 250].map(v => partDuTotal(v, 500)!);

    expect(parts.reduce((s, p) => s + p, 0)).toBeCloseTo(100, 6);
    expect(parts[2]).toBeCloseTo(10, 6);
  });

  it('sans total, aucune part', () => {
    expect(partDuTotal(120, 0)).toBeNull();
    expect(partDuTotal(120, null)).toBeNull();
    expect(partDuTotal(120, -30)).toBeNull();
  });

  it('une part ne dépasse jamais 100 %', () => {
    expect(partDuTotal(800, 500)).toBe(100);
  });
});
