import {
  fuelSensorModeForChosenDevice, gpsDeviceIdToSend, replayAfterReplacement, shouldProposeReplacement, trimIdentifier
} from './vehicle-gps-save.helpers';

/**
 * Boucle « Doublon refusé » du 14/09/2026 (HTZ 278 : fiche #382060 à l'IMEI mal saisi,
 * vrai boîtier #384940 créé par l'ingestion) — règles de l'écran d'administration.
 */
describe('vehicle-gps-save.helpers (boucle Doublon refusé du 14/09/2026)', () => {
  const WRONG_IMEI = '860141078677153';
  const REAL_IMEI = '860141076677153';

  describe('shouldProposeReplacement', () => {
    const dup = (replaceSuggested?: boolean) => ({ message: 'Doublon refusé : …', replaceSuggested });

    it('suit le serveur : pas de confirmation si le remplacement échouerait (cas A, IMEI inchangé)', () => {
      expect(shouldProposeReplacement(dup(false), WRONG_IMEI, WRONG_IMEI)).toBe(false);
    });

    it('propose le remplacement quand le serveur le juge possible (cas B, IMEI corrigé)', () => {
      expect(shouldProposeReplacement(dup(true), REAL_IMEI, WRONG_IMEI)).toBe(true);
    });

    it("propose le remplacement même si l'IMEI est inchangé (fiche de réserve vide qui porte la SIM saisie)", () => {
      expect(shouldProposeReplacement(dup(true), WRONG_IMEI, WRONG_IMEI)).toBe(true);
    });

    it("appareil de la société par défaut de l'ingestion choisi dans la liste : suit replaceSuggested du serveur", () => {
      const foreign = (replaceSuggested?: boolean) =>
        ({ message: 'Cet appareil GPS appartient à une autre société. Si vous confirmez…', replaceSuggested });
      expect(shouldProposeReplacement(foreign(true), REAL_IMEI, WRONG_IMEI)).toBe(true);
      expect(shouldProposeReplacement(foreign(false), REAL_IMEI, WRONG_IMEI)).toBe(false);
    });

    it("jamais sans IMEI saisi, ni sans réponse du serveur", () => {
      expect(shouldProposeReplacement(dup(true), '', WRONG_IMEI)).toBe(false);
      expect(shouldProposeReplacement(null, REAL_IMEI, WRONG_IMEI)).toBe(false);
    });

    it("serveur antérieur sans replaceSuggested : repli sur « doublon et l'IMEI a changé »", () => {
      expect(shouldProposeReplacement(dup(), ` ${REAL_IMEI} `, WRONG_IMEI)).toBe(true);
      expect(shouldProposeReplacement(dup(), WRONG_IMEI, WRONG_IMEI)).toBe(false);
      expect(shouldProposeReplacement({ message: 'Cet appareil GPS appartient à une autre société.' }, REAL_IMEI, WRONG_IMEI)).toBe(false);
    });
  });

  describe('gpsDeviceIdToSend', () => {
    it("désigne TOUJOURS par son id un appareil choisi dans la liste, même différent du boîtier actuel", () => {
      // Par l'IMEI, le serveur transférerait la fiche de société et détacherait sans rien
      // dire le véhicule qui la portait (contre-relecture du 14/09/2026).
      expect(gpsDeviceIdToSend({ hasGPS: true, gpsDeviceId: '384940' })).toBe(384940);
      expect(gpsDeviceIdToSend({ hasGPS: true, gpsDeviceId: 382060 })).toBe(382060);
    });

    it("n'envoie rien pour l'option « -- Sélectionner -- », sans appareil, ou sans GPS", () => {
      expect(gpsDeviceIdToSend({ hasGPS: true, gpsDeviceId: 'null' })).toBeUndefined();
      expect(gpsDeviceIdToSend({ hasGPS: true, gpsDeviceId: null })).toBeUndefined();
      expect(gpsDeviceIdToSend({ hasGPS: true, gpsDeviceId: undefined })).toBeUndefined();
      expect(gpsDeviceIdToSend({ hasGPS: true, gpsDeviceId: '' })).toBeUndefined();
      expect(gpsDeviceIdToSend({ hasGPS: false, gpsDeviceId: 384940 })).toBeUndefined();
    });
  });

  describe('replayAfterReplacement', () => {
    it('rejoue sur la fiche retenue par le serveur, pas sur la fiche supprimée', () => {
      const replay = replayAfterReplacement({ gpsDeviceId: 382060, mileage: 12345 }, 384940);
      expect(replay).toEqual({ gpsDeviceId: 384940, mileage: 12345 });
    });

    it('sans id retenu, garde celui du formulaire', () => {
      expect(replayAfterReplacement({ gpsDeviceId: undefined, mileage: 1 }, undefined).gpsDeviceId).toBeUndefined();
    });

    it("ne renvoie pas les identifiants et réglages déjà appliqués par le remplacement", () => {
      // Le MAT saisi pour l'ancienne fiche (« nr08g1040 ») réécrirait l'orthographe du boîtier qui émet.
      const replay = replayAfterReplacement({
        gpsDeviceId: 382060, mileage: 12345, gpsBrand: 'NORON',
        gpsImei: REAL_IMEI, gpsMat: 'nr08g1040', gpsSimNumber: '92005328', gpsSimOperator: 'ooredoo', gpsFuelSensorMode: 'raw_255'
      }, 384940);
      expect(replay).toEqual({ gpsDeviceId: 384940, mileage: 12345, gpsBrand: 'NORON' });
    });
  });

  describe('fuelSensorModeForChosenDevice', () => {
    it("garde le réglage du véhicule quand l'appareil choisi n'a que la valeur par défaut (HTZ 278 en « liters »)", () => {
      expect(fuelSensorModeForChosenDevice('raw_255', 'liters')).toBe('liters');
      expect(fuelSensorModeForChosenDevice(undefined, 'liters')).toBe('liters');
    });

    it("prend le réglage explicite de l'appareil, et la valeur par défaut sans aucun réglage", () => {
      expect(fuelSensorModeForChosenDevice('percent', 'liters')).toBe('percent');
      expect(fuelSensorModeForChosenDevice('raw_255', undefined)).toBe('raw_255');
      expect(fuelSensorModeForChosenDevice(null, null)).toBe('raw_255');
    });
  });

  describe('trimIdentifier', () => {
    it('retire les espaces de bord (une espace collée ferait perdre le boîtier à l’ingestion)', () => {
      expect(trimIdentifier(`${REAL_IMEI} `)).toBe(REAL_IMEI);
      expect(trimIdentifier('   ')).toBeUndefined();
      expect(trimIdentifier(undefined)).toBeUndefined();
    });
  });
});
