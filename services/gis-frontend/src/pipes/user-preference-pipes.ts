import { Pipe, PipeTransform, ChangeDetectorRef, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { UserPreferencesService } from '../services/user-preferences.service';

/** fr-FR number formatting with thousands separators (matches the app's
 *  existing formatNumber helpers so swapping in a pipe never regresses
 *  readability of large values like "1 234 567 km"). */
function fmt(value: number, digits: number): string {
  return value.toLocaleString('fr-FR', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  });
}

/** Espace FINE INSÉCABLE entre la valeur et son unité. Une espace ordinaire est
 *  SÉCABLE : dans une cellule étroite « 2 615,55 » et « TND » tombaient sur deux
 *  lignes (recette de Karim du 19/09/2026 — une valeur reste sur UNE ligne).
 *  C'est aussi le séparateur de milliers de fr-FR : la valeur entière se compose
 *  donc d'une seule et même espace. */
const ESPACE_INSECABLE = '\u202F';

/**
 * Pipes that read from UserPreferencesService and re-emit when the
 * relevant preference changes. Each pipe holds a subscription on the
 * shared prefs$ stream and marks the host component for check whenever
 * the value flips, so a setting change in /settings or /profile is
 * reflected immediately on every page without a manual reload.
 *
 * Each pipe is implemented standalone (no shared abstract base class) —
 * Angular's standalone pipe compiler requires the decorator on the same
 * class definition that holds the lifecycle hooks.
 *
 * Input contracts (always SI / canonical so backend payloads are
 * untouched):
 *   appDistance: kilometres        -> km | mi
 *   appSpeed   : kilometres/hour   -> km/h | mph
 *   appVolume  : litres            -> L | gal
 *   appTemp    : °C                -> °C | °F
 *   appCurrency: numeric amount    -> "1 234,50 TND" (active currency)
 *   appDate    : Date / ISO / epoch -> formatted with active dateFormat
 */

@Pipe({ name: 'appDistance', standalone: true, pure: false })
export class AppDistancePipe implements PipeTransform, OnDestroy {
  private sub: Subscription;
  constructor(private prefs: UserPreferencesService, cdr: ChangeDetectorRef) {
    this.sub = this.prefs.prefs$.subscribe(() => cdr.markForCheck());
  }
  ngOnDestroy() { this.sub?.unsubscribe(); }
  transform(km: number | null | undefined, digits: number = 0): string {
    if (km === null || km === undefined || isNaN(km as number)) return '-';
    const { value, unit } = this.prefs.convertDistance(km);
    return `${fmt(value, digits)}${ESPACE_INSECABLE}${unit}`;
  }
}

@Pipe({ name: 'appSpeed', standalone: true, pure: false })
export class AppSpeedPipe implements PipeTransform, OnDestroy {
  private sub: Subscription;
  constructor(private prefs: UserPreferencesService, cdr: ChangeDetectorRef) {
    this.sub = this.prefs.prefs$.subscribe(() => cdr.markForCheck());
  }
  ngOnDestroy() { this.sub?.unsubscribe(); }
  transform(kmh: number | null | undefined, digits: number = 0): string {
    if (kmh === null || kmh === undefined || isNaN(kmh as number)) return '-';
    const { value, unit } = this.prefs.convertSpeed(kmh);
    return `${fmt(value, digits)}${ESPACE_INSECABLE}${unit}`;
  }
}

@Pipe({ name: 'appVolume', standalone: true, pure: false })
export class AppVolumePipe implements PipeTransform, OnDestroy {
  private sub: Subscription;
  constructor(private prefs: UserPreferencesService, cdr: ChangeDetectorRef) {
    this.sub = this.prefs.prefs$.subscribe(() => cdr.markForCheck());
  }
  ngOnDestroy() { this.sub?.unsubscribe(); }
  transform(liters: number | null | undefined, digits: number = 1): string {
    if (liters === null || liters === undefined || isNaN(liters as number)) return '-';
    const { value, unit } = this.prefs.convertVolume(liters);
    return `${fmt(value, digits)}${ESPACE_INSECABLE}${unit}`;
  }
}

@Pipe({ name: 'appTemp', standalone: true, pure: false })
export class AppTempPipe implements PipeTransform, OnDestroy {
  private sub: Subscription;
  constructor(private prefs: UserPreferencesService, cdr: ChangeDetectorRef) {
    this.sub = this.prefs.prefs$.subscribe(() => cdr.markForCheck());
  }
  ngOnDestroy() { this.sub?.unsubscribe(); }
  transform(celsius: number | null | undefined, digits: number = 0): string {
    if (celsius === null || celsius === undefined || isNaN(celsius as number)) return '-';
    const { value, unit } = this.prefs.convertTemperature(celsius);
    return `${fmt(value, digits)}${unit}`;
  }
}

@Pipe({ name: 'appCurrency', standalone: true, pure: false })
export class AppCurrencyPipe implements PipeTransform, OnDestroy {
  private sub: Subscription;
  constructor(private prefs: UserPreferencesService, cdr: ChangeDetectorRef) {
    this.sub = this.prefs.prefs$.subscribe(() => cdr.markForCheck());
  }
  ngOnDestroy() { this.sub?.unsubscribe(); }
  transform(amount: number | null | undefined, digits: number = 2): string {
    if (amount === null || amount === undefined || isNaN(amount as number)) {
      return this.prefs.formatCurrency(0, digits);
    }
    return this.prefs.formatCurrency(amount, digits);
  }
}

/** Remplaçant français de `x.toFixed(n)` pour un TEXTE affiché (écran, PDF, info-bulle) :
 *  « 12,5 » et « 161 980 » au lieu de « 12.5 » et « 161980 ». Jamais pour une valeur
 *  renvoyée à l'API, à un champ de saisie ou à un tracé SVG, qui attendent le point. */
export function nombreFr(value: number, digits: number = 0): string {
  return Number.isFinite(value) ? fmt(value, digits) : '-';
}

/** Relit le nombre en tête d'un texte affiché, au format français OU anglais :
 *  « 1 234,5 km » → 1234.5, « 12.5 km » → 12.5. parseFloat seul s'arrêtait à l'espace
 *  des milliers (« 1 234 » → 1) et à la virgule (« 12,5 » → 12). */
export function lireNombreFr(value: unknown): number {
  if (typeof value === 'number') return value;
  const texte = String(value ?? '')
    .replace(/(\d)[\u202F\u00A0 ](?=\d{3}(?!\d))/g, '$1')
    .replace(/^(\s*-?\d+),(\d)/, '$1.$2');
  return parseFloat(texte);
}

/** Nombre au format français, sans unité : « 42 383 », « 45,50 ». Le pipe `number`
 *  d'Angular suit la locale de l'application, restée en-US : il écrivait « 42,383 » km,
 *  virgule des milliers à l'anglaise (recette de Karim du 14/10/2026). */
@Pipe({ name: 'appNumber', standalone: true })
export class AppNumberPipe implements PipeTransform {
  transform(value: number | null | undefined, digits: number = 0): string {
    if (value === null || value === undefined || isNaN(value as number)) return '-';
    return fmt(value, digits);
  }
}

@Pipe({ name: 'appDate', standalone: true, pure: false })
export class AppDatePipe implements PipeTransform, OnDestroy {
  private sub: Subscription;
  constructor(private prefs: UserPreferencesService, cdr: ChangeDetectorRef) {
    this.sub = this.prefs.prefs$.subscribe(() => cdr.markForCheck());
  }
  ngOnDestroy() { this.sub?.unsubscribe(); }
  transform(date: Date | string | number | null | undefined): string {
    return this.prefs.formatDate(date);
  }
}

/**
 * Convenience bundle so a standalone component can pull in every
 * preference pipe with a single spread:
 *   imports: [CommonModule, ...USER_PREF_PIPES]
 */
export const USER_PREF_PIPES = [
  AppDistancePipe,
  AppSpeedPipe,
  AppVolumePipe,
  AppTempPipe,
  AppCurrencyPipe,
  AppNumberPipe,
  AppDatePipe,
] as const;
