import { Injectable } from '@angular/core';
import { Platform } from '@ionic/angular';
import { registerPlugin } from '@capacitor/core';
import { Geolocation } from '@capacitor/geolocation';
import { Device } from '@capacitor/device';
import type { BackgroundGeolocationPlugin, Location } from '@capacitor-community/background-geolocation';

const BackgroundGeolocation = registerPlugin<BackgroundGeolocationPlugin>('BackgroundGeolocation');

/** Une position du téléphone, quelle que soit sa source (plugin natif ou navigateur). */
export interface PhoneFix {
  /** Millisecondes epoch. */
  time: number;
  latitude: number;
  longitude: number;
  accuracyM: number | null;
  /** km/h, null si inconnu. */
  speedKph: number | null;
  heading: number | null;
  isMocked: boolean;
}

export type PhoneFixCallback = (fix: PhoneFix | null, error?: { code?: string; message?: string }) => void;

/**
 * État de la localisation pour le SUIVI :
 *  - granted  : position PRÉCISE accordée, localisation du téléphone allumée ;
 *  - coarse   : seule la position « approximative » est accordée (Android 12+) — ~2 km
 *               de flou, inutilisable (le serveur écarte tout point au-delà de 100 m) ;
 *  - denied   : refusée (définitivement ou pas encore demandée : l'invite peut revenir) ;
 *  - disabled : la localisation du TÉLÉPHONE est coupée (réglages rapides) — ce n'est
 *               pas une question de permission, les réglages de l'application n'y
 *               peuvent rien (constat 19).
 */
export type LocationStatus = 'granted' | 'coarse' | 'denied' | 'disabled';

/** Erreur de @capacitor/geolocation 8 quand la localisation du téléphone est coupée. */
export const GEOLOCATION_DISABLED_CODE = 'OS-PLUG-GLOC-0007';

/** L'erreur dit-elle « localisation du téléphone coupée » (et non « permission refusée ») ? */
export function isLocationDisabledError(e: any): boolean {
  const code = String(e?.code ?? '');
  const message = String(e?.message ?? '');
  return code === GEOLOCATION_DISABLED_CODE
    || /location services (are not enabled|disabled)/i.test(message);
}

/**
 * Accès à la localisation et à la batterie du téléphone, derrière une façade
 * INJECTABLE (les mandataires Capacitor ne se moquent pas, voir KvStore).
 *
 * - Suivi continu : @capacitor-community/background-geolocation, service au premier
 *   plan Android de type « location » avec sa notification permanente (le plugin la
 *   gère : backgroundTitle / backgroundMessage). Hors natif (navigateur), repli sur
 *   Geolocation.watchPosition.
 * - Position ponctuelle (jointe à une déclaration) : @capacitor/geolocation.
 */
@Injectable({ providedIn: 'root' })
export class PhoneLocationService {
  private watcherId: string | null = null;
  private webWatchId: string | null = null;
  /** Destinataire courant des positions (remplaçable sans toucher à l'observateur natif). */
  private callback: PhoneFixCallback | null = null;
  private starting: Promise<void> | null = null;

  constructor(private platform: Platform) {}

  get isNative(): boolean {
    return this.platform.is('capacitor');
  }

  /**
   * État de la localisation (voir LocationStatus). Le suivi exige 'granted' : la position
   * PRÉCISE. Relecture du 21/09/2026 (constats 11 et 21) : on acceptait « approximative »
   * comme accordée — le plugin de suivi, lui, exige FINE et COARSE, rejetait l'observateur
   * mais l'ajoutait quand même ; l'écran affichait « Suivi actif » avec des positions à
   * ~2 km que le serveur écarte toutes.
   */
  async getLocationStatus(): Promise<LocationStatus> {
    try {
      return this.toStatus(await Geolocation.checkPermissions());
    } catch (e) {
      // @capacitor/geolocation 8 REJETTE checkPermissions quand la localisation du
      // téléphone est coupée : ce n'est pas un refus de permission.
      return isLocationDisabledError(e) ? 'disabled' : 'denied';
    }
  }

  /** Position précise accordée ET localisation allumée : le suivi peut tourner. */
  async isPermissionGranted(): Promise<boolean> {
    return (await this.getLocationStatus()) === 'granted';
  }

  /**
   * Demande la permission système (position PRÉCISE : alias « location », FINE + COARSE).
   * Si seule l'approximative est accordée, Android propose alors de passer en « Précise ».
   * À n'appeler QU'APRÈS l'écran d'explication (Google Play).
   */
  async requestPermission(): Promise<LocationStatus> {
    try {
      return this.toStatus(await Geolocation.requestPermissions({ permissions: ['location'] }));
    } catch (e) {
      console.warn('[PhoneLocation] requestPermissions a échoué', e);
      return isLocationDisabledError(e) ? 'disabled' : 'denied';
    }
  }

  private toStatus(s: { location?: string; coarseLocation?: string }): LocationStatus {
    if (s.location === 'granted') return 'granted';
    if (s.coarseLocation === 'granted') return 'coarse';
    return 'denied';
  }

  /**
   * Localisation du téléphone coupée : une demande de position précise fait afficher par
   * Google Play Services la boîte « Activer la localisation ? » (bouton OK), sans quitter
   * l'application. Rend vrai si la localisation est allumée à l'issue.
   */
  async promptEnableLocation(): Promise<boolean> {
    try {
      await Geolocation.getCurrentPosition({ enableHighAccuracy: true, timeout: 10000, maximumAge: 60000 });
    } catch (e) {
      console.warn('[PhoneLocation] activation de la localisation refusée ou impossible', e);
    }
    return (await this.getLocationStatus()) !== 'disabled';
  }

  /** Ouvre la fiche Android de l'APPLICATION (permission refusée, ou « position précise » à cocher). */
  async openSettings(): Promise<void> {
    try { await BackgroundGeolocation.openSettings(); } catch { /* hors natif */ }
  }

  /**
   * Position courante, ou null si indisponible dans le délai (jamais bloquant).
   * `time` est l'heure de la MESURE (pas celle de l'appel). Sans position précise déjà
   * accordée, rend null SANS rien demander : l'invite système ne doit jamais apparaître
   * avant l'écran d'explication (LocationConsentService).
   * `maximumAgeMs` : âge maximal d'une position en cache acceptée par le système.
   */
  async getCurrentFix(timeoutMs = 8000, maximumAgeMs = 15000): Promise<PhoneFix | null> {
    if (!(await this.isPermissionGranted())) return null;
    try {
      const pos = await Geolocation.getCurrentPosition({ enableHighAccuracy: true, timeout: timeoutMs, maximumAge: maximumAgeMs });
      return {
        time: pos.timestamp || Date.now(),
        latitude: pos.coords.latitude,
        longitude: pos.coords.longitude,
        accuracyM: pos.coords.accuracy ?? null,
        speedKph: pos.coords.speed != null ? pos.coords.speed * 3.6 : null,
        heading: pos.coords.heading ?? null,
        isMocked: false
      };
    } catch {
      return null;
    }
  }

  /**
   * Démarre le suivi continu. `distanceFilterM` = déplacement minimal entre deux
   * positions livrées (le service de suivi applique ensuite sa cadence temporelle).
   *
   * UN SEUL observateur : s'il tourne déjà, on ne fait que remplacer le rappel — on ne
   * le retire JAMAIS pour le recréer. Relecture du 21/09/2026 (constats 2 et 4) : retirer
   * le dernier observateur fait rétrograder le service au premier plan (stopForeground),
   * et Android 12+ interdit de le relancer depuis l'arrière-plan
   * (ForegroundServiceStartNotAllowedException, avalée par le plugin) : le suivi mourait
   * en silence à chaque changement de cadence, écran verrouillé.
   */
  async startWatch(cb: PhoneFixCallback, options: { title: string; message: string; distanceFilterM: number }): Promise<void> {
    this.callback = cb;
    if (this.isWatching) return;
    // Deux démarrages simultanés ne doivent pas créer deux observateurs.
    if (!this.starting) {
      this.starting = this.addWatcher(options).finally(() => { this.starting = null; });
    }
    return this.starting;
  }

  private async addWatcher(options: { title: string; message: string; distanceFilterM: number }): Promise<void> {
    if (this.isNative) {
      this.watcherId = await BackgroundGeolocation.addWatcher(
        {
          backgroundTitle: options.title,
          backgroundMessage: options.message,
          // Jamais d'invite système déclenchée par le plugin : la permission se demande
          // uniquement après l'écran d'explication (exigence Google Play).
          requestPermissions: false,
          stale: false,
          distanceFilter: options.distanceFilterM
        },
        (location?: Location, error?: { code?: string; message?: string }) => {
          const cb = this.callback;
          if (!cb) return;
          if (error) { cb(null, error); return; }
          if (!location) return;
          cb({
            time: location.time ?? Date.now(),
            latitude: location.latitude,
            longitude: location.longitude,
            accuracyM: location.accuracy ?? null,
            speedKph: location.speed != null ? location.speed * 3.6 : null,
            heading: location.bearing ?? null,
            isMocked: !!location.simulated
          });
        }
      );
      return;
    }

    // Navigateur (dev) : pas d'arrière-plan, mais la même forme de données.
    this.webWatchId = await Geolocation.watchPosition({ enableHighAccuracy: true }, (pos, err) => {
      const cb = this.callback;
      if (!cb) return;
      if (err || !pos) { cb(null, { message: err?.message }); return; }
      cb({
        time: pos.timestamp || Date.now(),
        latitude: pos.coords.latitude,
        longitude: pos.coords.longitude,
        accuracyM: pos.coords.accuracy ?? null,
        speedKph: pos.coords.speed != null ? pos.coords.speed * 3.6 : null,
        heading: pos.coords.heading ?? null,
        isMocked: false
      });
    });
  }

  async stopWatch(): Promise<void> {
    this.callback = null;
    if (this.watcherId) {
      const id = this.watcherId;
      this.watcherId = null;
      try { await BackgroundGeolocation.removeWatcher({ id }); } catch (e) { console.warn('[PhoneLocation] removeWatcher', e); }
    }
    if (this.webWatchId) {
      const id = this.webWatchId;
      this.webWatchId = null;
      try { await Geolocation.clearWatch({ id }); } catch { /* ignore */ }
    }
  }

  get isWatching(): boolean {
    return !!this.watcherId || !!this.webWatchId;
  }

  /** Niveau de batterie 0..100, ou null si le téléphone ne le donne pas. */
  async getBatteryLevel(): Promise<number | null> {
    try {
      const info = await Device.getBatteryInfo();
      return info.batteryLevel != null ? Math.round(info.batteryLevel * 100) : null;
    } catch {
      return null;
    }
  }
}
