import { FENETRE_REDETECTION_MINUTES, peutEtreRedetecte } from './accident-redetection';

/**
 * Revue de l'intégration du 18/09/2026 : la liste des sinistres avertissait qu'un dossier
 * détecté récent serait recréé par la détection, la fiche non — alors que c'est depuis la
 * fiche (lien de la notification) qu'on écarte une fausse alerte. Règle unique, alignée
 * sur le serveur (DeleteAccidentEventCommandHandler.PeutEtreRedetecte, 409).
 */
describe('peutEtreRedetecte', () => {
  const now = Date.UTC(2026, 8, 18, 14, 0, 0);
  const ilYa = (minutes: number) => new Date(now - minutes * 60000).toISOString();

  it('dossier détecté il y a moins de 30 minutes : redétectable', () => {
    expect(peutEtreRedetecte('auto', ilYa(12), now)).toBe(true);
    expect(peutEtreRedetecte('auto', ilYa(FENETRE_REDETECTION_MINUTES - 1), now)).toBe(true);
  });

  it('au-delà de la fenêtre de scan : plus de redétection', () => {
    expect(peutEtreRedetecte('auto', ilYa(FENETRE_REDETECTION_MINUTES), now)).toBe(false);
    expect(peutEtreRedetecte('auto', ilYa(45), now)).toBe(false);
  });

  it('dossier manuel, date absente ou illisible : jamais', () => {
    expect(peutEtreRedetecte('manual', ilYa(5), now)).toBe(false);
    expect(peutEtreRedetecte('auto', null, now)).toBe(false);
    expect(peutEtreRedetecte('auto', 'pas une date', now)).toBe(false);
  });
});
