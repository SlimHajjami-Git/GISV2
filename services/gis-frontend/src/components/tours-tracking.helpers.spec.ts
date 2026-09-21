import {
  declarationDistanceWarning, driverDepartureSignaledAt, driverHasAppAccount, NO_APP_ACCOUNT_TOOLTIP, NO_DRIVER_TOOLTIP,
  phoneBatteryLabel, positionAgeLabel, sendButtonState, sendErrorMessage, sendStatusLabel, sendToast, shortDuration,
  tourSendStatus, trackingBadge, waypointSourceLabel, withAppBadge
} from './tours-tracking.helpers';

/**
 * Tournée envoyée au chauffeur et suivie boîtier / téléphone (21/09/2026).
 * Libellés et verrous purs, hors TestBed — le composant ne fait que les afficher.
 */
describe('tours-tracking.helpers', () => {
  const drivers = [
    { id: 1, userId: 11, accountStatus: 'active' },
    { id: 2, userId: null, accountStatus: null },
    { id: 3, userId: 13, accountStatus: 'inactive' }
  ];

  describe('compte application du chauffeur', () => {
    it('reconnaît un compte relié et actif', () => {
      expect(driverHasAppAccount(drivers[0])).toBe(true);
    });

    it('sans compte, ou compte inactif : pas d’application', () => {
      expect(driverHasAppAccount(drivers[1])).toBe(false);
      expect(driverHasAppAccount(drivers[2])).toBe(false);
      expect(driverHasAppAccount(null)).toBe(false);
    });

    it('le badge « application » ne s’ajoute qu’aux chauffeurs équipés', () => {
      expect(withAppBadge('Ali Ben Salah', drivers[0])).toBe('Ali Ben Salah · 📱 application');
      expect(withAppBadge('Sami Sans App', drivers[1])).toBe('Sami Sans App');
    });
  });

  describe('sendButtonState — bouton « Envoyer au chauffeur »', () => {
    it('désactivé, avec le motif « créez-le dans Utilisateurs », quand le chauffeur n’a pas de compte', () => {
      const s = sendButtonState({ status: 'planned', driverId: 2, sentAt: null }, drivers);
      expect(s.visible).toBe(true);
      expect(s.enabled).toBe(false);
      expect(s.tooltip).toBe(NO_APP_ACCOUNT_TOOLTIP);
    });

    it('désactivé de la même façon pour un compte inactif ou une fiche inconnue', () => {
      expect(sendButtonState({ status: 'planned', driverId: 3 }, drivers).tooltip).toBe(NO_APP_ACCOUNT_TOOLTIP);
      expect(sendButtonState({ status: 'planned', driverId: 99 }, drivers).tooltip).toBe(NO_APP_ACCOUNT_TOOLTIP);
    });

    it('désactivé sans chauffeur', () => {
      const s = sendButtonState({ status: 'in_progress', driverId: null }, drivers);
      expect(s.enabled).toBe(false);
      expect(s.tooltip).toBe(NO_DRIVER_TOOLTIP);
    });

    it('actif avec un compte application, « Envoyer » puis « Renvoyer » une fois envoyée', () => {
      expect(sendButtonState({ status: 'planned', driverId: 1 }, drivers))
        .toEqual({ visible: true, enabled: true, label: 'Envoyer au chauffeur', tooltip: null });
      expect(sendButtonState({ status: 'in_progress', driverId: 1, sentAt: '2026-09-21T08:00:00Z' }, drivers).label)
        .toBe('Renvoyer au chauffeur');
    });

    it('invisible pour une tournée terminée ou annulée', () => {
      expect(sendButtonState({ status: 'completed', driverId: 1 }, drivers).visible).toBe(false);
      expect(sendButtonState({ status: 'cancelled', driverId: 1 }, drivers).visible).toBe(false);
      expect(sendButtonState(null, drivers).visible).toBe(false);
    });
  });

  describe('sendToast — issue du push', () => {
    it.each([
      ['delivered_to_fcm', 'success', 'Envoyée au téléphone du chauffeur'],
      ['no_device', 'warning', "Envoyée, mais le chauffeur n'a pas encore ouvert l'application sur son téléphone"],
      ['firebase_off', 'warning', 'Enregistrée, notification non délivrée'],
      ['failed', 'warning', 'Enregistrée, notification non délivrée'],
      ['quiet_hours', 'info', 'Enregistrée, le chauffeur est en heures silencieuses']
    ])('%s → toast %s', (push, type, message) => {
      expect(sendToast({ push, resent: false })).toEqual({ type, title: 'Tournée envoyée', message });
    });

    it('un renvoi est titré « Tournée renvoyée »', () => {
      expect(sendToast({ push: 'delivered_to_fcm', resent: true }).title).toBe('Tournée renvoyée');
    });

    it('issue inconnue : toast neutre, jamais une exception', () => {
      expect(sendToast({ push: 'something_new', resent: false }).type).toBe('info');
    });
  });

  describe('sendErrorMessage', () => {
    it('DRIVER_NO_APP_ACCOUNT : le message du serveur, sinon l’infobulle', () => {
      expect(sendErrorMessage({ error: { code: 'DRIVER_NO_APP_ACCOUNT', message: 'Pas de compte.' } })).toBe('Pas de compte.');
      expect(sendErrorMessage({ error: { code: 'DRIVER_NO_APP_ACCOUNT' } })).toBe(NO_APP_ACCOUNT_TOOLTIP);
    });

    it('autre refus : le message du serveur, sinon un repli en français', () => {
      expect(sendErrorMessage({ error: { message: "Choisissez d'abord un chauffeur" } })).toBe("Choisissez d'abord un chauffeur");
      expect(sendErrorMessage({ status: 500 })).toBe("La tournée n'a pas pu être envoyée.");
    });
  });

  describe('waypointSourceLabel — source de validation d’une étape', () => {
    it.each([
      ['device', false, 'boîtier'],
      ['phone', false, 'téléphone'],
      ['geofence', false, 'zone'],
      ['manager', false, 'gestionnaire'],
      ['driver', true, 'chauffeur (non confirmée)'],
      ['driver', false, 'chauffeur']
    ])('%s (unconfirmed=%s) → « %s »', (arrivalSource, unconfirmed, label) => {
      expect(waypointSourceLabel({ arrivalSource, unconfirmed })).toBe(label);
    });

    it('source absente : rien à afficher', () => {
      expect(waypointSourceLabel({ arrivalSource: null })).toBe('');
      expect(waypointSourceLabel(undefined)).toBe('');
    });
  });

  describe('declarationDistanceWarning', () => {
    it('au-delà de 1 km : « arrivée déclarée à X km de l’étape »', () => {
      expect(declarationDistanceWarning({ driverDeclarationDistanceM: 1850 })).toBe("Arrivée déclarée à 1,9 km de l'étape");
    });

    it('à 1 km ou moins, ou sans déclaration : rien', () => {
      expect(declarationDistanceWarning({ driverDeclarationDistanceM: 1000 })).toBeNull();
      expect(declarationDistanceWarning({ driverDeclarationDistanceM: 40 })).toBeNull();
      expect(declarationDistanceWarning({ driverDeclarationDistanceM: null })).toBeNull();
    });
  });

  describe('trackingBadge — pastille de suivi', () => {
    const now = Date.parse('2026-09-21T10:00:00Z');

    it('boîtier ou téléphone : la source, et l’âge de la dernière position', () => {
      expect(trackingBadge({ source: 'device', positionAgeSeconds: 12 }, now))
        .toEqual({ kind: 'device', label: 'Suivi par boîtier', ageLabel: 'il y a 12 s' });
      expect(trackingBadge({ source: 'phone', positionAgeSeconds: 200 }, now))
        .toEqual({ kind: 'phone', label: 'Suivi par téléphone', ageLabel: 'il y a 3 min' });
    });

    it('interrompu : depuis sourceSince quand le moniteur l’a fixé', () => {
      const b = trackingBadge({ source: 'none', sourceSince: '2026-09-21T09:48:30Z', positionAgeSeconds: 900 }, now);
      expect(b.kind).toBe('lost');
      expect(b.label).toBe('Suivi interrompu depuis 12 min');
      expect(b.ageLabel).toBe('il y a 15 min');
    });

    it('interrompu : sinon depuis l’âge de la dernière position', () => {
      expect(trackingBadge({ source: 'none', sourceSince: null, positionAgeSeconds: 420 }, now).label)
        .toBe('Suivi interrompu depuis 7 min');
    });

    it('interruption longue : heures et minutes', () => {
      expect(trackingBadge({ source: null, positionAgeSeconds: 3900 }, now).label).toBe('Suivi interrompu depuis 1 h 05');
    });

    it('aucune donnée : « en attente » avant le premier appel, « aucune position » ensuite', () => {
      expect(trackingBadge(null, now)).toEqual({ kind: 'none', label: 'Suivi en attente', ageLabel: null });
      expect(trackingBadge({ source: 'none', sourceSince: null, positionAgeSeconds: null }, now).label).toBe('Aucune position reçue');
    });
  });

  describe('durées et batterie', () => {
    it('shortDuration / positionAgeLabel', () => {
      expect(shortDuration(59)).toBe('59 s');
      expect(shortDuration(61)).toBe('1 min');
      expect(shortDuration(3600 * 2 + 60 * 7)).toBe('2 h 07');
      expect(positionAgeLabel(null)).toBeNull();
      expect(positionAgeLabel(90)).toBe('il y a 1 min');
    });

    it('phoneBatteryLabel accepte 0-100 et 0-1', () => {
      expect(phoneBatteryLabel(63)).toBe('63 %');
      expect(phoneBatteryLabel(0.4)).toBe('40 %');
      expect(phoneBatteryLabel(null)).toBeNull();
    });
  });

  describe('état d’envoi (liste) et départ signalé (détail)', () => {
    it('non envoyée / envoyée / ouverte / partie', () => {
      expect(tourSendStatus({ sentAt: null })).toBe('not_sent');
      expect(tourSendStatus({ sentAt: 's' })).toBe('sent');
      expect(tourSendStatus({ sentAt: 's', openedAt: 'o' })).toBe('opened');
      expect(tourSendStatus({ sentAt: 's', openedAt: 'o', actualStartTime: 'a' })).toBe('departed');
      expect(sendStatusLabel('opened')).toBe('Ouverte');
    });

    it('« Départ signalé » seulement quand l’origine a été validée par le chauffeur', () => {
      const t = { actualStartTime: '2026-09-21T08:05:00Z', waypoints: [{ type: 'origin', arrivalSource: 'driver' }, { type: 'destination' }] };
      expect(driverDepartureSignaledAt(t)).toBe('2026-09-21T08:05:00Z');
      expect(driverDepartureSignaledAt({ ...t, waypoints: [{ type: 'origin', arrivalSource: 'device' }] })).toBeNull();
      expect(driverDepartureSignaledAt({ actualStartTime: null, waypoints: t.waypoints })).toBeNull();
    });
  });
});
