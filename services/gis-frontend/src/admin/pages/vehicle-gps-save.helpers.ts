/**
 * Règles d'enregistrement du boîtier GPS d'un véhicule dans l'espace administration,
 * partagées par les écrans Véhicules et Société et isolées pour être testées.
 *
 * Constat du 14/09/2026 (HTZ 278, 255, 292) : la fiche boîtier de ces véhicules
 * portait un IMEI mal saisi pendant que le vrai boîtier émettait sous une fiche
 * créée par l'ingestion. L'écran bouclait sur « Doublon refusé » en proposant un
 * remplacement voué à l'échec ; ces règles encadrent la sortie de cette impasse.
 */

/** Identifiant saisi sans espaces de bord (l'ingestion compare IMEI et MAT à l'identique). */
export function trimIdentifier(value: unknown): string | undefined {
  return typeof value === 'string' ? value.trim() || undefined : undefined;
}

/**
 * gpsDeviceId à envoyer au serveur : l'id de l'appareil choisi dans la liste, ou rien.
 * L'option « -- Sélectionner -- » (`[value]="null"`) produit la chaîne 'null', qui
 * n'est pas envoyée.
 *
 * Un appareil choisi dans la liste est TOUJOURS désigné par son id, jamais par son
 * IMEI : par l'IMEI, le serveur passerait par le chemin qui transfère la fiche de
 * société et détache sans rien dire le véhicule qui la portait (contre-relecture du
 * 14/09/2026). Pour une fiche créée par l'ingestion dans la société par défaut, c'est
 * le serveur qui propose le rattachement (replaceSuggested) quand il aboutit.
 */
export function gpsDeviceIdToSend(
  formData: { hasGPS?: boolean; gpsDeviceId?: unknown }
): number | undefined {
  if (!formData.hasGPS) return undefined;
  const chosen = formData.gpsDeviceId;
  if (chosen === null || chosen === undefined || chosen === '' || chosen === 'null') return undefined;
  const id = Number(chosen);
  return Number.isInteger(id) && id > 0 ? id : undefined;
}

/**
 * Faut-il proposer le remplacement de boîtier après un refus de mise à jour ?
 * Le serveur calcule si le remplacement aboutirait avec les valeurs saisies
 * (replaceSuggested) : on s'y fie, que le refus soit un doublon ou un appareil de la
 * société par défaut de l'ingestion. Avec un serveur antérieur qui ne le renvoie pas,
 * repli sur « doublon et l'IMEI saisi diffère de l'IMEI actuel ».
 */
export function shouldProposeReplacement(
  errorBody: { message?: string; replaceSuggested?: boolean } | null | undefined,
  newImei: string | undefined,
  currentImei: string | null | undefined
): boolean {
  if (!errorBody || !newImei) return false;
  if (typeof errorBody.replaceSuggested === 'boolean') return errorBody.replaceSuggested;
  if (!(errorBody.message || '').includes('Doublon refusé')) return false;
  const normalize = (v: string | null | undefined) => (v || '').replace(/\s+/g, '').toUpperCase();
  return normalize(newImei) !== normalize(currentImei);
}

/** Champs que le remplacement de boîtier a déjà appliqués à la fiche retenue. */
const REPLACEMENT_FIELDS = ['gpsImei', 'gpsMat', 'gpsSimNumber', 'gpsSimOperator', 'gpsFuelSensorMode'] as const;

/**
 * Mise à jour rejouée après un remplacement réussi : elle désigne la fiche retenue
 * par le serveur. En mode rattachement, l'ancienne fiche du véhicule a été supprimée ;
 * un gpsDeviceId resté sur elle ferait échouer le rejeu (« Appareil GPS introuvable »).
 *
 * Les identifiants et réglages déjà envoyés au remplacement (IMEI, MAT, SIM, opérateur,
 * mode capteur) ne sont pas renvoyés : le remplacement les a appliqués avec ses propres
 * règles (orthographe du MAT du boîtier qui émet), et le rejeu, qui vise la fiche du
 * véhicule, les réécrirait tels que saisis pour l'ANCIENNE fiche.
 */
export function replayAfterReplacement<T extends { gpsDeviceId?: number }>(
  vehicleData: T,
  retainedDeviceId: number | null | undefined
): T {
  const replay: any = { ...vehicleData, gpsDeviceId: retainedDeviceId ?? vehicleData.gpsDeviceId };
  for (const field of REPLACEMENT_FIELDS) delete replay[field];
  return replay;
}

/** Valeur par défaut de gps_devices.fuel_sensor_mode (entité, base et ingestion). */
export const DEFAULT_FUEL_SENSOR_MODE = 'raw_255';

/**
 * Mode capteur carburant à afficher quand un appareil est choisi dans la liste.
 * Le capteur est câblé sur le véhicule : si l'appareil choisi n'a que la valeur par
 * défaut (fiche créée par l'ingestion) et que le véhicule édité a un réglage explicite
 * (« liters » sur HTZ 278 / 255 / 292), c'est ce réglage qui est gardé. Sans cela, le
 * choix dans la liste remettait « raw_255 » et le remplacement l'écrivait sur le vrai
 * boîtier (contre-relecture du 14/09/2026). La valeur reste visible et modifiable.
 */
export function fuelSensorModeForChosenDevice(
  deviceMode: string | null | undefined,
  vehicleMode: string | null | undefined
): string {
  if (deviceMode && deviceMode !== DEFAULT_FUEL_SENSOR_MODE) return deviceMode;
  if (vehicleMode) return vehicleMode;
  return deviceMode || DEFAULT_FUEL_SENSOR_MODE;
}
