import { of } from 'rxjs';
import { SimpleChange } from '@angular/core';
import { VehiclesComponent } from './vehicles.component';
import { VehiclePopupComponent } from './shared/vehicle-popup.component';

/**
 * Écran Véhicules, bouton « Modifier » d'une ligne de la liste (relecture du 25/09/2026).
 * La fiche s'ouvre sur la ligne de liste : celle-ci perdait le carburant et le contrat
 * d'acquisition que GET /api/vehicles renvoie. Un véhicule en Crédit repassait en
 * « Achat » à l'enregistrement — ses traites à venir étaient supprimées — et, le
 * carburant devenu obligatoire, aucune modification ne pouvait plus être enregistrée.
 */
describe('Véhicules — « Modifier » depuis la liste garde le carburant et le crédit', () => {
  const dto = {
    id: 12, companyId: 3, name: 'Clio', type: 'citadine', brand: 'Renault', model: 'Clio',
    plate: 'AB-1', year: 2022, color: 'Blanc', status: 'available', hasGps: false, mileage: 1000,
    fuelTankCapacity: 50, fuelType: 'essence',
    acquisitionType: 'leasing', purchasePrice: 5000, purchaseDate: '2026-01-10T00:00:00Z',
    leasingMonthlyPayment: 800, leasingDurationMonths: 36, leasingStartDate: '2026-01-15T00:00:00Z', leasingPaymentDay: 5
  };
  let envoye: any;
  const api: any = {
    getVehicles: () => of([dto]),
    updateVehicle: (_id: number, p: any) => { envoye = p; return of(void 0); },
    getFuelTypes: () => of([]),
    getAcquisitionPayments: () => of([]),
    isAuthenticated: () => true
  };
  const cdr: any = { detectChanges() {} };

  /** L'écran charge la liste, le client clique « Modifier » sur la ligne ; la fiche s'ouvre. */
  function modifierLaLigne() {
    const ecran = new VehiclesComponent(
      { navigate() {} } as any, api, {} as any, { run: (f: any) => f() } as any, cdr,
      { tick() {} } as any, {} as any, {} as any);
    ecran.loadVehicles();
    const ligne = (ecran as any).filteredVehicles[0];
    ecran.openEditPopup(ligne);
    const fiche = new VehiclePopupComponent(api, { get: () => of([]) } as any, cdr, { current: { currency: 'TND' } } as any);
    fiche.vehicle = (ecran as any).selectedVehicle;
    fiche.isOpen = true;
    fiche.ngOnChanges({ isOpen: new SimpleChange(false, true, false) });
    fiche.saved.subscribe((v: any) => ecran.saveVehicle(v));
    return { ligne, fiche };
  }

  beforeEach(() => { envoye = null; });

  it('la ligne de liste porte le carburant et le contrat d\'acquisition', () => {
    const { ligne } = modifierLaLigne();
    expect(ligne.fuelType).toBe('essence');
    expect(ligne.acquisitionType).toBe('leasing');
    expect(ligne.leasingDurationMonths).toBe(36);
  });

  it('la fiche s\'ouvre sur « Crédit » et le vrai carburant ; « Mettre à jour » sans rien toucher ne change rien', () => {
    const { fiche } = modifierLaLigne();
    const alerte = jest.spyOn(window, 'alert').mockImplementation(() => {});
    try {
      expect(fiche.formData.fuelType).toBe('essence');
      expect(fiche.formData.acquisitionType).toBe('leasing');
      fiche.onSubmit();
      expect(alerte).not.toHaveBeenCalled();
      expect(envoye.fuelType).toBe('essence');
      expect(envoye.acquisitionType).toBe('leasing');
      expect(envoye.leasingMonthlyPayment).toBe(800);
    } finally {
      alerte.mockRestore();
    }
  });
});
