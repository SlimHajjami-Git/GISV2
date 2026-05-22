import { Component, OnInit, OnDestroy, AfterViewInit, ChangeDetectorRef, ViewChild, ElementRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../services/api.service';
import { SignalRService } from '../services/signalr.service';
import { Vehicle, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { DateFilterBarComponent, CardComponent, LegendItemComponent } from './shared/ui';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, DateFilterBarComponent, CardComponent, LegendItemComponent, ...USER_PREF_PIPES],
  template: `
<app-layout>
<div class="aurora">

  <!-- Animated background layers -->
  <div class="aurora-bg">
    <canvas #networkCanvas class="network-canvas"></canvas>
    <div class="orb orb-1"></div>
    <div class="orb orb-2"></div>
    <div class="orb orb-3"></div>
    <div class="grid-overlay"></div>
  </div>

  <div class="aurora-inner">

    <!-- ══ HEADER ══ -->
    <header class="hdr fade-in" style="--d:0">
      <div class="hdr-left">
        <div class="hdr-badge">
          <span class="pulse-dot" [class.on]="isConnected"></span>
          {{ isConnected ? 'LIVE' : 'OFFLINE' }}
        </div>
        <h1 class="hdr-title">
          <span class="title-gradient">Dashboard</span>
        </h1>
        <div class="hdr-chips">
          <span class="glass-chip">{{ totalMotion }} <small>véhicules</small></span>
          <span class="glass-chip">{{ drivers.length }} <small>conducteurs</small></span>
        </div>
      </div>
      <div class="hdr-right">
        <ui-date-filter-bar [selectedPeriod]="selectedPeriod" [fromDate]="fromDate" [toDate]="toDate"
          (periodChange)="onPeriodChange($event)" (dateRangeChange)="onDateRangeChange($event)" (applyFilter)="applyFilter()">
        </ui-date-filter-bar>
      </div>
    </header>

    <!-- ══ HERO KPIs ══ -->
    <div class="hero-kpis fade-in" style="--d:1">
      <div class="hero-kpi" style="--ac:#60a5fa;--ag:rgba(96,165,250,.2)">
        <div class="hero-glow"></div>
        <div class="hero-label">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="1"/><path d="M16 8h4l3 4v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
          Véhicules
        </div>
        <div class="hero-num">{{ dVehicles }}</div>
        <div class="hero-bar"><div class="hero-bar-fill" style="width:100%;background:var(--ac)"></div></div>
      </div>
      <div class="hero-kpi" style="--ac:#34d399;--ag:rgba(52,211,153,.2)">
        <div class="hero-glow"></div>
        <div class="hero-label">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M4.93 19.07A10 10 0 0119.07 4.93"/></svg>
          En mouvement
        </div>
        <div class="hero-num">{{ dMoving }}</div>
        <div class="hero-bar"><div class="hero-bar-fill" [style.width.%]="totalMotion?(motionData.movingIgnition/totalMotion)*100:0" style="background:var(--ac)"></div></div>
      </div>
      <div class="hero-kpi" style="--ac:#fbbf24;--ag:rgba(251,191,36,.2)">
        <div class="hero-glow"></div>
        <div class="hero-label">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="4"/><path d="M9 17V7h4a3 3 0 110 6H9"/></svg>
          À l'arrêt
        </div>
        <div class="hero-num">{{ dStopped }}</div>
        <div class="hero-bar"><div class="hero-bar-fill" [style.width.%]="totalMotion?(motionData.stationary/totalMotion)*100:0" style="background:var(--ac)"></div></div>
      </div>
      <div class="hero-kpi" style="--ac:#f87171;--ag:rgba(248,113,113,.2)">
        <div class="hero-glow"></div>
        <div class="hero-label">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 8A6 6 0 006 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 01-3.46 0"/></svg>
          Alertes
        </div>
        <div class="hero-num">{{ dAlerts }}</div>
        <div class="hero-bar"><div class="hero-bar-fill" style="width:100%;background:var(--ac)"></div></div>
      </div>
      <div class="hero-kpi" style="--ac:#a78bfa;--ag:rgba(167,139,250,.2)">
        <div class="hero-glow"></div>
        <div class="hero-label">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 22V5a2 2 0 012-2h8a2 2 0 012 2v17"/><path d="M15 11h2a2 2 0 012 2v4a2 2 0 004 0V9l-3-3"/></svg>
          Carburant
        </div>
        <div class="hero-num">{{ dFuel }}<span class="hero-unit">L</span></div>
        <div class="hero-bar"><div class="hero-bar-fill" style="width:100%;background:var(--ac)"></div></div>
      </div>
      <div class="hero-kpi" style="--ac:#22d3ee;--ag:rgba(34,211,238,.2)">
        <div class="hero-glow"></div>
        <div class="hero-label">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="2" y="6" width="20" height="14" rx="3"/><path d="M2 10h20"/></svg>
          Coût total
        </div>
        <div class="hero-num">{{ dCost }}<span class="hero-unit">DT</span></div>
        <div class="hero-bar"><div class="hero-bar-fill" style="width:100%;background:var(--ac)"></div></div>
      </div>
    </div>

    <!-- ══ MAIN GRID ══ -->
    <div class="grid">

      <!-- Fleet Status -->
      <section class="glass-card gc-fleet fade-in" style="--d:2">
        <div class="gc-head"><span class="gc-title">État flotte</span></div>
        <div class="fleet-body">
          <div class="donut-wrap">
            <svg viewBox="0 0 160 160">
              <defs><filter id="dg"><feGaussianBlur stdDeviation="4" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
              <circle cx="80" cy="80" r="62" fill="none" stroke="rgba(255,255,255,.04)" stroke-width="14"/>
              <circle *ngFor="let s of donutSegs;let si=index" cx="80" cy="80" r="62" fill="none"
                [attr.stroke]="s.color" [attr.stroke-width]="hSeg===si?22:14" stroke-linecap="round"
                [attr.stroke-dasharray]="s.da2+' '+(circ62-s.da2)" [attr.stroke-dashoffset]="s.offset2"
                class="donut-seg" [attr.filter]="hSeg===si?'url(#dg)':'none'"
                (mouseenter)="hSeg=si" (mouseleave)="hSeg=-1"/>
            </svg>
            <div class="donut-center">
              <span class="donut-big">{{ totalMotion }}</span>
              <span class="donut-sub">VÉHICULES</span>
            </div>
          </div>
          <div class="fleet-rows">
            <div *ngFor="let s of donutSegs;let si=index" class="fleet-row-item" (mouseenter)="hSeg=si" (mouseleave)="hSeg=-1" [class.row-hl]="hSeg===si">
              <span class="dot-glow" [style.--dc]="s.color"></span>
              <span class="fleet-name">{{ s.name }}</span>
              <b class="fleet-val">{{ s.value }}</b>
            </div>
          </div>
        </div>
        <div class="fleet-strip">
          <div *ngFor="let s of fleetSegs" class="strip-seg" [style.flex]="s.value||0" [style.background]="s.color" [class.strip-hide]="!s.value"></div>
        </div>
      </section>

      <!-- Fuel Chart -->
      <section class="glass-card gc-chart fade-in" style="--d:3">
        <div class="gc-head">
          <span class="gc-title">Consommation carburant</span>
          <span class="neon-badge green">{{ totalFuelConsumed | number:'1.0-0' }} L</span>
        </div>
        <div class="chart-area" (mousemove)="onChartHover($event)" (mouseleave)="cIdx=-1">
          <svg viewBox="0 0 500 140" preserveAspectRatio="none" class="chart-svg">
            <defs>
              <linearGradient id="aFill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stop-color="#34d399" stop-opacity=".3"/>
                <stop offset="100%" stop-color="#34d399" stop-opacity="0"/>
              </linearGradient>
              <linearGradient id="aStroke" x1="0" y1="0" x2="1" y2="0">
                <stop offset="0%" stop-color="#6ee7b7"/><stop offset="50%" stop-color="#34d399"/><stop offset="100%" stop-color="#059669"/>
              </linearGradient>
              <filter id="lg"><feGaussianBlur stdDeviation="3.5" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
            </defs>
            <line *ngFor="let y of [0,35,70,105,140]" x1="0" [attr.y1]="y" x2="500" [attr.y2]="y" stroke="rgba(255,255,255,.04)" stroke-width=".5"/>
            <polygon *ngIf="cPts" [attr.points]="'0,140 '+cPts+' 500,140'" fill="url(#aFill)" class="area-path"/>
            <polyline *ngIf="cPts" fill="none" stroke="url(#aStroke)" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round" [attr.points]="cPts" filter="url(#lg)" class="line-path"/>
            <ng-container *ngFor="let p of cPoints;let pi=index">
              <circle *ngIf="cIdx===pi" [attr.cx]="p.x" [attr.cy]="p.y" r="16" fill="#34d399" opacity=".1"/>
              <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="cIdx===pi?5:2.5" [attr.fill]="cIdx===pi?'#34d399':'#0f1729'" stroke="#34d399" [attr.stroke-width]="cIdx===pi?2.5:1.5" class="chart-pt"/>
            </ng-container>
            <line *ngIf="cIdx>=0&&cPoints[cIdx]" [attr.x1]="cPoints[cIdx].x" y1="0" [attr.x2]="cPoints[cIdx].x" y2="140" stroke="#34d399" stroke-width=".6" opacity=".2" stroke-dasharray="4 3"/>
          </svg>
          <div class="chart-x"><span *ngFor="let l of cLabels">{{ l }}</span></div>
          <div class="chart-tooltip" *ngIf="cIdx>=0&&cVals[cIdx]!==undefined" [style.left.%]="cPoints[cIdx]?(cPoints[cIdx].x/5):0">
            <b>{{ cVals[cIdx]|number:'1.0-0' }} L</b><span>{{ cLabels[cIdx]||'' }}</span>
          </div>
        </div>
      </section>

      <!-- Vehicle Health -->
      <section class="glass-card gc-health fade-in" style="--d:4">
        <div class="gc-head"><span class="gc-title">Santé véhicules</span></div>
        <div class="health-body">
          <div class="gauge-box">
            <svg viewBox="0 0 160 160">
              <defs><filter id="gg"><feGaussianBlur stdDeviation="5" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
              <circle cx="80" cy="80" r="65" fill="none" stroke="rgba(255,255,255,.04)" stroke-width="10"/>
              <circle cx="80" cy="80" r="65" fill="none" [attr.stroke]="hColor" stroke-width="10" stroke-linecap="round"
                stroke-dasharray="408" [attr.stroke-dashoffset]="408-(hPct/100)*408" class="gauge-ring" filter="url(#gg)"/>
            </svg>
            <div class="gauge-inner">
              <span class="gauge-big" [style.color]="hColor">{{ hPct }}<small>%</small></span>
              <span class="gauge-sub">en bon état</span>
            </div>
          </div>
          <div class="health-bars">
            <div *ngFor="let h of hItems" class="health-row">
              <div class="health-info"><span class="health-name">{{ h.name }}</span><b [style.color]="h.color">{{ h.value }}</b></div>
              <div class="bar-track"><div class="bar-fill" [style.width.%]="totalHealth?(h.value/totalHealth)*100:0" [style.background]="h.color" [style.box-shadow]="'0 0 12px '+h.color+'55'"></div></div>
            </div>
          </div>
        </div>
      </section>

      <!-- Dépenses -->
      <section class="glass-card gc-exp fade-in" style="--d:5">
        <div class="gc-head"><span class="gc-title">Dépenses</span><span class="neon-badge amber">{{ totalCost | appCurrency:0 }}</span></div>
        <div class="exp-rows">
          <div *ngFor="let e of expItems" class="exp-row">
            <div class="exp-meta"><span class="dot-glow" [style.--dc]="e.color"></span><span class="exp-name">{{ e.name }}</span><b class="exp-amt">{{ e.value | appCurrency:0 }}</b></div>
            <div class="bar-track"><div class="bar-fill" [style.width.%]="totalCost?(e.value/totalCost)*100:0" [style.background]="e.color" [style.box-shadow]="'0 0 10px '+e.color+'44'"></div></div>
          </div>
        </div>
      </section>

      <!-- Kilométrage -->
      <section class="glass-card gc-km fade-in" style="--d:6">
        <div class="gc-head"><span class="gc-title">Kilométrage</span></div>
        <div class="list-rows" *ngIf="topUnits.length">
          <div *ngFor="let u of pUnits" class="list-row">
            <span class="row-bar" [style.background]="u.color"></span>
            <span class="row-name" style="min-width:90px">{{ u.name }}</span>
            <div class="bar-track bar-lg"><div class="bar-fill" [style.width.%]="(u.mileage/maxMileage)*100" [style.background]="u.color" [style.box-shadow]="'0 0 10px '+u.color+'44'"></div></div>
            <span class="row-mono">{{ u.mileage | appDistance:0 }}</span>
          </div>
          <div class="pgr" *ngIf="topUnits.length>5"><button (click)="unP=unP-1" [disabled]="unP===0">&#8249;</button><span>{{ unP+1 }}/{{ Math.ceil(topUnits.length/5) }}</span><button (click)="unP=unP+1" [disabled]="(unP+1)*5>=topUnits.length">&#8250;</button></div>
        </div>
        <div class="empty-msg" *ngIf="!topUnits.length">Aucune donnée</div>
      </section>

      <!-- Géozones (paired with km span2 + geo span1 = 3) -->
      <section class="glass-card gc-geo fade-in" style="--d:7">
        <div class="gc-head"><span class="gc-title">Géozones</span></div>
        <div class="list-rows" *ngIf="geofences.length">
          <div *ngFor="let g of geofences" class="list-row">
            <span class="dot-glow lg" [style.--dc]="g.color"></span>
            <span class="row-name flex-1">{{ g.name }}</span>
            <b class="row-mono">{{ g.count }}</b>
          </div>
        </div>
        <div class="empty-msg" *ngIf="!geofences.length">Aucune géozone</div>
      </section>

      <!-- Scores -->
      <section class="glass-card gc-scores fade-in" style="--d:8">
        <div class="gc-head"><span class="gc-title">Scores de conduite</span></div>
        <div class="list-rows" *ngIf="drivingScores.length">
          <div *ngFor="let s of pScores;let i=index" class="list-row">
            <span class="rank-badge" [class.gold]="scP*5+i<1" [class.silver]="scP*5+i===1" [class.bronze]="scP*5+i===2">{{ scP*5+i+1 }}</span>
            <span class="row-name">{{ s.plate }}</span>
            <div class="bar-track"><div class="bar-fill" [style.width.%]="s.score" [style.background]="'linear-gradient(90deg,'+scoreC(s.score)+','+scoreE(s.score)+')'" [style.box-shadow]="'0 0 10px '+scoreC(s.score)+'55'"></div></div>
            <b class="score-num" [style.color]="scoreC(s.score)">{{ s.score }}</b>
          </div>
          <div class="pgr" *ngIf="drivingScores.length>5"><button (click)="scP=scP-1" [disabled]="scP===0">&#8249;</button><span>{{ scP+1 }}/{{ Math.ceil(drivingScores.length/5) }}</span><button (click)="scP=scP+1" [disabled]="(scP+1)*5>=drivingScores.length">&#8250;</button></div>
        </div>
        <div class="empty-msg" *ngIf="!drivingScores.length">Aucun score</div>
      </section>

      <!-- Alertes (paired with scores span2 + alerts span1 = 3) -->
      <section class="glass-card gc-alerts fade-in" style="--d:9">
        <div class="gc-head">
          <span class="gc-title">Alertes</span>
          <span class="neon-badge red" *ngIf="alerts.length">{{ alerts.length }}</span>
        </div>
        <div class="alert-list" *ngIf="alerts.length">
          <div *ngFor="let a of pagedAlerts" class="alert-row" [class.alert-new]="a._isNew">
            <span class="alert-dot" [class.a-warn]="a.severity==='warning'" [class.a-danger]="a.severity==='danger'" [class.a-info]="a.severity==='info'"></span>
            <div class="alert-body"><span class="alert-msg">{{ a.message }}</span><span class="alert-time">{{ a.time }}</span></div>
          </div>
          <div class="pgr" *ngIf="alerts.length>6"><button (click)="alertsP=alertsP-1" [disabled]="alertsP===0">&#8249;</button><span>{{ alertsP+1 }}/{{ Math.ceil(alerts.length/6) }}</span><button (click)="alertsP=alertsP+1" [disabled]="(alertsP+1)*6>=alerts.length">&#8250;</button></div>
        </div>
        <div class="empty-msg" *ngIf="!alerts.length">Aucune alerte</div>
      </section>

      <!-- Trajets -->
      <section class="glass-card gc-trips fade-in" style="--d:10">
        <div class="gc-head"><span class="gc-title">Derniers trajets</span></div>
        <div class="list-rows" *ngIf="recentTrips.length">
          <div *ngFor="let t of pTrips" class="list-row">
            <svg viewBox="0 0 20 28" width="14" height="20" fill="none" class="trip-svg">
              <circle cx="10" cy="5" r="3.5" stroke="#34d399" stroke-width="1.5"/><line x1="10" y1="9" x2="10" y2="19" stroke="rgba(255,255,255,.15)" stroke-width="1.5" stroke-dasharray="2 2"/><circle cx="10" cy="23" r="3.5" stroke="#f87171" stroke-width="1.5"/>
            </svg>
            <div class="trip-info"><span class="row-name">{{ t.plate }}</span><span class="trip-sub">{{ t.distance }} km · {{ t.duration }}</span></div>
            <span class="trip-date">{{ t.date }}</span>
          </div>
          <div class="pgr" *ngIf="recentTrips.length>5"><button (click)="trP=trP-1" [disabled]="trP===0">&#8249;</button><span>{{ trP+1 }}/{{ Math.ceil(recentTrips.length/5) }}</span><button (click)="trP=trP+1" [disabled]="(trP+1)*5>=recentTrips.length">&#8250;</button></div>
        </div>
        <div class="empty-msg" *ngIf="!recentTrips.length">Aucun trajet</div>
      </section>

      <!-- Conso/Véhicule (paired with trips span2 + conso span1 = 3) -->
      <section class="glass-card gc-conso fade-in" style="--d:11">
        <div class="gc-head"><span class="gc-title">Conso / Véhicule</span><span class="neon-badge muted">L/100km</span></div>
        <div class="list-rows" *ngIf="vehicleFuelStats.length">
          <div *ngFor="let v of pFuel" class="fuel-row">
            <div class="fuel-meta"><span class="row-name">{{ v.plate }}</span><b [style.color]="fuelC(v.consumption)">{{ v.consumption|number:'1.1-1' }}</b></div>
            <div class="bar-track bar-sm"><div class="bar-fill" [style.width.%]="(v.consumption/maxFuelConsumption)*100" [style.background]="fuelC(v.consumption)"></div></div>
            <span class="fuel-sub">{{ v.totalLiters | appVolume:0 }} · {{ v.totalKm | appDistance:0 }}</span>
          </div>
          <div class="pgr" *ngIf="vehicleFuelStats.length>5"><button (click)="fuP=fuP-1" [disabled]="fuP===0">&#8249;</button><span>{{ fuP+1 }}/{{ Math.ceil(vehicleFuelStats.length/5) }}</span><button (click)="fuP=fuP+1" [disabled]="(fuP+1)*5>=vehicleFuelStats.length">&#8250;</button></div>
        </div>
        <div class="empty-msg" *ngIf="!vehicleFuelStats.length">Aucune donnée</div>
      </section>

      <!-- Conducteurs (full width) -->
      <section class="glass-card gc-drv fade-in" style="--d:12">
        <div class="gc-head"><span class="gc-title">Conducteurs</span><span class="neon-badge muted">{{ drivers.length }}</span></div>
        <div class="list-rows" *ngIf="drivers.length">
          <div *ngFor="let d of pDrv" class="list-row">
            <div class="drv-avatar" [class.drv-on]="d.active">{{ d.initials }}</div>
            <div class="trip-info"><span class="row-name">{{ d.name }}</span><span class="trip-sub">{{ d.vehicle||'Non assigné' }}</span></div>
            <span class="drv-status" [class.st-on]="d.active">{{ d.active?'Actif':'—' }}</span>
          </div>
          <div class="pgr" *ngIf="drivers.length>5"><button (click)="drP=drP-1" [disabled]="drP===0">&#8249;</button><span>{{ drP+1 }}/{{ Math.ceil(drivers.length/5) }}</span><button (click)="drP=drP+1" [disabled]="(drP+1)*5>=drivers.length">&#8250;</button></div>
        </div>
        <div class="empty-msg" *ngIf="!drivers.length">Aucun conducteur</div>
      </section>

    </div><!-- grid -->
  </div><!-- inner -->
</div><!-- aurora -->
</app-layout>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Space+Grotesk:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500;700&display=swap');

    /* ════════════════════════════════════════════
       AURORA — ANIMATED DARK DASHBOARD
    ════════════════════════════════════════════ */

    :host { --font: 'Space Grotesk', sans-serif; --mono: 'JetBrains Mono', ui-monospace, monospace; }

    .aurora {
      flex: 1; min-height: calc(100vh - 42px); overflow-y: auto;
      background: #070b14; position: relative; color: #e2e8f0;
    }

    /* ── Animated background ── */
    .aurora-bg { position: fixed; inset: 0; z-index: 0; overflow: hidden; pointer-events: none; }
    .network-canvas { position: absolute; inset: 0; width: 100%; height: 100%; opacity: .45; }

    .orb {
      position: absolute; border-radius: 50%; filter: blur(100px);
      animation: drift 20s ease-in-out infinite alternate;
    }
    .orb-1 {
      width: 600px; height: 600px; top: -10%; left: -5%;
      background: radial-gradient(circle, rgba(99,102,241,.2) 0%, transparent 70%);
      animation-duration: 22s;
    }
    .orb-2 {
      width: 500px; height: 500px; top: 40%; right: -10%;
      background: radial-gradient(circle, rgba(16,185,129,.15) 0%, transparent 70%);
      animation-duration: 26s; animation-delay: -8s;
    }
    .orb-3 {
      width: 450px; height: 450px; bottom: -5%; left: 30%;
      background: radial-gradient(circle, rgba(139,92,246,.12) 0%, transparent 70%);
      animation-duration: 30s; animation-delay: -14s;
    }
    @keyframes drift {
      0%   { transform: translate(0, 0) scale(1); }
      33%  { transform: translate(40px, -30px) scale(1.05); }
      66%  { transform: translate(-20px, 20px) scale(0.95); }
      100% { transform: translate(30px, -10px) scale(1.02); }
    }

    .grid-overlay {
      position: absolute; inset: 0;
      background-image:
        linear-gradient(rgba(255,255,255,.015) 1px, transparent 1px),
        linear-gradient(90deg, rgba(255,255,255,.015) 1px, transparent 1px);
      background-size: 60px 60px;
      mask-image: radial-gradient(ellipse 70% 60% at 50% 40%, black 30%, transparent 80%);
      -webkit-mask-image: radial-gradient(ellipse 70% 60% at 50% 40%, black 30%, transparent 80%);
    }

    .aurora-inner {
      position: relative; z-index: 1;
      max-width: 1440px; margin: 0 auto; padding: 0 28px 60px;
    }

    /* ── Staggered entrance ── */
    .fade-in {
      animation: fadeUp .7s cubic-bezier(.16,1,.3,1) both;
      animation-delay: calc(var(--d, 0) * 70ms + 100ms);
    }
    @keyframes fadeUp {
      from { opacity: 0; transform: translateY(24px) scale(.97); filter: blur(6px); }
      to   { opacity: 1; transform: none; filter: blur(0); }
    }

    /* ════════════════════════════════════════════
       HEADER
    ════════════════════════════════════════════ */
    .hdr {
      display: flex; justify-content: space-between; align-items: flex-end;
      padding: 26px 0 18px; flex-wrap: wrap; gap: 16px;
    }
    .hdr-badge {
      display: inline-flex; align-items: center; gap: 7px;
      font: 700 9px/1 var(--mono); text-transform: uppercase; letter-spacing: 1.2px;
      padding: 5px 14px; border-radius: 20px;
      background: rgba(255,255,255,.04); border: 1px solid rgba(255,255,255,.06);
      color: #94a3b8; margin-bottom: 8px;
    }
    .pulse-dot {
      width: 7px; height: 7px; border-radius: 50%; background: #ef4444;
      position: relative;
    }
    .pulse-dot.on { background: #34d399; }
    .pulse-dot.on::after {
      content: ''; position: absolute; inset: -3px; border-radius: 50%; background: #34d399;
      animation: pulse 2s ease-in-out infinite; opacity: 0;
    }
    @keyframes pulse { 0%{transform:scale(1);opacity:.5} 100%{transform:scale(2.5);opacity:0} }

    .hdr-title { margin: 0; }
    .title-gradient {
      font: 800 34px/1 var(--font); letter-spacing: -.8px;
      background: linear-gradient(135deg, #f1f5f9 0%, #60a5fa 50%, #a78bfa 100%);
      -webkit-background-clip: text; -webkit-text-fill-color: transparent;
      background-clip: text;
      background-size: 200% 200%;
      animation: gradientShift 8s ease-in-out infinite;
    }
    @keyframes gradientShift {
      0%, 100% { background-position: 0% 50%; }
      50% { background-position: 100% 50%; }
    }

    .hdr-chips { display: flex; gap: 8px; margin-top: 10px; }
    .glass-chip {
      font: 700 13px/1 var(--mono); color: #e2e8f0;
      padding: 6px 14px; border-radius: 20px;
      background: rgba(255,255,255,.05); border: 1px solid rgba(255,255,255,.08);
      backdrop-filter: blur(8px);
    }
    .glass-chip small { font-size: 10px; font-weight: 500; color: #64748b; margin-left: 4px; font-family: var(--font); }
    .hdr-right { display: flex; align-items: center; gap: 12px; }

    /* ── Date filter bar dark override ── */
    ::ng-deep .aurora .filter-bar {
      background: rgba(255,255,255,.04) !important;
      border: 1px solid rgba(255,255,255,.08) !important;
      border-bottom: none !important;
      border-radius: 14px !important;
      backdrop-filter: blur(12px);
      padding: 6px 12px !important;
    }
    ::ng-deep .aurora .period-buttons button {
      background: transparent !important;
      border-color: rgba(255,255,255,.1) !important;
      color: #64748b !important;
      font-size: 11px !important;
      padding: 5px 10px !important;
    }
    ::ng-deep .aurora .period-buttons button:first-child { border-radius: 8px 0 0 8px !important; }
    ::ng-deep .aurora .period-buttons button:last-child { border-radius: 0 8px 8px 0 !important; border-right: 1px solid rgba(255,255,255,.1) !important; }
    ::ng-deep .aurora .period-buttons button.active {
      background: rgba(99,102,241,.25) !important;
      border-color: rgba(99,102,241,.4) !important;
      color: #a5b4fc !important;
    }
    ::ng-deep .aurora .period-buttons button:hover:not(.active) {
      background: rgba(255,255,255,.04) !important;
    }
    ::ng-deep .aurora .date-label { color: #475569 !important; font-size: 11px !important; }
    ::ng-deep .aurora .date-input {
      background: transparent !important;
      color: #94a3b8 !important;
      border: none !important;
      font-size: 11px !important;
      text-decoration: none !important;
    }
    ::ng-deep .aurora .date-input::-webkit-calendar-picker-indicator { filter: invert(.6); }
    ::ng-deep .aurora .btn-apply {
      background: rgba(99,102,241,.3) !important;
      border: 1px solid rgba(99,102,241,.4) !important;
      border-radius: 8px !important;
      color: #a5b4fc !important;
      font-size: 11px !important;
      padding: 5px 12px !important;
    }
    ::ng-deep .aurora .btn-apply:hover { background: rgba(99,102,241,.45) !important; }

    /* ════════════════════════════════════════════
       HERO KPIs
    ════════════════════════════════════════════ */
    .hero-kpis {
      display: grid; grid-template-columns: repeat(6, 1fr); gap: 12px;
      margin: 8px 0 22px;
    }
    .hero-kpi {
      position: relative; overflow: hidden;
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.06);
      border-radius: 16px; padding: 20px 18px 16px;
      display: flex; flex-direction: column; gap: 6px;
      cursor: default;
      transition: transform .3s cubic-bezier(.34,1.56,.64,1), border-color .3s, box-shadow .3s;
    }
    .hero-kpi::before {
      content: ''; position: absolute; top: 0; left: 0; right: 0; height: 2px;
      background: linear-gradient(90deg, var(--ac), transparent);
    }
    .hero-glow {
      position: absolute; top: -30%; right: -30%; width: 100px; height: 100px;
      background: radial-gradient(circle, var(--ag) 0%, transparent 70%);
      opacity: 0; transition: opacity .4s; pointer-events: none;
    }
    .hero-kpi:hover .hero-glow { opacity: 1; }
    .hero-kpi:hover {
      transform: translateY(-4px) scale(1.02);
      border-color: color-mix(in srgb, var(--ac) 30%, rgba(255,255,255,.06));
      box-shadow: 0 12px 40px -8px color-mix(in srgb, var(--ac) 25%, transparent);
    }
    .hero-label {
      display: flex; align-items: center; gap: 6px;
      font: 500 10px/1 var(--font); color: #64748b; text-transform: uppercase; letter-spacing: .6px;
    }
    .hero-label svg { color: var(--ac); opacity: .7; }
    .hero-num {
      font: 700 36px/1 var(--mono); color: #f1f5f9; letter-spacing: -1px;
      font-variant-numeric: tabular-nums;
    }
    .hero-unit { font-size: 14px; font-weight: 500; color: #475569; margin-left: 2px; }
    .hero-bar { height: 3px; border-radius: 3px; background: rgba(255,255,255,.04); overflow: hidden; margin-top: 2px; }
    .hero-bar-fill { height: 100%; border-radius: 3px; transition: width .9s cubic-bezier(.16,1,.3,1); }

    /* ════════════════════════════════════════════
       GLASS CARDS
    ════════════════════════════════════════════ */
    .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; }
    .gc-fleet  { grid-column: span 1; }
    .gc-chart  { grid-column: span 2; }
    .gc-health { grid-column: span 2; }
    .gc-exp    { grid-column: span 1; }
    .gc-km     { grid-column: span 2; }
    .gc-geo    { grid-column: span 1; }
    .gc-scores { grid-column: span 2; }
    .gc-alerts { grid-column: span 1; }
    .gc-trips  { grid-column: span 2; }
    .gc-conso  { grid-column: span 1; }
    .gc-drv    { grid-column: span 3; }

    .glass-card {
      background: rgba(255,255,255,.025);
      border: 1px solid rgba(255,255,255,.06);
      border-radius: 20px; padding: 22px; position: relative; overflow: hidden;
      backdrop-filter: blur(16px); -webkit-backdrop-filter: blur(16px);
      transition: transform .28s cubic-bezier(.34,1.56,.64,1), border-color .28s, box-shadow .28s;
    }
    .glass-card::before {
      content: ''; position: absolute; top: 0; left: 0; right: 0; height: 1px;
      background: linear-gradient(90deg, transparent 10%, rgba(255,255,255,.06) 50%, transparent 90%);
    }
    .glass-card:hover {
      transform: translateY(-3px);
      border-color: rgba(255,255,255,.1);
      box-shadow: 0 20px 60px -15px rgba(0,0,0,.4);
    }
    .gc-head { display: flex; align-items: center; gap: 10px; margin-bottom: 18px; }
    .gc-title {
      font: 600 13px/1 var(--font); color: #cbd5e1;
      letter-spacing: .02em; flex: 1;
    }

    /* ── Neon Badges ── */
    .neon-badge {
      font: 700 12px/1 var(--mono); padding: 4px 12px; border-radius: 14px; letter-spacing: -.02em;
    }
    .neon-badge.green { color: #34d399; background: rgba(52,211,153,.1); border: 1px solid rgba(52,211,153,.15); }
    .neon-badge.amber { color: #fbbf24; background: rgba(251,191,36,.1); border: 1px solid rgba(251,191,36,.15); }
    .neon-badge.red   { color: #f87171; background: rgba(248,113,113,.1); border: 1px solid rgba(248,113,113,.15); }
    .neon-badge.muted { color: #64748b; background: rgba(100,116,139,.08); border: 1px solid rgba(100,116,139,.1); font-family: var(--font); font-weight: 500; font-size: 11px; }

    /* ── Shared elements ── */
    .dot-glow {
      width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0;
      background: var(--dc); box-shadow: 0 0 8px var(--dc);
    }
    .dot-glow.lg { width: 10px; height: 10px; }

    .bar-track { flex: 1; height: 6px; background: rgba(255,255,255,.04); border-radius: 6px; overflow: hidden; }
    .bar-track.bar-lg { height: 10px; }
    .bar-track.bar-sm { height: 5px; }
    .bar-fill { height: 100%; border-radius: 6px; transition: width .9s cubic-bezier(.16,1,.3,1); }

    .row-name { font: 600 12px/1.3 var(--font); color: #e2e8f0; }
    .row-mono { font: 700 12px/1 var(--mono); color: #e2e8f0; min-width: 75px; text-align: right; }
    .flex-1 { flex: 1; }

    /* ════════════════════════════════════════════
       FLEET DONUT
    ════════════════════════════════════════════ */
    .fleet-body { display: flex; gap: 20px; align-items: center; }
    .donut-wrap { position: relative; width: 145px; height: 145px; flex-shrink: 0; }
    .donut-wrap svg { width: 100%; height: 100%; transform: rotate(-90deg); }
    .donut-seg { transition: stroke-dasharray .6s ease, stroke-dashoffset .6s ease, stroke-width .2s ease; }
    .donut-center { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    .donut-big { font: 700 30px/1 var(--mono); color: #f1f5f9; letter-spacing: -1px; }
    .donut-sub { font: 700 8px/1 var(--font); color: #475569; text-transform: uppercase; letter-spacing: 1.5px; margin-top: 4px; }

    .fleet-rows { display: flex; flex-direction: column; gap: 3px; flex: 1; }
    .fleet-row-item {
      display: flex; align-items: center; gap: 10px; padding: 6px 10px;
      border-radius: 10px; font-size: 12px; transition: all .18s;
    }
    .fleet-row-item:hover, .row-hl { background: rgba(255,255,255,.04); transform: translateX(4px); }
    .fleet-name { flex: 1; font: 500 12px/1.3 var(--font); color: #94a3b8; }
    .fleet-val { font: 700 14px/1 var(--mono); color: #e2e8f0; }

    .fleet-strip { display: flex; height: 5px; border-radius: 100px; overflow: hidden; gap: 2px; margin-top: 16px; background: rgba(255,255,255,.03); }
    .strip-seg { min-width: 0; transition: flex .7s ease; border-radius: 100px; }
    .strip-hide { flex: 0 !important; }

    /* ════════════════════════════════════════════
       CHART
    ════════════════════════════════════════════ */
    .chart-area {
      background: rgba(255,255,255,.02); border-radius: 14px;
      padding: 16px 18px 10px; position: relative; cursor: crosshair;
      border: 1px solid rgba(255,255,255,.04);
    }
    .chart-svg { width: 100%; height: 125px; display: block; overflow: visible; }
    .line-path { stroke-dasharray: 2000; stroke-dashoffset: 2000; animation: draw 1.6s ease forwards .3s; }
    .area-path { opacity: 0; animation: fadeA .6s ease forwards .8s; }
    @keyframes draw { to { stroke-dashoffset: 0; } }
    @keyframes fadeA { to { opacity: 1; } }
    .chart-pt { transition: r .18s ease, fill .18s ease, stroke-width .18s ease; }
    .chart-x { display: flex; justify-content: space-between; font: 500 10px/1 var(--font); color: #475569; margin-top: 10px; }
    .chart-tooltip {
      position: absolute; top: 6px; transform: translateX(-50%);
      background: rgba(15,23,41,.9); border: 1px solid rgba(255,255,255,.1);
      border-radius: 12px; padding: 8px 14px;
      box-shadow: 0 12px 40px rgba(0,0,0,.4); pointer-events: none; z-index: 5;
      display: flex; flex-direction: column; align-items: center;
      animation: tipIn .15s ease;
    }
    .chart-tooltip b { font: 700 15px/1 var(--mono); color: #34d399; }
    .chart-tooltip span { font: 500 10px/1 var(--font); color: #475569; }
    @keyframes tipIn { from { opacity: 0; transform: translateX(-50%) translateY(5px) scale(.9); } }

    /* ════════════════════════════════════════════
       HEALTH
    ════════════════════════════════════════════ */
    .health-body { display: flex; gap: 28px; align-items: center; }
    .gauge-box { position: relative; width: 145px; height: 145px; flex-shrink: 0; }
    .gauge-box svg { width: 100%; height: 100%; transform: rotate(-90deg); }
    .gauge-ring { transition: stroke-dashoffset 1.2s cubic-bezier(.16,1,.3,1); }
    .gauge-inner { position: absolute; inset: 0; display: flex; flex-direction: column; align-items: center; justify-content: center; }
    .gauge-big { font: 700 30px/1 var(--mono); }
    .gauge-big small { font-size: 18px; font-weight: 500; }
    .gauge-sub { font: 500 10px/1 var(--font); color: #475569; margin-top: 3px; }
    .health-bars { flex: 1; display: flex; flex-direction: column; gap: 14px; }
    .health-row { display: flex; flex-direction: column; gap: 6px; }
    .health-info { display: flex; justify-content: space-between; }
    .health-name { font: 500 12px/1 var(--font); color: #94a3b8; }
    .health-info b { font: 700 14px/1 var(--mono); }

    /* ════════════════════════════════════════════
       EXPENSES
    ════════════════════════════════════════════ */
    .exp-rows { display: flex; flex-direction: column; gap: 16px; }
    .exp-row { display: flex; flex-direction: column; gap: 7px; }
    .exp-meta { display: flex; align-items: center; gap: 10px; }
    .exp-name { flex: 1; font: 500 12px/1 var(--font); color: #94a3b8; }
    .exp-amt { font: 700 12px/1 var(--mono); color: #e2e8f0; }

    /* ════════════════════════════════════════════
       LIST ROWS
    ════════════════════════════════════════════ */
    .list-rows { display: flex; flex-direction: column; gap: 2px; }
    .list-row {
      display: flex; align-items: center; gap: 10px;
      padding: 8px 10px; border-radius: 10px;
      transition: all .15s;
    }
    .list-row:hover { background: rgba(255,255,255,.03); transform: translateX(3px); }
    .row-bar { width: 4px; height: 22px; border-radius: 3px; flex-shrink: 0; }

    .rank-badge {
      width: 24px; height: 24px; border-radius: 8px;
      display: flex; align-items: center; justify-content: center;
      font: 700 10px/1 var(--mono); color: #475569;
      background: rgba(255,255,255,.04); flex-shrink: 0;
      transition: transform .2s;
    }
    .list-row:hover .rank-badge { transform: scale(1.12); }
    .rank-badge.gold   { background: linear-gradient(135deg, #fbbf24, #f59e0b); color: #fff; box-shadow: 0 0 12px rgba(251,191,36,.3); }
    .rank-badge.silver { background: linear-gradient(135deg, #94a3b8, #64748b); color: #fff; box-shadow: 0 0 12px rgba(148,163,184,.3); }
    .rank-badge.bronze { background: linear-gradient(135deg, #d97706, #92400e); color: #fff; box-shadow: 0 0 12px rgba(217,119,6,.3); }
    .score-num { font: 700 14px/1 var(--mono); min-width: 32px; text-align: right; }

    /* ════════════════════════════════════════════
       FUEL
    ════════════════════════════════════════════ */
    .fuel-row { display: flex; flex-direction: column; gap: 4px; padding: 6px 0; }
    .fuel-meta { display: flex; justify-content: space-between; }
    .fuel-meta b { font: 700 13px/1 var(--mono); }
    .fuel-sub { font: 400 11px/1 var(--font); color: #475569; }

    /* ════════════════════════════════════════════
       ALERTS
    ════════════════════════════════════════════ */
    .alert-list { display: flex; flex-direction: column; gap: 0; }
    .alert-row {
      display: flex; gap: 12px; padding: 8px 6px; border-radius: 8px;
      transition: background .15s;
    }
    .alert-row:hover { background: rgba(255,255,255,.03); }
    .alert-row.alert-new { animation: alertGlow .6s ease; }
    @keyframes alertGlow { 0% { background: rgba(99,102,241,.08); } 100% { background: transparent; } }
    .alert-dot {
      width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0; margin-top: 4px;
      background: rgba(255,255,255,.06); border: 2px solid #475569;
    }
    .a-warn   { border-color: #fbbf24; background: rgba(251,191,36,.15); box-shadow: 0 0 8px rgba(251,191,36,.2); }
    .a-danger { border-color: #f87171; background: rgba(248,113,113,.15); box-shadow: 0 0 8px rgba(248,113,113,.2); }
    .a-info   { border-color: #60a5fa; background: rgba(96,165,250,.15); box-shadow: 0 0 8px rgba(96,165,250,.2); }
    .alert-body { min-width: 0; }
    .alert-msg { font: 500 12px/1.4 var(--font); color: #e2e8f0; display: block; }
    .alert-time { font: 500 10px/1 var(--font); color: #475569; display: block; margin-top: 2px; }

    /* ════════════════════════════════════════════
       TRIPS & DRIVERS
    ════════════════════════════════════════════ */
    .trip-svg { flex-shrink: 0; }
    .trip-info { display: flex; flex-direction: column; flex: 1; min-width: 0; }
    .trip-sub { font: 400 11px/1 var(--font); color: #475569; }
    .trip-date { font: 500 11px/1 var(--font); color: #475569; margin-left: auto; }

    .drv-avatar {
      width: 34px; height: 34px; border-radius: 10px;
      background: rgba(99,102,241,.12); color: #818cf8;
      display: flex; align-items: center; justify-content: center;
      font: 700 11px/1 var(--font); flex-shrink: 0;
      border: 2px solid transparent; transition: transform .2s;
    }
    .list-row:hover .drv-avatar { transform: scale(1.08); }
    .drv-on { border-color: #34d399; box-shadow: 0 0 10px rgba(52,211,153,.2); }
    .drv-status {
      font: 600 10px/1 var(--font); padding: 3px 10px; border-radius: 14px;
      background: rgba(255,255,255,.04); color: #475569;
    }
    .st-on { background: rgba(52,211,153,.08); color: #34d399; border: 1px solid rgba(52,211,153,.15); }

    /* ════════════════════════════════════════════
       PAGINATION
    ════════════════════════════════════════════ */
    .pgr {
      display: flex; align-items: center; justify-content: center;
      gap: 10px; margin-top: 12px; padding-top: 12px;
      border-top: 1px solid rgba(255,255,255,.05);
    }
    .pgr button {
      width: 30px; height: 30px; border: 1px solid rgba(255,255,255,.08);
      border-radius: 10px; background: rgba(255,255,255,.03); color: #94a3b8;
      font-size: 15px; font-weight: 700; cursor: pointer;
      display: flex; align-items: center; justify-content: center;
      transition: all .2s cubic-bezier(.34,1.56,.64,1);
    }
    .pgr button:hover:not(:disabled) {
      background: #6366f1; color: #fff; border-color: #6366f1;
      transform: scale(1.12); box-shadow: 0 0 16px rgba(99,102,241,.3);
    }
    .pgr button:disabled { opacity: .15; cursor: default; }
    .pgr span { font: 500 11px/1 var(--font); color: #475569; }

    .empty-msg { padding: 32px; text-align: center; color: #334155; font: 500 12px/1 var(--font); }

    /* ════════════════════════════════════════════
       RESPONSIVE
    ════════════════════════════════════════════ */
    @media (max-width: 1200px) { .hero-kpis { grid-template-columns: repeat(3, 1fr); } }
    @media (max-width: 900px) {
      .grid { grid-template-columns: repeat(2, 1fr); }
      .gc-chart, .gc-scores, .gc-km, .gc-trips, .gc-health { grid-column: span 2; }
      .gc-drv { grid-column: span 2; }
      .hero-kpis { grid-template-columns: repeat(3, 1fr); }
    }
    @media (max-width: 600px) {
      .grid { grid-template-columns: 1fr; }
      .gc-chart, .gc-scores, .gc-km, .gc-trips, .gc-fleet, .gc-health, .gc-drv { grid-column: span 1; }
      .hero-kpis { grid-template-columns: repeat(2, 1fr); }
      .aurora-inner { padding: 0 14px 36px; }
      .hdr { flex-direction: column; align-items: flex-start; }
      .fleet-body { flex-direction: column; }
      .health-body { flex-direction: column; }
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('networkCanvas') canvasRef!: ElementRef<HTMLCanvasElement>;
  private netAnim = 0;
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

  dVehicles=0; dMoving=0; dStopped=0; dAlerts=0; dFuel=0; dCost=0;

  fleetSegs:any[]=[]; donutSegs:any[]=[];
  readonly circ56=2*Math.PI*56;
  readonly circ62=2*Math.PI*62;
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
  ngAfterViewInit(){this.initNetwork();}

  ngOnDestroy(){
    this.destroy$.next();this.destroy$.complete();
    if(this.netAnim) cancelAnimationFrame(this.netAnim);
  }

  private initNetwork(){
    const canvas=this.canvasRef?.nativeElement;
    if(!canvas)return;
    const ctx=canvas.getContext('2d');
    if(!ctx)return;

    const dpr=window.devicePixelRatio||1;
    const resize=()=>{
      canvas.width=canvas.offsetWidth*dpr;
      canvas.height=canvas.offsetHeight*dpr;
      ctx.setTransform(dpr,0,0,dpr,0,0);
    };
    resize();
    window.addEventListener('resize',resize);

    // Particles representing GPS-tracked vehicles
    const N=35;
    const particles:{x:number;y:number;vx:number;vy:number;r:number;color:string;pulse:number;type:string}[]=[];
    const colors=['rgba(96,165,250,1)','rgba(52,211,153,1)','rgba(167,139,250,1)','rgba(251,191,36,1)','rgba(34,211,238,1)','rgba(248,113,113,1)'];
    const types=['car','truck','pin'];
    const w=()=>canvas.offsetWidth;
    const h=()=>canvas.offsetHeight;

    for(let i=0;i<N;i++){
      const speed=.15+Math.random()*.25;
      const angle=Math.random()*Math.PI*2;
      particles.push({
        x:Math.random()*w(), y:Math.random()*h(),
        vx:Math.cos(angle)*speed, vy:Math.sin(angle)*speed,
        r:1.5+Math.random()*2, color:colors[i%colors.length],
        pulse:Math.random()*Math.PI*2, type:types[i%3]
      });
    }

    const CONN_DIST=140;
    const draw=()=>{
      const W=w(),H=h();
      ctx.clearRect(0,0,W,H);

      // Update positions
      for(const p of particles){
        p.x+=p.vx; p.y+=p.vy;
        p.pulse+=.02;
        if(p.x<-20)p.x=W+20; if(p.x>W+20)p.x=-20;
        if(p.y<-20)p.y=H+20; if(p.y>H+20)p.y=-20;
      }

      // Draw connection lines between nearby particles (fleet network)
      for(let i=0;i<N;i++){
        for(let j=i+1;j<N;j++){
          const dx=particles[i].x-particles[j].x;
          const dy=particles[i].y-particles[j].y;
          const dist=Math.sqrt(dx*dx+dy*dy);
          if(dist<CONN_DIST){
            const alpha=(1-dist/CONN_DIST)*.15;
            ctx.beginPath();
            ctx.moveTo(particles[i].x,particles[i].y);
            ctx.lineTo(particles[j].x,particles[j].y);
            ctx.strokeStyle=`rgba(148,163,184,${alpha})`;
            ctx.lineWidth=.6;
            ctx.stroke();
          }
        }
      }

      // Draw particles
      for(const p of particles){
        const pulseR=p.r+Math.sin(p.pulse)*0.8;

        // Outer ping ring (GPS signal effect)
        const pingAlpha=(.3+Math.sin(p.pulse*1.5)*.15)*.15;
        ctx.beginPath();
        ctx.arc(p.x,p.y,pulseR+6+Math.sin(p.pulse)*3,0,Math.PI*2);
        ctx.strokeStyle=p.color.replace(',1)',`,${pingAlpha})`);
        ctx.lineWidth=.5;
        ctx.stroke();

        // Core dot
        ctx.beginPath();
        ctx.arc(p.x,p.y,pulseR,0,Math.PI*2);
        ctx.fillStyle=p.color;
        ctx.fill();

        // Glow
        ctx.beginPath();
        ctx.arc(p.x,p.y,pulseR+3,0,Math.PI*2);
        const grd=ctx.createRadialGradient(p.x,p.y,pulseR*.5,p.x,p.y,pulseR+3);
        grd.addColorStop(0,p.color.replace(',1)',',0.3)'));
        grd.addColorStop(1,'transparent');
        ctx.fillStyle=grd;
        ctx.fill();
      }

      this.netAnim=requestAnimationFrame(draw);
    };
    this.netAnim=requestAnimationFrame(draw);
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
      const da2=(s.value/tot)*this.circ62;
      const offset2=-(cum/tot)*this.circ62;
      cum+=s.value;
      return{...s,da,offset,da2,offset2};
    });
    this.expItems=[
      {name:'Carburant',color:'#60a5fa',value:this.fuelCost},
      {name:'Entretien',color:'#34d399',value:this.maintenanceCost},
      {name:'Réparation',color:'#fbbf24',value:this.repairCost},
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

  scoreC(s:number):string{return s>=80?'#22c55e':s>=60?'#f59e0b':'#ef4444';}
  scoreE(s:number):string{return s>=80?'#4ade80':s>=60?'#fbbf24':'#f87171';}
  fuelC(c:number):string{return c<=6?'#22c55e':c<=8?'#f59e0b':'#ef4444';}

  onPeriodChange(p:string){this.selectedPeriod=p;}
  onDateRangeChange(r:{from:string;to:string}){this.fromDate=r.from;this.toDate=r.to;}
  applyFilter(){this.scP=0;this.unP=0;this.fuP=0;this.alertsP=0;this.trP=0;this.drP=0;this.loadAll();}
}
