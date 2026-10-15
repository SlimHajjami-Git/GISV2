import { Subject, of } from 'rxjs';
import { VehiclesComponent } from './vehicles.component';

/**
 * Double clic sur « Ajouter » = deux véhicules de même plaque (campagne GPA du 06/10/2026,
 * erreur n°16). Tant que la première création n'a pas répondu, un second envoi est ignoré ;
 * la base refuse de toute façon le doublon (migration 062).
 */
describe('Véhicules — un double clic sur « Ajouter » n\'envoie qu\'une création', () => {
  let reponse: Subject<any>;
  let creations: number;
  const api: any = {
    getVehicles: () => of([]),
    isAuthenticated: () => true,
    createVehicle: () => { creations++; return reponse; },
    updateVehicle: () => { creations++; return reponse; },
  };
  const cdr: any = { detectChanges() {} };
  const fiche = { name: 'Kangoo', plate: 'QA-16-DBL', mileage: 100, fuelType: 'diesel', type: 'utilitaire' };

  function ecran(): VehiclesComponent {
    return new VehiclesComponent(
      { navigate() {} } as any, api, {} as any, { run: (f: any) => f() } as any, cdr,
      { tick() {} } as any, {} as any, {} as any, { getSubscriptionFeatures: () => ({ moduleMonitoring: false }) } as any);
  }

  beforeEach(() => { reponse = new Subject<any>(); creations = 0; });

  it('le second clic, avant la réponse, ne crée rien', () => {
    const e = ecran();
    e.openAddPopup();
    e.saveVehicle(fiche);
    e.saveVehicle(fiche);
    expect(creations).toBe(1);
    expect(e.savingVehicle).toBe(true);
  });

  it('après la réponse, on peut de nouveau enregistrer', () => {
    const e = ecran();
    e.openAddPopup();
    e.saveVehicle(fiche);
    reponse.next(42); reponse.complete();
    expect(e.savingVehicle).toBe(false);
    expect(e.isPopupOpen).toBe(false);
  });

  it('un refus du serveur libère le bouton pour corriger la saisie', () => {
    const alerte = jest.spyOn(window, 'alert').mockImplementation(() => {});
    jest.spyOn(console, 'error').mockImplementation(() => {});
    const e = ecran();
    e.openAddPopup();
    e.saveVehicle(fiche);
    reponse.error({ error: { message: 'Ce matricule est déjà utilisé par un autre véhicule de la société.' } });
    expect(e.savingVehicle).toBe(false);
    expect(alerte).toHaveBeenCalledWith('Ce matricule est déjà utilisé par un autre véhicule de la société.');
    alerte.mockRestore();
  });
});
