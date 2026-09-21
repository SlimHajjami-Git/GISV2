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

  constructor(private platform: Platform) {}

  get isNative(): boolean {
    return this.platform.is('capacitor');
  }

  /** La permission de localisation (au premier plan) est-elle déjà accordée ? */
  async isPermissionGranted(): Promise<boolean> {
    try {
      const s = await Geolocation.checkPermissions();
      return s.location === 'granted' || s.coarseLocation === 'granted';
    } catch {
      return false;
    }
  }

  /** Demande la permission système. À n'appeler QU'APRÈS l'écran d'explication (Google Play). */
  async requestPermission(): Promise<boolean> {
    try {
      const s = await Geolocation.requestPermissions({ permissions: ['location'] });
      return s.location === 'granted' || s.coarseLocation === 'granted';
    } catch (e) {
      console.warn('[PhoneLocation] requestPermissions a échoué', e);
      return false;
    }
  }

  /** Ouvre les réglages de l'application (permission refusée définitivement). */
  async openSettings(): Promise<void> {
    try { await BackgroundGeolocation.openSettings(); } catch { /* hors natif */ }
  }

  /**
   * Position courante, ou null si indisponible dans le délai (jamais bloquant).
   * Sans permission déjà accordée, rend null SANS rien demander : l'invite système ne
   * doit jamais apparaître avant l'écran d'explication (LocationConsentService).
   */
  async getCurrentFix(timeoutMs = 8000): Promise<PhoneFix | null> {
    if (!(await this.isPermissionGranted())) return null;
    try {
      const pos = await Geolocation.getCurrentPosition({ enableHighAccuracy: true, timeout: timeoutMs, maximumAge: 15000 });
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
   */
  async startWatch(cb: PhoneFixCallback, options: { title: string; message: string; distanceFilterM: number }): Promise<void> {
    await this.stopWatch();

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
