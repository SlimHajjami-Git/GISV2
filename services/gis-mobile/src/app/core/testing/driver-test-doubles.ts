import { PhoneFix, PhoneFixCallback } from '../services/phone-location.service';

/**
 * Doublures partagées par les specs de l'espace chauffeur. Les mandataires des plugins
 * Capacitor ne se moquent pas avec spyOn (voir KvStore) : les services passent par des
 * façades injectables, remplacées ici par des versions en mémoire.
 */

/** KvStore en mémoire (remplace @capacitor/preferences). */
export class MemoryKvStore {
  readonly data = new Map<string, string>();

  async get<T>(key: string): Promise<T | null> {
    const v = this.data.get(key);
    return v === undefined ? null : (JSON.parse(v) as T);
  }

  async set<T>(key: string, value: T): Promise<void> {
    this.data.set(key, JSON.stringify(value));
  }

  async remove(key: string): Promise<void> {
    this.data.delete(key);
  }

  /** Lecture synchrone pour les assertions. */
  peek<T>(key: string): T | null {
    const v = this.data.get(key);
    return v === undefined ? null : (JSON.parse(v) as T);
  }
}

/** PhoneLocationService factice : capteur piloté par le test. */
export class FakePhoneLocation {
  permissionGranted = true;
  batteryLevel: number | null = 80;
  currentFix: PhoneFix | null = null;
  watchCallback: PhoneFixCallback | null = null;
  watchOptions: { title: string; message: string; distanceFilterM: number } | null = null;
  startWatchCalls = 0;
  stopWatchCalls = 0;
  isNative = true;

  async isPermissionGranted() { return this.permissionGranted; }
  async requestPermission() { return this.permissionGranted; }
  async openSettings() { /* rien */ }
  async getCurrentFix() { return this.currentFix; }
  async getBatteryLevel() { return this.batteryLevel; }

  async startWatch(cb: PhoneFixCallback, options: { title: string; message: string; distanceFilterM: number }) {
    this.startWatchCalls++;
    this.watchCallback = cb;
    this.watchOptions = options;
  }

  async stopWatch() {
    this.stopWatchCalls++;
    this.watchCallback = null;
  }

  get isWatching() { return this.watchCallback !== null; }
}

/** Une position d'essai ; `dLat` décale vers le nord (0,001° ≈ 111 m). */
export function fixAt(dLat = 0, timeMs = Date.now()): PhoneFix {
  return {
    time: timeMs,
    latitude: 36.8 + dLat,
    longitude: 10.18,
    accuracyM: 8,
    speedKph: 40,
    heading: 90,
    isMocked: false
  };
}
