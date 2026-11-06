import { decocherNouveauxVehicules, replierNouvellesSocietes } from './monitoring-visibilite';

describe('Monitoring de la console admin : véhicules décochés au départ', () => {
  it('au premier chargement, tous les véhicules de toutes les sociétés sont décochés', () => {
    const vus = new Set<string>(), masques = new Set<string>();
    decocherNouveauxVehicules([{ id: 1 }, { id: 2 }, { id: 3 }], vus, masques);
    expect([...masques].sort()).toEqual(['1', '2', '3']);
  });

  it('au rafraîchissement, ce que l administrateur a coché reste coché', () => {
    const vus = new Set<string>(), masques = new Set<string>();
    decocherNouveauxVehicules([{ id: 1 }, { id: 2 }], vus, masques);
    masques.delete('1'); // l'administrateur coche le véhicule 1
    decocherNouveauxVehicules([{ id: 1 }, { id: 2 }], vus, masques);
    expect(masques.has('1')).toBe(false);
    expect(masques.has('2')).toBe(true);
  });

  it('un véhicule ajouté entre deux rafraîchissements arrive décoché', () => {
    const vus = new Set<string>(), masques = new Set<string>();
    decocherNouveauxVehicules([{ id: 1 }], vus, masques);
    masques.delete('1');
    decocherNouveauxVehicules([{ id: 1 }, { id: 7 }], vus, masques);
    expect([...masques]).toEqual(['7']);
  });

  it('toutes les sociétés arrivent repliées ; une société dépliée le reste au rafraîchissement', () => {
    const vues = new Set<string>(), repliees = new Set<string>();
    replierNouvellesSocietes(['1', '7', '22'], vues, repliees);
    expect([...repliees].sort()).toEqual(['1', '22', '7']);
    repliees.delete('7'); // l'administrateur déplie la société 7
    replierNouvellesSocietes(['1', '7', '22', '30'], vues, repliees);
    expect(repliees.has('7')).toBe(false);
    expect(repliees.has('30')).toBe(true); // nouvelle société : repliée
  });

  it('ignore un véhicule sans identifiant', () => {
    const vus = new Set<string>(), masques = new Set<string>();
    decocherNouveauxVehicules([{ id: null }, {}], vus, masques);
    expect(masques.size).toBe(0);
  });
});
