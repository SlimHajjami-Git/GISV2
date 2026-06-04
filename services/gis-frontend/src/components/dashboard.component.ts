import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { SignalRService } from '../services/signalr.service';
import { Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { DateFilterBarComponent } from './shared/ui';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, DateFilterBarComponent],
  template: `
<app-layout>
<div class="dash">
  <div class="dash-bg"></div>
  <div class="dash-inner">

    <!-- ════ HEADER ════ -->
    <header class="page-head reveal" style="--d:0">
      <div class="ph-left">
        <div class="ph-eyebrow">
          <span class="live" [class.on]="isConnected">
            <span class="live-dot"></span>{{ isConnected ? 'En direct' : 'Hors ligne' }}
          </span>
          <span class="ph-sep">·</span>
          <span class="ph-company">{{ company?.name || 'Flotte' }}</span>
        </div>
        <h1 class="ph-title">Tableau de bord</h1>
        <p class="ph-date">{{ todayLabel }}</p>
      </div>
      <div class="ph-right">
        <ui-date-filter-bar [selectedPeriod]="selectedPeriod" [fromDate]="fromDate" [toDate]="toDate" [periods]="periodsFr"
          (periodChange)="onPeriodChange($event)" (dateRangeChange)="onDateRangeChange($event)" (applyFilter)="applyFilter()">
        </ui-date-filter-bar>
      </div>
    </header>

    <!-- ════ KPI STRIP ════ -->
    <div class="kpis reveal" style="--d:1">
      <div class="kpi" style="--c:var(--primary)">
        <div class="kpi-ic">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="1" y="3" width="15" height="13" rx="1.5"/><path d="M16 8h4l3 4v4h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
        </div>
        <div class="kpi-meta"><span class="kpi-label">Véhicules</span><span class="kpi-num">{{ dVehicles }}</span></div>
      </div>
      <div class="kpi" style="--c:#10b981">
        <div class="kpi-ic">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="3"/><path d="M5 12a7 7 0 0 1 7-7"/><path d="M19 12a7 7 0 0 1-7 7"/></svg>
        </div>
        <div class="kpi-meta"><span class="kpi-label">En circulation</span><span class="kpi-num">{{ dMoving }}</span></div>
      </div>
      <div class="kpi" style="--c:#64748b">
        <div class="kpi-ic">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="4" y="4" width="6" height="16" rx="1.5"/><rect x="14" y="4" width="6" height="16" rx="1.5"/></svg>
        </div>
        <div class="kpi-meta"><span class="kpi-label">À l'arrêt</span><span class="kpi-num">{{ dStopped }}</span></div>
      </div>
      <div class="kpi" style="--c:#0ea5e9">
        <div class="kpi-ic">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M3 22V5a2 2 0 0 1 2-2h7a2 2 0 0 1 2 2v17"/><path d="M14 11h2a2 2 0 0 1 2 2v4a2 2 0 0 0 3 0V9l-3-3"/><path d="M3 11h11"/></svg>
        </div>
        <div class="kpi-meta"><span class="kpi-label">Carburant</span><span class="kpi-num">{{ dFuel }}<i>L</i></span></div>
      </div>
      <div class="kpi" style="--c:#f59e0b">
        <div class="kpi-ic">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="2" y="6" width="20" height="13" rx="2.5"/><path d="M2 10h20"/></svg>
        </div>
        <div class="kpi-meta"><span class="kpi-label">Coût total</span><span class="kpi-num">{{ dCost | number:'1.0-0' }}<i>DT</i></span></div>
      </div>
      <div class="kpi" style="--c:#ef4444" [class.kpi-alert]="dAlerts>0">
        <div class="kpi-ic">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.7 21a2 2 0 0 1-3.4 0"/></svg>
        </div>
        <div class="kpi-meta"><span class="kpi-label">Alertes</span><span class="kpi-num">{{ dAlerts }}</span></div>
      </div>
    </div>

    <!-- ════ MAIN GRID ════ -->
    <div class="grid">

      <!-- État flotte -->
      <section class="card c4 reveal" style="--d:2">
        <div class="card-h"><h3>État de la flotte</h3></div>
        <div class="fleet">
          <div class="donut">
            <svg viewBox="0 0 160 160">
              <circle cx="80" cy="80" r="62" fill="none" class="donut-track" stroke-width="13"/>
              <circle *ngFor="let s of donutSegs;let si=index" cx="80" cy="80" r="62" fill="none"
                [attr.stroke]="s.color" [attr.stroke-width]="hSeg===si?17:13" stroke-linecap="round"
                [attr.stroke-dasharray]="s.da2+' '+(circ62-s.da2)" [attr.stroke-dashoffset]="s.offset2"
                class="donut-seg" (mouseenter)="hSeg=si" (mouseleave)="hSeg=-1"/>
            </svg>
            <div class="donut-c">
              <span class="donut-n">{{ totalMotion }}</span>
              <span class="donut-l">véhicules</span>
            </div>
          </div>
          <div class="legend">
            <div *ngFor="let s of fleetSegs" class="leg-row" (mouseenter)="hSeg=donutIdx(s.name)" (mouseleave)="hSeg=-1" [class.hl]="hSeg>=0 && donutSegs[hSeg]?.name===s.name">
              <span class="leg-dot" [style.background]="s.color"></span>
              <span class="leg-name">{{ s.name }}</span>
              <b class="leg-val">{{ s.value }}</b>
            </div>
          </div>
        </div>
      </section>

      <!-- Activité / consommation -->
      <section class="card c8 reveal" style="--d:3">
        <div class="card-h">
          <h3>Consommation carburant</h3>
          <span class="badge badge-primary">{{ totalFuelConsumed | number:'1.0-0' }} L</span>
        </div>
        <div class="chart" (mousemove)="onChartHover($event)" (mouseleave)="cIdx=-1">
          <svg viewBox="0 0 500 140" preserveAspectRatio="none" class="chart-svg">
            <defs>
              <linearGradient id="cFill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stop-color="#6366f1" stop-opacity=".22"/>
                <stop offset="100%" stop-color="#6366f1" stop-opacity="0"/>
              </linearGradient>
            </defs>
            <line *ngFor="let y of [0,35,70,105,140]" x1="0" [attr.y1]="y" x2="500" [attr.y2]="y" class="chart-grid"/>
            <polygon *ngIf="cPts" [attr.points]="'0,140 '+cPts+' 500,140'" fill="url(#cFill)" class="chart-area"/>
            <polyline *ngIf="cPts" fill="none" stroke="#6366f1" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round" [attr.points]="cPts" class="chart-line"/>
            <ng-container *ngFor="let p of cPoints;let pi=index">
              <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="cIdx===pi?5:0" fill="#6366f1" class="chart-pt"/>
            </ng-container>
            <line *ngIf="cIdx>=0&&cPoints[cIdx]" [attr.x1]="cPoints[cIdx].x" y1="0" [attr.x2]="cPoints[cIdx].x" y2="140" class="chart-cursor"/>
          </svg>
          <div class="chart-x"><span *ngFor="let l of cLabels">{{ l }}</span></div>
          <div class="chart-tip" *ngIf="cIdx>=0&&cVals[cIdx]!==undefined" [style.left.%]="cPoints[cIdx]?(cPoints[cIdx].x/5):0">
            <b>{{ cVals[cIdx]|number:'1.0-0' }} L</b><span>{{ cLabels[cIdx]||'' }}</span>
          </div>
        </div>
      </section>

      <!-- Santé véhicules -->
      <section class="card c4 reveal" style="--d:4">
        <div class="card-h"><h3>Santé des véhicules</h3></div>
        <div class="health">
          <div class="gauge">
            <svg viewBox="0 0 160 160">
              <circle cx="80" cy="80" r="64" fill="none" class="donut-track" stroke-width="11"/>
              <circle cx="80" cy="80" r="64" fill="none" [attr.stroke]="hColor" stroke-width="11" stroke-linecap="round"
                stroke-dasharray="402" [attr.stroke-dashoffset]="402-(hPct/100)*402" class="gauge-ring"/>
            </svg>
            <div class="gauge-c">
              <span class="gauge-n" [style.color]="hColor">{{ hPct }}<i>%</i></span>
              <span class="gauge-l">en bon état</span>
            </div>
          </div>
          <div class="hbars">
            <div *ngFor="let h of hItems" class="hbar">
              <div class="hbar-top"><span>{{ h.name }}</span><b [style.color]="h.color">{{ h.value }}</b></div>
              <div class="track"><div class="fill" [style.width.%]="totalHealth?(h.value/totalHealth)*100:0" [style.background]="h.color"></div></div>
            </div>
          </div>
        </div>
      </section>

      <!-- Dépenses -->
      <section class="card c4 reveal" style="--d:5">
        <div class="card-h"><h3>Dépenses</h3><span class="badge badge-amber">{{ totalCost|number:'1.0-0' }} DT</span></div>
        <div class="rows">
          <div *ngFor="let e of expItems" class="exp">
            <div class="exp-top"><span class="leg-dot sm" [style.background]="e.color"></span><span class="exp-name">{{ e.name }}</span><b>{{ e.value|number:'1.0-0' }} DT</b></div>
            <div class="track"><div class="fill" [style.width.%]="totalCost?(e.value/totalCost)*100:0" [style.background]="e.color"></div></div>
          </div>
        </div>
      </section>

      <!-- Alertes -->
      <section class="card c4 reveal" style="--d:6">
        <div class="card-h"><h3>Alertes</h3><span class="badge badge-red" *ngIf="alerts.length">{{ alerts.length }}</span></div>
        <div class="rows tight" *ngIf="alerts.length">
          <div *ngFor="let a of pagedAlerts" class="alert" [class.is-new]="a._isNew">
            <span class="alert-dot" [class.a-warn]="a.severity==='warning'" [class.a-danger]="a.severity==='danger'" [class.a-info]="a.severity==='info'"></span>
            <div class="alert-body"><span class="alert-msg">{{ a.message }}</span><span class="alert-time">{{ a.time }}</span></div>
          </div>
          <div class="pgr" *ngIf="alerts.length>5"><button (click)="alertsP=alertsP-1" [disabled]="alertsP===0">‹</button><span>{{ alertsP+1 }}/{{ Math.ceil(alerts.length/5) }}</span><button (click)="alertsP=alertsP+1" [disabled]="(alertsP+1)*5>=alerts.length">›</button></div>
        </div>
        <div class="empty" *ngIf="!alerts.length">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><path d="M22 4 12 14.01l-3-3"/></svg>
          <span>Aucune alerte</span>
        </div>
      </section>

      <!-- Kilométrage -->
      <section class="card c6 reveal" style="--d:7">
        <div class="card-h"><h3>Kilométrage par véhicule</h3></div>
        <div class="rows" *ngIf="topUnits.length">
          <div *ngFor="let u of pUnits" class="row">
            <span class="row-name">{{ u.name }}</span>
            <div class="track"><div class="fill primary" [style.width.%]="(u.mileage/maxMileage)*100"></div></div>
            <span class="row-val">{{ u.mileage|number:'1.0-0' }} km</span>
          </div>
          <div class="pgr" *ngIf="topUnits.length>5"><button (click)="unP=unP-1" [disabled]="unP===0">‹</button><span>{{ unP+1 }}/{{ Math.ceil(topUnits.length/5) }}</span><button (click)="unP=unP+1" [disabled]="(unP+1)*5>=topUnits.length">›</button></div>
        </div>
        <div class="empty" *ngIf="!topUnits.length"><span>Aucune donnée</span></div>
      </section>

      <!-- Scores de conduite -->
      <section class="card c6 reveal" style="--d:8">
        <div class="card-h"><h3>Scores de conduite</h3></div>
        <div class="rows" *ngIf="drivingScores.length">
          <div *ngFor="let s of pScores;let i=index" class="row">
            <span class="rank" [class.gold]="scP*5+i<1" [class.silver]="scP*5+i===1" [class.bronze]="scP*5+i===2">{{ scP*5+i+1 }}</span>
            <span class="row-name">{{ s.plate }}</span>
            <div class="track"><div class="fill" [style.width.%]="s.score" [style.background]="scoreC(s.score)"></div></div>
            <b class="row-val" [style.color]="scoreC(s.score)">{{ s.score }}</b>
          </div>
          <div class="pgr" *ngIf="drivingScores.length>5"><button (click)="scP=scP-1" [disabled]="scP===0">‹</button><span>{{ scP+1 }}/{{ Math.ceil(drivingScores.length/5) }}</span><button (click)="scP=scP+1" [disabled]="(scP+1)*5>=drivingScores.length">›</button></div>
        </div>
        <div class="empty" *ngIf="!drivingScores.length"><span>Aucun score</span></div>
      </section>

      <!-- Derniers trajets -->
      <section class="card c6 reveal" style="--d:9">
        <div class="card-h"><h3>Derniers trajets</h3></div>
        <div class="rows" *ngIf="recentTrips.length">
          <div *ngFor="let t of pTrips" class="row">
            <svg viewBox="0 0 20 28" width="13" height="19" fill="none" class="trip-ic"><circle cx="10" cy="5" r="3.2" stroke="#10b981" stroke-width="1.6"/><line x1="10" y1="9" x2="10" y2="19" stroke="currentColor" stroke-width="1.4" stroke-dasharray="2 2"/><circle cx="10" cy="23" r="3.2" stroke="#ef4444" stroke-width="1.6"/></svg>
            <div class="row-stack"><span class="row-name">{{ t.plate }}</span><span class="row-sub">{{ t.distance }} km · {{ t.duration }}</span></div>
            <span class="row-date">{{ t.date }}</span>
          </div>
          <div class="pgr" *ngIf="recentTrips.length>5"><button (click)="trP=trP-1" [disabled]="trP===0">‹</button><span>{{ trP+1 }}/{{ Math.ceil(recentTrips.length/5) }}</span><button (click)="trP=trP+1" [disabled]="(trP+1)*5>=recentTrips.length">›</button></div>
        </div>
        <div class="empty" *ngIf="!recentTrips.length"><span>Aucun trajet</span></div>
      </section>

      <!-- Conso / véhicule -->
      <section class="card c6 reveal" style="--d:10">
        <div class="card-h"><h3>Consommation / véhicule</h3><span class="badge badge-muted">L/100km</span></div>
        <div class="rows" *ngIf="vehicleFuelStats.length">
          <div *ngFor="let v of pFuel" class="row">
            <span class="row-name">{{ v.plate }}</span>
            <div class="track"><div class="fill" [style.width.%]="(v.consumption/maxFuelConsumption)*100" [style.background]="fuelC(v.consumption)"></div></div>
            <b class="row-val" [style.color]="fuelC(v.consumption)">{{ v.consumption|number:'1.1-1' }}</b>
          </div>
          <div class="pgr" *ngIf="vehicleFuelStats.length>5"><button (click)="fuP=fuP-1" [disabled]="fuP===0">‹</button><span>{{ fuP+1 }}/{{ Math.ceil(vehicleFuelStats.length/5) }}</span><button (click)="fuP=fuP+1" [disabled]="(fuP+1)*5>=vehicleFuelStats.length">›</button></div>
        </div>
        <div class="empty" *ngIf="!vehicleFuelStats.length"><span>Aucune donnée</span></div>
      </section>

      <!-- Géozones -->
      <section class="card c4 reveal" style="--d:11">
        <div class="card-h"><h3>Géozones</h3></div>
        <div class="rows tight" *ngIf="geofences.length">
          <div *ngFor="let g of geofences" class="row">
            <span class="leg-dot" style="background:var(--primary)"></span>
            <span class="row-name flex">{{ g.name }}</span>
            <b class="row-val">{{ g.count }}</b>
          </div>
        </div>
        <div class="empty" *ngIf="!geofences.length"><span>Aucune géozone</span></div>
      </section>

      <!-- Conducteurs -->
      <section class="card c8 reveal" style="--d:12">
        <div class="card-h"><h3>Conducteurs</h3><span class="badge badge-muted">{{ drivers.length }}</span></div>
        <div class="drv-grid" *ngIf="drivers.length">
          <div *ngFor="let d of pDrv" class="drv">
            <div class="drv-av" [class.on]="d.active">{{ d.initials }}</div>
            <div class="row-stack"><span class="row-name">{{ d.name }}</span><span class="row-sub">{{ d.vehicle||'Non assigné' }}</span></div>
            <span class="drv-st" [class.on]="d.active">{{ d.active?'Actif':'—' }}</span>
          </div>
        </div>
        <div class="pgr" *ngIf="drivers.length>8"><button (click)="drP=drP-1" [disabled]="drP===0">‹</button><span>{{ drP+1 }}/{{ Math.ceil(drivers.length/8) }}</span><button (click)="drP=drP+1" [disabled]="(drP+1)*8>=drivers.length">›</button></div>
        <div class="empty" *ngIf="!drivers.length"><span>Aucun conducteur</span></div>
      </section>

    </div>
  </div>
</div>
</app-layout>
  `,
  styles: [`
    /* ══════════════════════════════════════════════════════════
       CALYPSO COMMAND — clean, theme-aware fleet dashboard.
       Built entirely on the app's design tokens (global_styles.css)
       so it follows light / dark mode and matches every other page.
    ══════════════════════════════════════════════════════════ */

    :host {
      --r: 14px;            /* card radius */
      --gap: 16px;
      --line: var(--border-color);
      --tint: color-mix(in srgb, var(--primary) 7%, transparent);
    }

    .dash {
      flex: 1;
      min-height: calc(100vh - 42px);
      background: var(--bg-page);
      position: relative;
      color: var(--text-primary);
      font-family: var(--font-family);
    }

    /* Soft brand glow behind the header — subtle, theme-aware, no neon. */
    .dash-bg {
      position: absolute; inset: 0 0 auto 0; height: 320px; z-index: 0; pointer-events: none;
      background:
        radial-gradient(60% 100% at 18% 0%, color-mix(in srgb, var(--primary) 12%, transparent) 0%, transparent 70%),
        radial-gradient(50% 100% at 92% 0%, color-mix(in srgb, var(--info) 9%, transparent) 0%, transparent 72%);
      opacity: .8;
    }

    .dash-inner {
      position: relative; z-index: 1;
      max-width: 1600px; margin: 0 auto;
      padding: 22px 24px 56px;
    }

    /* Staggered entrance — degrades gracefully when body.no-animations
       zeroes the duration (fill:both lands on the final, visible state). */
    .reveal { animation: rise .55s cubic-bezier(.16,1,.3,1) both; animation-delay: calc(var(--d,0) * 55ms + 60ms); }
    @keyframes rise { from { opacity: 0; transform: translateY(14px); } to { opacity: 1; transform: none; } }

    /* ════ HEADER ════ */
    .page-head { display: flex; justify-content: space-between; align-items: flex-end; gap: 18px; flex-wrap: wrap; margin-bottom: 22px; }
    .ph-eyebrow { display: flex; align-items: center; gap: 8px; font-size: 12px; font-weight: 600; color: var(--text-muted); }
    .ph-company { color: var(--text-secondary); }
    .ph-sep { opacity: .5; }
    .live { display: inline-flex; align-items: center; gap: 6px; padding: 3px 10px; border-radius: 100px;
      background: color-mix(in srgb, var(--danger) 12%, transparent); color: var(--danger); font-size: 11px; font-weight: 700; letter-spacing: .2px; }
    .live.on { background: color-mix(in srgb, var(--success) 14%, transparent); color: var(--success); }
    .live-dot { width: 6px; height: 6px; border-radius: 50%; background: currentColor; }
    .live.on .live-dot { box-shadow: 0 0 0 0 currentColor; animation: ping 2s ease-out infinite; }
    @keyframes ping { 0% { box-shadow: 0 0 0 0 color-mix(in srgb, var(--success) 60%, transparent); } 100% { box-shadow: 0 0 0 7px transparent; } }
    .ph-title { margin: 8px 0 2px; font-size: 27px; font-weight: 800; letter-spacing: -.6px; color: var(--text-primary); }
    .ph-date { margin: 0; font-size: 13px; color: var(--text-muted); text-transform: capitalize; }
    .ph-right { display: flex; align-items: center; }

    /* Re-skin the shared date filter to match (it ships sky-blue + hard borders). */
    ::ng-deep .dash .filter-bar {
      background: var(--bg-card) !important; border: 1px solid var(--line) !important;
      border-radius: 10px !important; padding: 6px 8px !important; box-shadow: var(--shadow-sm); gap: 12px !important;
    }
    ::ng-deep .dash .period-buttons button {
      background: transparent !important; border: 1px solid var(--line) !important; border-right: none !important;
      color: var(--text-secondary) !important; font-size: 12px !important; font-weight: 600 !important; padding: 5px 11px !important;
    }
    ::ng-deep .dash .period-buttons button:last-child { border-right: 1px solid var(--line) !important; }
    ::ng-deep .dash .period-buttons button.active { background: var(--primary) !important; border-color: var(--primary) !important; color: #fff !important; }
    ::ng-deep .dash .period-buttons button:hover:not(.active) { background: var(--tint) !important; }
    ::ng-deep .dash .date-label { color: var(--text-muted) !important; }
    ::ng-deep .dash .date-input { color: var(--text-secondary) !important; text-decoration: none !important; }
    ::ng-deep .dash .btn-apply { background: var(--primary) !important; border-radius: 7px !important; }
    ::ng-deep .dash .btn-apply:hover { background: var(--primary-dark) !important; }

    /* ════ KPI STRIP ════ */
    .kpis { display: grid; grid-template-columns: repeat(6, 1fr); gap: var(--gap); margin-bottom: 22px; }
    .kpi {
      display: flex; align-items: center; gap: 13px; padding: 16px 16px;
      background: var(--bg-card); border: 1px solid var(--line); border-radius: var(--r);
      box-shadow: var(--shadow-sm); position: relative; overflow: hidden;
      transition: transform .25s cubic-bezier(.34,1.56,.64,1), box-shadow .25s, border-color .25s;
    }
    .kpi::before { content: ''; position: absolute; left: 0; top: 0; bottom: 0; width: 3px; background: var(--c); opacity: .9; }
    .kpi:hover { transform: translateY(-3px); box-shadow: var(--shadow-md); border-color: color-mix(in srgb, var(--c) 35%, var(--line)); }
    .kpi-ic { width: 40px; height: 40px; border-radius: 11px; flex-shrink: 0; display: flex; align-items: center; justify-content: center;
      background: color-mix(in srgb, var(--c) 13%, transparent); color: var(--c); }
    .kpi-ic svg { width: 21px; height: 21px; }
    .kpi-meta { display: flex; flex-direction: column; gap: 3px; min-width: 0; }
    .kpi-label { font-size: 11px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: .4px; }
    .kpi-num { font-size: 27px; font-weight: 800; line-height: 1; color: var(--text-primary); letter-spacing: -1px; font-variant-numeric: tabular-nums; }
    .kpi-num i { font-size: 13px; font-weight: 600; font-style: normal; color: var(--text-muted); margin-left: 3px; }
    .kpi-alert::before { animation: alertbar 1.6s ease-in-out infinite; }
    @keyframes alertbar { 0%,100% { opacity: .9; } 50% { opacity: .35; } }

    /* ════ GRID + CARDS ════ */
    .grid { display: grid; grid-template-columns: repeat(12, 1fr); gap: var(--gap); }
    .c4 { grid-column: span 4; } .c6 { grid-column: span 6; } .c8 { grid-column: span 8; }

    .card {
      background: var(--bg-card); border: 1px solid var(--line); border-radius: var(--r);
      padding: 18px 20px 20px; box-shadow: var(--shadow-sm);
      transition: box-shadow .25s, border-color .25s; min-width: 0;
    }
    .card:hover { box-shadow: var(--shadow-md); }
    .card-h { display: flex; align-items: center; gap: 10px; margin-bottom: 16px; }
    .card-h h3 { margin: 0; flex: 1; font-size: 13.5px; font-weight: 700; color: var(--text-primary); letter-spacing: -.1px; }

    .badge { font-size: 11px; font-weight: 700; padding: 3px 10px; border-radius: 8px; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .badge-primary { color: var(--primary); background: var(--tint); }
    .badge-amber { color: #b45309; background: color-mix(in srgb, var(--warning) 16%, transparent); }
    .badge-red { color: var(--danger); background: color-mix(in srgb, var(--danger) 14%, transparent); }
    .badge-muted { color: var(--text-muted); background: color-mix(in srgb, var(--secondary) 12%, transparent); font-weight: 600; }

    /* shared bits */
    .track { flex: 1; height: 7px; border-radius: 100px; background: color-mix(in srgb, var(--secondary) 14%, transparent); overflow: hidden; }
    .fill { height: 100%; border-radius: 100px; background: var(--primary); transition: width .9s cubic-bezier(.16,1,.3,1); }
    .fill.primary { background: var(--primary); }
    .leg-dot { width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0; }
    .leg-dot.sm { width: 8px; height: 8px; }
    .rows { display: flex; flex-direction: column; gap: 9px; }
    .rows.tight { gap: 2px; }

    .row { display: flex; align-items: center; gap: 11px; padding: 6px 8px; border-radius: 9px; transition: background .15s; }
    .row:hover { background: var(--tint); }
    .row-name { font-size: 12.5px; font-weight: 600; color: var(--text-primary); min-width: 76px; }
    .row-name.flex { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .row-val { font-size: 12.5px; font-weight: 700; color: var(--text-primary); min-width: 64px; text-align: right; font-variant-numeric: tabular-nums; }
    .row-stack { display: flex; flex-direction: column; flex: 1; min-width: 0; }
    .row-sub { font-size: 11px; color: var(--text-muted); }
    .row-date { font-size: 11px; color: var(--text-muted); margin-left: auto; }

    /* ── Donut ── */
    .fleet { display: flex; gap: 18px; align-items: center; }
    .donut { position: relative; width: 138px; height: 138px; flex-shrink: 0; }
    .donut svg { width: 100%; height: 100%; transform: rotate(-90deg); }
    .donut-track { stroke: color-mix(in srgb, var(--secondary) 16%, transparent); }
    .donut-seg { transition: stroke-width .2s ease, stroke-dasharray .7s cubic-bezier(.16,1,.3,1); cursor: pointer; }
    .donut-c { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    .donut-n { font-size: 30px; font-weight: 800; color: var(--text-primary); letter-spacing: -1px; line-height: 1; font-variant-numeric: tabular-nums; }
    .donut-l { font-size: 10px; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: 1px; margin-top: 4px; }
    .legend { flex: 1; display: flex; flex-direction: column; gap: 1px; }
    .leg-row { display: flex; align-items: center; gap: 9px; padding: 5px 8px; border-radius: 8px; transition: background .15s; }
    .leg-row.hl, .leg-row:hover { background: var(--tint); }
    .leg-name { flex: 1; font-size: 12px; font-weight: 500; color: var(--text-secondary); }
    .leg-val { font-size: 13px; font-weight: 700; color: var(--text-primary); font-variant-numeric: tabular-nums; }

    /* ── Chart ── */
    .chart { position: relative; padding-top: 4px; }
    .chart-svg { width: 100%; height: 150px; display: block; overflow: visible; }
    .chart-grid { stroke: var(--line); stroke-width: 1; }
    .chart-line { stroke-dasharray: 1400; stroke-dashoffset: 1400; animation: draw 1.4s ease forwards .2s; }
    .chart-area { opacity: 0; animation: fade .6s ease forwards .7s; }
    @keyframes draw { to { stroke-dashoffset: 0; } }
    @keyframes fade { to { opacity: 1; } }
    .chart-pt { transition: r .15s ease; }
    .chart-cursor { stroke: var(--primary); stroke-width: 1; opacity: .35; stroke-dasharray: 3 3; }
    .chart-x { display: flex; justify-content: space-between; font-size: 10.5px; color: var(--text-muted); margin-top: 9px; font-variant-numeric: tabular-nums; }
    .chart-tip { position: absolute; top: -2px; transform: translateX(-50%); background: var(--text-primary); color: var(--bg-card);
      border-radius: 9px; padding: 6px 11px; box-shadow: var(--shadow-lg); pointer-events: none; z-index: 5;
      display: flex; flex-direction: column; align-items: center; gap: 1px; }
    .chart-tip b { font-size: 13px; font-weight: 700; font-variant-numeric: tabular-nums; }
    .chart-tip span { font-size: 10px; opacity: .7; }

    /* ── Health gauge ── */
    .health { display: flex; gap: 22px; align-items: center; }
    .gauge { position: relative; width: 130px; height: 130px; flex-shrink: 0; }
    .gauge svg { width: 100%; height: 100%; transform: rotate(-90deg); }
    .gauge-ring { transition: stroke-dashoffset 1.2s cubic-bezier(.16,1,.3,1); }
    .gauge-c { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    .gauge-n { font-size: 28px; font-weight: 800; letter-spacing: -1px; line-height: 1; font-variant-numeric: tabular-nums; }
    .gauge-n i { font-size: 15px; font-weight: 600; font-style: normal; }
    .gauge-l { font-size: 10.5px; color: var(--text-muted); margin-top: 3px; }
    .hbars { flex: 1; display: flex; flex-direction: column; gap: 12px; }
    .hbar { display: flex; flex-direction: column; gap: 6px; }
    .hbar-top { display: flex; justify-content: space-between; font-size: 12px; }
    .hbar-top span { color: var(--text-secondary); font-weight: 500; }
    .hbar-top b { font-weight: 700; font-variant-numeric: tabular-nums; }

    /* ── Expenses ── */
    .exp { display: flex; flex-direction: column; gap: 7px; }
    .exp-top { display: flex; align-items: center; gap: 9px; font-size: 12.5px; }
    .exp-name { flex: 1; color: var(--text-secondary); font-weight: 500; }
    .exp-top b { font-weight: 700; color: var(--text-primary); font-variant-numeric: tabular-nums; }

    /* ── Alerts ── */
    .alert { display: flex; gap: 11px; padding: 9px 8px; border-radius: 9px; transition: background .15s; }
    .alert:hover { background: var(--tint); }
    .alert.is-new { animation: flash .8s ease; }
    @keyframes flash { 0% { background: var(--tint); } 100% { background: transparent; } }
    .alert-dot { width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; margin-top: 5px; background: var(--text-muted); }
    .alert-dot.a-warn { background: var(--warning); } .alert-dot.a-danger { background: var(--danger); } .alert-dot.a-info { background: var(--info); }
    .alert-body { min-width: 0; }
    .alert-msg { display: block; font-size: 12.5px; font-weight: 500; color: var(--text-primary); line-height: 1.4; }
    .alert-time { display: block; font-size: 10.5px; color: var(--text-muted); margin-top: 1px; }

    /* ── Rank badges ── */
    .rank { width: 22px; height: 22px; border-radius: 7px; flex-shrink: 0; display: flex; align-items: center; justify-content: center;
      font-size: 11px; font-weight: 700; color: var(--text-muted); background: color-mix(in srgb, var(--secondary) 12%, transparent); font-variant-numeric: tabular-nums; }
    .rank.gold { background: linear-gradient(135deg, #fbbf24, #f59e0b); color: #fff; }
    .rank.silver { background: linear-gradient(135deg, #cbd5e1, #94a3b8); color: #fff; }
    .rank.bronze { background: linear-gradient(135deg, #d6a06a, #b45309); color: #fff; }

    /* ── Trips ── */
    .trip-ic { flex-shrink: 0; color: var(--text-muted); }

    /* ── Drivers ── */
    .drv-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 4px 18px; }
    .drv { display: flex; align-items: center; gap: 11px; padding: 7px 8px; border-radius: 9px; transition: background .15s; }
    .drv:hover { background: var(--tint); }
    .drv-av { width: 34px; height: 34px; border-radius: 10px; flex-shrink: 0; display: flex; align-items: center; justify-content: center;
      font-size: 12px; font-weight: 700; background: var(--tint); color: var(--primary); border: 2px solid transparent; }
    .drv-av.on { border-color: var(--success); }
    .drv-st { font-size: 10.5px; font-weight: 600; padding: 3px 9px; border-radius: 100px; margin-left: auto;
      color: var(--text-muted); background: color-mix(in srgb, var(--secondary) 12%, transparent); }
    .drv-st.on { color: var(--success); background: color-mix(in srgb, var(--success) 14%, transparent); }

    /* ── Pagination ── */
    .pgr { display: flex; align-items: center; justify-content: center; gap: 10px; margin-top: 12px; padding-top: 12px; border-top: 1px solid var(--line); }
    .pgr button { width: 28px; height: 28px; border: 1px solid var(--line); border-radius: 8px; background: var(--bg-card); color: var(--text-secondary);
      font-size: 16px; line-height: 1; cursor: pointer; display: flex; align-items: center; justify-content: center; transition: all .2s; }
    .pgr button:hover:not(:disabled) { background: var(--primary); color: #fff; border-color: var(--primary); }
    .pgr button:disabled { opacity: .35; cursor: default; }
    .pgr span { font-size: 11px; font-weight: 600; color: var(--text-muted); font-variant-numeric: tabular-nums; }

    /* ── Empty ── */
    .empty { display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 30px 16px; color: var(--text-muted); }
    .empty svg { width: 26px; height: 26px; opacity: .6; }
    .empty span { font-size: 12px; font-weight: 500; }

    /* ════ RESPONSIVE ════ */
    @media (max-width: 1280px) {
      .c8 { grid-column: span 12; } .c6 { grid-column: span 6; } .c4 { grid-column: span 4; }
    }
    @media (max-width: 1024px) {
      .kpis { grid-template-columns: repeat(3, 1fr); }
      .c4, .c6, .c8 { grid-column: span 6; }
    }
    @media (max-width: 760px) {
      .dash-inner { padding: 16px 14px 40px; }
      .kpis { grid-template-columns: repeat(2, 1fr); }
      .grid { grid-template-columns: 1fr; }
      .c4, .c6, .c8 { grid-column: span 1; }
      .fleet, .health { flex-direction: column; align-items: stretch; }
      .donut, .gauge { margin: 0 auto; }
      .drv-grid { grid-template-columns: 1fr; }
      .page-head { flex-direction: column; align-items: stretch; }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  company: Company | null = null;
  selectedPeriod = 'week'; fromDate = ''; toDate = '';
  isConnected = false;
  todayLabel = '';
  Math = Math;

  periodsFr = [
    { value: 'today', label: "Aujourd'hui" },
    { value: 'yesterday', label: 'Hier' },
    { value: 'week', label: 'Semaine' },
    { value: 'month', label: 'Mois' },
  ];

  motionData = {stationary:0,ignitionOn:0,moving:0,movingIgnition:0,lbs:0,wifi:0,noState:0,noCoords:0};
  healthData = {healthy:0,attention:0,unhealthy:0};
  geofences:{name:string;color:string;count:number}[] = [];
  topUnits:{name:string;color:string;mileage:number}[] = [];
  maxMileage = 1;
  totalFuelConsumed = 0;
  cPts=''; cLabels:string[]=[]; cVals:number[]=[]; cPoints:{x:number;y:number}[]=[]; cIdx=-1;
  hSeg=-1;
  fuelCost=0; maintenanceCost=0; repairCost=0; otherCost=0; totalCost=0;
  drivingScores:{plate:string;score:number}[]=[];
  vehicleFuelStats:{plate:string;consumption:number;totalLiters:number;totalKm:number}[]=[];
  maxFuelConsumption=1;
  alerts:{message:string;severity:string;time:string;_isNew?:boolean}[]=[];
  recentTrips:{plate:string;distance:string;duration:string;date:string}[]=[];
  drivers:{name:string;initials:string;vehicle:string;active:boolean}[]=[];

  private refreshPending=false;

  dVehicles=0; dMoving=0; dStopped=0; dAlerts=0; dFuel=0; dCost=0;

  fleetSegs:any[]=[]; donutSegs:any[]=[];
  readonly circ62=2*Math.PI*62;
  expItems:any[]=[]; hItems:any[]=[];

  get totalMotion():number{return this.motionData.stationary+this.motionData.ignitionOn+this.motionData.movingIgnition+this.motionData.noState+this.motionData.noCoords;}
  get totalHealth():number{return this.healthData.healthy+this.healthData.attention+this.healthData.unhealthy;}
  get hPct():number{return this.totalHealth?Math.round((this.healthData.healthy/this.totalHealth)*100):0;}
  get hColor():string{const p=this.hPct;return p>=80?'#10b981':p>=50?'#f59e0b':'#ef4444';}

  scP=0;unP=0;fuP=0;alertsP=0;trP=0;drP=0;
  get pScores(){return this.drivingScores.slice(this.scP*5,this.scP*5+5);}
  get pUnits(){return this.topUnits.slice(this.unP*5,this.unP*5+5);}
  get pFuel(){return this.vehicleFuelStats.slice(this.fuP*5,this.fuP*5+5);}
  get pagedAlerts(){return this.alerts.slice(this.alertsP*5,this.alertsP*5+5);}
  get pTrips(){return this.recentTrips.slice(this.trP*5,this.trP*5+5);}
  get pDrv(){return this.drivers.slice(this.drP*8,this.drP*8+8);}

  constructor(private router:Router,private apiService:ApiService,private signalrService:SignalRService,private cdr:ChangeDetectorRef){}

  ngOnInit(){
    if(!this.apiService.isAuthenticated()){this.router.navigate(['/login']);return;}
    const u=this.apiService.getCurrentUserSync();
    if(u) this.company={id:u.companyId.toString(),name:u.companyName,type:'transport',subscriptionId:'1'} as Company;
    const today=new Date();
    this.todayLabel=today.toLocaleDateString('fr-FR',{weekday:'long',day:'numeric',month:'long',year:'numeric'});
    this.toDate=today.toISOString().split('T')[0];
    this.fromDate=new Date(today.getTime()-7*86400000).toISOString().split('T')[0];
    this.cLabels=Array.from({length:7},(_,i)=>new Date(today.getTime()-(6-i)*86400000).toLocaleDateString('fr-FR',{day:'2-digit',month:'2-digit'}));
    this.loadAll();
    this.wire();
  }

  ngOnDestroy(){
    this.destroy$.next();this.destroy$.complete();
  }

  private anim(from:number,to:number,cb:(v:number)=>void){
    const t0=performance.now();
    const step=(now:number)=>{const p=Math.min((now-t0)/900,1);cb(Math.round(from+(to-from)*(1-Math.pow(1-p,4))));this.cdr.detectChanges();if(p<1)requestAnimationFrame(step);};
    requestAnimationFrame(step);
  }

  private rebuild(){
    const tot=this.totalMotion||1;
    this.anim(this.dVehicles,this.totalMotion,v=>this.dVehicles=v);
    this.anim(this.dMoving,this.motionData.movingIgnition,v=>this.dMoving=v);
    this.anim(this.dStopped,this.motionData.stationary,v=>this.dStopped=v);
    this.anim(this.dAlerts,this.alerts.length,v=>this.dAlerts=v);
    this.anim(this.dFuel,this.totalFuelConsumed,v=>this.dFuel=v);
    this.anim(this.dCost,this.totalCost,v=>this.dCost=v);

    const segs=[
      {name:'En circulation',color:'#10b981',value:this.motionData.movingIgnition},
      {name:'Moteur allumé',color:'#f59e0b',value:this.motionData.ignitionOn},
      {name:'À l\'arrêt',color:'#64748b',value:this.motionData.stationary},
      {name:'Maintenance',color:'#6366f1',value:this.motionData.noState},
      {name:'Sans GPS',color:'#cbd5e1',value:this.motionData.noCoords},
    ];
    this.fleetSegs=segs.map(s=>({...s,pct:tot?Math.round((s.value/tot)*100):0}));
    let cum=0;
    this.donutSegs=segs.filter(s=>s.value>0).map(s=>{
      const da2=(s.value/tot)*this.circ62;
      const offset2=-(cum/tot)*this.circ62;
      cum+=s.value;
      return{...s,da2,offset2};
    });
    this.expItems=[
      {name:'Carburant',color:'#6366f1',value:this.fuelCost},
      {name:'Entretien',color:'#10b981',value:this.maintenanceCost},
      {name:'Réparation',color:'#f59e0b',value:this.repairCost},
      {name:'Autres',color:'#94a3b8',value:this.otherCost},
    ];
    this.hItems=[
      {name:'Bon état',color:'#10b981',value:this.healthData.healthy},
      {name:'Attention',color:'#f59e0b',value:this.healthData.attention},
      {name:'Mauvais',color:'#ef4444',value:this.healthData.unhealthy},
    ];
  }

  private wire(){
    this.signalrService.connectionState$.pipe(takeUntil(this.destroy$)).subscribe((s:string)=>{this.isConnected=s==='Connected';this.cdr.detectChanges();});
    this.signalrService.alert$.pipe(takeUntil(this.destroy$)).subscribe((a:any)=>{
      const item={message:`${a.vehicleName}: ${a.type}`,severity:'warning',time:'À l\'instant',_isNew:true};
      this.alerts.unshift(item);this.alertsP=0;this.rebuild();this.cdr.detectChanges();
      setTimeout(()=>{item._isNew=false;this.cdr.detectChanges();},800);
    });
    this.signalrService.geofenceEvent$.pipe(takeUntil(this.destroy$)).subscribe((ev:any)=>{
      const gf=this.geofences.find(g=>g.name===ev.geofenceName);
      if(gf){if(ev.eventType==='entry')gf.count++;else if(ev.eventType==='exit'&&gf.count>0)gf.count--;this.cdr.detectChanges();}
    });
    this.signalrService.positionUpdate$.pipe(takeUntil(this.destroy$)).subscribe(()=>{
      if(!this.refreshPending){
        this.refreshPending=true;
        setTimeout(()=>{this.refreshPending=false;this.loadAll();},30000);
      }
    });
  }

  loadAll(){
    this.apiService.getDashboardAll(this.selectedPeriod).pipe(takeUntil(this.destroy$)).subscribe({
      next:(d:any)=>{
        if(d.vehicleStatus){const v=d.vehicleStatus;this.motionData={stationary:v.stopped??0,ignitionOn:v.ignitionOn??0,moving:0,movingIgnition:v.moving??0,lbs:0,wifi:0,noState:v.maintenance??0,noCoords:v.noGps??0};}
        if(d.expenses){const e=d.expenses;this.fuelCost=e.fuelCost??0;this.maintenanceCost=e.maintenanceCost??0;this.repairCost=e.repairCost??0;this.otherCost=e.otherCost??0;this.totalCost=e.totalCost??(this.fuelCost+this.maintenanceCost+this.repairCost+this.otherCost);}
        if(d.drivingScores?.length)this.drivingScores=d.drivingScores.map((s:any)=>({plate:s.plate,score:s.score})).sort((a:any,b:any)=>b.score-a.score);
        if(d.healthData)this.healthData={healthy:d.healthData.healthy??0,attention:d.healthData.attention??0,unhealthy:d.healthData.unhealthy??0};
        if(d.topUnits?.length){this.topUnits=d.topUnits.map((u:any)=>({name:u.name,color:u.color,mileage:Math.round(u.mileage??0)})).sort((a:any,b:any)=>b.mileage-a.mileage);this.maxMileage=Math.max(...this.topUnits.map(u=>u.mileage),1);}
        if(d.geofences)this.geofences=d.geofences.map((g:any)=>({name:g.name,color:g.color,count:g.count??0}));
        if(d.alerts)this.alerts=d.alerts.map((a:any)=>({message:a.message,severity:a.severity,time:a.time}));
        if(d.recentTrips)this.recentTrips=d.recentTrips.map((t:any)=>({plate:t.plate,distance:t.distance,duration:t.duration,date:t.date}));
        if(d.drivers)this.drivers=d.drivers.map((dr:any)=>({name:dr.name,initials:dr.initials,vehicle:dr.vehicle,active:dr.active}));
        if(d.fuelConsumption){
          const fc=d.fuelConsumption;
          if(fc.vehicleStats?.length){this.vehicleFuelStats=fc.vehicleStats.map((v:any)=>({plate:v.plate||'Inconnu',consumption:Number(v.consumption)||0,totalLiters:Math.round(Number(v.totalLiters)||0),totalKm:Math.round(Number(v.totalKm)||0)}));this.maxFuelConsumption=Math.max(...this.vehicleFuelStats.map(v=>v.consumption),1);}
          if(fc.fleetTotalLiters>0)this.totalFuelConsumed=Math.round(fc.fleetTotalLiters);
          if(fc.chartValues?.length){
            this.cVals=fc.chartValues as number[];
            const mx=Math.max(...this.cVals,0.1);
            this.cPoints=this.cVals.map((v:number,i:number)=>({x:this.cVals.length>1?(i/(this.cVals.length-1))*500:250,y:130-(v/mx)*122}));
            this.cPts=this.cPoints.map(p=>`${p.x.toFixed(0)},${p.y.toFixed(0)}`).join(' ');
            if(fc.chartDays?.length){const step=Math.max(1,Math.floor(fc.chartDays.length/7));this.cLabels=(fc.chartDays as string[]).filter((_:string,i:number)=>i%step===0||i===fc.chartDays.length-1).map((dd:string)=>{const p=dd.split('-');return p.length>=3?`${p[2]}/${p[1]}`:dd;});}
          }
        }
        this.rebuild();this.cdr.detectChanges();
      },
      error:(err:any)=>console.error('Dashboard error:',err)
    });
  }

  onChartHover(e:MouseEvent){
    if(!this.cPoints.length)return;
    const svg=(e.currentTarget as HTMLElement).querySelector('.chart-svg');
    if(!svg)return;
    const r=svg.getBoundingClientRect();
    this.cIdx=Math.round(Math.max(0,Math.min(1,(e.clientX-r.left)/r.width))*(this.cPoints.length-1));
  }

  donutIdx(name:string):number{return this.donutSegs.findIndex(s=>s.name===name);}

  scoreC(s:number):string{return s>=80?'#10b981':s>=60?'#f59e0b':'#ef4444';}
  scoreE(s:number):string{return s>=80?'#34d399':s>=60?'#fbbf24':'#f87171';}
  fuelC(c:number):string{return c<=6?'#10b981':c<=8?'#f59e0b':'#ef4444';}

  onPeriodChange(p:string){this.selectedPeriod=p;}
  onDateRangeChange(r:{from:string;to:string}){this.fromDate=r.from;this.toDate=r.to;}
  applyFilter(){this.scP=0;this.unP=0;this.fuP=0;this.alertsP=0;this.trP=0;this.drP=0;this.loadAll();}
}
