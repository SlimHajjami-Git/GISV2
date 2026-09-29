import { Component, EventEmitter, Input, Output, ChangeDetectorRef, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService, BatteryHistoryDto } from '../../services/api.service';
import { CHART, ChartGeometry, buildBatteryChart, nearestIndex } from './battery-history-chart.helpers';

/**
 * Fenêtre « santé de la batterie » ouverte quand un admin clique sur la notification
 * « batterie en fin de vie » (Slim, 29/09/2026 : « l'utilisateur verra la chute du
 * voltage à chaque démarrage et comprendra que sa batterie est en train de mourir »).
 *
 * <p>Le graphe montre, sur la période, le creux et le sommet de la tension par tranche :
 * plateau haut quand l'alternateur débite, chute profonde à l'arrêt, et des creux qui
 * s'enfoncent au fil des jours. Les démarrages sont posés dessus — ce sont eux que
 * l'alerte juge — et la ligne rouge est le seuil.</p>
 *
 * <p>Le tracé est du SVG écrit à la main, comme le graphe du tableau de bord : le
 * projet embarque chart.js mais ne s'en sert nulle part, et une dépendance de plus
 * pour six chemins et deux axes ne se justifie pas.</p>
 */
@Component({
  selector: 'app-battery-history-modal',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="bh-overlay" *ngIf="open" (click)="close.emit()">
      <div class="bh-modal" (click)="$event.stopPropagation()">
        <header class="bh-head">
          <div>
            <h2>Santé de la batterie</h2>
            <p class="bh-sub">{{ data?.plate || vehicleLabel || 'Véhicule' }}</p>
          </div>
          <button type="button" class="bh-close" (click)="close.emit()" aria-label="Fermer">×</button>
        </header>

        <div class="bh-body">
          <p class="bh-state" *ngIf="loading">Chargement de la courbe…</p>
          <p class="bh-state bh-err" *ngIf="error">{{ error }}</p>

          <ng-container *ngIf="!loading && !error && data">
            <p class="bh-state" *ngIf="!data.supported">
              Ce véhicule n'a pas de boîtier capable de mesurer la tension batterie.
            </p>
            <p class="bh-state" *ngIf="data.supported && !geometry">
              Aucune mesure sur les {{ data.days }} derniers jours.
            </p>

            <ng-container *ngIf="geometry as g">
              <p class="bh-lede">
                Chaque creux est un arrêt moteur : la batterie seule y montre ce qu'elle vaut.
                <ng-container *ngIf="data.medianV != null">
                  Sur ses derniers démarrages, celle-ci tourne à
                  <b [class.bh-bad]="data.medianV < data.thresholdV">{{ data.medianV.toFixed(1) }} V</b>
                  (seuil {{ data.thresholdV.toFixed(1) }} V).
                </ng-container>
              </p>

              <div class="bh-chart" (mousemove)="onHover($event)" (mouseleave)="hoverIndex = -1">
                <svg [attr.viewBox]="'0 0 ' + CHART.width + ' ' + CHART.height" preserveAspectRatio="none">
                  <line *ngFor="let t of g.yTicks" class="bh-grid"
                        [attr.x1]="CHART.left" [attr.y1]="t.y" [attr.x2]="CHART.right" [attr.y2]="t.y"/>
                  <text *ngFor="let t of g.yTicks" class="bh-ax" text-anchor="end"
                        [attr.x]="CHART.left - 8" [attr.y]="t.y + 4">{{ t.label }}</text>

                  <path [attr.d]="g.bandPath" class="bh-band"/>
                  <path [attr.d]="g.minPath" class="bh-line" fill="none"/>

                  <ng-container *ngIf="g.medianY !== null">
                    <line class="bh-median" [attr.x1]="CHART.left" [attr.y1]="g.medianY"
                          [attr.x2]="CHART.right" [attr.y2]="g.medianY"/>
                  </ng-container>
                  <ng-container *ngIf="g.thresholdY !== null">
                    <line class="bh-threshold" [attr.x1]="CHART.left" [attr.y1]="g.thresholdY"
                          [attr.x2]="CHART.right" [attr.y2]="g.thresholdY"/>
                    <text class="bh-threshold-label" [attr.x]="CHART.right" [attr.y]="g.thresholdY - 6"
                          text-anchor="end">seuil {{ data.thresholdV.toFixed(1) }} V</text>
                  </ng-container>

                  <circle *ngFor="let s of g.starts" class="bh-start" [class.bh-start-low]="s.low"
                          [attr.cx]="s.x" [attr.cy]="s.y" r="3.5">
                    <title>Démarrage du {{ s.label }} — {{ s.volts.toFixed(1) }} V</title>
                  </circle>

                  <ng-container *ngIf="hoverIndex >= 0 && g.hover[hoverIndex] as h">
                    <line class="bh-cursor" [attr.x1]="h.x" [attr.y1]="CHART.top"
                          [attr.x2]="h.x" [attr.y2]="CHART.bottom"/>
                    <circle class="bh-cursor-dot" [attr.cx]="h.x" [attr.cy]="h.minY" r="4"/>
                  </ng-container>

                  <line class="bh-axis" [attr.x1]="CHART.left" [attr.y1]="CHART.bottom"
                        [attr.x2]="CHART.right" [attr.y2]="CHART.bottom"/>
                  <text *ngFor="let t of g.xTicks" class="bh-ax" text-anchor="middle"
                        [attr.x]="t.x" [attr.y]="CHART.bottom + 20">{{ t.label }}</text>
                </svg>

                <div class="bh-tip" *ngIf="hoverIndex >= 0 && g.hover[hoverIndex] as h"
                     [style.left.%]="(h.x / CHART.width) * 100">
                  <span>{{ h.label }}</span>
                  <b>{{ h.minV.toFixed(1) }} V</b>
                  <em *ngIf="h.maxV > h.minV">jusqu'à {{ h.maxV.toFixed(1) }} V</em>
                </div>
              </div>

              <div class="bh-legend">
                <span><i class="sw sw-line"></i> tension la plus basse de la tranche</span>
                <span><i class="sw sw-band"></i> écart entre l'arrêt et l'alternateur</span>
                <span><i class="sw sw-dot"></i> démarrage</span>
                <span><i class="sw sw-thr"></i> seuil d'alerte</span>
              </div>
            </ng-container>
          </ng-container>
        </div>

        <footer class="bh-foot">
          <div class="bh-ranges">
            <button type="button" *ngFor="let d of ranges" [class.on]="days === d"
                    (click)="setDays(d)">{{ d }} j</button>
          </div>
          <button type="button" class="bh-done" (click)="close.emit()">Fermer</button>
        </footer>
      </div>
    </div>
  `,
  styles: [`
    .bh-overlay{position:fixed;inset:0;background:rgba(15,23,42,.55);display:flex;align-items:center;
      justify-content:center;z-index:1200;padding:16px}
    .bh-modal{background:#fff;border-radius:14px;width:min(860px,100%);max-height:92vh;overflow:auto;
      box-shadow:0 24px 60px rgba(15,23,42,.28);display:flex;flex-direction:column}
    .bh-head{display:flex;align-items:flex-start;justify-content:space-between;gap:16px;
      padding:18px 22px 10px}
    .bh-head h2{margin:0;font-size:1.05rem;font-weight:650;color:#0f172a}
    .bh-sub{margin:2px 0 0;font-size:.85rem;color:#64748b}
    .bh-close{border:0;background:transparent;font-size:1.6rem;line-height:1;color:#94a3b8;cursor:pointer;
      padding:0 4px}
    .bh-close:hover{color:#0f172a}
    .bh-body{padding:4px 22px 8px}
    .bh-state{margin:24px 0;text-align:center;color:#64748b;font-size:.9rem}
    .bh-err{color:#b91c1c}
    .bh-lede{margin:0 0 10px;font-size:.86rem;color:#475569;line-height:1.45}
    .bh-bad{color:#b91c1c}
    .bh-chart{position:relative}
    .bh-chart svg{width:100%;height:300px;display:block}
    .bh-grid{stroke:#e2e8f0;stroke-width:1}
    .bh-axis{stroke:#cbd5e1;stroke-width:1.5}
    .bh-ax{fill:#94a3b8;font-size:11px}
    .bh-band{fill:#3b82f6;fill-opacity:.13}
    .bh-line{stroke:#2563eb;stroke-width:2;stroke-linejoin:round;stroke-linecap:round}
    .bh-threshold{stroke:#dc2626;stroke-width:1.5;stroke-dasharray:6 4}
    .bh-threshold-label{fill:#dc2626;font-size:11px}
    .bh-median{stroke:#f59e0b;stroke-width:1.5;stroke-dasharray:2 4}
    .bh-start{fill:#1d4ed8;stroke:#fff;stroke-width:1}
    .bh-start-low{fill:#dc2626}
    .bh-cursor{stroke:#94a3b8;stroke-width:1;stroke-dasharray:3 3}
    .bh-cursor-dot{fill:#2563eb;stroke:#fff;stroke-width:2}
    .bh-tip{position:absolute;top:0;transform:translateX(-50%);background:#0f172a;color:#fff;
      border-radius:8px;padding:6px 10px;font-size:.75rem;display:flex;gap:8px;align-items:baseline;
      pointer-events:none;white-space:nowrap}
    .bh-tip b{font-weight:650}
    .bh-tip em{font-style:normal;opacity:.7}
    .bh-legend{display:flex;flex-wrap:wrap;gap:14px;margin:10px 0 2px;font-size:.75rem;color:#64748b}
    .bh-legend .sw{display:inline-block;width:14px;height:0;vertical-align:middle;margin-right:5px}
    .sw-line{border-top:2px solid #2563eb}
    .sw-band{height:8px;background:rgba(59,130,246,.2);border-radius:2px}
    .sw-dot{height:8px;width:8px;border-radius:50%;background:#dc2626}
    .sw-thr{border-top:2px dashed #dc2626}
    .bh-foot{display:flex;align-items:center;justify-content:space-between;gap:12px;
      padding:10px 22px 18px}
    .bh-ranges{display:flex;gap:6px}
    .bh-ranges button{border:1px solid #e2e8f0;background:#fff;border-radius:8px;padding:5px 11px;
      font-size:.8rem;color:#475569;cursor:pointer}
    .bh-ranges button.on{background:#eff6ff;border-color:#bfdbfe;color:#1d4ed8;font-weight:600}
    .bh-done{border:0;background:#0f172a;color:#fff;border-radius:9px;padding:8px 18px;font-size:.85rem;
      cursor:pointer}
  `]
})
export class BatteryHistoryModalComponent implements OnChanges {
  /** Véhicule dont on trace la batterie. Null ⇒ la fenêtre reste fermée. */
  @Input() vehicleId: number | null = null;
  /** Libellé de repli tant que la réponse n'est pas arrivée (plaque de la notification). */
  @Input() vehicleLabel: string | null = null;
  @Output() close = new EventEmitter<void>();

  readonly CHART = CHART;
  readonly ranges = [3, 7, 14, 30];

  open = false;
  loading = false;
  error: string | null = null;
  days = 7;
  data: BatteryHistoryDto | null = null;
  geometry: ChartGeometry | null = null;
  hoverIndex = -1;

  constructor(private api: ApiService, private cdr: ChangeDetectorRef) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['vehicleId']) return;
    this.open = this.vehicleId != null;
    if (this.open) {
      this.days = 7;
      this.load();
    }
  }

  setDays(d: number) {
    if (this.days === d) return;
    this.days = d;
    this.load();
  }

  onHover(e: MouseEvent) {
    const host = (e.currentTarget as HTMLElement).getBoundingClientRect();
    if (host.width === 0) return;
    const x = ((e.clientX - host.left) / host.width) * CHART.width;
    this.hoverIndex = nearestIndex(this.geometry, x);
  }

  private load() {
    if (this.vehicleId == null) return;
    this.loading = true;
    this.error = null;
    this.hoverIndex = -1;

    this.api.getVehicleBatteryHistory(this.vehicleId, this.days).subscribe({
      next: (dto) => {
        this.data = dto;
        this.geometry = buildBatteryChart(dto);
        this.loading = false;
        // Zoneless (voir main.ts) : sans ce coup de pouce l'écran reste sur
        // « Chargement… » après le retour HTTP.
        this.cdr.detectChanges();
      },
      error: () => {
        this.error = "La courbe n'a pas pu être chargée.";
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }
}
