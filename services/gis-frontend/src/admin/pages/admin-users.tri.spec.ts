import { trierParDerniereConnexion } from './admin-users.helpers';

describe('Users (console admin) : tri par dernière connexion', () => {
  it('du plus récent au plus ancien, les « Jamais » en dernier', () => {
    const tries = trierParDerniereConnexion([
      { name: 'Ancien', lastLoginAt: '2026-10-01T08:00:00Z' },
      { name: 'Jamais B' },
      { name: 'Récent', lastLoginAt: '2026-11-05T16:59:00Z' },
      { name: 'Jamais A', lastLoginAt: null },
      { name: 'Milieu', lastLoginAt: new Date('2026-10-24T11:04:00Z') },
    ]);
    expect(tries.map(u => u.name)).toEqual(['Récent', 'Milieu', 'Ancien', 'Jamais A', 'Jamais B']);
  });

  it('ne modifie pas la liste reçue', () => {
    const liste = [{ name: 'A', lastLoginAt: '2026-01-01' }, { name: 'B', lastLoginAt: '2026-02-01' }];
    trierParDerniereConnexion(liste);
    expect(liste.map(u => u.name)).toEqual(['A', 'B']);
  });

  it('une date illisible compte comme « Jamais »', () => {
    const tries = trierParDerniereConnexion([{ name: 'Illisible', lastLoginAt: 'pas une date' }, { name: 'Daté', lastLoginAt: '2026-03-01' }]);
    expect(tries.map(u => u.name)).toEqual(['Daté', 'Illisible']);
  });
});
