import { TestBed } from '@angular/core/testing';
import { AlertController } from '@ionic/angular';
import { LOCATION_CONSENT_KEY, LocationConsentService } from './location-consent.service';
import { KvStore } from './kv-store.service';
import { PhoneLocationService } from './phone-location.service';
import { FakePhoneLocation, MemoryKvStore } from '../testing/driver-test-doubles';

/**
 * AlertController factice : note chaque boîte (en-tête, message) et « touche » le bouton
 * dont le texte est le prochain du script (sinon le bouton d'annulation).
 */
class FakeAlerts {
  shown: { header?: string; message?: string; buttons: string[] }[] = [];
  script: string[] = [];

  async create(opts: any) {
    const buttons = (opts.buttons || []).map((b: any) => typeof b === 'string' ? { text: b } : b);
    return {
      present: async () => {
        this.shown.push({ header: opts.header, message: opts.message, buttons: buttons.map((b: any) => b.text) });
        const wanted = this.script.shift();
        const btn = buttons.find((b: any) => b.text === wanted) ?? buttons.find((b: any) => b.role === 'cancel') ?? buttons[0];
        btn?.handler?.();
      }
    };
  }
}

describe('LocationConsentService (explication puis permission)', () => {
  let store: MemoryKvStore;
  let loc: FakePhoneLocation;
  let alerts: FakeAlerts;
  let service: LocationConsentService;

  beforeEach(() => {
    store = new MemoryKvStore();
    loc = new FakePhoneLocation();
    alerts = new FakeAlerts();
    TestBed.configureTestingModule({
      providers: [
        LocationConsentService,
        { provide: KvStore, useValue: store },
        { provide: PhoneLocationService, useValue: loc },
        { provide: AlertController, useValue: alerts }
      ]
    });
    service = TestBed.inject(LocationConsentService);
  });

  const headers = () => alerts.shown.map(a => a.header);

  it('déjà acceptée + position précise accordée : rien ne s\'affiche', async () => {
    await store.set(LOCATION_CONSENT_KEY, true);
    loc.status = 'granted';
    expect(await service.ensure()).toBeTrue();
    expect(alerts.shown.length).toBe(0);
  });

  it('première fois : explication PUIS invite système', async () => {
    loc.status = 'denied';
    loc.requestResult = 'granted';
    alerts.script = ['Continuer'];
    expect(await service.ensure()).toBeTrue();
    expect(headers()).toEqual([LocationConsentService.DISCLOSURE_HEADER]);
    expect(loc.requestCalls).toBe(1);
    expect(await store.get(LOCATION_CONSENT_KEY)).toBeTrue();
  });

  it('explication refusée : aucune invite système', async () => {
    loc.status = 'denied';
    alerts.script = ['Refuser'];
    expect(await service.ensure()).toBeFalse();
    expect(loc.requestCalls).toBe(0);
  });

  it('position « approximative » : ce n\'est PAS accordé — nouvelle demande de position précise', async () => {
    await store.set(LOCATION_CONSENT_KEY, true);
    loc.status = 'coarse';
    loc.requestResult = 'granted';            // Android propose « Précise », le chauffeur accepte
    alerts.script = ['Continuer'];
    expect(await service.ensure()).toBeTrue();
    expect(loc.requestCalls).toBe(1);
  });

  it('toujours approximative après la demande : message exact et réglages de l\'APPLICATION', async () => {
    await store.set(LOCATION_CONSENT_KEY, true);
    loc.status = 'coarse';
    loc.requestResult = 'coarse';
    alerts.script = ['Continuer', LocationConsentService.APP_SETTINGS_BUTTON];
    expect(await service.ensure()).toBeFalse();
    const last = alerts.shown[alerts.shown.length - 1];
    expect(last.header).toBe(LocationConsentService.COARSE_HEADER);
    expect(last.message).toContain('Utiliser la position exacte');
    expect(loc.openSettingsCalls).toBe(1);
  });

  it('permission refusée : message et réglages de l\'application', async () => {
    await store.set(LOCATION_CONSENT_KEY, true);
    loc.status = 'denied';
    loc.requestResult = 'denied';
    alerts.script = ['Continuer', LocationConsentService.APP_SETTINGS_BUTTON];
    expect(await service.ensure()).toBeFalse();
    expect(headers()[headers().length - 1]).toBe(LocationConsentService.DENIED_HEADER);
    expect(loc.openSettingsCalls).toBe(1);
  });

  it('localisation du TÉLÉPHONE coupée : « Activer » (boîte système), jamais les réglages de l\'application', async () => {
    await store.set(LOCATION_CONSENT_KEY, true);
    loc.status = 'disabled';
    alerts.script = ['Activer'];
    expect(await service.ensure()).toBeTrue();
    expect(headers()).toEqual([LocationConsentService.DISABLED_HEADER]);
    expect(alerts.shown[0].message).toContain('réglages rapides');
    expect(loc.enablePrompts).toBe(1);
    expect(loc.openSettingsCalls).toBe(0);
    expect(loc.requestCalls).toBe(0);
  });

  it('localisation coupée et boîte refusée : consigne des réglages rapides, faux', async () => {
    await store.set(LOCATION_CONSENT_KEY, true);
    loc.status = 'disabled';
    loc.enableAccepted = false;
    alerts.script = ['Activer'];
    expect(await service.ensure()).toBeFalse();
    expect(alerts.shown[1].message).toBe(LocationConsentService.STILL_DISABLED_MESSAGE);
    expect(loc.openSettingsCalls).toBe(0);
  });

  it('consentement restauré (sauvegarde Android) mais permission absente : l\'explication PRÉCÈDE toujours l\'invite', async () => {
    await store.set(LOCATION_CONSENT_KEY, true);   // restauré d'une sauvegarde après réinstallation
    loc.status = 'denied';
    loc.requestResult = 'granted';
    alerts.script = ['Continuer'];
    expect(await service.ensure()).toBeTrue();
    expect(headers()[0]).toBe(LocationConsentService.DISCLOSURE_HEADER);
    expect(loc.requestCalls).toBe(1);
  });
});
