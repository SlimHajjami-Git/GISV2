/**
 * Batterie du monitoring — NEMS = MINIMUM DU JOUR de l'octet « Batterie » (34-36)
 * (Karim, 25/09/2026).
 *
 * Le serveur calcule le minimum depuis minuit (heure de Tunis) et marque les
 * véhicules concernés (`batteryIsDailyMin`, `batteryDayEndUtc`). À l'écran, le
 * temps réel (SignalR) et les rechargements ne doivent jamais le remplacer par la
 * dernière trame : ils ne peuvent que le FAIRE BAISSER dans la même journée. Passé
 * minuit, le minimum d'hier n'est plus affiché.
 *
 * Les autres véhicules (Teltonika) gardent l'ancien comportement : la dernière
 * valeur reçue remplace la précédente.
 */

/** Seuil de l'icône « Anomalie batterie » pour un NEMS (VoltageScale.NemsBatteryLowWarningV). */
export const NEMS_BATTERY_LOW_WARNING_V = 11.5;

const DAY_MS = 24 * 60 * 60 * 1000;

/** Champs batterie des statistiques d'un véhicule (VehicleStatsDto). */
export interface BatteryStats {
  batteryVoltage?: number | null;
  batteryLevel?: number | null;
  batteryIsDailyMin?: boolean;
  batteryDayEndUtc?: string | null;
}

/** Champs batterie d'une mise à jour temps réel (PositionUpdate). */
export interface LiveBatteryUpdate {
  batteryVoltage?: number | null;
  batteryPercent?: number | null;
  recordedAt: string;
}

/**
 * Ce qu'une trame temps réel change dans la batterie affichée.
 *
 * - `embedded` (monitoring de l'admin) : rien. Son API ne renvoie aucune batterie ;
 *   recopier la trame y afficherait une valeur jamais retenue par le serveur.
 * - Véhicule « minimum du jour » : la trame ne compte que si elle a une tension et
 *   qu'elle est plus basse (même journée), ou qu'elle ouvre une nouvelle journée.
 *   Une trame d'une journée passée, arrivée en retard, est ignorée.
 * - Autres véhicules : la valeur reçue remplace l'ancienne (comportement historique).
 */
export function mergeLiveBattery(
  stats: BatteryStats | null | undefined,
  update: LiveBatteryUpdate,
  embedded = false
): Partial<BatteryStats> {
  if (embedded) return {};

  if (!stats?.batteryIsDailyMin) {
    return {
      ...(update.batteryPercent != null ? { batteryLevel: update.batteryPercent } : {}),
      ...(update.batteryVoltage != null ? { batteryVoltage: update.batteryVoltage } : {})
    };
  }

  if (update.batteryVoltage == null) return {};
  const recordedAt = Date.parse(update.recordedAt);
  if (Number.isNaN(recordedAt)) return {};

  const level = update.batteryPercent ?? null;
  const dayEnd = stats.batteryDayEndUtc ? Date.parse(stats.batteryDayEndUtc) : NaN;

  if (!Number.isNaN(dayEnd)) {
    if (recordedAt >= dayEnd) {
      // Nouvelle journée : la trame devient le premier minimum. On avance la fin
      // de journée jusqu'à dépasser la trame — pas seulement de 24 h : sans
      // rechargement depuis plusieurs jours, un seul pas laisserait une échéance
      // déjà passée, et la valeur tout juste reçue s'afficherait « N/A ».
      let nextEnd = dayEnd;
      while (nextEnd <= recordedAt) nextEnd += DAY_MS;
      return {
        batteryVoltage: update.batteryVoltage,
        batteryLevel: level,
        batteryDayEndUtc: new Date(nextEnd).toISOString()
      };
    }
    if (recordedAt < dayEnd - DAY_MS) return {};   // trame d'un jour passé
  }

  if (stats.batteryVoltage == null || update.batteryVoltage < stats.batteryVoltage) {
    return { batteryVoltage: update.batteryVoltage, batteryLevel: level };
  }
  return {};
}

/**
 * Statistiques d'un véhicule après un rechargement de la liste. Si l'écran connaît
 * déjà, pour la même journée, un minimum plus bas que celui du serveur (reçu en
 * temps réel, pas encore en base ou masqué par le cache de 8 s du serveur), on le
 * garde : un minimum ne remonte jamais dans la journée.
 */
export function keepLowerDailyMin<T extends BatteryStats>(
  previous: BatteryStats | null | undefined,
  fresh: T | null | undefined
): T | null | undefined {
  if (!fresh?.batteryIsDailyMin || !previous?.batteryIsDailyMin) return fresh;
  if (!fresh.batteryDayEndUtc || previous.batteryDayEndUtc !== fresh.batteryDayEndUtc) return fresh;
  if (previous.batteryVoltage == null) return fresh;
  if (fresh.batteryVoltage != null && fresh.batteryVoltage <= previous.batteryVoltage) return fresh;
  return { ...fresh, batteryVoltage: previous.batteryVoltage, batteryLevel: previous.batteryLevel ?? null } as T;
}

/**
 * Valeurs à afficher maintenant. Pour un minimum du jour dont la journée est finie
 * (minuit passé sans rechargement), rien : afficher le minimum d'hier serait faux.
 */
export function dailyBatteryView(
  stats: BatteryStats | null | undefined,
  nowMs: number
): { voltage: number | null; level: number | null } {
  if (!stats) return { voltage: null, level: null };
  if (stats.batteryIsDailyMin && stats.batteryDayEndUtc) {
    const dayEnd = Date.parse(stats.batteryDayEndUtc);
    if (!Number.isNaN(dayEnd) && nowMs >= dayEnd) return { voltage: null, level: null };
  }
  return { voltage: stats.batteryVoltage ?? null, level: stats.batteryLevel ?? null };
}

/** Icône « Anomalie batterie » d'un NEMS : minimum du jour sous 11,5 V. */
export function isDailyMinLow(stats: BatteryStats | null | undefined, nowMs: number): boolean {
  const { voltage } = dailyBatteryView(stats, nowMs);
  return voltage != null && voltage < NEMS_BATTERY_LOW_WARNING_V;
}
