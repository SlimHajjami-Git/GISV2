import { costCategoryFamily, costCreditFamily, fuelDetailToSave } from './vehicle-costs.component';

/**
 * Défauts M9-COUTS (17/09/2026) : ventilation des types anciens à l'écran Coûts, et détail
 * du plein renvoyé par la modification (PUT /api/costs/{id}).
 */
describe('vehicle-costs : postes et détail du plein', () => {
  describe('costCategoryFamily (parité avec VehicleCostCategory.Classify)', () => {
    it('codes, libellés et synonymes de la table ExpenseImportRow.Types', () => {
      expect(costCategoryFamily('fuel')).toBe('fuel');
      expect(costCategoryFamily('carburant')).toBe('fuel');
      expect(costCategoryFamily(' Carburant ')).toBe('fuel');
      expect(costCategoryFamily('maintenance')).toBe('maintenance');
      expect(costCategoryFamily('Entretien')).toBe('maintenance');
      expect(costCategoryFamily('repair')).toBe('repair');
      expect(costCategoryFamily('Réparation')).toBe('repair');
      expect(costCategoryFamily('Réparation  accident')).toBe('repair');
    });

    it('accent décomposé : même mot pour l’utilisateur', () => {
      expect(costCategoryFamily('Re\u0301paration')).toBe('repair');
    });

    it('hors table, crédits et clés d’Object.prototype : aucun poste', () => {
      expect(costCategoryFamily('carburant diesel')).toBeNull();
      expect(costCategoryFamily('insurance')).toBeNull();
      expect(costCategoryFamily('avoir')).toBeNull();
      expect(costCreditFamily('avoir')).toBe('credit_note');
      expect(costCategoryFamily('constructor')).toBeNull();
      expect(costCategoryFamily(null)).toBeNull();
      expect(costCategoryFamily(undefined)).toBeNull();
    });
  });

  describe('fuelDetailToSave', () => {
    const plein = { amount: 100.72, liters: 45.68, pricePerLiter: 2.21 };

    it('plein modifié sans toucher montant ni litres : prix au litre renvoyé tel quel', () => {
      const form = { type: 'fuel', fuelType: 'diesel', liters: 45.68 };
      expect(fuelDetailToSave(form, 100.72, plein))
        .toEqual({ fuelType: 'diesel', liters: 45.68, pricePerLiter: 2.21 });
    });

    it('litres saisis en texte par le champ numérique : même plein', () => {
      const form = { type: 'carburant', fuelType: 'diesel', liters: '45.68' };
      expect(fuelDetailToSave(form, 100.72, plein).pricePerLiter).toBe(2.21);
    });

    it('montant ou litres changés : prix au litre effacé, le montant saisi n’est pas recalculé', () => {
      expect(fuelDetailToSave({ type: 'fuel', liters: 45.68 }, 101, plein).pricePerLiter).toBeNull();
      expect(fuelDetailToSave({ type: 'fuel', liters: 50 }, 100.72, plein))
        .toEqual({ fuelType: null, liters: 50, pricePerLiter: null });
    });

    it('plein reclassé hors carburant : litres, carburant et prix au litre effacés', () => {
      const form = { type: 'toll', fuelType: 'diesel', liters: 45.68 };
      expect(fuelDetailToSave(form, 100.72, plein))
        .toEqual({ fuelType: null, liters: null, pricePerLiter: null });
    });

    it('création : pas de prix au litre', () => {
      expect(fuelDetailToSave({ type: 'fuel', fuelType: 'gasoline', liters: 30 }, 60, null))
        .toEqual({ fuelType: 'gasoline', liters: 30, pricePerLiter: null });
      expect(fuelDetailToSave({ type: 'fuel', fuelType: '', liters: 0 }, 60, null))
        .toEqual({ fuelType: null, liters: null, pricePerLiter: null });
    });
  });
});
