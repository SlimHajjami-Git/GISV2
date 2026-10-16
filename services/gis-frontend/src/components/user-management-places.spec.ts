import { UserManagementComponent } from './user-management.component';

/**
 * Plafond de 3 utilisateurs annoncé trop tard (campagne GPA du 06/10/2026, erreur n°20) :
 * le refus n'arrivait qu'après les trois étapes du formulaire, et l'écran ne montrait aucun
 * « 3/3 ». Le compteur suit la règle du serveur : les comptes chauffeur ne comptent pas.
 */
describe('Utilisateurs — places de l\'abonnement', () => {
  function ecran(max: number, comptes: Array<'staff' | 'driver'>, edition = false, chauffeur = false): UserManagementComponent {
    const c = new UserManagementComponent(
      {} as any, {} as any, {} as any, {} as any, {} as any,
      { getLimit: (k: string) => (k === 'maxUsers' ? max : 0) } as any, {} as any);
    c.users = comptes.map((accountType, i) => ({ id: i + 1, accountType } as any));
    (c as any).editingUser = edition ? c.users[0] : null;
    c.userForm.isDriverAccount = chauffeur;
    return c;
  }

  it('compte les places sans les chauffeurs', () => {
    const c = ecran(3, ['staff', 'staff', 'driver']);
    expect(c.placesUtilisees).toBe(2);
    expect(c.placesPleines).toBe(false);
    expect(c.creationBloqueeParLesPlaces).toBe(false);
  });

  it('3 sur 3 : la création d\'un compte de gestion est bloquée dès l\'ouverture du formulaire', () => {
    const c = ecran(3, ['staff', 'staff', 'staff']);
    expect(c.placesPleines).toBe(true);
    expect(c.creationBloqueeParLesPlaces).toBe(true);
  });

  it('3 sur 3 : un compte chauffeur reste possible, et une modification aussi', () => {
    expect(ecran(3, ['staff', 'staff', 'staff'], false, true).creationBloqueeParLesPlaces).toBe(false);
    expect(ecran(3, ['staff', 'staff', 'staff'], true).creationBloqueeParLesPlaces).toBe(false);
  });

  it('plafond inconnu : rien n\'est bloqué à l\'écran, le serveur tranche', () => {
    const c = ecran(0, ['staff', 'staff', 'staff']);
    expect(c.placesPleines).toBe(false);
    expect(c.creationBloqueeParLesPlaces).toBe(false);
  });
});
