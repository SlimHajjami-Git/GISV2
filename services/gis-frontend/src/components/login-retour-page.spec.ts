import { cibleApresConnexion } from './login.component';

/**
 * Après connexion, retour toujours au tableau de bord (campagne GPA du 06/10/2026, erreur
 * n°21) : la page demandée (?returnUrl=, posé par AuthGuard) était ignorée. Elle est suivie
 * désormais — seulement si c'est une page de l'application.
 */
describe('Connexion — retour à la page demandée', () => {
  it.each([
    ['/vehicles', '/vehicles'],
    ['/expenses?vehicleId=12', '/expenses?vehicleId=12'],
    ['/maintenance-templates', '/maintenance-templates'],
  ])('« %s » → %s', (demande, attendu) => {
    expect(cibleApresConnexion(demande)).toBe(attendu);
  });

  it.each([
    [null], [undefined], [''], ['https://exemple.com'], ['//exemple.com'], ['/\\exemple.com'],
    ['vehicles'], ['/login'], ['/login?returnUrl=%2Fvehicles'], ['/admin/clients'], ['/inscription'],
  ])('« %s » → tableau de bord', (demande) => {
    expect(cibleApresConnexion(demande as any)).toBe('/dashboard');
  });
});
