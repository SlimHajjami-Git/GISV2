import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { SignalRService } from '../services/signalr.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { DateFilterBarComponent, CardComponent, LegendItemComponent } from './shared/ui';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, DateFilterBarComponent, CardComponent, LegendItemComponent],
  template: `
<app-layout>
<div class="dash">
  <div class="dash-bg"></div>
  <div class="dash-grain"></div>

  <div class="dash-inner">

    <!-- ── HEADER ── -->
    <header class="d-head reveal" style="--i:0">
      <div class="d-head-left">
        <h1 class="d-title">Tableau de bord</h1>
        <div class="d-meta">
          <span class="live" [class.on]="isConnected">
            <span class="live-ring"></span>
            <span class="live-core"></span>
            {{ isConnected ? 'En direct' : 'Hors ligne' }}
          </span>
          <span class="d-chip"><span class="chip-dot"></span>{{ totalMotion }} véhicules</span>
          <span class="d-chip"><span class="chip-dot drv"></span>{{ drivers.length }} conducteurs</span>
        </div>
      </div>
      <div class="d-head-right">
        <ui-date-filter-bar [selectedPeriod]="selectedPeriod" [fromDate]="fromDate" [toDate]="toDate"
          (periodChange)="onPeriodChange($event)" (dateRangeChange)="onDateRangeChange($event)" (applyFilter)="applyFilter()">
        </ui-date-filter-bar>
      </div>
    </header>

    <!-- ── KPI CARDS ── -->
    <div class="kpi-row">
      <div class="kpi reveal-kpi" style="--kc:#3b82f6;--kg:rgba(59,130,246,.15);--ks:rgba(59,130,246,.35);--kd:0">
        <div class="kpi-glow"></div>
        <div class="kpi-icon"><svg viewBox="0 0 24 24" fill="none" stroke="var(--kc)" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" width="20" height="20"><rect x="1" y="3" width="15" height="13" rx="1"/><path d="M16 8h4l3 4v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg></div>
        <span class="kpi-num">{{ dVehicles }}</span>
        <span class="kpi-lbl">Véhicules</span>
        <div class="kpi-shimmer"></div>
      </div>
      <div class="kpi reveal-kpi" style="--kc:#10b981;--kg:rgba(16,185,129,.15);--ks:rgba(16,185,129,.35);--kd:1">
        <div class="kpi-glow"></div>
        <div class="kpi-icon"><svg viewBox="0 0 24 24" fill="none" stroke="var(--kc)" stroke-width="1.6" stroke-linecap="round" width="20" height="20"><circle cx="12" cy="12" r="3"/><path d="M4.93 19.07A10 10 0 0119.07 4.93"/><path d="M7.76 16.24a6 6 0 018.49-8.49"/></svg></div>
        <span class="kpi-num">{{ dMoving }}</span>
        <span class="kpi-lbl">En mouvement</span>
        <div class="kpi-shimmer"></div>
      </div>
      <div class="kpi reveal-kpi" style="--kc:#f59e0b;--kg:rgba(245,158,11,.15);--ks:rgba(245,158,11,.35);--kd:2">
        <div class="kpi-glow"></div>
        <div class="kpi-icon"><svg viewBox="0 0 24 24" fill="none" stroke="var(--kc)" stroke-width="1.6" width="20" height="20"><rect x="3" y="3" width="18" height="18" rx="4"/><path d="M9 17V7h4a3 3 0 110 6H9"/></svg></div>
        <span class="kpi-num">{{ dStopped }}</span>
        <span class="kpi-lbl">À l'arrêt</span>
        <div class="kpi-shimmer"></div>
      </div>
      <div class="kpi reveal-kpi" style="--kc:#ef4444;--kg:rgba(239,68,68,.15);--ks:rgba(239,68,68,.35);--kd:3">
        <div class="kpi-glow"></div>
        <div class="kpi-icon"><svg viewBox="0 0 24 24" fill="none" stroke="var(--kc)" stroke-width="1.6" stroke-linecap="round" width="20" height="20"><path d="M18 8A6 6 0 006 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 01-3.46 0"/></svg></div>
        <span class="kpi-num">{{ dAlerts }}</span>
        <span class="kpi-lbl">Alertes</span>
        <div class="kpi-shimmer"></div>
      </div>
      <div class="kpi reveal-kpi" style="--kc:#8b5cf6;--kg:rgba(139,92,246,.15);--ks:rgba(139,92,246,.35);--kd:4">
        <div class="kpi-glow"></div>
        <div class="kpi-icon"><svg viewBox="0 0 24 24" fill="none" stroke="var(--kc)" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" width="20" height="20"><path d="M3 22V5a2 2 0 012-2h8a2 2 0 012 2v17"/><path d="M15 11h2a2 2 0 012 2v4a2 2 0 004 0V9l-3-3"/><path d="M3 22h12"/><rect x="6" y="6" width="6" height="5" rx="1"/></svg></div>
        <span class="kpi-num">{{ dFuel }}<small>L</small></span>
        <span class="kpi-lbl">Carburant</span>
        <div class="kpi-shimmer"></div>
      </div>
      <div class="kpi reveal-kpi" style="--kc:#06b6d4;--kg:rgba(6,182,212,.15);--ks:rgba(6,182,212,.35);--kd:5">
        <div class="kpi-glow"></div>
        <div class="kpi-icon"><svg viewBox="0 0 24 24" fill="none" stroke="var(--kc)" stroke-width="1.6" stroke-linecap="round" width="20" height="20"><rect x="2" y="6" width="20" height="14" rx="3"/><path d="M2 10h20"/><circle cx="17" cy="14.5" r="1.5" fill="var(--kc)" stroke="none"/></svg></div>
        <span class="kpi-num">{{ dCost }}<small>DT</small></span>
        <span class="kpi-lbl">Coût total</span>
        <div class="kpi-shimmer"></div>
      </div>
    </div>

    <!-- ── BENTO GRID ── -->
    <div class="bento">

      <!-- Fleet Status -->
      <section class="card card--fleet reveal" style="--i:2">
        <div class="card-head"><h3>État flotte</h3></div>
        <div class="fleet-row">
          <div class="donut-wrap">
            <svg viewBox="0 0 140 140">
              <defs>
                <filter id="donutGlow"><feGaussianBlur stdDeviation="3" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
              </defs>
              <circle cx="70" cy="70" r="56" fill="none" stroke="var(--border-color)" stroke-width="12" opacity=".08"/>
              <circle *ngFor="let s of donutSegs;let si=index" cx="70" cy="70" r="56" fill="none"
                [attr.stroke]="s.color" [attr.stroke-width]="hSeg===si?20:13" stroke-linecap="round"
                [attr.stroke-dasharray]="s.da+' '+(circ56-s.da)" [attr.stroke-dashoffset]="s.offset"
                class="donut-seg" [attr.filter]="hSeg===si?'url(#donutGlow)':'none'"
                (mouseenter)="hSeg=si" (mouseleave)="hSeg=-1"/>
            </svg>
            <div class="donut-center">
              <span class="donut-num">{{ totalMotion }}</span>
              <span class="donut-lbl">total</span>
            </div>
          </div>
          <div class="fleet-legend">
            <div *ngFor="let s of donutSegs;let si=index" class="lg-item" (mouseenter)="hSeg=si" (mouseleave)="hSeg=-1" [class.lg-active]="hSeg===si">
              <span class="lg-bar" [style.background]="s.color"></span>
              <span class="lg-name">{{ s.name }}</span>
              <b class="lg-val">{{ s.value }}</b>
            </div>
          </div>
        </div>
        <div class="fleet-strip">
          <div *ngFor="let s of fleetSegs" class="strip-seg" [style.flex]="s.value||0" [style.background]="s.color" [class.strip-hide]="!s.value"></div>
        </div>
      </section>

      <!-- Fuel Consumption Chart -->
      <section class="card card--chart reveal" style="--i:3">
        <div class="card-head">
          <h3>Consommation carburant</h3>
          <span class="badge badge--green">{{ totalFuelConsumed | number:'1.0-0' }} L</span>
        </div>
        <div class="chart-wrap" (mousemove)="onChartHover($event)" (mouseleave)="cIdx=-1">
          <svg viewBox="0 0 500 130" preserveAspectRatio="none" class="chart-svg">
            <defs>
              <linearGradient id="cGrad" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stop-color="#10b981" stop-opacity=".22"/>
                <stop offset="100%" stop-color="#10b981" stop-opacity="0"/>
              </linearGradient>
              <linearGradient id="lineGrad" x1="0" y1="0" x2="1" y2="0">
                <stop offset="0%" stop-color="#6ee7b7"/>
                <stop offset="50%" stop-color="#10b981"/>
                <stop offset="100%" stop-color="#059669"/>
              </linearGradient>
              <filter id="lineGlow"><feGaussianBlur stdDeviation="3" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
            </defs>
            <line *ngFor="let y of [0,32,65,97,130]" x1="0" [attr.y1]="y" x2="500" [attr.y2]="y" stroke="var(--border-color)" stroke-width=".35" opacity=".15"/>
            <polygon *ngIf="cPts" [attr.points]="'0,130 '+cPts+' 500,130'" fill="url(#cGrad)" class="chart-area"/>
            <polyline *ngIf="cPts" fill="none" stroke="url(#lineGrad)" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round" [attr.points]="cPts" filter="url(#lineGlow)" class="chart-line"/>
            <ng-container *ngFor="let p of cPoints;let pi=index">
              <circle *ngIf="cIdx===pi" [attr.cx]="p.x" [attr.cy]="p.y" r="12" fill="#10b981" opacity=".12" class="chart-ring"/>
              <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="cIdx===pi?5:2.5" [attr.fill]="cIdx===pi?'#10b981':'var(--bg-card)'" stroke="#10b981" [attr.stroke-width]="cIdx===pi?2:1.5" class="chart-dot"/>
            </ng-container>
            <line *ngIf="cIdx>=0&&cPoints[cIdx]" [attr.x1]="cPoints[cIdx].x" y1="0" [attr.x2]="cPoints[cIdx].x" y2="130" stroke="#10b981" stroke-width=".8" opacity=".15" stroke-dasharray="4 3"/>
          </svg>
          <div class="chart-labels"><span *ngFor="let l of cLabels">{{ l }}</span></div>
          <div class="chart-tip" *ngIf="cIdx>=0&&cVals[cIdx]!==undefined" [style.left.%]="cPoints[cIdx]?(cPoints[cIdx].x/5):0">
            <b>{{ cVals[cIdx]|number:'1.0-0' }} L</b><span>{{ cLabels[cIdx]||'' }}</span>
          </div>
        </div>
      </section>

      <!-- Kilométrage -->
      <section class="card card--km reveal" style="--i:4">
        <div class="card-head"><h3>Kilométrage</h3></div>
        <div class="rows" *ngIf="topUnits.length">
          <div *ngFor="let u of pUnits" class="row-item">
            <span class="row-accent" [style.background]="u.color"></span>
            <span class="row-plate" style="min-width:90px">{{ u.name }}</span>
            <div class="progress progress--lg"><div class="progress-fill" [style.width.%]="(u.mileage/maxMileage)*100" [style.background]="u.color"></div></div>
            <span class="row-val mono">{{ u.mileage|number:'1.0-0' }} km</span>
          </div>
          <div class="pager" *ngIf="topUnits.length>5"><button (click)="unP=unP-1" [disabled]="unP===0">&#8249;</button><span>{{ unP+1 }}/{{ Math.ceil(topUnits.length/5) }}</span><button (click)="unP=unP+1" [disabled]="(unP+1)*5>=topUnits.length">&#8250;</button></div>
        </div>
        <div class="empty" *ngIf="!topUnits.length">Aucune donnée</div>
      </section>

      <!-- Dépenses -->
      <section class="card card--exp reveal" style="--i:5">
        <div class="card-head"><h3>Dépenses</h3><span class="badge badge--amber">{{ totalCost|number:'1.0-0' }} DT</span></div>
        <div class="exp-list">
          <div *ngFor="let e of expItems" class="exp-item">
            <div class="exp-header">
              <span class="exp-dot" [style.background]="e.color"></span>
              <span class="exp-name">{{ e.name }}</span>
              <b class="exp-val mono">{{ e.value|number:'1.0-0' }} DT</b>
            </div>
            <div class="progress progress--sm"><div class="progress-fill" [style.width.%]="totalCost?(e.value/totalCost)*100:0" [style.background]="e.color"></div></div>
          </div>
        </div>
      </section>

      <!-- Vehicle Health -->
      <section class="card card--health reveal" style="--i:6">
        <div class="card-head"><h3>Santé véhicules</h3></div>
        <div class="health-layout">
          <div class="gauge-wrap">
            <svg viewBox="0 0 140 140">
              <defs>
                <filter id="gaugeGlow"><feGaussianBlur stdDeviation="4" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
              </defs>
              <circle cx="70" cy="70" r="58" fill="none" stroke="var(--border-color)" stroke-width="8" opacity=".08"/>
              <circle cx="70" cy="70" r="58" fill="none" [attr.stroke]="hColor" stroke-width="9" stroke-linecap="round" stroke-dasharray="364" [attr.stroke-dashoffset]="364-(hPct/100)*364" class="gauge-arc" filter="url(#gaugeGlow)"/>
            </svg>
            <div class="gauge-center">
              <span class="gauge-pct" [style.color]="hColor">{{ hPct }}%</span>
              <span class="gauge-lbl">en bon état</span>
            </div>
          </div>
          <div class="health-items">
            <div *ngFor="let h of hItems" class="health-card" [style.--hc]="h.color">
              <b>{{ h.value }}</b>
              <span>{{ h.name }}</span>
              <div class="progress progress--xs"><div class="progress-fill" [style.width.%]="totalHealth?(h.value/totalHealth)*100:0" [style.background]="h.color"></div></div>
            </div>
          </div>
        </div>
      </section>

      <!-- Geozones -->
      <section class="card card--geo reveal" style="--i:7">
        <div class="card-head"><h3>Géozones</h3></div>
        <div class="geo-list" *ngIf="geofences.length">
          <div *ngFor="let g of geofences" class="geo-item">
            <span class="geo-dot" [style.background]="g.color" [style.box-shadow]="'0 0 8px '+g.color+'44'"></span>
            <span class="geo-name">{{ g.name }}</span>
            <b class="geo-count mono">{{ g.count }}</b>
          </div>
        </div>
        <div class="empty" *ngIf="!geofences.length">Aucune géozone</div>
      </section>

      <!-- Driving Scores -->
      <section class="card card--scores reveal" style="--i:8">
        <div class="card-head"><h3>Scores de conduite</h3></div>
        <div class="rows" *ngIf="drivingScores.length">
          <div *ngFor="let s of pScores;let i=index" class="row-item">
            <span class="rank" [class.rank-gold]="scP*5+i<1" [class.rank-silver]="scP*5+i===1" [class.rank-bronze]="scP*5+i===2">{{ scP*5+i+1 }}</span>
            <span class="row-plate">{{ s.plate }}</span>
            <div class="progress"><div class="progress-fill" [style.width.%]="s.score" [style.background]="'linear-gradient(90deg,'+scoreC(s.score)+','+scoreE(s.score)+')'"></div></div>
            <b class="row-score mono" [style.color]="scoreC(s.score)">{{ s.score }}</b>
          </div>
          <div class="pager" *ngIf="drivingScores.length>5"><button (click)="scP=scP-1" [disabled]="scP===0">&#8249;</button><span>{{ scP+1 }}/{{ Math.ceil(drivingScores.length/5) }}</span><button (click)="scP=scP+1" [disabled]="(scP+1)*5>=drivingScores.length">&#8250;</button></div>
        </div>
        <div class="empty" *ngIf="!drivingScores.length">Aucun score</div>
      </section>

      <!-- Alerts -->
      <section class="card card--alerts reveal" style="--i:9">
        <div class="card-head"><h3>Alertes récentes</h3><span class="alert-pill" *ngIf="alerts.length">{{ alerts.length }}</span></div>
        <div class="timeline" *ngIf="alerts.length">
          <div *ngFor="let a of pagedAlerts" class="tl-item" [class.tl-new]="a._isNew">
            <div class="tl-dot" [class.tl-warn]="a.severity==='warning'" [class.tl-danger]="a.severity==='danger'" [class.tl-info]="a.severity==='info'"></div>
            <div class="tl-body"><span class="tl-msg">{{ a.message }}</span><span class="tl-time">{{ a.time }}</span></div>
          </div>
          <div class="pager" *ngIf="alerts.length>6"><button (click)="alertsP=alertsP-1" [disabled]="alertsP===0">&#8249;</button><span>{{ alertsP+1 }}/{{ Math.ceil(alerts.length/6) }}</span><button (click)="alertsP=alertsP+1" [disabled]="(alertsP+1)*6>=alerts.length">&#8250;</button></div>
        </div>
        <div class="empty" *ngIf="!alerts.length">Aucune alerte</div>
      </section>

      <!-- Fuel per Vehicle -->
      <section class="card reveal" style="--i:10">
        <div class="card-head"><h3>Conso / Véhicule</h3><span class="badge badge--muted">L/100km</span></div>
        <div class="rows" *ngIf="vehicleFuelStats.length">
          <div *ngFor="let v of pFuel" class="fuel-item">
            <div class="fuel-top"><span class="row-plate">{{ v.plate }}</span><b class="mono" [style.color]="fuelC(v.consumption)">{{ v.consumption|number:'1.1-1' }}</b></div>
            <div class="progress progress--sm"><div class="progress-fill" [style.width.%]="(v.consumption/maxFuelConsumption)*100" [style.background]="fuelC(v.consumption)"></div></div>
            <span class="row-sub">{{ v.totalLiters|number:'1.0-0' }}L · {{ v.totalKm|number:'1.0-0' }}km</span>
          </div>
          <div class="pager" *ngIf="vehicleFuelStats.length>5"><button (click)="fuP=fuP-1" [disabled]="fuP===0">&#8249;</button><span>{{ fuP+1 }}/{{ Math.ceil(vehicleFuelStats.length/5) }}</span><button (click)="fuP=fuP+1" [disabled]="(fuP+1)*5>=vehicleFuelStats.length">&#8250;</button></div>
        </div>
        <div class="empty" *ngIf="!vehicleFuelStats.length">Aucune donnée</div>
      </section>

      <!-- Recent Trips -->
      <section class="card card--trips reveal" style="--i:11">
        <div class="card-head"><h3>Derniers trajets</h3></div>
        <div class="rows" *ngIf="recentTrips.length">
          <div *ngFor="let t of pTrips" class="row-item">
            <svg viewBox="0 0 20 28" width="14" height="20" fill="none" class="trip-icon">
              <circle cx="10" cy="5" r="3.5" stroke="#10b981" stroke-width="1.5"/>
              <line x1="10" y1="9" x2="10" y2="19" stroke="var(--text-muted)" stroke-width="1.5" stroke-dasharray="2 2"/>
              <circle cx="10" cy="23" r="3.5" stroke="#ef4444" stroke-width="1.5"/>
            </svg>
            <div class="row-col"><span class="row-plate">{{ t.plate }}</span><span class="row-sub">{{ t.distance }} km · {{ t.duration }}</span></div>
            <span class="row-sub" style="margin-left:auto">{{ t.date }}</span>
          </div>
          <div class="pager" *ngIf="recentTrips.length>5"><button (click)="trP=trP-1" [disabled]="trP===0">&#8249;</button><span>{{ trP+1 }}/{{ Math.ceil(recentTrips.length/5) }}</span><button (click)="trP=trP+1" [disabled]="(trP+1)*5>=recentTrips.length">&#8250;</button></div>
        </div>
        <div class="empty" *ngIf="!recentTrips.length">Aucun trajet</div>
      </section>

      <!-- Drivers -->
      <section class="card reveal" style="--i:12">
        <div class="card-head"><h3>Conducteurs</h3><span class="badge badge--muted">{{ drivers.length }}</span></div>
        <div class="rows" *ngIf="drivers.length">
          <div *ngFor="let d of pDrv" class="row-item">
            <div class="avatar" [class.avatar--on]="d.active">{{ d.initials }}</div>
            <div class="row-col"><span class="row-plate">{{ d.name }}</span><span class="row-sub">{{ d.vehicle||'Non assigné' }}</span></div>
            <span class="status" [class.status--on]="d.active">{{ d.active?'Actif':'—' }}</span>
          </div>
          <div class="pager" *ngIf="drivers.length>5"><button (click)="drP=drP-1" [disabled]="drP===0">&#8249;</button><span>{{ drP+1 }}/{{ Math.ceil(drivers.length/5) }}</span><button (click)="drP=drP+1" [disabled]="(drP+1)*5>=drivers.length">&#8250;</button></div>
        </div>
        <div class="empty" *ngIf="!drivers.length">Aucun conducteur</div>
      </section>

    </div><!-- bento -->
  </div><!-- dash-inner -->
</div><!-- dash -->
</app-layout>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Sora:wght@400;600;700;800&family=JetBrains+Mono:wght@400;500;700&display=swap');

    /* ═══════════════════════════════════════
       FOUNDATION
    ═══════════════════════════════════════ */
    .dash {
      flex: 1; min-height: calc(100vh - 42px); overflow-y: auto;
      background: var(--bg-page); position: relative;
    }

    /* Ambient gradient background */
    .dash-bg {
      position: fixed; inset: 0; pointer-events: none; z-index: 0;
      background:
        radial-gradient(ellipse 80% 60% at 10% 20%, rgba(99,102,241,.06) 0%, transparent 60%),
        radial-gradient(ellipse 60% 50% at 85% 70%, rgba(16,185,129,.05) 0%, transparent 55%),
        radial-gradient(ellipse 50% 40% at 50% 10%, rgba(139,92,246,.04) 0%, transparent 50%);
    }

    /* Subtle noise texture */
    .dash-grain {
      position: fixed; inset: 0; pointer-events: none; z-index: 0;
      opacity: 0.025;
      background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 256 256' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='4' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)'/%3E%3C/svg%3E");
    }

    .dash-inner {
      position: relative; z-index: 1;
      max-width: 1440px; margin: 0 auto; padding: 0 28px 56px;
    }
    .mono { font-family: 'JetBrains Mono', ui-monospace, monospace; }

    /* ═══════════════════════════════════════
       ANIMATIONS
    ═══════════════════════════════════════ */
    .reveal {
      animation: revealUp .65s cubic-bezier(.16,1,.3,1) both;
      animation-delay: calc(var(--i, 0) * 65ms + 80ms);
    }
    @keyframes revealUp {
      from { opacity: 0; transform: translateY(22px) scale(.98); }
      to   { opacity: 1; transform: translateY(0) scale(1); }
    }
    .reveal-kpi {
      animation: kpiPop .6s cubic-bezier(.34,1.56,.64,1) both;
      animation-delay: calc(var(--kd, 0) * 60ms + 160ms);
    }
    @keyframes kpiPop {
      from { opacity: 0; transform: translateY(16px) scale(.92); }
      to   { opacity: 1; transform: translateY(0) scale(1); }
    }

    /* ═══════════════════════════════════════
       HEADER
    ═══════════════════════════════════════ */
    .d-head {
      display: flex; justify-content: space-between; align-items: flex-end;
      padding: 24px 0 16px; flex-wrap: wrap; gap: 14px;
    }
    .d-title {
      font-family: 'Sora', sans-serif; font-size: 28px; font-weight: 800;
      color: var(--text-primary); margin: 0; letter-spacing: -.6px;
      background: linear-gradient(135deg, var(--text-primary) 0%, color-mix(in srgb, var(--text-primary) 70%, #6366f1) 100%);
      -webkit-background-clip: text; -webkit-text-fill-color: transparent;
      background-clip: text;
    }
    .d-meta {
      display: flex; align-items: center; gap: 8px; margin-top: 8px; flex-wrap: wrap;
    }
    .d-chip {
      display: inline-flex; align-items: center; gap: 6px;
      font-size: 11px; font-weight: 600; color: var(--text-muted);
      background: var(--bg-secondary); padding: 4px 12px; border-radius: 20px;
      border: 1px solid var(--border-color); letter-spacing: .01em;
      backdrop-filter: blur(8px);
    }
    .chip-dot {
      width: 6px; height: 6px; border-radius: 50%;
      background: #6366f1;
    }
    .chip-dot.drv { background: #f59e0b; }
    .d-head-right { display: flex; align-items: center; gap: 12px; }

    /* ── Live badge ── */
    .live {
      display: inline-flex; align-items: center; gap: 7px;
      font-size: 10px; font-weight: 700; text-transform: uppercase;
      letter-spacing: .6px; padding: 5px 14px 5px 12px; border-radius: 20px;
      color: #ef4444; background: rgba(239,68,68,.06);
      border: 1px solid rgba(239,68,68,.12); position: relative;
      backdrop-filter: blur(8px);
    }
    .live.on {
      color: #10b981; background: rgba(16,185,129,.06);
      border-color: rgba(16,185,129,.15);
    }
    .live-core {
      width: 6px; height: 6px; border-radius: 50%; background: currentColor;
      position: relative; z-index: 1;
    }
    .live-ring {
      position: absolute; left: 12px; width: 6px; height: 6px;
      border-radius: 50%; background: currentColor; opacity: 0;
    }
    .live.on .live-ring {
      animation: livePulse 2s ease-in-out infinite;
    }
    @keyframes livePulse {
      0%   { transform: scale(1); opacity: .55; }
      100% { transform: scale(4); opacity: 0; }
    }

    /* ═══════════════════════════════════════
       KPI CARDS
    ═══════════════════════════════════════ */
    .kpi-row {
      display: grid; grid-template-columns: repeat(6, 1fr);
      gap: 12px; margin: 6px 0 20px;
    }
    .kpi {
      background: color-mix(in srgb, var(--bg-card) 85%, transparent);
      backdrop-filter: blur(12px); -webkit-backdrop-filter: blur(12px);
      border-radius: 16px;
      padding: 20px 18px 18px; position: relative; overflow: hidden;
      border: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
      cursor: default;
      display: flex; flex-direction: column; gap: 10px;
      transition: transform .3s cubic-bezier(.34,1.56,.64,1), box-shadow .3s ease, border-color .3s ease;
    }
    .kpi::before {
      content: ''; position: absolute; top: 0; left: 0; right: 0; height: 3px;
      background: linear-gradient(90deg, var(--kc), color-mix(in srgb, var(--kc) 25%, transparent));
      border-radius: 3px 3px 0 0;
    }
    .kpi-glow {
      position: absolute; top: -40px; right: -40px; width: 100px; height: 100px;
      background: radial-gradient(circle, var(--kg) 0%, transparent 70%);
      opacity: 0; transition: opacity .4s ease;
      pointer-events: none;
    }
    .kpi:hover .kpi-glow { opacity: 1; }
    .kpi:hover {
      transform: translateY(-5px) scale(1.02);
      border-color: color-mix(in srgb, var(--kc) 30%, var(--border-color));
      box-shadow:
        0 12px 32px -8px var(--ks),
        0 0 0 1px color-mix(in srgb, var(--kc) 8%, transparent);
    }

    /* Shimmer sweep on load */
    .kpi-shimmer {
      position: absolute; inset: 0; pointer-events: none;
      background: linear-gradient(110deg, transparent 30%, rgba(255,255,255,.08) 50%, transparent 70%);
      animation: shimmer 2s ease-in-out 1;
      animation-delay: calc(var(--kd, 0) * 60ms + 500ms);
      animation-fill-mode: both;
    }
    @keyframes shimmer {
      0%   { transform: translateX(-100%); opacity: 1; }
      100% { transform: translateX(100%); opacity: 0; }
    }

    .kpi-icon {
      width: 38px; height: 38px; border-radius: 12px;
      background: var(--kg); display: flex;
      align-items: center; justify-content: center;
      transition: transform .2s ease, box-shadow .2s ease;
    }
    .kpi:hover .kpi-icon {
      transform: scale(1.08);
      box-shadow: 0 4px 12px -2px var(--ks);
    }
    .kpi-num {
      font-family: 'JetBrains Mono', ui-monospace, monospace;
      font-size: 30px; font-weight: 700; line-height: 1;
      color: var(--text-primary); letter-spacing: -.5px;
      font-variant-numeric: tabular-nums;
    }
    .kpi-num small {
      font-size: 13px; font-weight: 500; opacity: .4; margin-left: 2px;
    }
    .kpi-lbl {
      font-family: 'Sora', sans-serif;
      font-size: 11px; font-weight: 500; color: var(--text-muted);
      letter-spacing: .01em;
    }

    /* ═══════════════════════════════════════
       BENTO GRID
    ═══════════════════════════════════════ */
    .bento { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; }
    .card--fleet  { grid-column: span 1; }
    .card--chart  { grid-column: span 2; }
    .card--km     { grid-column: span 2; }
    .card--exp    { grid-column: span 1; }
    .card--health { grid-column: span 2; }
    .card--geo    { grid-column: span 1; }
    .card--scores { grid-column: span 2; }
    .card--alerts { grid-column: span 1; }
    .card--trips  { grid-column: span 2; }

    /* ── Card base ── */
    .card {
      background: color-mix(in srgb, var(--bg-card) 90%, transparent);
      backdrop-filter: blur(10px); -webkit-backdrop-filter: blur(10px);
      border-radius: 18px; padding: 22px;
      border: 1px solid color-mix(in srgb, var(--border-color) 50%, transparent);
      position: relative; overflow: hidden;
      transition: transform .25s cubic-bezier(.34,1.56,.64,1), box-shadow .25s ease, border-color .25s ease;
    }
    .card::after {
      content: ''; position: absolute; top: 0; left: 0; right: 0; height: 1px;
      background: linear-gradient(90deg, transparent, rgba(255,255,255,.06), transparent);
    }
    .card:hover {
      transform: translateY(-3px);
      box-shadow: 0 20px 50px -12px rgba(0,0,0,.08), 0 0 0 1px rgba(255,255,255,.02);
      border-color: color-mix(in srgb, var(--primary, #6366f1) 12%, var(--border-color));
    }
    .card-head {
      display: flex; align-items: center; gap: 8px; margin-bottom: 18px;
    }
    .card-head h3 {
      font-family: 'Sora', sans-serif; font-size: 13px;
      font-weight: 700; color: var(--text-primary);
      margin: 0; flex: 1; letter-spacing: -.01em;
    }

    /* ── Badges ── */
    .badge {
      font-family: 'JetBrains Mono', monospace;
      font-size: 13px; font-weight: 700; letter-spacing: -.02em;
    }
    .badge--green { color: #10b981; }
    .badge--amber { color: #f59e0b; }
    .badge--muted { font-size: 11px; color: var(--text-muted); font-weight: 500; font-family: 'Sora', sans-serif; }
    .alert-pill {
      background: linear-gradient(135deg, #fef2f2, #fee2e2); color: #ef4444;
      font-family: 'JetBrains Mono', monospace;
      font-size: 11px; font-weight: 700; padding: 3px 11px; border-radius: 14px;
      border: 1px solid rgba(239,68,68,.15);
    }

    /* ═══════════════════════════════════════
       FLEET DONUT
    ═══════════════════════════════════════ */
    .fleet-row { display: flex; gap: 18px; align-items: center; }
    .donut-wrap { position: relative; width: 135px; height: 135px; flex-shrink: 0; }
    .donut-wrap svg { width: 100%; height: 100%; transform: rotate(-90deg); }
    .donut-seg {
      transition: stroke-dasharray .6s ease, stroke-dashoffset .6s ease, stroke-width .2s ease;
    }
    .donut-center {
      position: absolute; inset: 0;
      display: flex; flex-direction: column; align-items: center; justify-content: center;
    }
    .donut-num {
      font-family: 'JetBrains Mono', monospace;
      font-size: 28px; font-weight: 700; color: var(--text-primary);
      line-height: 1; letter-spacing: -.5px;
    }
    .donut-lbl {
      font-size: 9px; color: var(--text-muted);
      text-transform: uppercase; letter-spacing: .8px; margin-top: 3px;
      font-weight: 600;
    }
    .fleet-legend { display: flex; flex-direction: column; gap: 3px; flex: 1; }
    .lg-item {
      display: flex; align-items: center; gap: 8px; padding: 6px 10px;
      border-radius: 10px; font-size: 12px; color: var(--text-secondary);
      cursor: default; transition: all .18s ease;
    }
    .lg-item:hover, .lg-active {
      background: var(--bg-secondary); transform: translateX(4px);
    }
    .lg-bar { width: 4px; height: 22px; border-radius: 3px; flex-shrink: 0; transition: height .2s ease; }
    .lg-active .lg-bar { height: 28px; }
    .lg-name { flex: 1; font-weight: 500; }
    .lg-val {
      font-family: 'JetBrains Mono', monospace;
      font-weight: 700; color: var(--text-primary); font-size: 14px;
    }
    .fleet-strip {
      display: flex; height: 6px; border-radius: 100px;
      overflow: hidden; gap: 2px; margin-top: 16px; background: var(--bg-tertiary);
    }
    .strip-seg { min-width: 0; transition: flex .7s ease; border-radius: 100px; }
    .strip-hide { flex: 0 !important; }

    /* ═══════════════════════════════════════
       FUEL CHART
    ═══════════════════════════════════════ */
    .chart-wrap {
      background: color-mix(in srgb, var(--bg-secondary) 80%, transparent);
      border-radius: 14px;
      padding: 16px 18px 10px; position: relative; cursor: crosshair;
      border: 1px solid var(--border-color); border-color: transparent;
    }
    .chart-svg { width: 100%; height: 120px; display: block; overflow: visible; }
    .chart-line {
      stroke-dasharray: 2000; stroke-dashoffset: 2000;
      animation: drawLine 1.6s ease forwards .3s;
    }
    .chart-area { opacity: 0; animation: fadeIn .6s ease forwards .8s; }
    @keyframes drawLine { to { stroke-dashoffset: 0; } }
    @keyframes fadeIn { to { opacity: 1; } }
    .chart-ring { animation: ringPop .2s ease; }
    @keyframes ringPop { from { r: 0; opacity: 0; } }
    .chart-dot { transition: r .18s ease, fill .18s ease, stroke-width .18s ease; }
    .chart-labels {
      display: flex; justify-content: space-between;
      font-size: 10px; color: var(--text-muted); margin-top: 10px;
      font-weight: 500;
    }
    .chart-tip {
      position: absolute; top: 6px; transform: translateX(-50%);
      background: color-mix(in srgb, var(--bg-card) 92%, transparent);
      backdrop-filter: blur(12px); -webkit-backdrop-filter: blur(12px);
      border: 1px solid var(--border-color);
      border-radius: 12px; padding: 8px 14px;
      box-shadow: 0 10px 30px -6px rgba(0,0,0,.12);
      pointer-events: none; z-index: 5;
      display: flex; flex-direction: column; align-items: center;
      animation: tipPop .15s ease;
    }
    .chart-tip b {
      font-family: 'JetBrains Mono', monospace;
      font-size: 15px; font-weight: 700; color: #10b981;
    }
    .chart-tip span { font-size: 10px; color: var(--text-muted); font-weight: 500; }
    @keyframes tipPop { from { opacity: 0; transform: translateX(-50%) translateY(5px) scale(.95); } }

    /* ═══════════════════════════════════════
       PROGRESS BARS
    ═══════════════════════════════════════ */
    .progress {
      flex: 1; height: 8px; background: var(--bg-tertiary);
      border-radius: 100px; overflow: hidden;
    }
    .progress--lg { height: 14px; }
    .progress--sm { height: 6px; }
    .progress--xs { height: 4px; }
    .progress-fill {
      height: 100%; border-radius: 100px; position: relative;
      transition: width .9s cubic-bezier(.16,1,.3,1);
    }

    /* ═══════════════════════════════════════
       ROWS
    ═══════════════════════════════════════ */
    .rows { display: flex; flex-direction: column; gap: 2px; }
    .row-item {
      display: flex; align-items: center; gap: 10px;
      padding: 8px 10px; border-radius: 10px; font-size: 12px;
      transition: all .15s ease;
    }
    .row-item:hover {
      background: var(--bg-secondary);
      transform: translateX(2px);
    }
    .row-accent { width: 4px; height: 22px; border-radius: 3px; flex-shrink: 0; }
    .row-plate { font-size: 12px; font-weight: 600; color: var(--text-primary); }
    .row-val {
      font-size: 12px; font-weight: 700; color: var(--text-primary);
      min-width: 80px; text-align: right;
    }
    .row-sub { font-size: 11px; color: var(--text-muted); }
    .row-col { display: flex; flex-direction: column; flex: 1; min-width: 0; }
    .row-score {
      font-size: 14px; font-weight: 700; min-width: 32px; text-align: right;
    }

    /* ── Ranks ── */
    .rank {
      width: 24px; height: 24px; border-radius: 8px;
      display: flex; align-items: center; justify-content: center;
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px; font-weight: 700;
      color: var(--text-muted); background: var(--bg-tertiary);
      flex-shrink: 0; transition: transform .2s ease;
    }
    .row-item:hover .rank { transform: scale(1.1); }
    .rank-gold   { background: linear-gradient(135deg, #fbbf24, #f59e0b); color: #fff; box-shadow: 0 2px 8px rgba(245,158,11,.3); }
    .rank-silver { background: linear-gradient(135deg, #e2e8f0, #94a3b8); color: #fff; box-shadow: 0 2px 8px rgba(148,163,184,.3); }
    .rank-bronze { background: linear-gradient(135deg, #d97706, #92400e); color: #fff; box-shadow: 0 2px 8px rgba(217,119,6,.3); }

    /* ═══════════════════════════════════════
       EXPENSES
    ═══════════════════════════════════════ */
    .exp-list { display: flex; flex-direction: column; gap: 16px; }
    .exp-item { display: flex; flex-direction: column; gap: 6px; }
    .exp-header { display: flex; align-items: center; gap: 8px; font-size: 12px; }
    .exp-dot { width: 9px; height: 9px; border-radius: 4px; flex-shrink: 0; }
    .exp-name { flex: 1; color: var(--text-secondary); font-weight: 500; }
    .exp-val { color: var(--text-primary); font-size: 12px; font-weight: 700; }

    /* ═══════════════════════════════════════
       HEALTH
    ═══════════════════════════════════════ */
    .health-layout { display: flex; gap: 24px; align-items: center; }
    .gauge-wrap { position: relative; width: 135px; height: 135px; flex-shrink: 0; }
    .gauge-wrap svg { width: 100%; height: 100%; transform: rotate(-90deg); }
    .gauge-arc {
      transition: stroke-dashoffset 1.2s cubic-bezier(.16,1,.3,1);
    }
    .gauge-center {
      position: absolute; inset: 0;
      display: flex; flex-direction: column; align-items: center; justify-content: center;
    }
    .gauge-pct {
      font-family: 'JetBrains Mono', monospace;
      font-size: 28px; font-weight: 700; line-height: 1;
    }
    .gauge-lbl { font-size: 10px; color: var(--text-muted); margin-top: 3px; font-weight: 500; }
    .health-items { display: flex; flex-direction: column; gap: 10px; flex: 1; }
    .health-card {
      display: flex; flex-direction: column; gap: 5px;
      padding: 12px 16px; border-radius: 12px;
      background: var(--bg-secondary);
      border-left: 3px solid var(--hc);
      transition: transform .15s ease, background .15s ease;
    }
    .health-card:hover { transform: translateX(3px); }
    .health-card b {
      font-family: 'JetBrains Mono', monospace;
      font-size: 20px; font-weight: 700; color: var(--text-primary); line-height: 1;
    }
    .health-card span { font-size: 11px; color: var(--text-muted); font-weight: 500; }

    /* ═══════════════════════════════════════
       GEOZONES
    ═══════════════════════════════════════ */
    .geo-list { display: flex; flex-direction: column; gap: 2px; }
    .geo-item {
      display: flex; align-items: center; gap: 12px;
      padding: 8px 10px; border-radius: 10px; transition: all .15s;
    }
    .geo-item:hover { background: var(--bg-secondary); transform: translateX(2px); }
    .geo-dot {
      width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0;
      transition: transform .2s ease;
    }
    .geo-item:hover .geo-dot { transform: scale(1.3); }
    .geo-name { flex: 1; font-size: 13px; font-weight: 500; color: var(--text-secondary); }
    .geo-count { font-size: 14px; font-weight: 700; color: var(--text-primary); }

    /* ═══════════════════════════════════════
       ALERTS TIMELINE
    ═══════════════════════════════════════ */
    .timeline { display: flex; flex-direction: column; gap: 0; }
    .tl-item {
      display: flex; gap: 12px; padding: 8px 4px;
      border-radius: 8px; transition: background .15s;
    }
    .tl-item:hover { background: var(--bg-secondary); }
    .tl-item.tl-new { animation: alertFlash .6s ease; }
    @keyframes alertFlash {
      0% { background: rgba(99,102,241,.08); }
      100% { background: transparent; }
    }
    .tl-dot {
      width: 10px; height: 10px; border-radius: 50%;
      background: var(--bg-tertiary); border: 2px solid var(--text-muted);
      flex-shrink: 0; margin-top: 3px;
      transition: transform .2s ease;
    }
    .tl-item:hover .tl-dot { transform: scale(1.2); }
    .tl-warn   { border-color: #f59e0b; background: rgba(245,158,11,.15); box-shadow: 0 0 6px rgba(245,158,11,.2); }
    .tl-danger { border-color: #ef4444; background: rgba(239,68,68,.15); box-shadow: 0 0 6px rgba(239,68,68,.2); }
    .tl-info   { border-color: #3b82f6; background: rgba(59,130,246,.15); box-shadow: 0 0 6px rgba(59,130,246,.2); }
    .tl-body { min-width: 0; }
    .tl-msg { font-size: 12px; font-weight: 500; color: var(--text-primary); line-height: 1.4; display: block; }
    .tl-time { font-size: 10px; color: var(--text-muted); display: block; margin-top: 2px; font-weight: 500; }

    /* ═══════════════════════════════════════
       FUEL PER VEHICLE
    ═══════════════════════════════════════ */
    .fuel-item { display: flex; flex-direction: column; gap: 4px; padding: 6px 0; }
    .fuel-top { display: flex; justify-content: space-between; font-size: 12px; }
    .fuel-top b { font-size: 13px; font-weight: 700; }

    /* ═══════════════════════════════════════
       DRIVERS
    ═══════════════════════════════════════ */
    .avatar {
      width: 34px; height: 34px; border-radius: 10px;
      background: color-mix(in srgb, var(--primary, #6366f1) 12%, transparent);
      color: var(--primary, #6366f1); display: flex; align-items: center; justify-content: center;
      font-family: 'Sora', sans-serif;
      font-size: 11px; font-weight: 700; flex-shrink: 0;
      border: 2px solid transparent;
      transition: transform .2s ease;
    }
    .row-item:hover .avatar { transform: scale(1.08); }
    .avatar--on { border-color: #22c55e; box-shadow: 0 0 8px rgba(34,197,94,.2); }
    .status {
      font-size: 10px; font-weight: 600; padding: 3px 10px;
      border-radius: 14px; background: var(--bg-tertiary); color: var(--text-muted);
    }
    .status--on {
      background: rgba(16,185,129,.08); color: #10b981;
      border: 1px solid rgba(16,185,129,.15);
    }
    .trip-icon { flex-shrink: 0; }

    /* ═══════════════════════════════════════
       PAGINATION
    ═══════════════════════════════════════ */
    .pager {
      display: flex; align-items: center; justify-content: center;
      gap: 10px; margin-top: 12px; padding-top: 12px;
      border-top: 1px solid var(--border-color);
    }
    .pager button {
      width: 30px; height: 30px; border: 1px solid var(--border-color);
      border-radius: 10px; background: var(--bg-card); color: var(--text-secondary);
      font-size: 15px; font-weight: 700; cursor: pointer;
      display: flex; align-items: center; justify-content: center;
      transition: all .2s cubic-bezier(.34,1.56,.64,1);
    }
    .pager button:hover:not(:disabled) {
      background: var(--primary, #6366f1); color: #fff; border-color: var(--primary, #6366f1);
      transform: scale(1.1);
      box-shadow: 0 4px 12px rgba(99,102,241,.25);
    }
    .pager button:disabled { opacity: .2; cursor: default; }
    .pager span { font-size: 11px; color: var(--text-muted); font-weight: 500; }

    /* ═══════════════════════════════════════
       EMPTY STATE
    ═══════════════════════════════════════ */
    .empty {
      padding: 32px; text-align: center; color: var(--text-muted);
      font-size: 12px; opacity: .45; font-weight: 500;
    }

    /* ═══════════════════════════════════════
       RESPONSIVE
    ═══════════════════════════════════════ */
    @media (max-width: 1200px) {
      .kpi-row { grid-template-columns: repeat(3, 1fr); }
    }
    @media (max-width: 900px) {
      .bento { grid-template-columns: repeat(2, 1fr); }
      .card--chart, .card--scores, .card--km, .card--trips { grid-column: span 2; }
      .card--fleet { grid-column: span 1; }
      .kpi-row { grid-template-columns: repeat(3, 1fr); }
    }
    @media (max-width: 600px) {
      .bento { grid-template-columns: 1fr; }
      .card--chart, .card--scores, .card--km, .card--trips, .card--fleet { grid-column: span 1; }
      .kpi-row { grid-template-columns: repeat(2, 1fr); }
      .dash-inner { padding: 0 14px 36px; }
      .d-head { flex-direction: column; align-items: flex-start; }
      .fleet-row { flex-direction: column; }
      .health-layout { flex-direction: column; }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
  company: Company | null = null;
  selectedPeriod = 'week'; fromDate = ''; toDate = '';
  isConnected = false;
  Math = Math;

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

  // Display values for animated counting
  dVehicles=0; dMoving=0; dStopped=0; dAlerts=0; dFuel=0; dCost=0;

  fleetSegs:any[]=[]; donutSegs:any[]=[];
  readonly circ56=2*Math.PI*56;
  expItems:any[]=[]; hItems:any[]=[];

  get totalMotion():number{return this.motionData.stationary+this.motionData.ignitionOn+this.motionData.movingIgnition+this.motionData.noState+this.motionData.noCoords;}
  get totalHealth():number{return this.healthData.healthy+this.healthData.attention+this.healthData.unhealthy;}
  get hPct():number{return this.totalHealth?Math.round((this.healthData.healthy/this.totalHealth)*100):0;}
  get hColor():string{const p=this.hPct;return p>=80?'#22c55e':p>=50?'#eab308':'#ef4444';}

  scP=0;unP=0;fuP=0;alertsP=0;trP=0;drP=0;
  get pScores(){return this.drivingScores.slice(this.scP*5,this.scP*5+5);}
  get pUnits(){return this.topUnits.slice(this.unP*5,this.unP*5+5);}
  get pFuel(){return this.vehicleFuelStats.slice(this.fuP*5,this.fuP*5+5);}
  get pagedAlerts(){return this.alerts.slice(this.alertsP*6,this.alertsP*6+6);}
  get pTrips(){return this.recentTrips.slice(this.trP*5,this.trP*5+5);}
  get pDrv(){return this.drivers.slice(this.drP*5,this.drP*5+5);}

  constructor(private router:Router,private apiService:ApiService,private signalrService:SignalRService,private cdr:ChangeDetectorRef){}

  ngOnInit(){
    if(!this.apiService.isAuthenticated()){this.router.navigate(['/login']);return;}
    const u=this.apiService.getCurrentUserSync();
    if(u) this.company={id:u.companyId.toString(),name:u.companyName,type:'transport',subscriptionId:'1'} as Company;
    const today=new Date();
    this.toDate=today.toISOString().split('T')[0];
    this.fromDate=new Date(today.getTime()-7*86400000).toISOString().split('T')[0];
    this.cLabels=Array.from({length:7},(_,i)=>new Date(today.getTime()-(6-i)*86400000).toLocaleDateString('fr-FR',{day:'2-digit',month:'2-digit'}));
    this.loadAll();
    this.wire();
  }
  ngOnDestroy(){this.destroy$.next();this.destroy$.complete();}

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
      {name:'En mouvement',color:'#84cc16',value:this.motionData.movingIgnition},
      {name:'Moteur allumé',color:'#f97316',value:this.motionData.ignitionOn},
      {name:'À l\'arrêt',color:'#ef4444',value:this.motionData.stationary},
      {name:'Maintenance',color:'#6b7280',value:this.motionData.noState},
      {name:'Sans GPS',color:'#94a3b8',value:this.motionData.noCoords},
    ];
    this.fleetSegs=segs.map(s=>({...s,pct:tot?Math.round((s.value/tot)*100):0}));
    let cum=0;
    this.donutSegs=segs.filter(s=>s.value>0).map(s=>{
      const da=(s.value/tot)*this.circ56;
      const offset=-(cum/tot)*this.circ56;
      cum+=s.value;
      return{...s,da,offset};
    });
    this.expItems=[
      {name:'Carburant',color:'#3b82f6',value:this.fuelCost},
      {name:'Entretien',color:'#10b981',value:this.maintenanceCost},
      {name:'Réparation',color:'#f59e0b',value:this.repairCost},
      {name:'Autres',color:'#94a3b8',value:this.otherCost},
    ];
    this.hItems=[
      {name:'Bon état',color:'#22c55e',value:this.healthData.healthy},
      {name:'Attention',color:'#eab308',value:this.healthData.attention},
      {name:'Mauvais',color:'#ef4444',value:this.healthData.unhealthy},
    ];
  }

  private wire(){
    this.signalrService.connectionState$.pipe(takeUntil(this.destroy$)).subscribe((s:string)=>{this.isConnected=s==='Connected';this.cdr.detectChanges();});
    this.signalrService.alert$.pipe(takeUntil(this.destroy$)).subscribe((a:any)=>{
      const item={message:`${a.vehicleName}: ${a.type}`,severity:'warning',time:'À l\'instant',_isNew:true};
      this.alerts.unshift(item);this.alertsP=0;this.rebuild();this.cdr.detectChanges();
      setTimeout(()=>{item._isNew=false;this.cdr.detectChanges();},600);
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
        if(d.topUnits?.length){this.topUnits=d.topUnits.map((u:any)=>({name:u.name,color:u.color,mileage:Math.round(u.mileage??0)}));this.maxMileage=Math.max(...this.topUnits.map(u=>u.mileage),1);}
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
            this.cPoints=this.cVals.map((v:number,i:number)=>({x:this.cVals.length>1?(i/(this.cVals.length-1))*500:250,y:125-(v/mx)*118}));
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

  scoreC(s:number):string{return s>=80?'#22c55e':s>=60?'#f59e0b':'#ef4444';}
  scoreE(s:number):string{return s>=80?'#4ade80':s>=60?'#fbbf24':'#f87171';}
  fuelC(c:number):string{return c<=6?'#22c55e':c<=8?'#f59e0b':'#ef4444';}

  onPeriodChange(p:string){this.selectedPeriod=p;}
  onDateRangeChange(r:{from:string;to:string}){this.fromDate=r.from;this.toDate=r.to;}
  applyFilter(){this.scP=0;this.unP=0;this.fuP=0;this.alertsP=0;this.trP=0;this.drP=0;this.loadAll();}
}
