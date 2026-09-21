import {
  driverLinkErrorMessage, driverRecordLabel, driverRecordLinkedTo, linkableDriverRecords
} from './driver-account-link.helpers';

/**
 * Choix de la fiche chauffeur reliée à un compte chauffeur (relecture du 21/09/2026).
 * Fonctions pures, hors TestBed — l'écran Utilisateurs ne fait que les afficher.
 */
describe('driver-account-link.helpers', () => {
  const fiches = [
    { id: 33, firstName: 'Bilel', lastName: 'Libre', email: 'bilel@transporttest.tn', status: 'inactive', userId: null },
    { id: 32, firstName: 'Zied', lastName: 'Autre', email: 'zied@transporttest.tn', status: 'active', userId: 99, accountStatus: 'active' },
    { id: 31, firstName: 'Karim', lastName: 'Chauffeur', email: 'karim@transporttest.tn', status: 'active', userId: 11, accountStatus: 'active' },
    { id: 30, firstName: 'Ali', lastName: 'Ben Salah', email: null, status: 'active', userId: null }
  ];

  it('propose les fiches libres, triées par nom, jamais celle d’un autre compte', () => {
    expect(linkableDriverRecords(fiches, null).map(f => f.id)).toEqual([30, 33]);
  });

  it('en modification, ajoute la fiche déjà reliée à CE compte (id en nombre ou en chaîne)', () => {
    expect(linkableDriverRecords(fiches, 11).map(f => f.id)).toEqual([30, 33, 31]);
    expect(linkableDriverRecords(fiches, '11').map(f => f.id)).toEqual([30, 33, 31]);
    expect(driverRecordLinkedTo(fiches, 11)).toBe(31);
    expect(driverRecordLinkedTo(fiches, 10)).toBeNull();
    expect(driverRecordLinkedTo(fiches, null)).toBeNull();
    expect(linkableDriverRecords(null, 11)).toEqual([]);
  });

  it('libellé : nom, e-mail s’il existe, fiche reliée à ce compte ou inactive', () => {
    expect(driverRecordLabel(fiches[3], null)).toBe('Ali Ben Salah');
    expect(driverRecordLabel(fiches[2], 11)).toBe('Karim Chauffeur — karim@transporttest.tn (reliée à ce compte)');
    expect(driverRecordLabel(fiches[0], null)).toBe('Bilel Libre — bilel@transporttest.tn (fiche inactive)');
    expect(driverRecordLabel({ id: 7 }, null)).toBe('Fiche n° 7');
  });

  it('seuls les 404/409 qui parlent de la fiche vont sous le sélecteur', () => {
    const introuvable = { status: 404, error: { message: 'Fiche chauffeur introuvable dans votre société.' } };
    const prise = { status: 409, error: { message: 'Cette fiche chauffeur est déjà reliée à un autre compte : ouvrez ce compte.' } };
    const compteRelie = { status: 409, error: { message: 'Ce compte est déjà relié à une autre fiche chauffeur.' } };
    expect(driverLinkErrorMessage(introuvable)).toBe('Fiche chauffeur introuvable dans votre société.');
    expect(driverLinkErrorMessage(prise)).toContain('déjà reliée à un autre compte');
    expect(driverLinkErrorMessage(compteRelie)).toBe('Ce compte est déjà relié à une autre fiche chauffeur.');
    // Même statut, autre champ : le toast suffit, le sélecteur n'y est pour rien.
    expect(driverLinkErrorMessage({ status: 409, error: { message: 'Cet email est déjà utilisé' } })).toBeNull();
    expect(driverLinkErrorMessage({ status: 400, error: { message: 'fiche' } })).toBeNull();
    expect(driverLinkErrorMessage({ status: 404 })).toBeNull();
  });
});
