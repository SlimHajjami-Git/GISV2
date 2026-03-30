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
<div class="D">
  <div class="D-inner">

    <!-- HEADER -->
    <div class="hd an" style="--i:0">
      <div><h1 class="hd-t">Tableau de bord</h1><p class="hd-s">{{ totalMotion }} véhicules · {{ drivers.length }} conducteurs</p></div>
      <div class="hd-r">
        <span class="conn" [class.on]="isConnected"><b></b>{{ isConnected ? 'Live' : 'Off' }}</span>
        <ui-date-filter-bar [selectedPeriod]="selectedPeriod" [fromDate]="fromDate" [toDate]="toDate"
          (periodChange)="onPeriodChange($event)" (dateRangeChange)="onDateRangeChange($event)" (applyFilter)="applyFilter()"></ui-date-filter-bar>
      </div>
    </div>

    <!-- COLORED KPI TILES -->
    <div class="kpi-band an" style="--i:1">
      <div class="kt" style="--g1:#3b82f6;--g2:#2563eb"><div class="kt-top"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" width="22" height="22"><rect x="1" y="3" width="15" height="13" rx="1"/><path d="M16 8h4l3 4v5h-7V8z"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg></div><span class="kt-n">{{ dVehicles }}</span><span class="kt-l">Véhicules</span></div>
      <div class="kt" style="--g1:#10b981;--g2:#059669"><div class="kt-top"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round" width="22" height="22"><circle cx="12" cy="12" r="3"/><path d="M4.93 19.07A10 10 0 0119.07 4.93"/><path d="M7.76 16.24a6 6 0 018.49-8.49"/></svg></div><span class="kt-n">{{ dMoving }}</span><span class="kt-l">En mouvement</span></div>
      <div class="kt" style="--g1:#f59e0b;--g2:#d97706"><div class="kt-top"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.5" width="22" height="22"><rect x="3" y="3" width="18" height="18" rx="4"/><path d="M9 17V7h4a3 3 0 110 6H9"/></svg></div><span class="kt-n">{{ dStopped }}</span><span class="kt-l">À l'arrêt</span></div>
      <div class="kt" style="--g1:#ef4444;--g2:#dc2626"><div class="kt-top"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round" width="22" height="22"><path d="M18 8A6 6 0 006 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 01-3.46 0"/></svg></div><span class="kt-n">{{ dAlerts }}</span><span class="kt-l">Alertes</span></div>
      <div class="kt" style="--g1:#8b5cf6;--g2:#7c3aed"><div class="kt-top"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" width="22" height="22"><path d="M3 22V5a2 2 0 012-2h8a2 2 0 012 2v17"/><path d="M15 11h2a2 2 0 012 2v4a2 2 0 004 0V9l-3-3"/><path d="M3 22h12"/><rect x="6" y="6" width="6" height="5" rx="1"/></svg></div><span class="kt-n">{{ dFuel }}<small>L</small></span><span class="kt-l">Carburant</span></div>
      <div class="kt" style="--g1:#06b6d4;--g2:#0891b2"><div class="kt-top"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round" width="22" height="22"><rect x="2" y="6" width="20" height="14" rx="3"/><path d="M2 10h20"/><circle cx="17" cy="14.5" r="1.5" fill="#fff" stroke="none"/></svg></div><span class="kt-n">{{ dCost }}<small>DT</small></span><span class="kt-l">Coût total</span></div>
    </div>

    <!-- BENTO GRID -->
    <div class="bento">

      <!-- FLEET STATUS -->
      <section class="B B-fleet an" style="--i:2">
        <div class="B-h"><h3>État flotte</h3></div>
        <div class="fleet-row">
          <div class="donut-w">
            <svg viewBox="0 0 140 140">
              <defs><filter id="sg"><feGaussianBlur stdDeviation="2" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter></defs>
              <circle cx="70" cy="70" r="56" fill="none" stroke="var(--border-color)" stroke-width="14" opacity=".1"/>
              <circle *ngFor="let s of donutSegs;let si=index" cx="70" cy="70" r="56" fill="none"
                [attr.stroke]="s.color" [attr.stroke-width]="hSeg===si?20:14" stroke-linecap="round"
                [attr.stroke-dasharray]="s.da+' '+(circ56-s.da)" [attr.stroke-dashoffset]="s.offset"
                [attr.filter]="hSeg===si?'url(#sg)':null" class="seg-anim"
                (mouseenter)="hSeg=si" (mouseleave)="hSeg=-1"/>
            </svg>
            <div class="donut-c"><span class="dc-n">{{ totalMotion }}</span><span class="dc-l">total</span></div>
          </div>
          <div class="fleet-legend">
            <div *ngFor="let s of donutSegs;let si=index" class="fl" (mouseenter)="hSeg=si" (mouseleave)="hSeg=-1" [class.fl-hl]="hSeg===si">
              <i [style.background]="s.color"></i><span>{{ s.name }}</span><b>{{ s.value }}</b>
            </div>
          </div>
        </div>
        <!-- Mini strip -->
        <div class="mini-strip">
          <div *ngFor="let s of fleetSegs" class="ms" [style.flex]="s.value||0" [style.background]="s.color" [class.ms-hide]="!s.value"></div>
        </div>
      </section>

      <!-- FUEL CHART -->
      <section class="B B-chart an" style="--i:3">
        <div class="B-h"><h3>Consommation carburant</h3><span class="B-tag green">{{ totalFuelConsumed | number:'1.0-0' }} L</span></div>
        <div class="chart-w" (mousemove)="onChartHover($event)" (mouseleave)="cIdx=-1">
          <svg viewBox="0 0 500 130" preserveAspectRatio="none" class="csv">
            <defs>
              <linearGradient id="ag" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#10b981" stop-opacity=".25"/><stop offset="100%" stop-color="#10b981" stop-opacity="0"/></linearGradient>
              <filter id="lg2"><feGaussianBlur stdDeviation="1.5" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
            </defs>
            <line *ngFor="let y of [0,32,65,97,130]" x1="0" [attr.y1]="y" x2="500" [attr.y2]="y" stroke="var(--border-color)" stroke-width=".4" opacity=".2"/>
            <polygon *ngIf="cPts" [attr.points]="'0,130 '+cPts+' 500,130'" fill="url(#ag)" class="c-area"/>
            <polyline *ngIf="cPts" fill="none" stroke="#10b981" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round" [attr.points]="cPts" filter="url(#lg2)" class="c-line"/>
            <ng-container *ngFor="let p of cPoints;let pi=index">
              <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="cIdx===pi?5:3" fill="var(--bg-card)" stroke="#10b981" [attr.stroke-width]="cIdx===pi?2.5:1.5" class="c-dot"/>
            </ng-container>
            <line *ngIf="cIdx>=0&&cPoints[cIdx]" [attr.x1]="cPoints[cIdx].x" y1="0" [attr.x2]="cPoints[cIdx].x" y2="130" stroke="#10b981" stroke-width="1" opacity=".2" stroke-dasharray="3 3"/>
          </svg>
          <div class="c-labels"><span *ngFor="let l of cLabels">{{ l }}</span></div>
          <div class="c-tt" *ngIf="cIdx>=0&&cVals[cIdx]!==undefined" [style.left.%]="cPoints[cIdx]?(cPoints[cIdx].x/5):0"><b>{{ cVals[cIdx]|number:'1.0-0' }} L</b><span>{{ cLabels[cIdx]||'' }}</span></div>
        </div>
      </section>

      <!-- ROW 2: Mileage(2) + Expenses(1) = 3 -->
      <section class="B B-km an" style="--i:4">
        <div class="B-h"><h3>Kilométrage</h3></div>
        <div class="rws" *ngIf="topUnits.length">
          <div *ngFor="let u of pUnits" class="rw">
            <i class="rw-dot" [style.background]="u.color"></i>
            <span class="rw-plate" style="width:100px">{{ u.name }}</span>
            <div class="bar tall"><div class="bar-f" [style.width.%]="(u.mileage/maxMileage)*100" [style.background]="u.color"></div></div>
            <span class="rw-km">{{ u.mileage|number:'1.0-0' }} km</span>
          </div>
          <div class="pgr" *ngIf="topUnits.length>5"><button (click)="unP=unP-1" [disabled]="unP===0">‹</button><span>{{ unP+1 }}/{{ Math.ceil(topUnits.length/5) }}</span><button (click)="unP=unP+1" [disabled]="(unP+1)*5>=topUnits.length">›</button></div>
        </div>
        <div class="mt" *ngIf="!topUnits.length">Aucune donnée</div>
      </section>

      <section class="B B-exp an" style="--i:5">
        <div class="B-h"><h3>Dépenses</h3><span class="B-tag amber">{{ totalCost|number:'1.0-0' }} DT</span></div>
        <div class="exp-bars">
          <div *ngFor="let e of expItems" class="eb">
            <div class="eb-info"><i [style.background]="e.color"></i><span>{{ e.name }}</span><b>{{ e.value|number:'1.0-0' }} DT</b></div>
            <div class="eb-track"><div class="eb-fill" [style.width.%]="totalCost?(e.value/totalCost)*100:0" [style.background]="e.color"></div></div>
          </div>
        </div>
      </section>

      <!-- ROW 3: Health(2) + Geozones(1) = 3 -->
      <section class="B B-health an" style="--i:6">
        <div class="B-h"><h3>Santé véhicules</h3></div>
        <div class="h-layout">
          <div class="h-gauge-w">
            <svg viewBox="0 0 140 140">
              <circle cx="70" cy="70" r="58" fill="none" stroke="var(--border-color)" stroke-width="10" opacity=".1"/>
              <circle cx="70" cy="70" r="58" fill="none" stroke="#22c55e" stroke-width="10" stroke-linecap="round" stroke-dasharray="364" [attr.stroke-dashoffset]="364-(hPct/100)*364" class="h-arc"/>
            </svg>
            <div class="h-mid"><span class="h-pct" [style.color]="hColor">{{ hPct }}%</span><span class="h-lbl">en bon état</span></div>
          </div>
          <div class="h-items-col">
            <div *ngFor="let h of hItems" class="hi-card" [style.--hc]="h.color">
              <b>{{ h.value }}</b>
              <span>{{ h.name }}</span>
              <div class="hi-bar"><div class="hi-fill" [style.width.%]="totalHealth?(h.value/totalHealth)*100:0" [style.background]="h.color"></div></div>
            </div>
          </div>
        </div>
      </section>

      <section class="B B-geo an" style="--i:7">
        <div class="B-h"><h3>Géozones</h3></div>
        <div class="geo-l" *ngIf="geofences.length">
          <div *ngFor="let g of geofences" class="gi"><span class="gd" [style.background]="g.color"></span><span class="gn">{{ g.name }}</span><b class="gc">{{ g.count }}</b></div>
        </div>
        <div class="mt" *ngIf="!geofences.length">Aucune géozone</div>
      </section>

      <!-- ROW 4: Scores(2) + Alerts(1) = 3 -->
      <section class="B B-scores an" style="--i:8">
        <div class="B-h"><h3>Scores de conduite</h3></div>
        <div class="rws" *ngIf="drivingScores.length">
          <div *ngFor="let s of pScores;let i=index" class="rw">
            <span class="rk" [class.r1]="scP*5+i<1" [class.r2]="scP*5+i===1" [class.r3]="scP*5+i===2">{{ scP*5+i+1 }}</span>
            <span class="rw-plate">{{ s.plate }}</span>
            <div class="bar"><div class="bar-f" [style.width.%]="s.score" [style.background]="'linear-gradient(90deg,'+scoreC(s.score)+','+scoreE(s.score)+')'"></div></div>
            <b class="rw-v" [style.color]="scoreC(s.score)">{{ s.score }}</b>
          </div>
          <div class="pgr" *ngIf="drivingScores.length>5"><button (click)="scP=scP-1" [disabled]="scP===0">‹</button><span>{{ scP+1 }}/{{ Math.ceil(drivingScores.length/5) }}</span><button (click)="scP=scP+1" [disabled]="(scP+1)*5>=drivingScores.length">›</button></div>
        </div>
        <div class="mt" *ngIf="!drivingScores.length">Aucun score</div>
      </section>

      <section class="B B-alerts an" style="--i:9">
        <div class="B-h"><h3>Alertes récentes</h3><span class="alert-ct" *ngIf="alerts.length">{{ alerts.length }}</span></div>
        <div class="tl" *ngIf="alerts.length">
          <div *ngFor="let a of pagedAlerts" class="tl-i" [class.tl-new]="a._isNew">
            <div class="tl-dot" [class.tw]="a.severity==='warning'" [class.td]="a.severity==='danger'" [class.ti]="a.severity==='info'"></div>
            <div class="tl-body"><span class="tl-msg">{{ a.message }}</span><span class="tl-time">{{ a.time }}</span></div>
          </div>
          <div class="pgr" *ngIf="alerts.length>6"><button (click)="alertsP=alertsP-1" [disabled]="alertsP===0">‹</button><span>{{ alertsP+1 }}/{{ Math.ceil(alerts.length/6) }}</span><button (click)="alertsP=alertsP+1" [disabled]="(alertsP+1)*6>=alerts.length">›</button></div>
        </div>
        <div class="mt" *ngIf="!alerts.length">Aucune alerte</div>
      </section>

      <!-- FUEL/VEH -->
      <section class="B an" style="--i:10">
        <div class="B-h"><h3>Conso/Véhicule</h3><span class="B-tag muted">L/100km</span></div>
        <div class="rws" *ngIf="vehicleFuelStats.length">
          <div *ngFor="let v of pFuel" class="fv">
            <div class="fv-t"><span class="rw-plate">{{ v.plate }}</span><b [style.color]="fuelC(v.consumption)">{{ v.consumption|number:'1.1-1' }}</b></div>
            <div class="bar sm"><div class="bar-f" [style.width.%]="(v.consumption/maxFuelConsumption)*100" [style.background]="fuelC(v.consumption)"></div></div>
            <span class="rw-sub">{{ v.totalLiters|number:'1.0-0' }}L · {{ v.totalKm|number:'1.0-0' }}km</span>
          </div>
          <div class="pgr" *ngIf="vehicleFuelStats.length>5"><button (click)="fuP=fuP-1" [disabled]="fuP===0">‹</button><span>{{ fuP+1 }}/{{ Math.ceil(vehicleFuelStats.length/5) }}</span><button (click)="fuP=fuP+1" [disabled]="(fuP+1)*5>=vehicleFuelStats.length">›</button></div>
        </div>
        <div class="mt" *ngIf="!vehicleFuelStats.length">Aucune donnée</div>
      </section>

      <!-- TRIPS -->
      <section class="B B-trips an" style="--i:11">
        <div class="B-h"><h3>Derniers trajets</h3></div>
        <div class="rws" *ngIf="recentTrips.length">
          <div *ngFor="let t of pTrips" class="rw">
            <svg viewBox="0 0 20 28" width="14" height="20" fill="none" class="trip-svg"><circle cx="10" cy="5" r="3.5" stroke="#10b981" stroke-width="1.5"/><line x1="10" y1="9" x2="10" y2="19" stroke="#10b981" stroke-width="1.5" stroke-dasharray="2 2"/><circle cx="10" cy="23" r="3.5" stroke="#ef4444" stroke-width="1.5"/></svg>
            <div class="rw-col"><span class="rw-plate">{{ t.plate }}</span><span class="rw-sub">{{ t.distance }} km · {{ t.duration }}</span></div>
            <span class="rw-sub" style="margin-left:auto">{{ t.date }}</span>
          </div>
          <div class="pgr" *ngIf="recentTrips.length>5"><button (click)="trP=trP-1" [disabled]="trP===0">‹</button><span>{{ trP+1 }}/{{ Math.ceil(recentTrips.length/5) }}</span><button (click)="trP=trP+1" [disabled]="(trP+1)*5>=recentTrips.length">›</button></div>
        </div>
        <div class="mt" *ngIf="!recentTrips.length">Aucun trajet</div>
      </section>

      <!-- DRIVERS -->
      <section class="B an" style="--i:12">
        <div class="B-h"><h3>Conducteurs</h3><span class="B-tag muted">{{ drivers.length }}</span></div>
        <div class="rws" *ngIf="drivers.length">
          <div *ngFor="let d of pDrv" class="rw">
            <div class="av" [class.av-on]="d.active">{{ d.initials }}</div>
            <div class="rw-col"><span class="rw-plate">{{ d.name }}</span><span class="rw-sub">{{ d.vehicle||'Non assigné' }}</span></div>
            <span class="st" [class.st-on]="d.active">{{ d.active?'Actif':'—' }}</span>
          </div>
          <div class="pgr" *ngIf="drivers.length>5"><button (click)="drP=drP-1" [disabled]="drP===0">‹</button><span>{{ drP+1 }}/{{ Math.ceil(drivers.length/5) }}</span><button (click)="drP=drP+1" [disabled]="(drP+1)*5>=drivers.length">›</button></div>
        </div>
        <div class="mt" *ngIf="!drivers.length">Aucun conducteur</div>
      </section>

    </div>
  </div>
</div>
</app-layout>
  `,
  styles: [`
    .D { flex:1; overflow-y:auto; background:var(--bg-page); font-family:'Outfit',sans-serif; min-height:calc(100vh - 42px); }
    .D-inner { max-width:1440px; margin:0 auto; padding:0 24px 48px; }

    /* ANIMATIONS */
    .an { animation:slideUp .5s cubic-bezier(.22,1,.36,1) both; animation-delay:calc(var(--i,0)*50ms + 60ms); }
    @keyframes slideUp { from{opacity:0;transform:translateY(16px)} to{opacity:1;transform:translateY(0)} }

    /* HEADER */
    .hd { display:flex; justify-content:space-between; align-items:flex-end; padding:18px 0 10px; flex-wrap:wrap; gap:12px; }
    .hd-t { font-size:26px; font-weight:900; color:var(--text-primary); margin:0; letter-spacing:-.6px; }
    .hd-s { font-size:12px; color:var(--text-muted); margin:2px 0 0; }
    .hd-r { display:flex; align-items:center; gap:12px; flex-wrap:wrap; }
    .conn { font-size:10px; font-weight:700; color:#ef4444; background:rgba(239,68,68,.08); padding:4px 12px; border-radius:16px; display:flex; align-items:center; gap:6px; text-transform:uppercase; letter-spacing:.4px; }
    .conn.on { color:#10b981; background:rgba(16,185,129,.08); }
    .conn b { width:7px; height:7px; border-radius:50%; background:currentColor; display:inline-block; }

    /* COLORED KPI TILES */
    .kpi-band { display:grid; grid-template-columns:repeat(6,1fr); gap:12px; margin:8px 0 16px; }
    .kt {
      background:linear-gradient(135deg,var(--g1),var(--g2));
      border-radius:16px; padding:16px 14px; color:#fff;
      display:flex; flex-direction:column; gap:4px;
      animation:ktPop .5s cubic-bezier(.34,1.56,.64,1) both;
      animation-delay:calc(var(--i,1)*50ms + 120ms);
      transition:transform .2s cubic-bezier(.34,1.56,.64,1),box-shadow .2s;
      box-shadow:0 4px 20px -6px var(--g2);
      cursor:default;
    }
    .kt:hover { transform:translateY(-4px) scale(1.02); box-shadow:0 12px 28px -6px var(--g2); }
    @keyframes ktPop { from{opacity:0;transform:translateY(18px) scale(.9)} to{opacity:1;transform:translateY(0) scale(1)} }
    .kt-top { margin-bottom:4px; opacity:.85; }
    .kt-n { font-size:28px; font-weight:900; line-height:1; letter-spacing:-.8px; font-variant-numeric:tabular-nums; }
    .kt-n small { font-size:13px; font-weight:500; opacity:.7; margin-left:2px; }
    .kt-l { font-size:11px; font-weight:500; opacity:.75; }

    /* BENTO GRID */
    .bento { display:grid; grid-template-columns:repeat(3,1fr); gap:14px; }
    .B {
      background:var(--bg-card); border-radius:18px; padding:20px;
      border:1px solid var(--border-color);
      transition:transform .18s ease,box-shadow .18s;
    }
    .B:hover { transform:translateY(-2px); box-shadow:0 12px 32px -10px rgba(0,0,0,.08); }
    .B-fleet { grid-column:span 1; grid-row:span 1; }
    .B-chart { grid-column:span 2; }
    .B-exp { grid-column:span 1; }
    .B-health { grid-column:span 2; }
    .B-alerts { grid-column:span 1; }
    .B-geo { grid-column:span 1; }
    .B-scores { grid-column:span 2; }
    .B-km { grid-column:span 2; }
    .B-trips { grid-column:span 2; }

    .B-h { display:flex; align-items:center; gap:8px; margin-bottom:14px; }
    .B-h h3 { font-size:14px; font-weight:700; color:var(--text-primary); margin:0; flex:1; }
    .B-tag { font-size:14px; font-weight:800; }
    .B-tag.green { color:#10b981; }
    .B-tag.amber { color:#f59e0b; }
    .B-tag.muted { font-size:11px; color:var(--text-muted); font-weight:500; }
    .alert-ct { background:#fef2f2; color:#ef4444; font-size:11px; font-weight:700; padding:2px 10px; border-radius:14px; }

    /* DONUT */
    .fleet-row { display:flex; gap:16px; align-items:center; }
    .donut-w { position:relative; width:130px; height:130px; flex-shrink:0; }
    .donut-w svg { width:100%; height:100%; transform:rotate(-90deg); }
    .seg-anim { transition:stroke-dasharray .5s ease,stroke-dashoffset .5s ease,stroke-width .15s; }
    .donut-c { position:absolute; inset:0; display:flex; flex-direction:column; align-items:center; justify-content:center; }
    .dc-n { font-size:28px; font-weight:900; color:var(--text-primary); line-height:1; }
    .dc-l { font-size:9px; color:var(--text-muted); text-transform:uppercase; letter-spacing:.5px; margin-top:2px; }
    .fleet-legend { display:flex; flex-direction:column; gap:4px; flex:1; }
    .fl { display:flex; align-items:center; gap:8px; padding:4px 6px; border-radius:6px; font-size:12px; color:var(--text-secondary); transition:all .12s; cursor:default; }
    .fl:hover,.fl-hl { background:var(--bg-secondary); transform:translateX(3px); }
    .fl i { width:4px; height:20px; border-radius:2px; flex-shrink:0; }
    .fl span { flex:1; font-weight:500; }
    .fl b { font-weight:800; color:var(--text-primary); font-size:14px; }
    .mini-strip { display:flex; height:6px; border-radius:4px; overflow:hidden; gap:2px; margin-top:12px; background:var(--bg-tertiary); }
    .ms { min-width:0; transition:flex .6s ease; border-radius:3px; }
    .ms-hide { flex:0!important; }

    /* CHART */
    .chart-w { background:var(--bg-secondary); border-radius:12px; padding:12px 14px 6px; position:relative; cursor:crosshair; }
    .csv { width:100%; height:110px; display:block; overflow:visible; }
    .c-line { stroke-dasharray:2000; stroke-dashoffset:2000; animation:draw 1.3s ease forwards .3s; }
    .c-area { opacity:0; animation:fadein .6s ease forwards .8s; }
    @keyframes draw { to{stroke-dashoffset:0} }
    @keyframes fadein { to{opacity:1} }
    .c-dot { transition:r .12s,stroke-width .12s; }
    .c-labels { display:flex; justify-content:space-between; font-size:10px; color:var(--text-muted); margin-top:6px; }
    .c-tt { position:absolute; top:4px; transform:translateX(-50%); background:var(--bg-card); border:1px solid var(--border-color); border-radius:8px; padding:6px 10px; box-shadow:0 6px 16px -4px rgba(0,0,0,.12); pointer-events:none; z-index:5; display:flex; flex-direction:column; align-items:center; animation:ttpop .1s ease; }
    .c-tt b { font-size:13px; font-weight:800; color:#10b981; }
    .c-tt span { font-size:10px; color:var(--text-muted); }
    @keyframes ttpop { from{opacity:0;transform:translateX(-50%) translateY(4px)} }

    /* EXPENSES */
    .exp-bars { display:flex; flex-direction:column; gap:12px; }
    .eb { display:flex; flex-direction:column; gap:4px; }
    .eb-info { display:flex; align-items:center; gap:8px; font-size:12px; }
    .eb-info i { width:8px; height:8px; border-radius:3px; flex-shrink:0; }
    .eb-info span { flex:1; color:var(--text-secondary); font-weight:500; }
    .eb-info b { color:var(--text-primary); font-weight:700; }
    .eb-track { height:8px; background:var(--bg-tertiary); border-radius:5px; overflow:hidden; }
    .eb-fill { height:100%; border-radius:5px; transition:width .7s cubic-bezier(.4,0,.2,1); }

    /* HEALTH — redesigned */
    .h-layout { display:flex; gap:24px; align-items:center; }
    .h-gauge-w { position:relative; width:140px; height:140px; flex-shrink:0; }
    .h-gauge-w svg { width:100%; height:100%; transform:rotate(-90deg); }
    .h-arc { transition:stroke-dashoffset 1s ease; filter:drop-shadow(0 0 4px rgba(34,197,94,.3)); }
    .h-mid { position:absolute; inset:0; display:flex; flex-direction:column; align-items:center; justify-content:center; }
    .h-pct { font-size:28px; font-weight:900; line-height:1; }
    .h-lbl { font-size:10px; color:var(--text-muted); margin-top:2px; }
    .h-items-col { display:flex; flex-direction:column; gap:12px; flex:1; }
    .hi-card { display:flex; flex-direction:column; gap:4px; padding:10px 14px; border-radius:10px; background:var(--bg-secondary); border-left:3px solid var(--hc); }
    .hi-card b { font-size:18px; font-weight:800; color:var(--text-primary); line-height:1; }
    .hi-card span { font-size:11px; color:var(--text-muted); font-weight:500; }
    .hi-bar { height:4px; background:var(--bg-tertiary); border-radius:3px; overflow:hidden; margin-top:2px; }
    .hi-fill { height:100%; border-radius:3px; transition:width .7s ease; }

    /* ALERTS TIMELINE */
    .tl { display:flex; flex-direction:column; gap:0; }
    .tl-i { display:flex; gap:10px; padding:6px 0; }
    .tl-i.tl-new { animation:flash .5s ease; }
    @keyframes flash { from{background:rgba(99,102,241,.06)} }
    .tl-dot { width:10px; height:10px; border-radius:50%; background:var(--bg-tertiary); border:2px solid var(--text-muted); flex-shrink:0; margin-top:3px; }
    .tw { border-color:#f59e0b; background:rgba(245,158,11,.1); }
    .td { border-color:#ef4444; background:rgba(239,68,68,.1); }
    .ti { border-color:#3b82f6; background:rgba(59,130,246,.1); }
    .tl-body { min-width:0; }
    .tl-msg { font-size:12px; font-weight:500; color:var(--text-primary); line-height:1.35; display:block; }
    .tl-time { font-size:10px; color:var(--text-muted); display:block; margin-top:1px; }

    /* GEOZONES */
    .geo-l { display:flex; flex-direction:column; gap:2px; }
    .gi { display:flex; align-items:center; gap:10px; padding:6px; border-radius:6px; transition:background .12s; }
    .gi:hover { background:var(--bg-secondary); }
    .gd { width:10px; height:10px; border-radius:50%; flex-shrink:0; }
    .gn { flex:1; font-size:13px; font-weight:500; color:var(--text-secondary); }
    .gc { font-size:14px; font-weight:800; color:var(--text-primary); }

    /* ROWS (scores, mileage, etc) */
    .rws { display:flex; flex-direction:column; gap:2px; }
    .rw { display:flex; align-items:center; gap:10px; padding:6px 6px; border-radius:6px; font-size:12px; transition:background .1s; }
    .rw:hover { background:var(--bg-secondary); }
    .rk { width:22px; height:22px; border-radius:6px; display:flex; align-items:center; justify-content:center; font-size:10px; font-weight:700; color:var(--text-muted); background:var(--bg-tertiary); }
    .r1 { background:linear-gradient(135deg,#fbbf24,#f59e0b)!important; color:#fff!important; }
    .r2 { background:linear-gradient(135deg,#d1d5db,#9ca3af)!important; color:#fff!important; }
    .r3 { background:linear-gradient(135deg,#d97706,#92400e)!important; color:#fff!important; }
    .rw-plate { font-size:12px; font-weight:600; color:var(--text-primary); }
    .rw-dot { width:4px; height:18px; border-radius:2px; flex-shrink:0; }
    .rw-v { font-size:14px; font-weight:800; min-width:28px; text-align:right; }
    .rw-km { font-size:12px; font-weight:700; color:var(--text-primary); min-width:76px; text-align:right; }
    .rw-sub { font-size:11px; color:var(--text-muted); }
    .rw-col { display:flex; flex-direction:column; flex:1; min-width:0; }
    .trip-svg { flex-shrink:0; }
    .bar { flex:1; height:8px; background:var(--bg-tertiary); border-radius:5px; overflow:hidden; }
    .bar.tall { height:14px; }
    .bar.sm { height:6px; }
    .bar-f { height:100%; border-radius:5px; transition:width .6s cubic-bezier(.4,0,.2,1); }
    .fv { display:flex; flex-direction:column; gap:3px; padding:5px 0; }
    .fv-t { display:flex; justify-content:space-between; font-size:12px; }
    .fv-t b { font-size:13px; font-weight:800; }

    /* DRIVERS */
    .av { width:32px; height:32px; border-radius:10px; background:color-mix(in srgb,var(--primary) 10%,transparent); color:var(--primary); display:flex; align-items:center; justify-content:center; font-size:11px; font-weight:700; flex-shrink:0; border:2px solid transparent; }
    .av-on { border-color:#22c55e; }
    .st { font-size:10px; font-weight:600; padding:3px 10px; border-radius:14px; background:var(--bg-tertiary); color:var(--text-muted); }
    .st-on { background:rgba(16,185,129,.1); color:#10b981; }

    /* PAGINATION */
    .pgr { display:flex; align-items:center; justify-content:center; gap:10px; margin-top:10px; padding-top:10px; border-top:1px solid var(--border-color); }
    .pgr button { width:26px; height:26px; border:1px solid var(--border-color); border-radius:8px; background:var(--bg-card); color:var(--primary); font-size:14px; font-weight:700; cursor:pointer; display:flex; align-items:center; justify-content:center; transition:all .12s; }
    .pgr button:hover:not(:disabled) { background:var(--primary); color:#fff; border-color:var(--primary); }
    .pgr button:disabled { opacity:.2; cursor:default; }
    .pgr span { font-size:11px; color:var(--text-muted); }

    .mt { padding:24px; text-align:center; color:var(--text-muted); font-size:12px; opacity:.5; }

    /* RESPONSIVE */
    @media(max-width:1200px) { .kpi-band{grid-template-columns:repeat(3,1fr)} }
    @media(max-width:900px) { .bento{grid-template-columns:repeat(2,1fr)} .B-chart,.B-scores,.B-km,.B-trips{grid-column:span 2} .B-fleet{grid-column:span 1} .kpi-band{grid-template-columns:repeat(3,1fr)} }
    @media(max-width:600px) { .bento{grid-template-columns:1fr} .B-chart,.B-scores,.B-km,.B-trips,.B-fleet{grid-column:span 1} .kpi-band{grid-template-columns:repeat(2,1fr)} .D-inner{padding:0 14px 32px} .hd{flex-direction:column;align-items:flex-start} .fleet-row{flex-direction:column} }
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
    const svg=(e.currentTarget as HTMLElement).querySelector('.csv');
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
