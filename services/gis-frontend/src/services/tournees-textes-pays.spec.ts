import { textesTournees, TEXTES_TOURNEES_FRANCE, TEXTES_TOURNEES_TUNISIE } from './tournees-textes-pays';

/** Exemple de nom de tournee selon le pays du client (23/09/2026). */
describe('textesTournees', () => {
  it('un compte en euros garde l’exemple francais d’origine', () => {
    expect(textesTournees('EUR')).toBe(TEXTES_TOURNEES_FRANCE);
    expect(textesTournees('EUR').exempleNom).toBe('Ex: Livraison Lyon - Marseille');
  });

  it('tout autre compte voit un exemple tunisien — le GPS vise le marche tunisien', () => {
    for (const devise of ['TND', 'MAD', 'DZD', '', null, undefined]) {
      expect(textesTournees(devise)).toBe(TEXTES_TOURNEES_TUNISIE);
    }
    expect(TEXTES_TOURNEES_TUNISIE.exempleNom).toBe('Ex: Livraison Tunis - Sfax');
  });
});
