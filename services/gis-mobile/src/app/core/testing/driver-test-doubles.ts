import { LocationStatus, PhoneFix, PhoneFixCallback } from '../services/phone-location.service';

/**
 * Doublures partagées par les specs de l'espace chauffeur. Les mandataires des plugins
 * Capacitor ne se moquent pas avec spyOn (voir KvStore) : les services passent par des
 * façades injectables, remplacées ici par des versions en mémoire.
 */

/** KvStore en mémoire (remplace @capacitor/preferences) ; compte les écritures. */
export class MemoryKvStore {
  readonly data = new Map<string, string>();
  /** Nombre d'écritures par clé (le coût de persistance est une exigence testée). */
  readonly writes = new Map<string, number>();

  async get<T>(key: string): Promise<T | null> {
    const v = this.data.get(key);
    return v === undefined ? null : (JSON.parse(v) as T);
  }

  async set<T>(key: string, value: T): Promise<void> {
    this.data.set(key, JSON.stringify(value));
    this.writes.set(key, (this.writes.get(key) ?? 0) + 1);
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
  /** État de la localisation (position précise, approximative, refusée, coupée). */
  status: LocationStatus = 'granted';
  /** Ce que rend la demande de permission système. */
  requestResult: LocationStatus | null = null;
  /** La boîte « Activer la localisation » est acceptée. */
  enableAccepted = true;
  batteryLevel: number | null = 80;
  currentFix: PhoneFix | null = null;
  /** Arguments des demandes de position ponctuelle (délai, âge maximal). */
  fixRequests: { timeoutMs?: number; maximumAgeMs?: number }[] = [];
  watchCallback: PhoneFixCallback | null = null;
  watchOptions: { title: string; message: string; distanceFilterM: number } | null = null;
  startWatchCalls = 0;
  stopWatchCalls = 0;
  requestCalls = 0;
  enablePrompts = 0;
  openSettingsCalls = 0;
  isNative = true;
  private watching = false;

  /** Raccourci des anciens tests : permission (précise) accordée ou refusée. */
  set permissionGranted(v: boolean) { this.status = v ? 'granted' : 'denied'; }
  get permissionGranted() { return this.status === 'granted'; }

  async getLocationStatus() { return this.status; }
  async isPermissionGranted() { return this.status === 'granted'; }
  async requestPermission(): Promise<LocationStatus> {
    this.requestCalls++;
    if (this.requestResult) this.status = this.requestResult;
    return this.status;
  }
  async promptEnableLocation() {
    this.enablePrompts++;
    if (this.enableAccepted && this.status === 'disabled') this.status = 'granted';
    return this.status !== 'disabled';
  }
  async openSettings() { this.openSettingsCalls++; }
  async getCurrentFix(timeoutMs?: number, maximumAgeMs?: number) {
    this.fixRequests.push({ timeoutMs, maximumAgeMs });
    return this.status === 'granted' ? this.currentFix : null;
  }
  async getBatteryLevel() { return this.batteryLevel; }

  async startWatch(cb: PhoneFixCallback, options: { title: string; message: string; distanceFilterM: number }) {
    this.watchCallback = cb;
    if (this.watching) return;       // même contrat que le vrai : un seul observateur
    this.startWatchCalls++;
    this.watching = true;
    this.watchOptions = options;
  }

  async stopWatch() {
    if (this.watching) this.stopWatchCalls++;
    this.watching = false;
    this.watchCallback = null;
  }

  get isWatching() { return this.watching; }
}

/** AuthService factice : seul le compte connecté compte pour les files hors ligne. */
export class FakeAuth {
  userId: string | null = '42';
  ready = Promise.resolve();
  currentUserId() { return this.userId; }
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
