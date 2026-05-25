import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subscription } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService, TowEventDto } from '../services/api.service';

/**
 * /remorquages — dedicated page listing standalone tow detections
 * (engine-off + speed > 15 km/h + real displacement). Independent of the
 * accident module. Each row can be acknowledged once reviewed.
 */
@Component({
  selector: 'app-towing',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="tow-page">
        <header class="tow-head">
          <div>
            <h1 class="tow-title">Remorquages</h1>
            <p class="tow-sub">
              Détection automatique d'un véhicule déplacé moteur coupé
              (vitesse &gt; 15 km/h + changement de position confirmé sur plusieurs relevés).
            </p>
          </div>
          <button class="tow-refresh" (click)="reload()" [disabled]="loading">
            {{ loading ? 'Chargement…' : 'Actualiser' }}
          </button>
        </header>

        <div class="tow-filters">
          <button class="chip" [class.active]="filter === 'all'" (click)="setFilter('all')">Tous</button>
          <button class="chip" [class.active]="filter === 'active'" (click)="setFilter('active')">En cours</button>
          <button class="chip" [class.active]="filter === 'ended'" (click)="setFilter('ended')">Terminés</button>
          <button class="chip" [class.active]="filter === 'unack'" (click)="setFilter('unack')">À examiner</button>
        </div>

        <div *ngIf="error" class="tow-error">{{ error }}</div>

        <div *ngIf="!loading && events.length === 0" class="tow-empty">
          Aucun remorquage détecté{{ filter !== 'all' ? ' pour ce filtre' : '' }}.
        </div>

        <div class="tow-table" *ngIf="events.length > 0">
          <div class="tow-row tow-row--head">
            <div>Véhicule</div>
            <div>Début</div>
            <div>Statut</div>
            <div>Vitesse max</div>
            <div>Distance</div>
            <div>Lieu de départ</div>
            <div></div>
          </div>

          <div class="tow-row" *ngFor="let e of events" [class.tow-row--ack]="e.acknowledged">
            <div class="tow-veh">
              <span class="tow-veh-name">{{ e.vehicleName || e.vehiclePlate || ('#' + e.vehicleId) }}</span>
              <span class="tow-veh-uid" *ngIf="e.deviceUid">{{ e.deviceUid }}</span>
            </div>
            <div>{{ formatDate(e.startedAt) }}</div>
            <div>
              <span class="badge" [class.badge--active]="e.status === 'active'" [class.badge--ended]="e.status === 'ended'">
                {{ e.status === 'active' ? 'En cours' : 'Terminé' }}
              </span>
            </div>
            <div>{{ round(e.maxSpeedKph) }} km/h</div>
            <div>{{ formatDistance(e.distanceMeters) }}</div>
            <div class="tow-loc">
              <span *ngIf="e.startAddress">{{ e.startAddress }}</span>
              <a class="tow-map" [href]="mapUrl(e)" target="_blank" rel="noopener">
                {{ e.startLat.toFixed(5) }}, {{ e.startLon.toFixed(5) }}
              </a>
            </div>
            <div class="tow-actions">
              <span *ngIf="e.acknowledged" class="tow-ack-done">✓ examiné</span>
              <button *ngIf="!e.acknowledged" class="tow-ack" (click)="acknowledge(e)" [disabled]="ackBusyId === e.id">
                {{ ackBusyId === e.id ? '…' : 'Marquer examiné' }}
              </button>
            </div>
          </div>
        </div>

        <div class="tow-pager" *ngIf="totalPages > 1">
          <button (click)="prev()" [disabled]="page <= 1">‹ Précédent</button>
          <span>Page {{ page }} / {{ totalPages }}</span>
          <button (click)="next()" [disabled]="page >= totalPages">Suivant ›</button>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .tow-page { padding: 24px; max-width: 1200px; margin: 0 auto; }
    .tow-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; margin-bottom: 18px; }
    .tow-title { font-size: 26px; font-weight: 700; margin: 0 0 4px; color: var(--text-primary, #1f2937); }
    .tow-sub { margin: 0; color: var(--text-secondary, #6b7280); font-size: 14px; max-width: 680px; }
    .tow-refresh { padding: 8px 16px; border: 1px solid var(--border-color, #d1d5db); border-radius: 8px;
      background: var(--card-bg, #fff); color: var(--text-primary, #1f2937); cursor: pointer; font-weight: 600; }
    .tow-refresh:disabled { opacity: .6; cursor: default; }

    .tow-filters { display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; }
    .chip { padding: 6px 14px; border-radius: 999px; border: 1px solid var(--border-color, #d1d5db);
      background: var(--card-bg, #fff); color: var(--text-secondary, #6b7280); cursor: pointer; font-size: 13px; font-weight: 600; }
    .chip.active { background: #2563eb; color: #fff; border-color: #2563eb; }

    .tow-error { padding: 12px 16px; background: #fee2e2; color: #b91c1c; border-radius: 8px; margin-bottom: 14px; }
    .tow-empty { padding: 48px; text-align: center; color: var(--text-secondary, #6b7280);
      background: var(--card-bg, #fff); border: 1px dashed var(--border-color, #d1d5db); border-radius: 12px; }

    .tow-table { background: var(--card-bg, #fff); border: 1px solid var(--border-color, #e5e7eb); border-radius: 12px; overflow: hidden; }
    .tow-row { display: grid; grid-template-columns: 1.4fr 1.2fr .9fr .9fr .9fr 1.8fr 1.1fr;
      gap: 12px; align-items: center; padding: 14px 16px; border-bottom: 1px solid var(--border-color, #f1f5f9); font-size: 14px; color: var(--text-primary, #1f2937); }
    .tow-row--head { font-weight: 700; font-size: 12px; text-transform: uppercase; letter-spacing: .04em;
      color: var(--text-secondary, #6b7280); background: var(--bg-subtle, #f9fafb); }
    .tow-row:last-child { border-bottom: none; }
    .tow-row--ack { opacity: .62; }

    .tow-veh { display: flex; flex-direction: column; }
    .tow-veh-name { font-weight: 600; }
    .tow-veh-uid { font-size: 11px; color: var(--text-secondary, #9ca3af); }

    .badge { padding: 3px 10px; border-radius: 999px; font-size: 12px; font-weight: 700; }
    .badge--active { background: #fef3c7; color: #b45309; }
    .badge--ended { background: #e5e7eb; color: #4b5563; }

    .tow-loc { display: flex; flex-direction: column; gap: 2px; font-size: 13px; }
    .tow-map { color: #2563eb; text-decoration: none; font-size: 12px; }
    .tow-map:hover { text-decoration: underline; }

    .tow-actions { text-align: right; }
    .tow-ack { padding: 6px 12px; border: none; border-radius: 8px; background: #2563eb; color: #fff; cursor: pointer; font-weight: 600; font-size: 13px; }
    .tow-ack:disabled { opacity: .6; cursor: default; }
    .tow-ack-done { color: #16a34a; font-weight: 600; font-size: 13px; }

    .tow-pager { display: flex; align-items: center; justify-content: center; gap: 16px; margin-top: 18px; }
    .tow-pager button { padding: 6px 14px; border: 1px solid var(--border-color, #d1d5db); border-radius: 8px;
      background: var(--card-bg, #fff); color: var(--text-primary, #1f2937); cursor: pointer; }
    .tow-pager button:disabled { opacity: .5; cursor: default; }
    .tow-pager span { color: var(--text-secondary, #6b7280); font-size: 14px; }

    @media (max-width: 900px) {
      .tow-row { grid-template-columns: 1fr 1fr; }
      .tow-row--head { display: none; }
      .tow-row > div { padding: 2px 0; }
    }
  `]
})
export class TowingComponent implements OnInit {
  events: TowEventDto[] = [];
  loading = false;
  error: string | null = null;
  ackBusyId: number | null = null;

  filter: 'all' | 'active' | 'ended' | 'unack' = 'all';
  page = 1;
  pageSize = 20;
  totalPages = 1;

  private subs: Subscription[] = [];

  constructor(private api: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void { this.reload(); }

  setFilter(f: 'all' | 'active' | 'ended' | 'unack'): void {
    if (this.filter === f) return;
    this.filter = f;
    this.page = 1;
    this.reload();
  }

  reload(): void {
    this.loading = true;
    this.error = null;
    const opts: any = { page: this.page, pageSize: this.pageSize };
    if (this.filter === 'active') opts.status = 'active';
    else if (this.filter === 'ended') opts.status = 'ended';
    else if (this.filter === 'unack') opts.acknowledged = false;

    const sub = this.api.getTowEvents(opts).subscribe({
      next: (res) => {
        this.events = res.items;
        this.totalPages = res.totalPages || 1;
        this.loading = false;
        this.cdr.markForCheck();
      },
      error: () => {
        this.error = 'Impossible de charger les remorquages.';
        this.loading = false;
        this.cdr.markForCheck();
      },
    });
    this.subs.push(sub);
  }

  acknowledge(e: TowEventDto): void {
    if (this.ackBusyId) return;
    this.ackBusyId = e.id;
    const sub = this.api.acknowledgeTowEvent(e.id).subscribe({
      next: () => {
        e.acknowledged = true;
        this.ackBusyId = null;
        if (this.filter === 'unack') this.events = this.events.filter((x) => x.id !== e.id);
        this.cdr.markForCheck();
      },
      error: () => { this.ackBusyId = null; this.cdr.markForCheck(); },
    });
    this.subs.push(sub);
  }

  prev(): void { if (this.page > 1) { this.page--; this.reload(); } }
  next(): void { if (this.page < this.totalPages) { this.page++; this.reload(); } }

  round(v: number): number { return Math.round(v); }

  formatDistance(meters: number): string {
    if (meters >= 1000) return (meters / 1000).toFixed(1) + ' km';
    return Math.round(meters) + ' m';
  }

  formatDate(iso: string): string {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '—';
    return d.toLocaleString('fr-FR', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  mapUrl(e: TowEventDto): string {
    return `https://www.openstreetmap.org/?mlat=${e.startLat}&mlon=${e.startLon}#map=16/${e.startLat}/${e.startLon}`;
  }

  ngOnDestroy(): void { this.subs.forEach((s) => s.unsubscribe()); }
}
