/**
 * Règle de Karim du 23/09/2026 : dans « Nouvelle dépense », on retire carburant,
 * entretien, maintenance, réparation, assurance, visite technique et vignette,
 * « parce qu'on peut les ajouter chacun dans l'écran qui correspond » (Carburant,
 * Entretiens, Réparations, Échéances). Le filtre de la liste garde tout : les
 * dépenses déjà enregistrées, et celles que ces écrans reportent ici, restent
 * visibles. Test de source : le gabarit est le seul endroit où la liste existe.
 */
declare const require: (m: string) => any;
declare const __dirname: string;
const { readFileSync } = require('fs');
const { join } = require('path');

describe('Dépenses — catégories de « Nouvelle dépense »', () => {
  const html: string = readFileSync(join(__dirname, 'expenses.component.html'), 'utf8');

  /** Les valeurs d'option d'un <select> repéré par son ngModel. */
  const options = (ngModel: string): string[] => {
    const debut = html.indexOf(`[(ngModel)]="${ngModel}"`);
    expect(debut).toBeGreaterThan(-1);
    const bloc = html.slice(debut, html.indexOf('</select>', debut));
    return [...bloc.matchAll(/<option value="([^"]*)"/g)].map(m => m[1]).filter(Boolean);
  };

  const RETIREES = ['carburant', 'entretien', 'maintenance', 'reparation', 'insurance', 'technical_inspection', 'tax'];

  it('ne propose plus les natures qui ont leur propre écran', () => {
    const formulaire = options('selectedCategory');
    for (const v of RETIREES) expect(formulaire).not.toContain(v);
    expect(formulaire).toEqual(['registration', 'transport_permit', 'peage', 'stationnement', 'amende', 'credit_note', 'autre']);
  });

  it('le filtre de la liste, lui, garde ces catégories pour les dépenses existantes', () => {
    const filtre = options('filterCategory');
    for (const v of ['carburant', 'entretien', 'reparation', 'insurance', 'technical_inspection', 'tax']) {
      expect(filtre).toContain(v);
    }
  });

  it('plus aucun sous-formulaire dédié dans le gabarit', () => {
    for (const v of ['carburant', 'entretien', 'reparation', 'insurance']) {
      expect(html).not.toContain(`*ngIf="selectedCategory === '${v}'"`);
    }
    expect(html).toContain('*ngIf="selectedCategory"');
  });
});
