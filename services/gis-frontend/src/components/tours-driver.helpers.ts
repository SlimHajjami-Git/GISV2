/**
 * Choix du chauffeur d'une tournée. Fonctions pures, testées dans
 * tours-driver.helpers.spec.ts.
 *
 * Constat du 18/09/2026 : le formulaire affichait une simple étiquette tirée
 * de vehicles.assigned_driver_id, vide pour les 433 véhicules de la prod — le
 * chauffeur n'était jamais renseigné. Le rattachement réel vit sur la fiche
 * chauffeur (drivers.assigned_vehicle_id).
 */

export interface DriverOption {
  id: number;
  firstName?: string | null;
  lastName?: string | null;
  assignedVehicleId?: number | null;
  status?: string | null;
  /** Identifiant porté par la tournée mais absent des fiches (fiche supprimée). */
  missing?: boolean;
}

/** Fiche active (statut absent = active, valeur par défaut en base). */
export function isDriverActive(d: DriverOption): boolean {
  return !d.status || d.status.toLowerCase() === 'active';
}

/** Chauffeur actif rattaché au véhicule, ou null. La liste arrive triée par nom. */
export function driverAssignedToVehicle(drivers: DriverOption[], vehicleId: number | null | undefined): number | null {
  if (vehicleId == null) return null;
  const match = drivers.find(d => isDriverActive(d) && d.assignedVehicleId != null
    && Number(d.assignedVehicleId) === Number(vehicleId));
  return match ? match.id : null;
}

/**
 * Chauffeur après un changement de véhicule : celui rattaché au véhicule,
 * sauf si l'utilisateur a déjà choisi lui-même (y compris « Aucun chauffeur »)
 * — un préremplissage n'écrase jamais un choix. Un chauffeur prérempli pour le
 * véhicule précédent est remplacé.
 */
export function driverAfterVehicleChange(
  drivers: DriverOption[],
  vehicleId: number | null | undefined,
  currentDriverId: number | null | undefined,
  chosenByUser: boolean
): number | null {
  if (chosenByUser) return currentDriverId ?? null;
  return driverAssignedToVehicle(drivers, vehicleId);
}

/**
 * Options de la liste : les chauffeurs actifs, plus le chauffeur déjà porté
 * par la tournée s'il est devenu inactif (sinon la liste l'afficherait vide).
 * Si sa fiche a été supprimée (la suppression est physique et tours."DriverId"
 * n'a pas de clé étrangère), une option « fiche introuvable » le représente :
 * relecture du 18/09/2026, le menu apparaissait vide alors qu'il portait un
 * identifiant, sans moyen de comprendre ce qui était sélectionné.
 */
export function selectableDrivers(drivers: DriverOption[], currentDriverId: number | null | undefined): DriverOption[] {
  const options = drivers.filter(d => isDriverActive(d) || (currentDriverId != null && d.id === currentDriverId));
  if (currentDriverId != null && !drivers.some(d => d.id === currentDriverId)) {
    options.push({ id: currentDriverId, missing: true });
  }
  return options;
}

/** Libellé d'une option : nom, et rappel du rattachement au véhicule choisi. */
export function driverOptionLabel(d: DriverOption, vehicleId: number | null | undefined): string {
  if (d.missing) return `Chauffeur #${d.id} (fiche introuvable)`;
  const name = `${d.firstName ?? ''} ${d.lastName ?? ''}`.trim() || `Chauffeur #${d.id}`;
  const suffixes: string[] = [];
  if (vehicleId != null && d.assignedVehicleId != null && Number(d.assignedVehicleId) === Number(vehicleId)) {
    suffixes.push('affecté au véhicule');
  }
  if (!isDriverActive(d)) suffixes.push('inactif');
  return suffixes.length ? `${name} (${suffixes.join(', ')})` : name;
}
