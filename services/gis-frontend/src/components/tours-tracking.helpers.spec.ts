import {
  declarationDistanceWarning, driverDepartureSignaledAt, driverHasAppAccount, NO_APP_ACCOUNT_TOOLTIP, NO_DRIVER_TOOLTIP,
  openedSinceLastSend, phoneBatteryLabel, phoneMarkerState, positionAgeLabel, sendButtonState, sendErrorMessage,
  sendStatusLabel, sendToast, shortDuration, tourIdFromUrl, tourSendStatus, trackingBadge, waypointSourceLabel, withAppBadge
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

    // Au « Je pars », le serveur force source = 'none' et sourceSince = heure du geste
    // jusqu'au cycle suivant du moniteur (30 s) : ce n'est pas une interruption.
    it('juste après « Je pars », boîtier vivant : « en attente », jamais « interrompu depuis 0 min »', () => {
      const b = trackingBadge({ source: 'none', sourceSince: '2026-09-21T09:59:50Z', deviceAvailable: true, phoneAvailable: false, positionAgeSeconds: 8 }, now);
      expect(b.kind).toBe('none');
      expect(b.label).toBe('Suivi en attente');
      expect(b.ageLabel).toBe('il y a 8 s');
    });

    it('une source émet mais le moniteur ne l’a pas encore retenue : « en attente », même longtemps après la bascule', () => {
      expect(trackingBadge({ source: 'none', sourceSince: '2026-09-21T09:40:00Z', phoneAvailable: true, positionAgeSeconds: 20 }, now).label)
        .toBe('Suivi en attente');
    });

    it('bascule de moins d’une minute sans source vivante (véhicule sans boîtier, premier lot du téléphone pas encore arrivé) : « en attente »', () => {
      expect(trackingBadge({ source: 'none', sourceSince: '2026-09-21T09:59:30Z', deviceAvailable: false, phoneAvailable: false, positionAgeSeconds: null }, now).label)
        .toBe('Suivi en attente');
    });

    it('au-delà de la minute de grâce, sans source vivante : bien « interrompu »', () => {
      const b = trackingBadge({ source: 'none', sourceSince: '2026-09-21T09:58:00Z', deviceAvailable: false, phoneAvailable: false, positionAgeSeconds: 300 }, now);
      expect(b.kind).toBe('lost');
      expect(b.label).toBe('Suivi interrompu depuis 2 min');
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

    // Contrat de bout en bout : entier 0-100 (mobile Math.round(level × 100), serveur short).
    it('phoneBatteryLabel : entier 0-100, un téléphone à 1 % affiche « 1 % » (et non « 100 % »)', () => {
      expect(phoneBatteryLabel(63)).toBe('63 %');
      expect(phoneBatteryLabel(1)).toBe('1 %');
      expect(phoneBatteryLabel(0)).toBe('0 %');
      expect(phoneBatteryLabel(100)).toBe('100 %');
      expect(phoneBatteryLabel(null)).toBeNull();
    });

    it('phoneBatteryLabel borne une valeur hors plage', () => {
      expect(phoneBatteryLabel(140)).toBe('100 %');
      expect(phoneBatteryLabel(-3)).toBe('0 %');
    });
  });

  describe('phoneMarkerState — marqueur du téléphone sur la carte', () => {
    const now = Date.parse('2026-09-21T10:00:00Z');
    const phone = (recordedAt: string) => ({ latitude: 36.8, longitude: 10.1, recordedAt });

    it('le téléphone suit la tournée : marqueur plein, daté', () => {
      expect(phoneMarkerState({ source: 'phone', phoneAvailable: true, phone: phone('2026-09-21T09:59:40Z') }, now))
        .toEqual({ visible: true, live: true, ageLabel: 'il y a 20 s', note: null });
    });

    it('boîtier aux commandes, point « eco » récent : grisé et daté, pas présenté comme du direct', () => {
      const s = phoneMarkerState({ source: 'device', phoneAvailable: true, phone: phone('2026-09-21T09:58:30Z') }, now);
      expect(s.visible).toBe(true);
      expect(s.live).toBe(false);
      expect(s.ageLabel).toBe('il y a 1 min');
      expect(s.note).toBe('Le boîtier suit la tournée');
    });

    it('boîtier aux commandes, point de plus de 3 min : marqueur retiré', () => {
      expect(phoneMarkerState({ source: 'device', phoneAvailable: false, phone: phone('2026-09-21T09:56:00Z') }, now).visible).toBe(false);
      expect(phoneMarkerState({ source: 'device', phone: phone('2026-09-21T06:00:00Z') }, now).visible).toBe(false);
    });

    it('téléphone muet depuis des heures, aucune source : dernière position grisée avec son âge', () => {
      expect(phoneMarkerState({ source: 'none', phoneAvailable: false, phone: phone('2026-09-21T07:30:00Z') }, now))
        .toEqual({ visible: true, live: false, ageLabel: 'il y a 2 h 30', note: 'Position non actualisée' });
    });

    it('source « phone » encore affichée mais téléphone plus vivant : grisé', () => {
      expect(phoneMarkerState({ source: 'phone', phoneAvailable: false, phone: phone('2026-09-21T09:56:00Z') }, now).live).toBe(false);
    });

    it('aucun point : pas de marqueur', () => {
      expect(phoneMarkerState({ source: 'phone', phone: null }, now).visible).toBe(false);
      expect(phoneMarkerState(null, now).visible).toBe(false);
    });
  });

  describe('état d’envoi (liste) et départ signalé (détail)', () => {
    it('non envoyée / envoyée / ouverte / partie', () => {
      expect(tourSendStatus({ sentAt: null })).toBe('not_sent');
      expect(tourSendStatus({ sentAt: '2026-09-21T08:00:00Z' })).toBe('sent');
      expect(tourSendStatus({ sentAt: '2026-09-21T08:00:00Z', openedAt: '2026-09-21T08:05:00Z' })).toBe('opened');
      expect(tourSendStatus({ sentAt: '2026-09-21T08:00:00Z', openedAt: '2026-09-21T08:05:00Z', actualStartTime: '2026-09-21T08:30:00Z' })).toBe('departed');
      expect(sendStatusLabel('opened')).toBe('Ouverte');
    });

    // Ouverte à 08:05, renvoyée (« Tournée mise à jour ») à 10:45 : le serveur garde
    // OpenedAt, la mise à jour n'a pas été vue.
    it('renvoyée après une ouverture : de nouveau « Envoyée », l’ancienne ouverture n’est plus montrée', () => {
      const t = { sentAt: '2026-09-21T10:45:00Z', openedAt: '2026-09-21T08:05:00Z' };
      expect(tourSendStatus(t)).toBe('sent');
      expect(openedSinceLastSend(t)).toBeNull();
    });

    it('ouverte après le dernier envoi (même seconde comprise) : « Ouverte »', () => {
      expect(openedSinceLastSend({ sentAt: '2026-09-21T10:45:00Z', openedAt: '2026-09-21T10:52:00Z' })).toBe('2026-09-21T10:52:00Z');
      expect(openedSinceLastSend({ sentAt: '2026-09-21T10:45:00Z', openedAt: '2026-09-21T10:45:00Z' })).toBe('2026-09-21T10:45:00Z');
      expect(openedSinceLastSend({ sentAt: '2026-09-21T10:45:00Z', openedAt: null })).toBeNull();
    });

    it('« Départ signalé » : l’heure du « Je pars » (DriverDepartedAt de l’origine)', () => {
      const t = {
        actualStartTime: '2026-09-21T08:05:00Z',
        waypoints: [{ type: 'origin', arrivalSource: 'driver', driverDepartedAt: '2026-09-21T08:05:00Z' }, { type: 'destination' }]
      };
      expect(driverDepartureSignaledAt(t)).toBe('2026-09-21T08:05:00Z');
    });

    // 30 s après le « Je pars », le moniteur confirme l'origine par le boîtier ou le
    // téléphone et remplace ArrivalSource : la ligne disparaissait du détail.
    it('toujours affiché après la confirmation de l’origine par le boîtier (arrivalSource = device)', () => {
      const t = {
        actualStartTime: '2026-09-21T08:05:00Z',
        waypoints: [{ type: 'origin', arrivalSource: 'device', driverDepartedAt: '2026-09-21T08:05:00Z' }, { type: 'destination' }]
      };
      expect(driverDepartureSignaledAt(t)).toBe('2026-09-21T08:05:00Z');
    });

    it('départ détecté par le boîtier ou lancé par le gestionnaire (pas de DriverDepartedAt) : rien', () => {
      const detectee = { type: 'origin', arrivalSource: 'device' };
      const gestionnaire = { type: 'origin', arrivalSource: 'manager', driverDepartedAt: null };
      expect(driverDepartureSignaledAt({ waypoints: [detectee] })).toBeNull();
      expect(driverDepartureSignaledAt({ waypoints: [gestionnaire] })).toBeNull();
      expect(driverDepartureSignaledAt({ waypoints: [] })).toBeNull();
      expect(driverDepartureSignaledAt(null)).toBeNull();
    });
  });

  describe('tourIdFromUrl — adresse /tournees/:id', () => {
    it('id du détail, 0 pour la liste, null pour une autre page', () => {
      expect(tourIdFromUrl('/tournees/5')).toBe(5);
      expect(tourIdFromUrl('/tours/12?x=1#top')).toBe(12);
      expect(tourIdFromUrl('/tournees')).toBe(0);
      expect(tourIdFromUrl('/tours/')).toBe(0);
      expect(tourIdFromUrl('/monitoring')).toBeNull();
      expect(tourIdFromUrl('/tournees-archive/5')).toBeNull();
      expect(tourIdFromUrl(null)).toBeNull();
    });
  });
});
