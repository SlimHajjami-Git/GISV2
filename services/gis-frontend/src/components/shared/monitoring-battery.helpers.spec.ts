import {
  BatteryStats, dailyBatteryView, isDailyMinLow, keepLowerDailyMin, mergeLiveBattery
} from './monitoring-battery.helpers';

/**
 * Monitoring — tension batterie d'un NEMS = minimum du jour de l'octet « Batterie »
 * (34-36), demande de Karim du 25/09/2026. Journée de Tunis du 25/09 : de
 * 2026-09-24T23:00Z à 2026-09-25T23:00Z.
 */
const FIN_DU_25 = '2026-09-25T23:00:00.000Z';
const nems = (patch: Partial<BatteryStats> = {}): BatteryStats => ({
  batteryVoltage: 12.5, batteryLevel: 83, batteryIsDailyMin: true, batteryDayEndUtc: FIN_DU_25, ...patch
});
const trame = (volts: number | null, recordedAt = '2026-09-25T10:00:00Z', percent: number | null = null) =>
  ({ batteryVoltage: volts, batteryPercent: percent, recordedAt });

describe('mergeLiveBattery — trame temps réel', () => {
  it('NEMS : une trame plus basse dans la journée devient le minimum', () => {
    expect(mergeLiveBattery(nems(), trame(11.4, '2026-09-25T10:00:00Z', 22)))
      .toEqual({ batteryVoltage: 11.4, batteryLevel: 22 });
  });

  it('NEMS : une trame plus haute ne remplace pas le minimum (c’était le défaut : dernière trame affichée)', () => {
    expect(mergeLiveBattery(nems(), trame(13.1))).toEqual({});
  });

  it('NEMS : une trame sans tension (octet de cap, 0) ne change rien', () => {
    expect(mergeLiveBattery(nems(), trame(null))).toEqual({});
  });

  it('NEMS : premier minimum de la journée quand le serveur n’en avait pas (N/A)', () => {
    expect(mergeLiveBattery(nems({ batteryVoltage: null, batteryLevel: null }), trame(12.3, undefined, 72)))
      .toEqual({ batteryVoltage: 12.3, batteryLevel: 72 });
  });

  it('NEMS : après minuit, la trame ouvre une nouvelle journée même si elle est plus haute', () => {
    expect(mergeLiveBattery(nems({ batteryVoltage: 10.8 }), trame(12.6, '2026-09-26T05:00:00Z', 89)))
      .toEqual({ batteryVoltage: 12.6, batteryLevel: 89, batteryDayEndUtc: '2026-09-26T23:00:00.000Z' });
  });

  it('NEMS : sans rechargement depuis deux jours, la fin de journée avance jusqu’à dépasser la trame', () => {
    const r = mergeLiveBattery(nems(), trame(12.4, '2026-09-27T08:00:00Z'));
    expect(r.batteryDayEndUtc).toBe('2026-09-27T23:00:00.000Z');
    expect(dailyBatteryView({ ...nems(), ...r }, Date.parse('2026-09-27T08:00:30Z')).voltage).toBe(12.4);
  });

  it('NEMS : une trame d’hier arrivée en retard est ignorée', () => {
    expect(mergeLiveBattery(nems(), trame(9.9, '2026-09-24T20:00:00Z'))).toEqual({});
  });

  it('monitoring admin (embarqué) : rien, son API ne renvoie pas de batterie', () => {
    expect(mergeLiveBattery({ batteryVoltage: null }, trame(12.1, undefined, 61), true)).toEqual({});
  });

  it('Teltonika : la dernière valeur remplace la précédente, comme avant', () => {
    expect(mergeLiveBattery({ batteryVoltage: 12.1, batteryLevel: 61 }, trame(13.9, undefined, 100)))
      .toEqual({ batteryVoltage: 13.9, batteryLevel: 100 });
  });
});

describe('keepLowerDailyMin — rechargement de la liste', () => {
  it('garde un minimum plus bas reçu en temps réel, pas encore vu par le serveur', () => {
    const avant = nems({ batteryVoltage: 11.2, batteryLevel: 11 });
    const serveur = nems({ batteryVoltage: 12.5, batteryLevel: 83 });
    expect(keepLowerDailyMin(avant, serveur)).toEqual(nems({ batteryVoltage: 11.2, batteryLevel: 11 }));
  });

  it('prend la valeur du serveur si elle est plus basse ou égale', () => {
    const serveur = nems({ batteryVoltage: 10.9, batteryLevel: 0 });
    expect(keepLowerDailyMin(nems({ batteryVoltage: 11.2 }), serveur)).toBe(serveur);
  });

  it('nouvelle journée côté serveur : sa valeur gagne, même plus haute', () => {
    const serveur = nems({ batteryVoltage: 12.8, batteryDayEndUtc: '2026-09-26T23:00:00.000Z' });
    expect(keepLowerDailyMin(nems({ batteryVoltage: 10.8 }), serveur)).toBe(serveur);
  });

  it('véhicule non NEMS ou premier chargement : la valeur du serveur telle quelle', () => {
    const teltonika = { batteryVoltage: 12.9, batteryLevel: 100 };
    expect(keepLowerDailyMin({ batteryVoltage: 11 }, teltonika)).toBe(teltonika);
    expect(keepLowerDailyMin(undefined, nems())).toEqual(nems());
  });
});

describe('dailyBatteryView / isDailyMinLow — affichage', () => {
  const pendantLaJournee = Date.parse('2026-09-25T15:00:00Z');
  const apresMinuit = Date.parse('2026-09-25T23:30:00Z');

  it('affiche le minimum pendant sa journée', () => {
    expect(dailyBatteryView(nems({ batteryVoltage: 11.4, batteryLevel: 22 }), pendantLaJournee))
      .toEqual({ voltage: 11.4, level: 22 });
  });

  it('n’affiche plus le minimum d’hier après minuit (N/A jusqu’au prochain rechargement)', () => {
    expect(dailyBatteryView(nems(), apresMinuit)).toEqual({ voltage: null, level: null });
  });

  it('Teltonika : pas de notion de journée', () => {
    expect(dailyBatteryView({ batteryVoltage: 12.7, batteryLevel: 94 }, apresMinuit))
      .toEqual({ voltage: 12.7, level: 94 });
  });

  it('icône « Anomalie batterie » : minimum sous 11,5 V', () => {
    expect(isDailyMinLow(nems({ batteryVoltage: 11.4 }), pendantLaJournee)).toBe(true);
    expect(isDailyMinLow(nems({ batteryVoltage: 11.5 }), pendantLaJournee)).toBe(false);
    expect(isDailyMinLow(nems({ batteryVoltage: null }), pendantLaJournee)).toBe(false);
    expect(isDailyMinLow(nems({ batteryVoltage: 10.8 }), apresMinuit)).toBe(false);
  });
});
