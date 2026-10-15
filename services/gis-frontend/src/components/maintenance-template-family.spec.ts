import { entretiensDuMemeType, familleEntretien } from './maintenance-template-family';

/**
 * Deux modèles de vidange sur un même véhicule (campagne GPA du 06/10/2026, erreur n°17).
 * Mêmes cas que le serveur (EntretiensDuMemeTypeTests.cs), noms réels de production.
 */
describe('Entretiens du même type', () => {
  it.each([
    ['Vidange + filtre à huile', 'Vidange Moteur'],
    ['VIDANGE MOTEUR HUILE + FIL HUILE', 'Vidange moteur 10000'],
    ['Changement pneus', 'CHANGMENT PNEUS'],
    ['Chaine de distribution', 'Changement Kit Chaîne Distribution '],
    ['Bougie', "BOUGIE D'ALLUMAGE"],
    ['Filtres air + habitacle', 'FILTRE AIR'],
  ])('« %s » et « %s » sont du même type', (a, b) => {
    expect(familleEntretien(a)).not.toBe('');
    expect(familleEntretien(a)).toBe(familleEntretien(b));
  });

  it.each([
    ['Bougie', 'Vidange moteur 10000'],
    ['Huile boite de vitesse', 'Vidange Moteur'],
    ['FILTRE AIR', 'FILTRE CLIM'],
    ['FILTRE AIR', 'Filtre à Essence'],
    ['Changement pneus', 'Changement Kit Chaîne Distribution'],
  ])('« %s » et « %s » sont de types différents', (a, b) => {
    expect(familleEntretien(a)).not.toBe(familleEntretien(b));
  });

  it('un nom sans mot ne donne pas de type', () => {
    expect(familleEntretien('46564546')).toBe('');
  });

  it('annonce la vidange déjà affectée au véhicule', () => {
    expect(entretiensDuMemeType(['Vidange + filtre à huile', 'Révision périodique'], ['Vidange Moteur']))
      .toEqual(['« Vidange Moteur » : ce véhicule a déjà « Vidange + filtre à huile », du même type.']);
  });

  it('annonce deux vidanges choisies ensemble', () => {
    expect(entretiensDuMemeType([], ['Vidange Moteur', 'Bougie', 'Vidange moteur 10000']))
      .toEqual(['« Vidange Moteur » et « Vidange moteur 10000 » sont du même type.']);
  });

  it('ne dit rien pour des entretiens de types différents', () => {
    expect(entretiensDuMemeType(['Vidange Moteur'], ['Bougie', 'FILTRE AIR'])).toEqual([]);
  });
});
