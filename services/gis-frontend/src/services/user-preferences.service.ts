import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

/**
 * Centralised store for every operator-facing display preference.
 *
 * Calypso 9 p5 — the /profile and /settings pages were saving values to
 * localStorage but nothing read them back, so changing the map style,
 * currency, distance unit, etc. had no visible effect ("Les modifs ne
 * fonctionnent pas"). This service owns the state, persists it on every
 * update, and emits via `prefs$` so consumers can re-render reactively.
 *
 * It also applies cross-cutting side-effects:
 *   - `animations` toggles the `no-animations` class on <body> so a
 *     single global CSS rule can disable transitions everywhere.
 *   - `theme` is mirrored on `document.body.dataset.theme` (light/dark/auto).
 */

export type Language = 'fr' | 'en' | 'ar';
export type Currency = 'TND' | 'MAD' | 'DZD' | 'EUR' | 'USD' | 'SAR' | 'AED';
export type DateFormat = 'dd/MM/yyyy' | 'MM/dd/yyyy' | 'yyyy-MM-dd';
export type DistanceUnit = 'km' | 'mi';
export type SpeedUnit = 'kmh' | 'mph';
export type VolumeUnit = 'L' | 'gal';
export type TemperatureUnit = 'C' | 'F';
export type MapStyle = 'streets' | 'satellite' | 'hybrid' | 'terrain';
export type Theme = 'light' | 'dark' | 'auto';

export interface UserPreferences {
  // Regional
  language: Language;
  timezone: string;
  currency: Currency;
  dateFormat: DateFormat;
  // Units
  distanceUnit: DistanceUnit;
  speedUnit: SpeedUnit;
  volumeUnit: VolumeUnit;
  temperatureUnit: TemperatureUnit;
  // Map
  mapStyle: MapStyle;
  showVehicleLabels: boolean;
  clustering: boolean;
  // Dashboard
  refreshInterval: number; // seconds (0 = manual only)
  animations: boolean;
  theme: Theme;
}

const STORAGE_KEY = 'userPreferences';
// Legacy keys read once on first init so existing operators keep their
// choices instead of getting a hard reset to defaults.
const LEGACY_PROFILE_KEY = 'userProfilePreferences';
const LEGACY_SETTINGS_KEY = 'appSettings';

const DEFAULTS: UserPreferences = {
  language: 'fr',
  timezone: 'Africa/Tunis',
  currency: 'TND',
  dateFormat: 'dd/MM/yyyy',
  distanceUnit: 'km',
  speedUnit: 'kmh',
  volumeUnit: 'L',
  temperatureUnit: 'C',
  mapStyle: 'streets',
  showVehicleLabels: true,
  clustering: false,
  refreshInterval: 30,
  animations: true,
  theme: 'light',
};

@Injectable({ providedIn: 'root' })
export class UserPreferencesService {
  private readonly _prefs$ = new BehaviorSubject<UserPreferences>(this.load());

  /** Hot stream of the full preferences object. Emits on every `update()`. */
  readonly prefs$ = this._prefs$.asObservable();

  constructor() {
    // Apply once at startup so the no-animations class / theme attribute
    // are correct even before anyone calls update().
    this.applyGlobals(this._prefs$.value);
  }

  /** Snapshot — handy for non-reactive consumers (one-off Leaflet config, etc.) */
  get current(): UserPreferences {
    return this._prefs$.value;
  }

  /**
   * Patch any subset of preferences. Persists to localStorage, emits the
   * new full object, and re-applies global side-effects (theme/animations).
   */
  update(patch: Partial<UserPreferences>): void {
    const next: UserPreferences = { ...this._prefs$.value, ...patch };
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // Ignore quota errors — in-memory state still updates.
    }
    this._prefs$.next(next);
    this.applyGlobals(next);
  }

  /** Wipe customisations back to factory defaults. */
  reset(): void {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {}
    this._prefs$.next({ ...DEFAULTS });
    this.applyGlobals(DEFAULTS);
  }

  // ---------------------------------------------------------------------------
  //  Conversion helpers — single source of truth for unit math.
  //  Each returns the converted value PLUS the display label so callers don't
  //  have to read the unit field separately.
  // ---------------------------------------------------------------------------

  convertDistance(km: number | null | undefined): { value: number; unit: string } {
    const n = km ?? 0;
    if (this.current.distanceUnit === 'mi') {
      return { value: n * 0.621371, unit: 'mi' };
    }
    return { value: n, unit: 'km' };
  }

  convertSpeed(kmh: number | null | undefined): { value: number; unit: string } {
    const n = kmh ?? 0;
    if (this.current.speedUnit === 'mph') {
      return { value: n * 0.621371, unit: 'mph' };
    }
    return { value: n, unit: 'km/h' };
  }

  convertVolume(liters: number | null | undefined): { value: number; unit: string } {
    const n = liters ?? 0;
    if (this.current.volumeUnit === 'gal') {
      return { value: n * 0.264172, unit: 'gal' };
    }
    return { value: n, unit: 'L' };
  }

  convertTemperature(celsius: number | null | undefined): { value: number; unit: string } {
    const n = celsius ?? 0;
    if (this.current.temperatureUnit === 'F') {
      return { value: n * 9 / 5 + 32, unit: '°F' };
    }
    return { value: n, unit: '°C' };
  }

  // ---------------------------------------------------------------------------
  //  Formatters
  // ---------------------------------------------------------------------------

  /**
   * `1234.5 -> "1 234,50 TND"` (with the user's currency code).
   * The currency code is appended as a suffix rather than the locale's
   * native symbol because most clients ship Tunisian-only screens and
   * the abbreviation reads clearer than a glyph.
   */
  formatCurrency(amount: number | null | undefined, digits: number = 2): string {
    const n = amount ?? 0;
    const formatted = n.toLocaleString('fr-FR', {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    });
    return `${formatted} ${this.current.currency}`;
  }

  /**
   * Apply the current `dateFormat` preference. Accepts Date, ISO string,
   * or null/undefined; returns "-" for falsy / unparseable input.
   */
  formatDate(date: Date | string | number | null | undefined): string {
    if (date === null || date === undefined || date === '') return '-';
    const d = date instanceof Date ? date : new Date(date);
    if (isNaN(d.getTime())) return '-';
    const dd = String(d.getDate()).padStart(2, '0');
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const yyyy = d.getFullYear();
    switch (this.current.dateFormat) {
      case 'MM/dd/yyyy': return `${mm}/${dd}/${yyyy}`;
      case 'yyyy-MM-dd': return `${yyyy}-${mm}-${dd}`;
      default: return `${dd}/${mm}/${yyyy}`;
    }
  }

  // ---------------------------------------------------------------------------
  //  Internals
  // ---------------------------------------------------------------------------

  private load(): UserPreferences {
    // 1. Preferred location (current key)
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        return { ...DEFAULTS, ...JSON.parse(raw) };
      }
    } catch {}

    // 2. Migrate from the two legacy keys so first-time use of the new
    //    service inherits the operator's previously-saved (but inert) values.
    const merged: Partial<UserPreferences> = {};
    try {
      const legacyProfile = localStorage.getItem(LEGACY_PROFILE_KEY);
      if (legacyProfile) {
        const p = JSON.parse(legacyProfile);
        if (p.language) merged.language = p.language;
        if (p.timezone) merged.timezone = p.timezone;
        if (p.currency) merged.currency = p.currency;
        if (p.dateFormat) merged.dateFormat = p.dateFormat;
        if (p.distanceUnit) merged.distanceUnit = p.distanceUnit;
        if (p.speedUnit) merged.speedUnit = p.speedUnit;
        if (p.volumeUnit) merged.volumeUnit = p.volumeUnit;
        if (p.temperatureUnit) merged.temperatureUnit = p.temperatureUnit;
      }
    } catch {}
    try {
      const legacySettings = localStorage.getItem(LEGACY_SETTINGS_KEY);
      if (legacySettings) {
        const s = JSON.parse(legacySettings);
        if (s.display) {
          if (s.display.mapStyle) merged.mapStyle = s.display.mapStyle;
          if (typeof s.display.showVehicleLabels === 'boolean') merged.showVehicleLabels = s.display.showVehicleLabels;
          if (typeof s.display.clustering === 'boolean') merged.clustering = s.display.clustering;
          if (s.display.refreshInterval !== undefined) merged.refreshInterval = Number(s.display.refreshInterval);
          if (typeof s.display.animations === 'boolean') merged.animations = s.display.animations;
          if (s.display.theme) merged.theme = s.display.theme;
        }
      }
    } catch {}

    return { ...DEFAULTS, ...merged };
  }

  private applyGlobals(prefs: UserPreferences): void {
    if (typeof document === 'undefined') return;
    document.body.classList.toggle('no-animations', !prefs.animations);
    if (prefs.theme) {
      document.body.dataset['theme'] = prefs.theme;
    }
  }
}
