import { Component, OnInit, OnDestroy, HostListener, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription, interval } from 'rxjs';
import { OfflineVehiclesService } from '../../services/offline-vehicles.service';
import { VehicleWithPosition } from '../../services/monitoring-api.service';

/**
 * Header bell showing count of vehicles currently offline.
 * Mirrors the look & feel of the existing notification bell in app-layout.
 * Data source: OfflineVehiclesService (global, populated by monitoring endpoint + SignalR).
 */
@Component({
  selector: 'app-offline-vehicles-bell',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="ov-wrapper">
      <button class="ov-btn"
              [class.has-offline]="offlineCount > 0"
              (click)="toggle($event)"
              [title]="buttonTitle()">
        <!-- Wi-Fi OFF icon (3 arcs + diagonal slash) -->
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <line x1="1" y1="1" x2="23" y2="23"/>
          <path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"/>
          <path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"/>
          <path d="M10.71 5.05A16 16 0 0 1 22.58 9"/>
          <path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88"/>
          <path d="M8.53 16.11a6 6 0 0 1 6.95 0"/>
          <line x1="12" y1="20" x2="12.01" y2="20"/>
        </svg>
        <span class="ov-badge" *ngIf="offlineCount > 0">{{ offlineCount > 9 ? '9+' : offlineCount }}</span>
      </button>

      <div class="ov-dropdown" *ngIf="open" (click)="$event.stopPropagation()">
        <div class="ov-header">
          <h3>Véhicules hors ligne</h3>
          <span class="ov-count" *ngIf="offlineCount > 0">{{ offlineCount }}</span>
        </div>

        <div class="ov-body">
          <div class="ov-list" *ngIf="offlineVehicles.length > 0">
            <div class="ov-item"
                 *ngFor="let v of offlineVehicles; trackBy: trackById"
                 (click)="goToVehicle(v)">
              <div class="ov-icon">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <line x1="1" y1="1" x2="23" y2="23"/>
                  <path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"/>
                  <path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"/>
                  <path d="M10.71 5.05A16 16 0 0 1 22.58 9"/>
                  <path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88"/>
                  <path d="M8.53 16.11a6 6 0 0 1 6.95 0"/>
                  <line x1="12" y1="20" x2="12.01" y2="20"/>
                </svg>
              </div>
              <div class="ov-content">
                <span class="ov-title">{{ v.name || v.plate }}</span>
                <span class="ov-plate" *ngIf="v.name && v.plate !== v.name">{{ v.plate }}</span>
                <span class="ov-time">{{ formatOfflineSince(v) }}</span>
              </div>
            </div>
          </div>

          <div class="ov-empty" *ngIf="offlineVehicles.length === 0">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
              <polyline points="22 4 12 14.01 9 11.01"/>
            </svg>
            <p>Tous les véhicules sont en ligne</p>
          </div>
        </div>

        <div class="ov-footer">
          <a (click)="viewAll()">Voir tous les véhicules</a>
        </div>
      </div>
    </div>
  `,
  styles: [`
    /* Matches .nav-icon-btn from app-layout so the button blends into the toolbar */
    .ov-wrapper {
      position: relative;
    }

    .ov-btn {
      width: 32px;
      height: 32px;
      border-radius: 4px;
      background: transparent;
      border: none;
      color: var(--text-secondary, #666);
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      transition: all 0.15s;
      position: relative;
    }

    .ov-btn:hover {
      background: rgba(0, 0, 0, 0.06);
      color: var(--text-primary, #333);
    }

    :host-context([data-theme="dark"]) .ov-btn:hover {
      background: rgba(255, 255, 255, 0.1);
    }

    .ov-btn.has-offline {
      color: #ef4444;
    }

    .ov-badge {
      position: absolute;
      top: 2px;
      right: 2px;
      min-width: 16px;
      height: 16px;
      padding: 0 4px;
      background: #ef4444;
      color: white;
      font-size: 10px;
      font-weight: 600;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      line-height: 1;
    }

    .ov-dropdown {
      position: absolute;
      top: calc(100% + 8px);
      right: 0;
      width: 360px;
      max-height: 480px;
      background: white;
      border-radius: 8px;
      box-shadow: 0 4px 24px rgba(0, 0, 0, 0.15), 0 0 0 1px rgba(0, 0, 0, 0.05);
      display: flex;
      flex-direction: column;
      z-index: 1001;
      overflow: hidden;
    }

    :host-context([data-theme="dark"]) .ov-dropdown {
      background: #1e293b;
      box-shadow: 0 4px 24px rgba(0, 0, 0, 0.5), 0 0 0 1px rgba(255, 255, 255, 0.08);
    }

    .ov-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 14px 16px;
      border-bottom: 1px solid #e5e7eb;
    }

    :host-context([data-theme="dark"]) .ov-header {
      border-bottom-color: #334155;
    }

    .ov-header h3 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    :host-context([data-theme="dark"]) .ov-header h3 {
      color: #f1f5f9;
    }

    .ov-count {
      background: #ef4444;
      color: white;
      font-size: 11px;
      font-weight: 600;
      padding: 2px 8px;
      border-radius: 10px;
      min-width: 22px;
      text-align: center;
    }

    .ov-body {
      flex: 1;
      overflow-y: auto;
      max-height: 360px;
    }

    .ov-list {
      display: flex;
      flex-direction: column;
    }

    .ov-item {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 12px 16px;
      cursor: pointer;
      transition: background 0.15s;
      border-bottom: 1px solid #f3f4f6;
    }

    :host-context([data-theme="dark"]) .ov-item {
      border-bottom-color: #2a3649;
    }

    .ov-item:last-child {
      border-bottom: none;
    }

    .ov-item:hover {
      background: #fef2f2;
    }

    :host-context([data-theme="dark"]) .ov-item:hover {
      background: rgba(239, 68, 68, 0.08);
    }

    .ov-icon {
      width: 36px;
      height: 36px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
      background: #fee2e2;
      color: #991b1b;
    }

    .ov-content {
      flex: 1;
      min-width: 0;
      display: flex;
      flex-direction: column;
    }

    .ov-title {
      font-size: 13px;
      font-weight: 600;
      color: #1f2937;
      line-height: 1.3;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    :host-context([data-theme="dark"]) .ov-title {
      color: #f1f5f9;
    }

    .ov-plate {
      font-size: 12px;
      color: #6b7280;
      line-height: 1.3;
      margin-top: 2px;
    }

    .ov-time {
      font-size: 11px;
      color: #9ca3af;
      margin-top: 4px;
    }

    .ov-empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 40px 20px;
      color: #10b981;
    }

    .ov-empty svg {
      margin-bottom: 12px;
    }

    .ov-empty p {
      margin: 0;
      font-size: 14px;
      color: #6b7280;
    }

    :host-context([data-theme="dark"]) .ov-empty p {
      color: #cbd5e1;
    }

    .ov-footer {
      padding: 12px 16px;
      border-top: 1px solid #e5e7eb;
      text-align: center;
    }

    :host-context([data-theme="dark"]) .ov-footer {
      border-top-color: #334155;
    }

    .ov-footer a {
      color: #6366f1;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      text-decoration: none;
    }

    .ov-footer a:hover {
      text-decoration: underline;
    }
  `]
})
export class OfflineVehiclesBellComponent implements OnInit, OnDestroy {
  open = false;
  offlineCount = 0;
  offlineVehicles: VehicleWithPosition[] = [];

  private subs: Subscription[] = [];

  constructor(
    private offlineService: OfflineVehiclesService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.subs.push(
      this.offlineService.offlineCount$.subscribe(c => {
        this.offlineCount = c;
        this.cdr.markForCheck();
      })
    );
    this.subs.push(
      this.offlineService.offlineVehicles$.subscribe(v => {
        this.offlineVehicles = v;
        this.cdr.markForCheck();
      })
    );
    // Heartbeat every 10s — re-runs CD so `formatOfflineSince(v)` time labels
    // ("Hors ligne depuis X min") stay visually fresh even between polls.
    this.subs.push(
      interval(10000).subscribe(() => this.cdr.markForCheck())
    );
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    this.offlineService.relax();
  }

  buttonTitle(): string {
    if (this.offlineCount === 0) return 'Tous les véhicules sont en ligne';
    if (this.offlineCount === 1) return '1 véhicule hors ligne';
    return `${this.offlineCount} véhicules hors ligne`;
  }

  toggle(event: Event): void {
    event.stopPropagation();
    this.open = !this.open;
    if (this.open) {
      // Switch service to fast-polling mode (5s) for a live popup
      this.offlineService.intensify();
    } else {
      this.offlineService.relax();
    }
  }

  @HostListener('document:click')
  onDocumentClick(): void {
    if (this.open) {
      this.open = false;
      this.offlineService.relax();
    }
  }

  trackById(_index: number, v: VehicleWithPosition): number {
    return v.id;
  }

  /** Click on a vehicle row → jump to monitoring and focus that vehicle */
  goToVehicle(v: VehicleWithPosition): void {
    this.open = false;
    this.router.navigate(['/monitoring'], { queryParams: { focus: v.id } });
  }

  /** Footer link — show full list */
  viewAll(): void {
    this.open = false;
    this.router.navigate(['/monitoring']);
  }

  /** "Hors ligne depuis 2h", "Hors ligne depuis 3 j", "Dernière position inconnue" */
  formatOfflineSince(v: VehicleWithPosition): string {
    const recordedAt = v.lastPosition?.recordedAt;
    if (!recordedAt) {
      return 'Dernière position inconnue';
    }
    const diffMs = Date.now() - new Date(recordedAt).getTime();
    if (diffMs < 0 || !isFinite(diffMs)) {
      return 'Hors ligne';
    }

    const minutes = Math.floor(diffMs / 60000);
    if (minutes < 1) return 'Hors ligne depuis quelques secondes';
    if (minutes < 60) return `Hors ligne depuis ${minutes} min`;

    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `Hors ligne depuis ${hours} h`;

    const days = Math.floor(hours / 24);
    if (days < 30) return `Hors ligne depuis ${days} j`;

    const months = Math.floor(days / 30);
    return `Hors ligne depuis ${months} mois`;
  }
}
