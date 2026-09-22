import { fakeAsync, flushMicrotasks } from '@angular/core/testing';
import {
  MESSENGER_PACKAGES, PositionShareService, SharedPosition, ShareOutcome, WHATSAPP_PACKAGES,
  buildPositionMessage, googleMapsUrl, hasKnownPosition, isSilentShareError,
  smsSafeText, smsUrl, whatsappUrl
} from './position-share.service';

/**
 * Doublure de ExternalAppLauncher : les mandataires des plugins Capacitor (Share,
 * DirectShare) ne se moquent pas avec spyOn. `installed` liste les paquets présents sur
 * le « téléphone » : sendToApp rend vrai seulement pour eux, comme le plugin natif.
 */
class FakeLauncher {
  native = true;
  installed: string[] = ['com.whatsapp', 'com.facebook.orca'];
  opened: string[] = [];
  sent: { pkg: string; text: string }[] = [];
  shares: { title: string; text: string; dialogTitle: string }[] = [];
  shareResult: () => Promise<void> = async () => {};
  sendDelay: Promise<void> | null = null;

  isNative() { return this.native; }

  open(url: string) { this.opened.push(url); }

  nativeShare(options: { title: string; text: string; dialogTitle: string }) {
    this.shares.push(options);
    return this.shareResult();
  }

  async sendToApp(pkg: string, text: string) {
    this.sent.push({ pkg, text });
    if (this.sendDelay) await this.sendDelay;
    return this.installed.includes(pkg);
  }
}

describe('PositionShareService (partage de la position d\'un véhicule)', () => {
  let launcher: FakeLauncher;
  let toasts: { message: string; color: string }[];
  let service: PositionShareService;

  const recordedAt = '2026-09-22T08:05:30Z';
  const dateLine = `Position du ${new Date(Date.parse(recordedAt)).toLocaleString('fr-FR')}`;
  const pos: SharedPosition = {
    label: '123 TU 4567',
    latitude: 36.8065,
    longitude: 10.1815,
    address: 'Rue d\'Alger & Cie, Tunis',
    recordedAt
  };
  const mapsUrl = 'https://www.google.com/maps?q=36.806500,10.181500';
  const fullText = ['Position du véhicule 123 TU 4567', 'Rue d\'Alger & Cie, Tunis', dateLine, mapsUrl].join('\n');

  beforeEach(() => {
    launcher = new FakeLauncher();
    toasts = [];
    const toastCtrl = {
      create: async (o: any) => { toasts.push({ message: o.message, color: o.color }); return { present: async () => {} }; }
    };
    service = new PositionShareService(launcher as any, toastCtrl as any);
  });

  describe('le message', () => {
    it('plaque, adresse, date locale fr-FR de la position, lien Google Maps à 6 décimales', () => {
      const msg = buildPositionMessage(pos);
      expect(msg.title).toBe('Position 123 TU 4567');
      expect(msg.mapsUrl).toBe(mapsUrl);
      expect(msg.text).toBe(fullText);
      expect(dateLine).toMatch(/^Position du \d{2}\/\d{2}\/2026 \d{2}:\d{2}:\d{2}$/);
    });

    it('sans adresse : pas de ligne vide ; sans plaque : « véhicule »', () => {
      const msg = buildPositionMessage({ ...pos, label: '  ', address: '   ' });
      expect(msg.text).toBe(['Position du véhicule véhicule', dateLine, mapsUrl].join('\n'));
      expect(msg.title).toBe('Position véhicule');
    });

    it('sans date exploitable, le message le dit au lieu de passer pour du temps réel', () => {
      for (const r of [null, undefined, '', 'pas une date']) {
        const lines = buildPositionMessage({ ...pos, recordedAt: r as any }).text.split('\n');
        expect(lines).toContain('Date de la position inconnue');
        expect(lines.some(l => l.startsWith('Position du 2'))).toBeFalse();
      }
    });

    it('lien arrondi à 6 décimales, coordonnées négatives comprises', () => {
      expect(googleMapsUrl(36.12345678, -0.6412349)).toBe('https://www.google.com/maps?q=36.123457,-0.641235');
    });

    it('position connue : nombres finis, pas le 0,0 d\'un boîtier sans fix', () => {
      expect(hasKnownPosition(36.8, 10.1)).toBeTrue();
      expect(hasKnownPosition(0, 10.1)).toBeTrue();
      expect(hasKnownPosition('36.8', '10.1')).toBeTrue();
      expect(hasKnownPosition(0, 0)).toBeFalse();
      expect(hasKnownPosition(null, 10.1)).toBeFalse();
      expect(hasKnownPosition(36.8, undefined)).toBeFalse();
      expect(hasKnownPosition(NaN, 10.1)).toBeFalse();
      expect(hasKnownPosition('', '')).toBeFalse();
    });
  });

  describe('les URL par canal (encodage)', () => {
    it('accents, retours à la ligne, & et espaces sont encodés', () => {
      const t = 'é & à\nb=c?d#e';
      expect(whatsappUrl(t)).toBe('https://wa.me/?text=%C3%A9%20%26%20%C3%A0%0Ab%3Dc%3Fd%23e');
      // SMS : le « & » est d'abord remplacé par « et » (voir le cas AOSP plus bas).
      expect(smsUrl(t)).toBe('sms:?body=%C3%A9%20et%20%C3%A0%0Ab%3Dc%3Fd%23e');
    });

    /**
     * Ce que lit l'application Messages d'AOSP (LaunchConversationActivity.getBody) :
     * Uri.getSchemeSpecificPart() décode une première fois, découpage sur « & », puis
     * URLDecoder.decode (« + » → espace, « % » invalide → exception non attrapée).
     */
    function aospSmsBody(url: string): string | null {
      const ssp = decodeURIComponent(url.slice('sms:'.length));
      if (!ssp.includes('?')) return null;
      for (const p of ssp.slice(ssp.indexOf('?') + 1).split('&')) {
        if (p.startsWith('body=')) return decodeURIComponent(p.slice(5).replace(/\+/g, ' '));
      }
      return null;
    }

    it('SMS lu par AOSP (double décodage) : ni & ni + ni % ne coupent le texte ni ne perdent le lien', () => {
      const text = ['Position du véhicule R&D + remorque', 'Zone 100% industrielle, Ben Arous', dateLine, mapsUrl].join('\n');
      // Avant correctif : le SMS s'arrêtait à « R », le lien Google Maps était perdu.
      expect(aospSmsBody(`sms:?body=${encodeURIComponent(text)}`)).toBe('Position du véhicule R');
      const body = aospSmsBody(smsUrl(text));
      expect(body).toBe(smsSafeText(text));
      expect(body).toBe(['Position du véhicule R et D plus remorque', 'Zone 100 pour cent industrielle, Ben Arous', dateLine, mapsUrl].join('\n'));
      expect(body!.split('\n').pop()).toBe(mapsUrl);
      // Une application qui décode une seule fois lit le même texte : pas de « %26 » affiché.
      expect(decodeURIComponent(smsUrl(text).slice('sms:?body='.length))).toBe(body!);
    });

    it('le nettoyage SMS laisse intacts accents, retours à la ligne et lien (coordonnées négatives comprises)', () => {
      const link = googleMapsUrl(-33.9, -0.64);
      expect(smsSafeText(`Rue d'Alger, Tunis\n${link}`)).toBe(`Rue d'Alger, Tunis\n${link}`);
      expect(smsSafeText('A & B')).toBe('A et B');
    });


    it('WhatsApp : le texte complet part directement à WhatsApp (ACTION_SEND), sans feuille ni lien', async () => {
      const out = await service.share('whatsapp', pos);
      expect(out).toBe('opened');
      expect(launcher.sent).toEqual([{ pkg: 'com.whatsapp', text: fullText }]);
      expect(launcher.opened.length).toBe(0);
      expect(launcher.shares.length).toBe(0);
      expect(toasts.length).toBe(0);
    });

    it('WhatsApp Business seul installé : il reçoit le message', async () => {
      launcher.installed = ['com.whatsapp.w4b'];
      const out = await service.share('whatsapp', pos);
      expect(out).toBe('opened');
      expect(launcher.sent.map(s => s.pkg)).toEqual(WHATSAPP_PACKAGES);
      expect(launcher.shares.length).toBe(0);
    });

    it('WhatsApp absent : message puis feuille de partage Android avec le texte complet', async () => {
      launcher.installed = [];
      const out = await service.share('whatsapp', pos);
      expect(out).toBe('native');
      expect(toasts).toEqual([{ message: "WhatsApp n'est pas installé", color: 'warning' }]);
      expect(launcher.shares).toEqual([{ title: 'Position 123 TU 4567', text: fullText, dialogTitle: 'Partager la position' }]);
    });

    it('WhatsApp hors application native (navigateur) : wa.me avec le message complet encodé', async () => {
      launcher.native = false;
      const out = await service.share('whatsapp', pos);
      expect(out).toBe('opened');
      expect(launcher.sent.length).toBe(0);
      expect(launcher.opened).toEqual([whatsappUrl(fullText)]);
      const q = launcher.opened[0].slice('https://wa.me/?text='.length);
      expect(q).not.toMatch(/[\s&\n]/);            // aucun & ni retour brut qui couperait le paramètre
      expect(decodeURIComponent(q)).toBe(fullText);
    });

    it('SMS : sms:?body= avec le message complet (« & » de l\'adresse écrit « et »)', async () => {
      const out = await service.share('sms', pos);
      expect(out).toBe('opened');
      const smsText = fullText.replace('Alger & Cie', 'Alger et Cie');
      expect(launcher.opened).toEqual([`sms:?body=${encodeURIComponent(smsText)}`]);
      expect(decodeURIComponent(launcher.opened[0].slice('sms:?body='.length))).toBe(smsText);
      expect(aospSmsBody(launcher.opened[0])).toBe(smsText);
    });

    it('Plus : feuille de partage native avec titre, texte complet et titre de dialogue', async () => {
      const out = await service.share('more', pos);
      expect(out).toBe('native');
      expect(launcher.opened.length).toBe(0);
      expect(launcher.shares).toEqual([{ title: 'Position 123 TU 4567', text: fullText, dialogTitle: 'Partager la position' }]);
    });
  });

  describe('Messenger', () => {
    it('installé : le texte COMPLET (plaque, adresse, date, lien) part directement à Messenger', async () => {
      const out = await service.share('messenger', pos);
      expect(out).toBe('opened');
      expect(launcher.sent).toEqual([{ pkg: MESSENGER_PACKAGES[0], text: fullText }]);
      expect(launcher.opened.length).toBe(0);
      expect(launcher.shares.length).toBe(0);
      expect(toasts.length).toBe(0);
    });

    it('absent : message puis feuille de partage Android avec le texte complet', async () => {
      launcher.installed = ['com.whatsapp'];
      const out = await service.share('messenger', pos);
      expect(out).toBe('native');
      expect(toasts).toEqual([{ message: "Messenger n'est pas installé", color: 'warning' }]);
      expect(launcher.shares).toEqual([{ title: 'Position 123 TU 4567', text: fullText, dialogTitle: 'Partager la position' }]);
    });

    it("absent puis feuille native refermée : pas d'autre message que « non installé »", async () => {
      launcher.installed = [];
      launcher.shareResult = () => Promise.reject(new Error('Share canceled'));
      const out = await service.share('messenger', pos);
      expect(out).toBe('cancelled');
      expect(toasts.map(t => t.message)).toEqual(["Messenger n'est pas installé"]);
    });

    it('second toucher pendant un envoi : ignoré (un seul envoi)', fakeAsync(() => {
      let release: () => void = () => {};
      launcher.sendDelay = new Promise<void>(r => { release = r; });
      let first: ShareOutcome | undefined;
      let second: ShareOutcome | undefined;
      service.share('messenger', pos).then(o => first = o);
      flushMicrotasks();
      service.share('whatsapp', pos).then(o => second = o);
      flushMicrotasks();
      expect(second).toBe('ignored');
      expect(launcher.sent.length).toBe(1);
      release();
      flushMicrotasks();
      expect(first).toBe('opened');
    }));

    it('hors application native (navigateur) : feuille de partage directement, sans « non installé »', async () => {
      launcher.native = false;
      const out = await service.share('messenger', pos);
      expect(out).toBe('native');
      expect(launcher.sent.length).toBe(0);
      expect(launcher.opened.length).toBe(0);
      expect(toasts.length).toBe(0);
    });
  });

  describe('annulation et erreurs', () => {
    it('feuille de partage refermée (Android « Share canceled ») : silencieux', async () => {
      launcher.shareResult = () => Promise.reject(new Error('Share canceled'));
      expect(await service.share('more', pos)).toBe('cancelled');
      expect(toasts.length).toBe(0);
    });

    it('feuille refermée côté navigateur (AbortError) ou déjà ouverte : silencieux', async () => {
      launcher.shareResult = () => Promise.reject(new DOMException('Share canceled', 'AbortError'));
      expect(await service.share('more', pos)).toBe('cancelled');
      launcher.shareResult = () => Promise.reject(new Error('Can\'t share while sharing is in progress'));
      expect(await service.share('more', pos)).toBe('cancelled');
      expect(toasts.length).toBe(0);
      expect(isSilentShareError({ message: 'Share API not available in this browser' })).toBeFalse();
    });

    it('partage indisponible : un message d\'erreur, pas un silence', async () => {
      launcher.shareResult = () => Promise.reject(new Error('Share API not available in this browser'));
      expect(await service.share('more', pos)).toBe('failed');
      expect(toasts).toEqual([{ message: 'Le partage n\'a pas pu s\'ouvrir sur cet appareil.', color: 'danger' }]);
    });

    it('sans position exploitable : rien ne s\'ouvre, message d\'avertissement', async () => {
      expect(await service.share('whatsapp', { ...pos, latitude: null })).toBe('failed');
      expect(await service.share('sms', { ...pos, latitude: 0, longitude: 0 })).toBe('failed');
      expect(launcher.opened.length).toBe(0);
      expect(toasts.every(t => t.color === 'warning')).toBeTrue();
    });
  });
});
