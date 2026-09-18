import {
  driverAfterVehicleChange, driverAssignedToVehicle, driverOptionLabel, DriverOption, isDriverActive, selectableDrivers
} from './tours-driver.helpers';

/**
 * Choix du chauffeur dans le formulaire Tournées (lot 0, 18/09/2026).
 */
describe('tours-driver.helpers', () => {
  const drivers: DriverOption[] = [
    { id: 1, firstName: 'Ali', lastName: 'Ben Salah', assignedVehicleId: 10, status: 'active' },
    { id: 2, firstName: 'Sami', lastName: 'Inactif', assignedVehicleId: 20, status: 'inactive' },
    { id: 3, firstName: 'Karim', lastName: 'Sans véhicule', assignedVehicleId: null, status: 'active' },
    { id: 4, firstName: 'Nour', lastName: 'Camion', assignedVehicleId: 30, status: null }
  ];

  describe('driverAssignedToVehicle (drivers.assigned_vehicle_id, pas vehicles.assigned_driver_id)', () => {
    it('trouve le chauffeur rattaché au véhicule', () => {
      expect(driverAssignedToVehicle(drivers, 10)).toBe(1);
    });

    it('ignore un chauffeur inactif', () => {
      expect(driverAssignedToVehicle(drivers, 20)).toBeNull();
    });

    it('statut absent = actif (valeur par défaut en base)', () => {
      expect(driverAssignedToVehicle(drivers, 30)).toBe(4);
    });

    it('aucun véhicule ou aucun rattachement : null', () => {
      expect(driverAssignedToVehicle(drivers, null)).toBeNull();
      expect(driverAssignedToVehicle(drivers, 99)).toBeNull();
    });
  });

  describe('driverAfterVehicleChange', () => {
    it('préremplit le chauffeur du véhicule quand rien n’a été choisi', () => {
      expect(driverAfterVehicleChange(drivers, 10, null, false)).toBe(1);
    });

    it('remplace un chauffeur prérempli pour le véhicule précédent', () => {
      expect(driverAfterVehicleChange(drivers, 30, 1, false)).toBe(4);
      expect(driverAfterVehicleChange(drivers, 99, 1, false)).toBeNull();
    });

    it('n’écrase jamais un choix de l’utilisateur', () => {
      expect(driverAfterVehicleChange(drivers, 10, 3, true)).toBe(3);
    });

    it('« Aucun chauffeur » choisi explicitement reste un choix', () => {
      expect(driverAfterVehicleChange(drivers, 10, null, true)).toBeNull();
    });
  });

  describe('selectableDrivers', () => {
    it('ne propose que les chauffeurs actifs', () => {
      expect(selectableDrivers(drivers, null).map(d => d.id)).toEqual([1, 3, 4]);
    });

    it('garde le chauffeur inactif déjà porté par la tournée', () => {
      expect(selectableDrivers(drivers, 2).map(d => d.id)).toEqual([1, 2, 3, 4]);
    });

    it('représente un chauffeur dont la fiche a été supprimée au lieu d’un menu vide', () => {
      const options = selectableDrivers(drivers, 77);
      expect(options.map(d => d.id)).toEqual([1, 3, 4, 77]);
      expect(driverOptionLabel(options[3], 10)).toBe('Chauffeur #77 (fiche introuvable)');
    });

    it('n’ajoute rien quand le chauffeur porté est connu', () => {
      expect(selectableDrivers(drivers, 1).some(d => d.missing)).toBe(false);
    });
  });

  describe('driverOptionLabel', () => {
    it('signale le rattachement au véhicule choisi', () => {
      expect(driverOptionLabel(drivers[0], 10)).toBe('Ali Ben Salah (affecté au véhicule)');
      expect(driverOptionLabel(drivers[0], 30)).toBe('Ali Ben Salah');
    });

    it('signale un chauffeur inactif', () => {
      expect(driverOptionLabel(drivers[1], null)).toBe('Sami Inactif (inactif)');
    });

    it('isDriverActive est insensible à la casse', () => {
      expect(isDriverActive({ id: 5, status: 'ACTIVE' })).toBe(true);
    });
  });
});
