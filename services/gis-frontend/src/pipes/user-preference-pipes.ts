import { Pipe, PipeTransform, ChangeDetectorRef, OnDestroy } from '@angular/core';
import { Subscription } from 'rxjs';
import { UserPreferencesService } from '../services/user-preferences.service';

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
    return `${value.toFixed(digits)} ${unit}`;
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
    return `${value.toFixed(digits)} ${unit}`;
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
    return `${value.toFixed(digits)} ${unit}`;
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
    return `${value.toFixed(digits)}${unit}`;
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
