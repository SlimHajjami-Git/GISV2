import {
  BatteryStats,
  NEMS_BATTERY_LOW_WARNING_V,
  batteryTitle,
  isStartReadingLow,
  mergeLiveBattery
} from './monitoring-battery.helpers';

/**
 * Monitoring — batterie d'un NEMS = tension relevée au DERNIER DÉMARRAGE, gardée
 * jusqu'au suivant (Slim, 29/09/2026). Le temps réel ne doit jamais la remplacer :
 * moteur tournant, l'octet 34-36 porte l'alternateur (13,5 à 14,4 V) et ne dit rien
 * de la batterie.
 */
describe('monitoring-battery.helpers', () => {
  const demarrage: BatteryStats = {
    batteryVoltage: 12.3,
    batteryLevel: 72,
    batteryIsStartReading: true,
    batteryMeasuredAt: '2026-09-29T06:12:00Z',
    batteryMedianVoltage: 12.3
  };

  const teltonika: BatteryStats = {
    batteryVoltage: 12.7,
    batteryLevel: 94,
    batteryIsStartReading: false
  };

  describe('mergeLiveBattery', () => {
    it('ne touche pas à la tension de démarrage d’un NEMS', () => {
      // Une trame à 14,1 V : c'est l'alternateur. La reprendre effacerait la seule
      // valeur qui parle de la batterie.
      expect(mergeLiveBattery(demarrage, { batteryVoltage: 14.1, batteryPercent: 100 })).toEqual({});
    });

    it('ne touche à rien non plus quand la trame n’apporte pas de batterie', () => {
      expect(mergeLiveBattery(demarrage, {})).toEqual({});
    });

    it('remplace la valeur des autres véhicules (comportement historique)', () => {
      expect(mergeLiveBattery(teltonika, { batteryVoltage: 12.1, batteryPercent: 61 }))
        .toEqual({ batteryVoltage: 12.1, batteryLevel: 61 });
    });

    it('ne comble pas un trou : une trame sans tension laisse la valeur en place', () => {
      expect(mergeLiveBattery(teltonika, { batteryPercent: 61 })).toEqual({ batteryLevel: 61 });
      expect(mergeLiveBattery(teltonika, { batteryVoltage: 12.1 })).toEqual({ batteryVoltage: 12.1 });
    });

    it('ne recopie rien dans le monitoring admin (embedded) : son API n’envoie pas de batterie', () => {
      expect(mergeLiveBattery(teltonika, { batteryVoltage: 12.1, batteryPercent: 61 }, true)).toEqual({});
    });

    it('sans statistiques connues, traite le véhicule comme non NEMS', () => {
      expect(mergeLiveBattery(null, { batteryVoltage: 12.1, batteryPercent: 61 }))
        .toEqual({ batteryVoltage: 12.1, batteryLevel: 61 });
    });
  });

  describe('isStartReadingLow', () => {
    it('allume l’icône quand la MÉDIANE passe sous 11,5 V', () => {
      expect(isStartReadingLow({ ...demarrage, batteryMedianVoltage: 11.4 })).toBe(true);
    });

    it('ne l’allume pas au seuil exact', () => {
      expect(isStartReadingLow({ ...demarrage, batteryMedianVoltage: NEMS_BATTERY_LOW_WARNING_V }))
        .toBe(false);
    });

    it('ignore un creux isolé : la dernière mesure est basse, la médiane non', () => {
      // 251 TU 8789 : 10,9 V au dernier démarrage (radio oubliée), 12,3 V de médiane.
      expect(isStartReadingLow({ ...demarrage, batteryVoltage: 10.9, batteryMedianVoltage: 12.3 }))
        .toBe(false);
    });

    it('allume même si le dernier démarrage était bon, quand la médiane est basse', () => {
      // 235 TU 5540 : 13,1 V après un long trajet, mais 11,3 V de médiane.
      expect(isStartReadingLow({ ...demarrage, batteryVoltage: 13.1, batteryMedianVoltage: 11.3 }))
        .toBe(true);
    });

    it('ne l’allume pas tant qu’il n’y a pas assez de démarrages', () => {
      expect(isStartReadingLow({ ...demarrage, batteryVoltage: 10.9, batteryMedianVoltage: null }))
        .toBe(false);
    });

    it('ne juge pas les véhicules non NEMS — leur icône vient de l’alerte de santé', () => {
      expect(isStartReadingLow({ batteryMedianVoltage: 10.2, batteryIsStartReading: false })).toBe(false);
      expect(isStartReadingLow(null)).toBe(false);
    });
  });

  describe('batteryTitle', () => {
    it('date la mesure et donne la médiane', () => {
      // La valeur peut avoir plusieurs jours : 14 boîtiers sur 226 n'avaient pas
      // redémarré depuis plus de 24 h sur TN le 29/09/2026.
      const titre = batteryTitle(demarrage);
      expect(titre).toContain('au démarrage du');
      expect(titre).toContain('29/09');
      expect(titre).toContain('médiane');
      expect(titre).toContain('12.3 V');
    });

    it('explique pourquoi le témoin reste éteint malgré une mesure basse', () => {
      const titre = batteryTitle({ ...demarrage, batteryVoltage: 10.9, batteryMedianVoltage: 12.3 });
      expect(titre).toContain('12.3 V');
    });

    it('dit qu’on n’a pas encore assez de démarrages', () => {
      expect(batteryTitle({ ...demarrage, batteryMedianVoltage: null }))
        .toContain('pas encore assez de démarrages');
    });

    it('dit clairement quand aucun démarrage n’a été relevé', () => {
      expect(batteryTitle({ ...demarrage, batteryVoltage: null }))
        .toBe('Aucun démarrage exploitable relevé');
    });

    it('reste lisible si la date est absente ou illisible', () => {
      expect(batteryTitle({ ...demarrage, batteryMeasuredAt: null }))
        .toContain('Tension relevée au dernier démarrage');
      expect(batteryTitle({ ...demarrage, batteryMeasuredAt: 'pas une date' }))
        .toContain('Tension relevée au dernier démarrage');
    });

    it('n’ajoute pas d’infobulle aux autres véhicules', () => {
      expect(batteryTitle(teltonika)).toBeNull();
      expect(batteryTitle(null)).toBeNull();
    });
  });
});
