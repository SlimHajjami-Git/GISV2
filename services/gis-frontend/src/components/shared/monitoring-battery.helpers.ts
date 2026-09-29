/**
 * Batterie du monitoring — NEMS = tension relevée au DERNIER DÉMARRAGE du véhicule
 * (Slim, 29/09/2026).
 *
 * L'octet « Batterie » (34-36) des trames NEMS mélange deux grandeurs : moteur
 * tournant il porte l'alternateur (13,5 à 14,4 V) et ne dit rien de la batterie.
 * Seul l'instant du démarrage la montre. Le serveur retient cette valeur sur le
 * boîtier et la sert telle quelle (`batteryIsStartReading`, `batteryMeasuredAt`) ;
 * elle ne change qu'au démarrage suivant, donc parfois plusieurs jours après —
 * d'où la date affichée en infobulle.
 *
 * À l'écran, le temps réel (SignalR) ne doit donc JAMAIS la remplacer. Le serveur
 * ne diffuse d'ailleurs plus de batterie pour ces véhicules
 * (BroadcastPositionCommandHandler.LiveBattery) ; ce garde-fou reste ici pour que
 * l'écran ne dépende pas de cette politique côté serveur.
 *
 * Les autres véhicules (Teltonika) gardent l'ancien comportement : la dernière
 * valeur reçue remplace la précédente.
 */

/** Seuil de l'icône « Anomalie batterie » pour un NEMS (VoltageScale.NemsBatteryLowWarningV). */
export const NEMS_BATTERY_LOW_WARNING_V = 11.5;

/** Champs batterie des statistiques d'un véhicule (VehicleStatsDto). */
export interface BatteryStats {
  batteryVoltage?: number | null;
  batteryLevel?: number | null;
  batteryIsStartReading?: boolean;
  batteryMeasuredAt?: string | null;
  /** Médiane des derniers démarrages : c'est elle qui allume le témoin, pas la dernière mesure. */
  batteryMedianVoltage?: number | null;
}

/** Champs batterie d'une mise à jour temps réel (PositionUpdate). */
export interface LiveBatteryUpdate {
  batteryVoltage?: number | null;
  batteryPercent?: number | null;
}

/**
 * Ce qu'une trame temps réel change dans la batterie affichée.
 *
 * - `embedded` (monitoring de l'admin) : rien. Son API ne renvoie aucune batterie ;
 *   recopier la trame y afficherait une valeur jamais retenue par le serveur.
 * - Véhicule « tension au démarrage » : rien non plus. La valeur ne change qu'au
 *   démarrage suivant, et c'est le serveur qui le décide.
 * - Autres véhicules : la valeur reçue remplace l'ancienne (comportement historique).
 */
export function mergeLiveBattery(
  stats: BatteryStats | null | undefined,
  update: LiveBatteryUpdate,
  embedded = false
): Partial<BatteryStats> {
  if (embedded || stats?.batteryIsStartReading) return {};

  return {
    ...(update.batteryPercent != null ? { batteryLevel: update.batteryPercent } : {}),
    ...(update.batteryVoltage != null ? { batteryVoltage: update.batteryVoltage } : {})
  };
}

/**
 * Icône « Anomalie batterie » d'un NEMS : MÉDIANE des derniers démarrages sous 11,5 V,
 * jamais la dernière mesure seule.
 *
 * Un démarrage bas isolé — radio oubliée, phares, trajet trop court pour recharger —
 * ne prouve rien : mesuré sur la production TN le 29/09/2026, 9 des 34 témoins allumés
 * par la dernière mesure venaient d'un creux isolé (251 TU 8789 : 10,9 V puis 12,3 |
 * 12,5 | 12,5 | 12,3). Médiane absente = moins de 3 démarrages connus = pas de témoin.
 */
export function isStartReadingLow(stats: BatteryStats | null | undefined): boolean {
  if (!stats?.batteryIsStartReading) return false;
  return stats.batteryMedianVoltage != null
      && stats.batteryMedianVoltage < NEMS_BATTERY_LOW_WARNING_V;
}

/**
 * Infobulle de la cellule Batterie. Pour un NEMS elle DATE la mesure — la valeur peut
 * avoir plusieurs jours si le véhicule n'a pas redémarré (14 boîtiers sur 226 sur TN le
 * 29/09/2026) — et donne la médiane, pour que l'écart entre le chiffre affiché et le
 * témoin s'explique de lui-même : « 11,4 V, mais 11,6 V de médiane, donc pas d'alerte ».
 */
export function batteryTitle(stats: BatteryStats | null | undefined): string | null {
  if (!stats?.batteryIsStartReading) return null;
  if (stats.batteryVoltage == null) return 'Aucun démarrage exploitable relevé';

  const quand = formatMeasuredAt(stats.batteryMeasuredAt);
  const base = quand
    ? `Tension relevée au démarrage du ${quand}`
    : 'Tension relevée au dernier démarrage';

  if (stats.batteryMedianVoltage == null) {
    return `${base} — pas encore assez de démarrages pour conclure`;
  }
  return `${base} — médiane des derniers démarrages : ${stats.batteryMedianVoltage.toFixed(1)} V`;
}

function formatMeasuredAt(iso: string | null | undefined): string | null {
  if (!iso) return null;
  const t = Date.parse(iso);
  if (Number.isNaN(t)) return null;
  return new Date(t).toLocaleString('fr-FR', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit'
  });
}
