import { Injectable } from '@angular/core';
import { ToastController } from '@ionic/angular';
import { Capacitor, registerPlugin } from '@capacitor/core';
import { Share } from '@capacitor/share';

/**
 * Partage de la position d'un véhicule vers WhatsApp, Messenger, SMS ou la feuille de
 * partage Android. Le message était recodé à l'identique dans la carte (monitoring) et
 * dans la fiche véhicule : il n'est plus construit qu'ici, pour que les deux écrans ne
 * puissent pas diverger (date, précision du lien, libellés).
 *
 * WhatsApp et Messenger reçoivent le TEXTE COMPLET par un ACTION_SEND adressé à leur
 * paquet (petit plugin natif de l'application, DirectSharePlugin.java) : c'est la voie
 * standard d'Android. Le schéma fb-messenger://share, essayé d'abord, n'est pas documenté
 * par Meta sur Android et n'emportait que le lien (Messenger pouvait s'ouvrir sans rien de
 * prérempli). Application absente : message, puis feuille de partage Android. Le SMS passe
 * par l'URL sms: (application par défaut).
 */

/** Ce que les écrans savent de la position à partager. */
export interface SharedPosition {
  /** Plaque, à défaut nom du véhicule. */
  label: string | null | undefined;
  latitude: number | null | undefined;
  longitude: number | null | undefined;
  address?: string | null;
  /** Horodatage de la position (ISO) : une dernière position connue peut être ancienne. */
  recordedAt?: string | null;
}

export interface PositionMessage {
  title: string;
  /** Texte complet, lignes séparées par « \n ». */
  text: string;
  mapsUrl: string;
}

export type ShareChannel = 'whatsapp' | 'messenger' | 'sms' | 'more';

/**
 * opened    : l'application externe a été lancée (WhatsApp, Messenger, SMS) ;
 * native    : la feuille de partage Android a abouti ;
 * cancelled : l'utilisateur a refermé la feuille de partage (aucun message) ;
 * ignored   : un partage est déjà en cours (double toucher) ;
 * failed    : pas de position utilisable, ou partage impossible (message affiché).
 */
export type ShareOutcome = 'opened' | 'native' | 'cancelled' | 'ignored' | 'failed';

/** Paquets essayés dans l'ordre : WhatsApp, puis WhatsApp Business. */
export const WHATSAPP_PACKAGES = ['com.whatsapp', 'com.whatsapp.w4b'];
/** Messenger (Messenger Lite a quitté le Play Store en 2020). */
export const MESSENGER_PACKAGES = ['com.facebook.orca'];

/** Plugin natif de l'application (android/app/src/main/java/tn/belive/gisv2/DirectSharePlugin.java). */
interface DirectSharePlugin {
  shareToPackage(options: { packageName: string; text: string }): Promise<{ opened: boolean }>;
}
const DirectShare = registerPlugin<DirectSharePlugin>('DirectShare');

/**
 * Une position est partageable si ses deux coordonnées sont des nombres finis, et pas
 * le couple 0,0 : c'est ce qu'envoie un boîtier sans fix, pas un lieu réel.
 */
export function hasKnownPosition(lat: unknown, lng: unknown): boolean {
  if (lat === null || lat === undefined || lng === null || lng === undefined || lat === '' || lng === '') {
    return false;
  }
  const la = Number(lat);
  const lo = Number(lng);
  if (!Number.isFinite(la) || !Number.isFinite(lo)) return false;
  return !(la === 0 && lo === 0);
}

/** Lien Google Maps universel (s'ouvre chez n'importe quel destinataire), 6 décimales ≈ 10 cm. */
export function googleMapsUrl(lat: number, lng: number): string {
  return `https://www.google.com/maps?q=${Number(lat).toFixed(6)},${Number(lng).toFixed(6)}`;
}

/**
 * Le message partagé. La date de la position est TOUJOURS dite : le destinataire ne doit
 * jamais prendre pour du temps réel la dernière trame d'un boîtier muet depuis des jours.
 * Sans horodatage exploitable, on l'écrit plutôt que de laisser croire à une position live.
 */
export function buildPositionMessage(p: SharedPosition): PositionMessage {
  const label = (p.label || '').trim() || 'véhicule';
  const mapsUrl = googleMapsUrl(Number(p.latitude), Number(p.longitude));
  const lines = [`Position du véhicule ${label}`];
  const address = (p.address || '').trim();
  if (address) lines.push(address);
  const recMs = p.recordedAt ? Date.parse(p.recordedAt) : NaN;
  lines.push(isNaN(recMs)
    ? 'Date de la position inconnue'
    : `Position du ${new Date(recMs).toLocaleString('fr-FR')}`);
  lines.push(mapsUrl);
  return { title: `Position ${label}`, text: lines.join('\n'), mapsUrl };
}

/** wa.me : repli HORS application native (navigateur), qui ouvre WhatsApp Web. */
export function whatsappUrl(text: string): string {
  return `https://wa.me/?text=${encodeURIComponent(text)}`;
}

/**
 * Corps de SMS lisible par l'application Messages d'AOSP (base de Google Messages), qui le
 * DÉCODE DEUX FOIS : Uri.getSchemeSpecificPart() rend déjà « %26 » en « & », puis elle
 * découpe sur « & » et repasse URLDecoder.decode. Un « & » coupait donc le SMS (le lien
 * Google Maps, en dernière ligne, disparaissait), un « + » devenait une espace et un « % »
 * faisait planter l'application SMS. On remplace ces trois caractères par des mots plutôt
 * que de doubler l'encodage : une application qui décode une seule fois afficherait sinon
 * « %26 ». Le lien Google Maps n'en contient aucun (chiffres, « . », « , », « - »).
 */
export function smsSafeText(text: string): string {
  return text.split('\n').map(line => line
    .replace(/&/g, ' et ')
    .replace(/\+/g, ' plus ')
    .replace(/%/g, ' pour cent ')
    .replace(/ {2,}/g, ' ')
    .trim()
  ).join('\n');
}

/** Application SMS par défaut, destinataire à choisir, corps prérempli (voir smsSafeText). */
export function smsUrl(text: string): string {
  return `sms:?body=${encodeURIComponent(smsSafeText(text))}`;
}

/**
 * Refermer la feuille de partage n'est pas une erreur (« Share canceled » côté Android,
 * AbortError côté navigateur), pas plus qu'un second toucher pendant qu'elle est ouverte
 * (« Can't share while sharing is in progress »).
 */
export function isSilentShareError(e: unknown): boolean {
  const err = e as { name?: string; message?: string } | null;
  const msg = String(err?.message ?? e ?? '');
  return err?.name === 'AbortError' || /cancel/i.test(msg) || /in progress/i.test(msg);
}

/**
 * Façade des appels natifs. Existe pour être REMPLAÇABLE dans les tests : le mandataire
 * d'un plugin Capacitor intercepte toute lecture de propriété, `spyOn(Share, 'share')`
 * n'a donc aucun effet (même raison que KvStore).
 */
@Injectable({ providedIn: 'root' })
export class ExternalAppLauncher {
  isNative(): boolean {
    return Capacitor.isNativePlatform();
  }

  /** Même voie que le guidage Google Maps / Waze de la tournée : ACTION_VIEW côté Android. */
  open(url: string): void {
    window.open(url, '_system');
  }

  async nativeShare(options: { title: string; text: string; dialogTitle: string }): Promise<void> {
    await Share.share(options);
  }

  /**
   * Envoie le texte à une application précise. Rend faux si elle n'est pas installée, ou
   * si le plugin natif manque (build antérieur) : l'appelant se replie alors.
   */
  async sendToApp(packageName: string, text: string): Promise<boolean> {
    try {
      const r = await DirectShare.shareToPackage({ packageName, text });
      return !!r?.opened;
    } catch {
      return false;
    }
  }
}

@Injectable({ providedIn: 'root' })
export class PositionShareService {
  /** Vrai pendant un partage : un second toucher (double tap) est ignoré. */
  private busy = false;

  constructor(
    private launcher: ExternalAppLauncher,
    private toastCtrl: ToastController
  ) {}

  async share(channel: ShareChannel, position: SharedPosition): Promise<ShareOutcome> {
    if (this.busy) return 'ignored';
    if (!hasKnownPosition(position.latitude, position.longitude)) {
      await this.toast('Aucune position connue pour ce véhicule.', 'warning');
      return 'failed';
    }
    const msg = buildPositionMessage(position);
    this.busy = true;
    try {
      switch (channel) {
        case 'whatsapp':
          return this.launcher.isNative()
            ? await this.sendToAppOrShare(WHATSAPP_PACKAGES, 'WhatsApp', msg)
            : this.openExternal(whatsappUrl(msg.text));
        case 'messenger':
          // Hors application native, aucune voie directe : feuille de partage, sans
          // prétendre que Messenger manque.
          return this.launcher.isNative()
            ? await this.sendToAppOrShare(MESSENGER_PACKAGES, 'Messenger', msg)
            : await this.shareNative(msg);
        case 'sms':
          return this.openExternal(smsUrl(msg.text));
        default:
          return await this.shareNative(msg);
      }
    } finally {
      this.busy = false;
    }
  }

  /**
   * Texte complet envoyé à la première application installée de la liste ; aucune :
   * on le dit, puis feuille de partage Android (l'utilisateur choisit autre chose).
   */
  private async sendToAppOrShare(packages: string[], appName: string, msg: PositionMessage): Promise<ShareOutcome> {
    for (const pkg of packages) {
      if (await this.launcher.sendToApp(pkg, msg.text)) return 'opened';
    }
    await this.toast(`${appName} n'est pas installé`, 'warning');
    return this.shareNative(msg);
  }

  private openExternal(url: string): ShareOutcome {
    try {
      this.launcher.open(url);
      return 'opened';
    } catch {
      void this.toast('Impossible d\'ouvrir l\'application de partage.', 'danger');
      return 'failed';
    }
  }

  private async shareNative(msg: PositionMessage): Promise<ShareOutcome> {
    try {
      await this.launcher.nativeShare({ title: msg.title, text: msg.text, dialogTitle: 'Partager la position' });
      return 'native';
    } catch (e) {
      if (isSilentShareError(e)) return 'cancelled';
      await this.toast('Le partage n\'a pas pu s\'ouvrir sur cet appareil.', 'danger');
      return 'failed';
    }
  }

  private async toast(message: string, color: 'warning' | 'danger'): Promise<void> {
    try {
      const t = await this.toastCtrl.create({ message, duration: 2500, color, position: 'bottom' });
      await t.present();
    } catch { /* un toast manqué ne doit pas bloquer le partage */ }
  }
}
