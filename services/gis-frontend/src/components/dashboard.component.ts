import { Component, OnInit, OnDestroy, AfterViewInit, ViewChild, ElementRef, ChangeDetectorRef } from '@angular/core';
import * as L from 'leaflet';
import { PermissionService, ModuleKey } from '../services/permission.service';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subject, takeUntil } from 'rxjs';
import { ApiService, VehicleExpiryDto, GpaDashboard, GpaDashboardAlert, GpaDashboardIntervention, GpaDashboardTopVehicle } from '../services/api.service';
import { SignalRService } from '../services/signalr.service';
import { Company } from '../models/types';
import { UserPreferencesService } from '../services/user-preferences.service';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, AppLayoutComponent, ...USER_PREF_PIPES],
  template: `
<app-layout>
<div class="dash">
  <div class="shell">

    <!-- ════ 1 · MASTHEAD : en-tête + filtre + KPIs (bande carbone) ════ -->
    <header class="mast anim" style="--i:0">
      <div class="mast-top">
        <div class="brand">
          <span class="live" *ngIf="hasGps" [class.off]="!isConnected"><span class="live-dot"></span>{{ isConnected ? 'En direct' : 'Hors ligne' }}</span>
          <span>Calypso&nbsp;·&nbsp;<b>{{ company?.name || 'Flotte' }}</b></span>
        </div>
        <div class="mast-date">{{ todayLabel }}</div>
      </div>

      <div class="mast-title">
        <h1>Tableau de bord</h1>
        <div class="meta">
          <span><b>{{ totalMotion }}</b>&nbsp;véhicules</span>
          <span *ngIf="drivers.length"><b>{{ activeDrivers }}</b>&nbsp;conducteurs actifs</span>
          <span *ngIf="hasGps && geofences.length"><b>{{ geofences.length }}</b>&nbsp;géozones</span>
        </div>
      </div>

      <!-- Filtre de période : pilules + plage personnalisée Du/Au (enfin fonctionnelle) -->
      <div class="toolbar" role="group" aria-label="Filtre de période">
        <nav class="pills">
          <button *ngFor="let p of periodsFr" class="pill" type="button"
            [class.active]="selectedPeriod===p.value" [attr.aria-pressed]="selectedPeriod===p.value"
            (click)="onPeriodClick(p.value)">{{ p.label }}</button>
          <button class="pill custom" type="button" [class.active]="selectedPeriod==='custom'"
            [attr.aria-pressed]="selectedPeriod==='custom'" (click)="applyCustom()">Personnalisé</button>
        </nav>
        <span class="tb-sep" aria-hidden="true"></span>
        <div class="range">
          <div class="rfield">
            <label for="d-from">Du</label>
            <input id="d-from" type="date" [(ngModel)]="fromDate">
          </div>
          <div class="rfield">
            <label for="d-to">Au</label>
            <input id="d-to" type="date" [(ngModel)]="toDate">
          </div>
          <button class="apply" type="button" (click)="applyCustom()">Appliquer</button>
        </div>
      </div>

      <!-- ════ 2 · KPIs en filets (dans le masthead) ════ -->
      <div class="kpis" *ngIf="hasGps">
        <div class="kpi">
          <span class="kpi-l">Véhicules</span>
          <span class="kpi-v">{{ dVehicles }}</span>
          <span class="kpi-s">flotte totale</span>
        </div>
        <div class="kpi">
          <span class="kpi-l"><span class="dot" style="background:#34d399"></span>En circulation</span>
          <span class="kpi-v">{{ dMoving }}</span>
          <span class="kpi-s">{{ movingPct }}&nbsp;% de la flotte</span>
        </div>
        <div class="kpi">
          <span class="kpi-l"><span class="dot" style="background:#94a3b8"></span>À l'arrêt</span>
          <span class="kpi-v">{{ dStopped }}</span>
          <span class="kpi-s">{{ stoppedPct }}&nbsp;% de la flotte</span>
        </div>
        <div class="kpi">
          <span class="kpi-l">Carburant</span>
          <span class="kpi-v">{{ dFuel | number:'1.0-0' }}<span class="kpi-u">L</span></span>
          <span class="kpi-s">sur la période</span>
        </div>
        <div class="kpi">
          <span class="kpi-l">Coût total</span>
          <span class="kpi-v">{{ costValue }}<span class="kpi-u">{{ currencyCode }}</span></span>
          <span class="kpi-s" *ngIf="costTrend!==null && costTrend!==0">
            <span class="kchip" [class.kchip-danger]="costTrend>0" [class.kchip-ok]="costTrend<0">
              <svg width="8" height="8" viewBox="0 0 10 10" fill="currentColor" aria-hidden="true" [style.transform]="costTrend<0?'rotate(180deg)':''"><path d="M5 1.6 8.6 7H1.4Z"/></svg>
              {{ absPct(costTrend) }}&nbsp;%</span>
            vs préc.
          </span>
          <span class="kpi-s" *ngIf="costTrend===null || costTrend===0">sur la période</span>
        </div>
        <div class="kpi">
          <span class="kpi-l">Alertes</span>
          <span class="kpi-v">{{ dAlerts }}</span>
          <span class="kpi-s">sur la période</span>
        </div>
      </div>

      <!-- Offre GPA (sans GPS) : cinq indicateurs de gestion, une icône chacun -->
      <div class="kpis k5" *ngIf="!hasGps">
        <div class="kpi kpi-ic" title="Coût total sur la période : carburant, entretiens, réparations et autres dépenses, remboursements d'assurance déduits. Les achats de véhicules sont à part.">
          <span class="kic" style="--kc:#a5b4fc"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M17.5 6.2A7 7 0 1 0 17.5 17.8"/><path d="M4 10.2h9M4 13.8h9"/></svg></span>
          <div class="ktx">
            <span class="kpi-l">Coût total</span>
            <span class="kpi-v"><ng-container *ngIf="gpa; else kDash"><span class="kpi-num">{{ entier(gpa.costs.total) }}<span class="kpi-dec">{{ decimales(gpa.costs.total) }}</span></span><span class="kpi-u">{{ currencySym }}</span></ng-container></span>
            <span class="kpi-s">sur la période · hors achats</span>
          </div>
        </div>
        <div class="kpi kpi-ic" title="Coût complet du parc, quelle que soit la période : prix d'achat des véhicules achetés comptant, apports et toutes les mensualités des contrats de crédit ou de leasing (échues et à venir).">
          <span class="kic" style="--kc:#7dd3fc"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M4 7.5h16l-1.4 11a2 2 0 0 1-2 1.7H7.4a2 2 0 0 1-2-1.7z"/><path d="M8.5 10V6.5a3.5 3.5 0 0 1 7 0V10"/></svg></span>
          <div class="ktx">
            <span class="kpi-l">Coût d'achats</span>
            <span class="kpi-v"><ng-container *ngIf="gpa; else kDash"><span class="kpi-num">{{ entier(gpa.acquisition.total) }}<span class="kpi-dec">{{ decimales(gpa.acquisition.total) }}</span></span><span class="kpi-u">{{ currencySym }}</span></ng-container></span>
            <span class="kpi-s" *ngIf="gpa">{{ achatsSousTitre }}</span>
          </div>
        </div>
        <div class="kpi kpi-ic" title="Somme des mensualités de leasing et de crédit encore à venir, tous contrats confondus.">
          <span class="kic" style="--kc:#c4b5fd"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="3.5" y="5" width="17" height="15" rx="2.5"/><path d="M3.5 10h17M8 3v4M16 3v4M8 14.5h3"/></svg></span>
          <div class="ktx">
            <span class="kpi-l">Reste à payer leasing</span>
            <span class="kpi-v"><ng-container *ngIf="gpa; else kDash"><span class="kpi-num">{{ entier(gpa.leasingRemaining.amount) }}<span class="kpi-dec">{{ decimales(gpa.leasingRemaining.amount) }}</span></span><span class="kpi-u">{{ currencySym }}</span></ng-container></span>
            <span class="kpi-s" *ngIf="gpa && gpa.leasingRemaining.installments>0">{{ gpa.leasingRemaining.installments }}&nbsp;échéance{{ gpa.leasingRemaining.installments>1?'s':'' }} · {{ gpa.leasingRemaining.contracts }}&nbsp;contrat{{ gpa.leasingRemaining.contracts>1?'s':'' }}</span>
            <span class="kpi-s" *ngIf="gpa && gpa.leasingRemaining.installments===0">aucune échéance à venir</span>
          </div>
        </div>
        <div class="kpi kpi-ic" title="Nombre d'interventions sur la période : entretiens et réparations enregistrés (réparations annulées exclues).">
          <span class="kic" style="--kc:#fcd34d"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a4 4 0 0 0-5.4 5.2L3.8 17a1.8 1.8 0 0 0 2.6 2.6l5.5-5.5a4 4 0 0 0 5.2-5.4l-2.5 2.5-2.1-.5-.5-2.1z"/></svg></span>
          <div class="ktx">
            <span class="kpi-l">Interventions</span>
            <span class="kpi-v"><ng-container *ngIf="gpa; else kDash">{{ nombre(gpa.interventions.total) }}</ng-container></span>
            <span class="kpi-s" *ngIf="gpa">{{ gpa.interventions.maintenance }}&nbsp;entretien{{ gpa.interventions.maintenance>1?'s':'' }} · {{ gpa.interventions.repairs }}&nbsp;réparation{{ gpa.interventions.repairs>1?'s':'' }}</span>
          </div>
        </div>
        <div class="kpi kpi-ic" title="Entretiens planifiés qui arrivent à échéance dans les 30 prochains jours ; les entretiens déjà dépassés sont comptés à part.">
          <span class="kic" [style.--kc]="gpa && gpa.upcomingMaintenance.overdue>0 ? '#fca5a5' : '#6ee7b7'"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="13" r="7.5"/><path d="M12 9.5V13l2.5 1.8M9.5 2.8h5"/></svg></span>
          <div class="ktx">
            <span class="kpi-l">Entretiens à venir</span>
            <span class="kpi-v"><ng-container *ngIf="gpa; else kDash">{{ gpa.upcomingMaintenance.next30Days }}</ng-container></span>
            <span class="kpi-s" *ngIf="gpa">sous 30&nbsp;jours
              <span class="kchip kchip-danger" *ngIf="gpa.upcomingMaintenance.overdue>0">{{ gpa.upcomingMaintenance.overdue }}&nbsp;en retard</span>
            </span>
          </div>
        </div>
        <ng-template #kDash><span class="kpi-na">—</span></ng-template>
      </div>
    </header>

    <!-- ════ BENTO ════ -->
    <div class="bento">

      <ng-container *ngIf="hasGps">
      <!-- ── Flotte en direct (Leaflet) ── -->
      <section class="card span-8 acc-indigo anim" style="--i:1">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M9 4L3 6.5v13L9 17l6 2.5 6-2.5v-13L15 6.5 9 4z"/><path d="M9 4v13M15 6.5v13"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Temps réel</div><h2>Flotte en direct</h2></div>
          <div class="head-right"><span class="chip"><i class="chip-dot" [style.background]="isConnected?'#10b981':'#ef4444'"></i>{{ totalMotion }} véhicules</span></div>
        </div>
        <div class="map-wrap">
          <div #fleetMap class="fleet-map"></div>
          <div class="map-legend">
            <span><i style="background:#10b981"></i>En circulation</span>
            <span><i style="background:#f59e0b"></i>Moteur allumé</span>
            <span><i style="background:#64748b"></i>À l'arrêt</span>
            <span><i style="background:#cbd5e1"></i>Hors ligne</span>
          </div>
        </div>
      </section>

      <!-- ── État de la flotte (donut) ── -->
      <section class="card acc-green anim" style="--i:2">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M21 12a9 9 0 1 1-9-9"/><path d="M12 3a9 9 0 0 1 9 9h-9z"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Flotte</div><h2>État de la flotte</h2></div>
        </div>
        <div class="donut-flex">
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
            <div *ngFor="let s of fleetSegs" class="lg" (mouseenter)="hSeg=donutIdx(s.name)" (mouseleave)="hSeg=-1" [class.hl]="hSeg>=0 && donutSegs[hSeg]?.name===s.name">
              <i [style.background]="s.color"></i>
              <span class="lbl">{{ s.name }}</span>
              <b>{{ s.value }}</b>
            </div>
          </div>
        </div>
      </section>

      <!-- ── Consommation carburant (graphique) ── -->
      <section class="card span-8 acc-cyan anim" style="--i:3">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3.5s-6 6.2-6 11a6 6 0 0 0 12 0c0-4.8-6-11-6-11z"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Carburant</div><h2>Consommation carburant</h2></div>
          <div class="head-right">
            <span class="chip"><b class="num">{{ totalFuelConsumed | number:'1.0-0' }}&nbsp;L</b></span>
            <span class="chip ghost" *ngIf="fuelEstimated" title="Estimation issue des capteurs GPS — chiffre indicatif">estimé</span>
          </div>
        </div>
        <div class="chart-wrap" *ngIf="cPath" (mousemove)="onChartHover($event)" (mouseleave)="cIdx=-1">
          <svg class="chart-svg" viewBox="0 0 660 230" preserveAspectRatio="none">
            <defs>
              <linearGradient id="fuelFill" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" class="chart-stop" stop-opacity=".22"/>
                <stop offset="1" class="chart-stop" stop-opacity="0"/>
              </linearGradient>
            </defs>
            <line *ngFor="let t of yTicks" class="gridline" x1="40" [attr.y1]="t.y" x2="620" [attr.y2]="t.y"/>
            <line class="gridline" x1="40" y1="190" x2="620" y2="190"/>
            <text *ngFor="let t of yTicks" class="ax" text-anchor="end" x="34" [attr.y]="t.y+3">{{ t.label }}</text>
            <path [attr.d]="cAreaPath" fill="url(#fuelFill)" class="chart-area"/>
            <path [attr.d]="cPath" fill="none" stroke-width="2" stroke-linecap="round" class="chart-line"/>
            <ng-container *ngIf="cIdx>=0 && cPoints[cIdx]">
              <line [attr.x1]="cPoints[cIdx].x" y1="52" [attr.x2]="cPoints[cIdx].x" y2="190" class="chart-cursor"/>
              <circle [attr.cx]="cPoints[cIdx].x" [attr.cy]="cPoints[cIdx].y" r="8" class="chart-pt halo"/>
              <circle [attr.cx]="cPoints[cIdx].x" [attr.cy]="cPoints[cIdx].y" r="4" class="chart-pt ring"/>
            </ng-container>
            <text *ngFor="let t of xTicks" class="ax" text-anchor="middle" [attr.x]="t.x" y="212">{{ t.label }}</text>
          </svg>
          <div class="chart-tip" *ngIf="cIdx>=0 && cVals[cIdx]!==undefined" [style.left.%]="cPoints[cIdx] ? (cPoints[cIdx].x/6.6) : 0">
            <span>{{ cLabels[cIdx] || '' }}</span><b>{{ cVals[cIdx] | number:'1.0-0' }} L</b>
          </div>
        </div>
        <div class="empty" *ngIf="!cPath"><span>Aucune donnée sur la période</span></div>
      </section>

      <!-- ── Santé des véhicules ── -->
      <section class="card acc-green anim" style="--i:4">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M3.5 12h4l2-5 3.5 10 2-5h5.5"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Maintenance</div><h2>Santé des véhicules</h2></div>
        </div>
        <div class="gauge-flex">
          <div class="gauge">
            <svg viewBox="0 0 140 140">
              <circle cx="70" cy="70" r="54" fill="none" class="donut-track" stroke-width="10"/>
              <circle cx="70" cy="70" r="54" fill="none" [attr.stroke]="hColor" stroke-width="10" stroke-linecap="round"
                stroke-dasharray="339.29" [attr.stroke-dashoffset]="339.29-(hPct/100)*339.29" transform="rotate(-90 70 70)" class="gauge-ring"/>
            </svg>
            <div class="donut-c">
              <span class="gauge-n">{{ hPct }}%</span>
              <span class="donut-l">en bon état</span>
            </div>
          </div>
          <div class="hbars">
            <div *ngFor="let h of hItems" class="hbar">
              <div class="hb-top"><span>{{ h.name }}</span><b>{{ h.value }}</b></div>
              <div class="bar"><i [style.width.%]="maxHealth ? (h.value/maxHealth)*100 : 0" [style.background]="h.color"></i></div>
            </div>
          </div>
        </div>
      </section>
      </ng-container>

      <!-- ── Dépenses ── -->
      <section class="card acc-indigo spend anim" *ngIf="hasGps" style="--i:5">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2v20M17 6.5H9.8a2.8 2.8 0 0 0 0 5.6h4.4a2.8 2.8 0 0 1 0 5.6H6.5"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Finances</div><h2>Dépenses</h2></div>
          <div class="head-right"><span class="chip"><b class="num">{{ totalCost | appCurrency:0 }}</b></span></div>
        </div>
        <div class="rows">
          <div *ngFor="let e of expItems" class="row">
            <div class="lblc"><i [style.background]="e.color"></i><span>{{ e.name }}</span></div>
            <div class="bar"><i [style.width.%]="maxExp ? (e.value/maxExp)*100 : 0" [style.background]="e.color"></i></div>
            <div class="val num">{{ e.value | appCurrency:0 }}</div>
          </div>
        </div>
      </section>

      <!-- ── Échéances à venir ── -->
      <section class="card acc-amber anim" *ngIf="hasGps" style="--i:6">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><rect x="3.5" y="5" width="17" height="16" rx="2.5"/><path d="M8 3v4M16 3v4M3.5 10.5h17"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Conformité</div><h2>Échéances à venir</h2></div>
          <div class="head-right"><span class="chip down" *ngIf="deadlinesOverdue>0">{{ deadlinesOverdue }} en retard</span></div>
        </div>
        <div class="rows" *ngIf="deadlines.length">
          <div *ngFor="let d of deadlines" class="row">
            <span class="plate">{{ d.vehiclePlate || d.vehicleName }}</span>
            <div class="grow"><div class="t1">{{ docLabel(d.documentType) }}</div><div class="t2 num">{{ d.expiryDate | date:'dd/MM/yyyy' }}</div></div>
            <span class="due" [ngClass]="dueCls(d)">{{ daysLabel(d) }}</span>
          </div>
        </div>
        <div class="empty" *ngIf="!deadlines.length"><span>Aucune échéance sous 60 jours</span></div>
      </section>

      <!-- ════ Offre GPA (sans boîtier GPS) : tableau de bord de gestion.
           Aucune donnée GPS : coûts saisis, entretiens, réparations, pleins et
           échéances. Source : /dashboard/gpa, et /fuel-expenses/real-consumption
           pour la consommation par véhicule (même calcul que l'écran Carburant). ════ -->
      <ng-container *ngIf="!hasGps">

      <!-- ── 3a · Répartition des coûts d'exploitation ── -->
      <section class="card acc-indigo spend gpa-split anim" style="--i:1">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M4 6h10M4 12h16M4 18h7"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Finances</div><h2 title="Répartition des coûts d'exploitation">Répartition des coûts d'exploitation</h2></div>
        </div>
        <div class="rows" *ngIf="gpa">
          <div *ngFor="let c of gpaSplit" class="row">
            <div class="lblc"><i [style.background]="c.color"></i><span>{{ c.name }}</span></div>
            <div class="bar"><i [style.width.%]="c.pct" [style.background]="c.color"></i></div>
            <div class="val num">{{ c.value | appCurrency:2 }}</div>
            <div class="pct num">{{ gpa.costs.total > 0 ? pctLabel(c.pct) : '—' }}</div>
          </div>
          <div class="row tot">
            <div class="lblc"><span>Total</span></div>
            <div class="grow"></div>
            <div class="val num">{{ gpa.costs.total | appCurrency:2 }}</div>
            <div class="pct num">{{ gpa.costs.total > 0 ? '100 %' : '—' }}</div>
          </div>
        </div>
        <div class="empty" *ngIf="!gpa"><span>{{ gpaErreur ? 'Données indisponibles' : 'Chargement…' }}</span></div>
      </section>

      <!-- ── 3b · Dernières alertes (entretiens et documents — aucune alerte GPS) ── -->
      <section class="card acc-red gpa-list anim" style="--i:2">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M10.3 4.2 2.8 17.5A2 2 0 0 0 4.5 20.5h15a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0z"/><path d="M12 9.5v4M12 16.8v.2"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Suivi</div><h2>Dernières alertes</h2></div>
          <div class="head-right" *ngIf="gpa && gpa.alerts.length">
            <span class="chip down" *ngIf="gpa.alertCounts.critical>0">{{ gpa.alertCounts.critical }}&nbsp;critique{{ gpa.alertCounts.critical>1?'s':'' }}</span>
            <span class="chip" [title]="gpa.alerts.length < gpa.alertCounts.total ? gpa.alerts.length + ' alertes affichées sur ' + gpa.alertCounts.total + ' (les plus urgentes)' : ''">{{ gpa.alertCounts.total }}&nbsp;au total</span>
          </div>
        </div>
        <div class="scroll" *ngIf="gpa && gpa.alerts.length">
          <div class="rows">
            <div *ngFor="let a of gpa.alerts" class="row" [title]="alerteInfoBulle(a)">
              <span class="al-ic" [class.dang]="a.severity==='critical'" [class.warn]="a.severity!=='critical'">
                <svg *ngIf="a.kind!=='document'" viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14.7 6.3a4 4 0 0 0-5.4 5.2L3.8 17a1.8 1.8 0 0 0 2.6 2.6l5.5-5.5a4 4 0 0 0 5.2-5.4l-2.5 2.5-2.1-.5-.5-2.1z"/></svg>
                <svg *ngIf="a.kind==='document'" viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5M9 13h6M9 17h4"/></svg>
              </span>
              <div class="grow">
                <div class="t1">{{ a.title }}</div>
                <div class="t2"><span class="plate xs" *ngIf="a.plate || a.vehicleName">{{ a.plate || a.vehicleName }}</span>{{ a.detail }}</div>
              </div>
              <span class="due" *ngIf="a.daysLeft!=null" [ngClass]="a.daysLeft<0?'red':a.daysLeft<=30?'amber':'green'">{{ joursLabel(a.daysLeft) }}</span>
            </div>
          </div>
        </div>
        <div class="empty" *ngIf="gpa && !gpa.alerts.length">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><path d="M22 4 12 14.01l-3-3"/></svg>
          <span>Aucune alerte</span>
        </div>
        <div class="empty" *ngIf="!gpa"><span>{{ gpaErreur ? 'Données indisponibles' : 'Chargement…' }}</span></div>
      </section>

      <!-- ── 3c · Consommation par véhicule (pleins saisis, L/100 km) ── -->
      <section class="card acc-cyan gpa-list anim" style="--i:3">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M5 20V6a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v14M3.5 20h11"/><path d="M15 9h2.2a1.8 1.8 0 0 1 1.8 1.8V17a1.5 1.5 0 0 0 3 0v-6.4L19.5 8"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Conduite</div><h2>Consommation par véhicule</h2></div>
          <div class="head-right" *ngIf="realFuel?.fleetConsumptionPer100Km != null">
            <span class="chip" title="Moyenne de la flotte sur la période : litres achetés ÷ kilomètres mesurés">Moy.&nbsp;<b class="num">{{ l100(realFuel.fleetConsumptionPer100Km) }}</b>&nbsp;L/100</span>
          </div>
        </div>
        <div class="scroll" *ngIf="consoRows.length">
          <div class="rows">
            <div *ngFor="let v of consoRows" class="row" [title]="consoInfoBulle(v)">
              <span class="plate">{{ v.plate }}</span>
              <div class="grow"><div class="bar"><i [style.width.%]="v.pct"></i></div></div>
              <span class="val num" *ngIf="v.c!=null">{{ l100(v.c) }}<small>&nbsp;L/100</small></span>
              <span class="val num muted" *ngIf="v.c==null">—</span>
            </div>
          </div>
        </div>
        <div class="empty" *ngIf="realFuel && !consoRows.length"><span>Aucun plein saisi sur la période</span></div>
        <div class="empty" *ngIf="!realFuel"><span>Chargement…</span></div>
      </section>

      <!-- ── 4a · Évolution mensuelle des coûts (12 mois, barres empilées + total) ── -->
      <section class="card span-8 acc-indigo gpa-evo anim" style="--i:4">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Tendance · 12 derniers mois</div><h2>Évolution mensuelle des coûts</h2></div>
          <div class="head-right">
            <div class="mlegend">
              <span *ngFor="let s of gpaSeries"><i [style.background]="s.color"></i>{{ s.name }}</span>
              <span *ngIf="gpaPartiel" title="Le dernier mois n'est pas terminé"><i class="hatch"></i>mois en cours</span>
            </div>
            <span class="chip" *ngIf="gpa" title="Total des 12 mois affichés (indépendant de la période choisie en haut)">12 mois&nbsp;<b class="num">{{ gpaTotal12 | appCurrency:2 }}</b></span>
          </div>
        </div>
        <div class="mchart" *ngIf="gpa && gpaMonths.length" (mouseleave)="mHover=-1">
          <div class="m-axis">
            <span *ngFor="let t of gpaTicks" [style.bottom.%]="t.pct">{{ t.label }}</span>
          </div>
          <div class="m-area">
            <i class="m-grid" *ngFor="let t of gpaTicks" [style.bottom.%]="t.pct"></i>
            <div class="m-col" *ngFor="let m of gpaMonths; let i=index" (mouseenter)="mHover=i" [class.on]="mHover===i" [class.partial]="m.isPartial">
              <span class="m-tot num" *ngIf="m.total" [style.bottom]="'calc(' + m.h + '% + 3px)'">{{ m.totLabel }}</span>
              <div class="m-bar" [style.height.%]="m.h">
                <i *ngFor="let s of m.segs" [style.flex-grow]="s.v" [style.background-color]="s.color"></i>
              </div>
              <span class="m-lbl">{{ m.lbl }}</span>
            </div>
            <div class="m-tip" *ngIf="mHover>=0 && gpaMonths[mHover]" [ngClass]="tipCls(mHover)" [style.left.%]="(mHover+0.5)/gpaMonths.length*100">
              <b>{{ gpaMonths[mHover].full }}</b>
              <span *ngFor="let s of gpaMonths[mHover].detail"><i [style.background]="s.color"></i>{{ s.name }}<em class="num">{{ s.v | appCurrency:2 }}</em></span>
              <span class="tt">Total<em class="num">{{ gpaMonths[mHover].total | appCurrency:2 }}</em></span>
            </div>
          </div>
        </div>
        <div class="empty" *ngIf="gpa && !gpaMonths.length"><span>Aucune dépense sur les 12 derniers mois</span></div>
        <div class="empty" *ngIf="!gpa"><span>{{ gpaErreur ? 'Données indisponibles' : 'Chargement…' }}</span></div>
      </section>

      <!-- ── 4b · Top 5 des véhicules par coût d'entretien et réparation ── -->
      <section class="card acc-amber gpa-top anim" style="--i:5">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M8 21h8M12 17v4M7 4h10v5a5 5 0 0 1-10 0z"/><path d="M17 5h3v2a3 3 0 0 1-3 3M7 5H4v2a3 3 0 0 0 3 3"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Entretien + réparation</div><h2 title="Top 5 des véhicules par coût d'entretien et réparation">Top 5 des véhicules par coût</h2></div>
          <div class="head-right mlegend" *ngIf="gpa && gpa.top5.length">
            <span><i style="background:#059669"></i>Entretien</span><span><i style="background:#d97706"></i>Réparation</span>
          </div>
        </div>
        <div class="rows" *ngIf="gpa && gpa.top5.length">
          <div *ngFor="let v of gpa.top5; let i=index" class="row" [title]="top5InfoBulle(v)">
            <span class="rank" [class.r1]="i===0" [class.r2]="i===1" [class.r3]="i===2" [class.rx]="i>2">{{ i+1 }}</span>
            <span class="plate">{{ v.plate || v.vehicleName }}</span>
            <div class="grow"><div class="bar stack"><i style="background:#059669" [style.width.%]="pctTop(v.maintenance)"></i><i style="background:#d97706" [style.left.%]="pctTop(v.maintenance)" [style.width.%]="pctTop(v.repair)"></i></div></div>
            <span class="val num">{{ v.total | appCurrency:2 }}</span>
          </div>
        </div>
        <div class="empty" *ngIf="gpa && !gpa.top5.length"><span>Aucun entretien ni réparation sur la période</span></div>
        <div class="empty" *ngIf="!gpa"><span>{{ gpaErreur ? 'Données indisponibles' : 'Chargement…' }}</span></div>
      </section>

      <!-- ── 5 · Dernières interventions (tableau compact, pleine largeur) ── -->
      <section class="card span-12 acc-slate gpa-int anim" style="--i:6">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2"/><rect x="9" y="3" width="6" height="4" rx="1.2"/><path d="M9 12h6M9 16h4"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Maintenance</div><h2>Dernières interventions</h2></div>
        </div>
        <div class="tbl-wrap" *ngIf="gpa && gpa.recentInterventions.length">
          <table class="itbl">
            <thead><tr>
              <th class="c-date">Date</th><th class="c-veh" title="Immatriculation du véhicule">Véhicule</th><th class="c-type">Type</th>
              <th>Description</th><th class="c-sup">Fournisseur</th>
              <th class="c-km r" title="Kilométrage relevé lors de l'intervention">Kilométrage</th><th class="c-cost r">Coût</th>
            </tr></thead>
            <tbody>
              <tr *ngFor="let r of gpa.recentInterventions">
                <td class="num">{{ r.date | date:'dd/MM/yyyy' }}</td>
                <td><span class="plate" [title]="r.vehicleName || ''">{{ r.plate || r.vehicleName }}</span></td>
                <td><span class="kind" [class.rep]="r.kind==='reparation'">{{ r.kind==='reparation' ? 'Réparation' : 'Entretien' }}</span></td>
                <td [title]="interventionLibelle(r)">{{ interventionLibelle(r) }}</td>
                <td class="c-sup" [title]="r.supplier || ''">{{ r.supplier || '—' }}</td>
                <td class="c-km r num">{{ r.mileageKm != null ? nombre(r.mileageKm) + ' km' : '—' }}</td>
                <td class="r num b">{{ r.cost | appCurrency:2 }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="empty" *ngIf="gpa && !gpa.recentInterventions.length"><span>Aucune intervention enregistrée</span></div>
        <div class="empty" *ngIf="!gpa"><span>{{ gpaErreur ? 'Données indisponibles' : 'Chargement…' }}</span></div>
      </section>

      <!-- ── 6 · Accès rapides (mêmes droits que le menu) ── -->
      <section class="card span-12 acc-indigo gpa-quick anim" style="--i:7" *ngIf="accesRapides.length">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M13 2 4.5 13.5H12L11 22l8.5-11.5H12z"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Raccourcis</div><h2>Accès rapides</h2></div>
        </div>
        <nav class="quick" [style.--qn]="accesRapides.length">
          <a *ngFor="let q of accesRapides" class="qk" [routerLink]="q.route" [ngClass]="q.acc" [title]="q.label + ' — ' + q.sub">
            <span class="qk-ic"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path *ngFor="let d of q.d" [attr.d]="d"/></svg></span>
            <span class="qk-t"><b>{{ q.label }}</b><small>{{ q.sub }}</small></span>
            <svg class="qk-go" viewBox="0 0 24 24" fill="none" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg>
          </a>
        </nav>
      </section>

      </ng-container>

      <ng-container *ngIf="hasGps">
      <!-- ── Répartition par type ── -->
      <section class="card acc-slate spend types anim" style="--i:7">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 3 3 8l9 5 9-5-9-5z"/><path d="M3 13l9 5 9-5"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Parc</div><h2>Répartition par type</h2></div>
        </div>
        <div class="rows" *ngIf="typeBreakdown.length">
          <div *ngFor="let t of typeBreakdown" class="row">
            <div class="lblc"><i [style.background]="t.color"></i><span>{{ t.type }}</span></div>
            <div class="bar"><i [style.width.%]="(t.count/maxTypeCount)*100" [style.background]="t.color"></i></div>
            <div class="val num">{{ t.count }} <small>· {{ t.km | number:'1.0-0' }}&nbsp;km</small></div>
          </div>
        </div>
        <div class="empty" *ngIf="!typeBreakdown.length"><span>Aucune donnée</span></div>
      </section>

      <!-- ── Alertes ── -->
      <section class="card acc-red anim" style="--i:8">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M10.3 4.2 2.8 17.5A2 2 0 0 0 4.5 20.5h15a2 2 0 0 0 1.7-3L13.7 4.2a2 2 0 0 0-3.4 0z"/><path d="M12 9.5v4M12 16.8v.2"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Sécurité</div><h2>Alertes</h2></div>
          <div class="head-right">
            <div class="pager" *ngIf="alerts.length>5">
              <button type="button" (click)="alertsP=alertsP-1" [disabled]="alertsP===0" aria-label="Précédent"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 5l-7 7 7 7"/></svg></button>
              <span class="num">{{ alertsP+1 }}&thinsp;/&thinsp;{{ Math.ceil(alerts.length/5) }}</span>
              <button type="button" (click)="alertsP=alertsP+1" [disabled]="(alertsP+1)*5>=alerts.length" aria-label="Suivant"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg></button>
            </div>
          </div>
        </div>
        <div class="rows" *ngIf="alerts.length">
          <div *ngFor="let a of pagedAlerts" class="row" [class.is-new]="a._isNew">
            <span class="al-ic" [class.warn]="a.severity==='warning'" [class.dang]="a.severity==='danger'" [class.info]="a.severity!=='warning' && a.severity!=='danger'">
              <svg *ngIf="a.severity==='warning'" viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="13" r="8"/><path d="M12 13l3.5-3.5M12 2.5h0"/></svg>
              <svg *ngIf="a.severity==='danger'" viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 21s7-5.6 7-11a7 7 0 1 0-14 0c0 5.4 7 11 7 11z"/><path d="M9.5 7.5l5 5M14.5 7.5l-5 5"/></svg>
              <svg *ngIf="a.severity!=='warning' && a.severity!=='danger'" viewBox="0 0 24 24" fill="none" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8v.2"/></svg>
            </span>
            <div class="grow"><div class="t1">{{ a.message }}</div><div class="t2">{{ a.time }}</div></div>
          </div>
        </div>
        <div class="empty" *ngIf="!alerts.length">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><path d="M22 4 12 14.01l-3-3"/></svg>
          <span>Aucune alerte</span>
        </div>
      </section>

      <!-- ── Kilométrage par véhicule ── -->
      <section class="card acc-indigo anim" style="--i:9">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="13" r="8.5"/><path d="M12 13l4-4"/><path d="M12 4.5V2.5M9 3h6"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Activité</div><h2>Kilométrage</h2></div>
          <div class="head-right"><span class="chip" *ngIf="periodDistance>0"><b class="num">{{ periodDistance | number:'1.0-0' }}&nbsp;km</b><span class="delta" *ngIf="distanceTrend!==null && distanceTrend!==0" [class.good]="distanceTrend>0" [class.bad]="distanceTrend<0"><svg viewBox="0 0 24 24" fill="currentColor" [style.transform]="distanceTrend<0?'rotate(180deg)':''"><path d="M12 5l7 10H5z"/></svg>{{ absPct(distanceTrend) }}%</span></span></div>
        </div>
        <div class="rows" *ngIf="topUnits.length">
          <div *ngFor="let u of pUnits" class="row">
            <span class="plate">{{ u.name }}</span>
            <div class="grow"><div class="bar"><i [style.width.%]="(u.mileage/maxMileage)*100" style="background:var(--primary)"></i></div></div>
            <div class="val num">{{ u.mileage | number:'1.0-0' }} <small>km</small></div>
          </div>
        </div>
        <div class="pgr-foot" *ngIf="topUnits.length>5">
          <div class="pager">
            <button type="button" (click)="unP=unP-1" [disabled]="unP===0" aria-label="Précédent"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 5l-7 7 7 7"/></svg></button>
            <span class="num">{{ unP+1 }}&thinsp;/&thinsp;{{ Math.ceil(topUnits.length/5) }}</span>
            <button type="button" (click)="unP=unP+1" [disabled]="(unP+1)*5>=topUnits.length" aria-label="Suivant"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg></button>
          </div>
        </div>
        <div class="empty" *ngIf="!topUnits.length"><span>Aucune donnée</span></div>
      </section>

      <!-- ── Scores de conduite ── -->
      <section class="card acc-green anim" style="--i:10">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="9" r="5.5"/><path d="M8.5 13.5 7 21l5-2.6L17 21l-1.5-7.5"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Conduite</div><h2>Scores de conduite</h2></div>
          <div class="head-right">
            <div class="pager" *ngIf="drivingScores.length>5">
              <button type="button" (click)="scP=scP-1" [disabled]="scP===0" aria-label="Précédent"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 5l-7 7 7 7"/></svg></button>
              <span class="num">{{ scP+1 }}&thinsp;/&thinsp;{{ Math.ceil(drivingScores.length/5) }}</span>
              <button type="button" (click)="scP=scP+1" [disabled]="(scP+1)*5>=drivingScores.length" aria-label="Suivant"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg></button>
            </div>
          </div>
        </div>
        <div class="rows" *ngIf="drivingScores.length">
          <div *ngFor="let s of pScores;let i=index" class="row">
            <span class="rank" [class.r1]="scP*5+i===0" [class.r2]="scP*5+i===1" [class.r3]="scP*5+i===2" [class.rx]="scP*5+i>2">{{ scP*5+i+1 }}</span>
            <span class="plate">{{ s.plate }}</span>
            <div class="grow"><div class="bar"><i [style.width.%]="s.score" [style.background]="scoreC(s.score)"></i></div></div>
            <span class="score num" [ngClass]="tintCls(s.score>=80?0:s.score>=60?1:2)">{{ s.score }}</span>
          </div>
        </div>
        <div class="empty" *ngIf="!drivingScores.length"><span>Aucun score</span></div>
      </section>

      <!-- ── Derniers trajets ── -->
      <section class="card acc-cyan anim" style="--i:11">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><circle cx="6" cy="19" r="2.2"/><circle cx="18" cy="5" r="2.2"/><path d="M8 19h7a4 4 0 0 0 0-8H9a4 4 0 0 1 0-8h6.5"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Activité</div><h2>Derniers trajets</h2></div>
          <div class="head-right">
            <div class="pager" *ngIf="recentTrips.length>5">
              <button type="button" (click)="trP=trP-1" [disabled]="trP===0" aria-label="Précédent"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 5l-7 7 7 7"/></svg></button>
              <span class="num">{{ trP+1 }}&thinsp;/&thinsp;{{ Math.ceil(recentTrips.length/5) }}</span>
              <button type="button" (click)="trP=trP+1" [disabled]="(trP+1)*5>=recentTrips.length" aria-label="Suivant"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg></button>
            </div>
          </div>
        </div>
        <div class="rows" *ngIf="recentTrips.length">
          <div *ngFor="let t of pTrips" class="row">
            <span class="plate">{{ t.plate }}</span>
            <div class="grow"><div class="t1 num">{{ t.distance }}&nbsp;km <small>· {{ t.duration }}</small></div></div>
            <span class="t2 num nof">{{ t.date }}</span>
          </div>
        </div>
        <div class="empty" *ngIf="!recentTrips.length"><span>Aucun trajet</span></div>
      </section>

      <!-- ── Consommation / véhicule ── -->
      <section class="card acc-amber anim" style="--i:12">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M5 20V6a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v14M3.5 20h11"/><path d="M15 9h2.2a1.8 1.8 0 0 1 1.8 1.8V17a1.5 1.5 0 0 0 3 0v-6.4L19.5 8"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Carburant</div><h2>Conso / véhicule</h2></div>
          <div class="head-right">
            <span class="chip ghost">L/100&nbsp;km</span>
            <div class="pager" *ngIf="vehicleFuelStats.length>5">
              <button type="button" (click)="fuP=fuP-1" [disabled]="fuP===0" aria-label="Précédent"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 5l-7 7 7 7"/></svg></button>
              <span class="num">{{ fuP+1 }}&thinsp;/&thinsp;{{ Math.ceil(vehicleFuelStats.length/5) }}</span>
              <button type="button" (click)="fuP=fuP+1" [disabled]="(fuP+1)*5>=vehicleFuelStats.length" aria-label="Suivant"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg></button>
            </div>
          </div>
        </div>
        <div class="rows" *ngIf="vehicleFuelStats.length">
          <div *ngFor="let v of pFuel" class="row">
            <span class="plate">{{ v.plate }}</span>
            <div class="grow"><div class="bar"><i [style.width.%]="(v.consumption/maxFuelConsumption)*100" [style.background]="fuelC(v.consumption)"></i></div></div>
            <span class="score num sm" [ngClass]="tintCls(v.consumption<=6?0:v.consumption<=8?1:2)">{{ v.consumption | number:'1.1-1' }}</span>
          </div>
        </div>
        <div class="empty" *ngIf="!vehicleFuelStats.length"><span>Aucune donnée</span></div>
      </section>

      <!-- ── Géozones ── -->
      <section class="card acc-cyan geo anim" style="--i:13">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M12 2.8 20 7v10l-8 4.2L4 17V7l8-4.2z"/><circle cx="12" cy="12" r="2.6"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Zones</div><h2>Géozones</h2></div>
          <div class="head-right"><span class="chip" *ngIf="geofences.length">{{ geofences.length }} actives</span></div>
        </div>
        <div class="rows" *ngIf="geofences.length">
          <div *ngFor="let g of geofences" class="row">
            <span class="gz-ic"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M3 21V8l7-4.5V21M10 21V12l7-3v12M3 21h18"/></svg></span>
            <div class="grow"><div class="t1">{{ g.name }}</div><div class="t2">passages sur la période</div></div>
            <span class="cnt num">{{ g.count }}</span>
          </div>
        </div>
        <div class="empty" *ngIf="!geofences.length"><span>Aucune géozone</span></div>
      </section>
      </ng-container>

      <!-- ── Conducteurs ── -->
      <section class="card span-12 acc-indigo anim" *ngIf="hasGps" style="--i:14">
        <div class="card-head">
          <span class="icon-chip"><svg viewBox="0 0 24 24" fill="none" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><circle cx="9" cy="8" r="3.4"/><path d="M2.8 20c.6-3.6 3.1-5.6 6.2-5.6s5.6 2 6.2 5.6"/><circle cx="17.5" cy="9.5" r="2.6"/><path d="M16 14.7c3-.3 5 1.6 5.4 4.3"/></svg></span>
          <div class="head-txt"><div class="eyebrow">Équipe</div><h2>Conducteurs</h2></div>
          <div class="head-right">
            <span class="chip">{{ drivers.length }} conducteurs</span>
            <div class="pager" *ngIf="drivers.length>8">
              <button type="button" (click)="drP=drP-1" [disabled]="drP===0" aria-label="Précédent"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 5l-7 7 7 7"/></svg></button>
              <span class="num">{{ drP+1 }}&thinsp;/&thinsp;{{ Math.ceil(drivers.length/8) }}</span>
              <button type="button" (click)="drP=drP+1" [disabled]="(drP+1)*8>=drivers.length" aria-label="Suivant"><svg viewBox="0 0 24 24" fill="none" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 5l7 7-7 7"/></svg></button>
            </div>
          </div>
        </div>
        <div class="drivers" *ngIf="drivers.length">
          <div *ngFor="let d of pDrv;let i=index" class="driver" [ngClass]="accFor(i)">
            <span class="avatar">{{ d.initials }}</span>
            <div class="grow"><div class="t1">{{ d.name }}</div><div class="t2 num">{{ d.vehicle || 'Non assigné' }}</div></div>
            <span *ngIf="d.active" class="badge-on">Actif</span>
            <span *ngIf="!d.active" class="badge-off">—</span>
          </div>
        </div>
        <div class="empty" *ngIf="!drivers.length"><span>Aucun conducteur</span></div>
      </section>

    </div><!-- /bento -->
  </div>
</div>
</app-layout>
  `,
  styles: [`
    /* ══════════════════════════════════════════════════════════
       CALYPSO COMMAND — masthead carbone éditorial + corps bento.
       Construit sur les tokens de l'app (global_styles.css) pour
       suivre les thèmes clair/sombre partout.
    ══════════════════════════════════════════════════════════ */

    :host{
      /* teintes 600 (texte teinté lisible sur surface claire) */
      --ok-600:#059669; --warn-600:#d97706; --bad-600:#dc2626;
      --acc-indigo:#4f46e5;  --acc-indigo-ink:#4338ca;
      --acc-green:#059669;   --acc-green-ink:#047857;
      --acc-amber:#d97706;   --acc-amber-ink:#b45309;
      --acc-red:#dc2626;     --acc-red-ink:#b91c1c;
      --acc-cyan:#0891b2;    --acc-cyan-ink:#0e7490;
      --acc-slate:#64748b;   --acc-slate-ink:#475569;

      --track:#eef2f7;
      --grid-line:#e9eef5;
      --dot-grain:rgba(100,116,139,.10);
      --card-shadow:0 1px 2px rgba(15,23,42,.05), 0 10px 28px -14px rgba(15,23,42,.14);
      --card-shadow-hover:0 2px 4px rgba(15,23,42,.06), 0 18px 40px -16px rgba(15,23,42,.20);
      --chip-bg:var(--bg-secondary);

      /* masthead carbone — constant dans les deux thèmes (signature) */
      --mast-bg1:#0d1425; --mast-bg2:#16213a; --mast-glow:rgba(99,102,241,.24);
      --mast-ink:#f8fafc; --mast-sub:#94a3b8; --mast-hair:rgba(148,163,184,.16);
      --mast-border:transparent;
    }
    :host-context([data-theme="dark"]){
      --ok-600:#34d399; --warn-600:#fbbf24; --bad-600:#f87171;
      --acc-indigo:#818cf8;  --acc-indigo-ink:#a5b4fc;
      --acc-green:#10b981;   --acc-green-ink:#34d399;
      --acc-amber:#f59e0b;   --acc-amber-ink:#fbbf24;
      --acc-red:#ef4444;     --acc-red-ink:#f87171;
      --acc-cyan:#06b6d4;    --acc-cyan-ink:#22d3ee;
      --acc-slate:#94a3b8;   --acc-slate-ink:#cbd5e1;

      --track:#2b3a52;
      --grid-line:#2a3a54;
      --dot-grain:rgba(148,163,184,.07);
      --card-shadow:0 1px 2px rgba(2,6,23,.5), 0 14px 34px -16px rgba(2,6,23,.7);
      --card-shadow-hover:0 2px 4px rgba(2,6,23,.5), 0 22px 48px -18px rgba(2,6,23,.85);
      --chip-bg:rgba(148,163,184,.10);

      --mast-bg1:#111a30; --mast-bg2:#1a2643; --mast-glow:rgba(99,102,241,.3);
      --mast-border:rgba(148,163,184,.14);
    }

    .dash{
      flex:1; min-height:calc(100vh - 42px);
      background:var(--bg-page); color:var(--text-primary);
      font-family:var(--font-family); font-size:13px; line-height:1.45;
      font-variant-numeric:tabular-nums; position:relative;
    }
    .dash::before{ /* trame de points très subtile */
      content:""; position:absolute; inset:0; pointer-events:none;
      background-image:radial-gradient(circle at 1px 1px, var(--dot-grain) 1px, transparent 1.5px);
      background-size:22px 22px;
    }
    .shell{max-width:1600px;margin:0 auto;padding:20px 24px 56px;position:relative}
    svg{display:block}
    button{font:inherit;color:inherit;background:none;border:0;cursor:pointer}
    .num{font-variant-numeric:tabular-nums}

    @keyframes rise{from{opacity:0;transform:translateY(12px)}to{opacity:1;transform:none}}
    .anim{opacity:0;animation:rise .55s cubic-bezier(.22,1,.36,1) forwards;animation-delay:calc(var(--i,0)*45ms)}
    @media (prefers-reduced-motion:reduce){
      *,*::before,*::after{animation-duration:.001s!important;animation-delay:0s!important;transition:none!important}
      .anim{opacity:1}
    }

    /* ════ 1 · MASTHEAD ════ */
    .mast{
      position:relative;overflow:hidden;
      border-radius:18px;border:1px solid var(--mast-border);
      padding:22px 28px 22px;margin-bottom:8px;
      color:var(--mast-ink);
      background:
        radial-gradient(1100px 380px at 88% -30%, var(--mast-glow), transparent 62%),
        linear-gradient(132deg,var(--mast-bg1) 0%, var(--mast-bg2) 58%, var(--mast-bg1) 100%);
      box-shadow:0 24px 50px -24px rgba(2,6,23,.5);
    }
    .mast::before{ /* colonnes fantômes façon gabarit éditorial */
      content:"";position:absolute;inset:0;pointer-events:none;
      background:repeating-linear-gradient(90deg, rgba(148,163,184,.05) 0 1px, transparent 1px 132px);
      mask:linear-gradient(180deg,transparent 4%,#000 30%,#000 78%,transparent 100%);
    }
    .mast>*{position:relative}

    .mast-top{display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap}
    .brand{display:flex;align-items:center;gap:9px;font-size:10.5px;font-weight:700;letter-spacing:.16em;text-transform:uppercase;color:var(--mast-sub)}
    .brand b{color:#e2e8f0;font-weight:700}
    .live{display:inline-flex;align-items:center;gap:7px;padding:4px 11px 4px 8px;border-radius:999px;
      background:rgba(16,185,129,.12);border:1px solid rgba(52,211,153,.35);
      color:#6ee7b7;font-size:10.5px;font-weight:700;letter-spacing:.12em;text-transform:uppercase}
    .live.off{background:rgba(239,68,68,.12);border-color:rgba(248,113,113,.35);color:#fca5a5}
    .live-dot{width:7px;height:7px;border-radius:50%;background:#34d399;position:relative}
    .live.off .live-dot{background:#f87171}
    .live-dot::after{content:"";position:absolute;inset:-4px;border-radius:50%;border:1.5px solid currentColor;opacity:.7;animation:pulse 2s ease-out infinite}
    @keyframes pulse{0%{transform:scale(.5);opacity:.8}80%,100%{transform:scale(1.5);opacity:0}}
    .mast-date{font-size:12px;color:var(--mast-sub);letter-spacing:.02em;text-transform:capitalize}

    .mast-title{display:flex;align-items:baseline;justify-content:space-between;gap:20px;flex-wrap:wrap;margin:12px 0 16px}
    .mast-title h1{margin:0;font-size:clamp(28px,3.2vw,42px);font-weight:800;letter-spacing:-.035em;line-height:1.02;color:var(--mast-ink)}
    .mast-title .meta{display:flex;gap:22px;font-size:12px;color:var(--mast-sub);white-space:nowrap;flex-wrap:wrap}
    .mast-title .meta b{color:#e2e8f0;font-weight:700}

    /* ── LA barre de filtre ── */
    .toolbar{
      display:flex;align-items:center;gap:12px 20px;flex-wrap:wrap;
      padding:12px 0;border-top:1px solid var(--mast-hair);border-bottom:1px solid var(--mast-hair);
    }
    .pills{display:flex;gap:4px;flex-wrap:wrap}
    .pill{
      border:1px solid transparent;background:none;border-radius:999px;
      padding:7px 14px;font-size:12px;font-weight:550;color:rgba(203,213,225,.72);
      transition:background .2s,color .2s,border-color .2s;white-space:nowrap;
    }
    .pill:hover{background:rgba(148,163,184,.12);color:#f1f5f9}
    .pill.active{
      background:rgba(99,102,241,.22);border-color:rgba(129,140,248,.55);color:#fff;font-weight:650;
      box-shadow:inset 0 0 0 1px rgba(129,140,248,.15), 0 4px 14px -6px rgba(99,102,241,.6);
    }
    .pill.active::before{content:"";display:inline-block;width:5px;height:5px;border-radius:50%;background:#a5b4fc;margin-right:7px;vertical-align:1.5px}
    .pill.custom:not(.active){color:rgba(148,163,184,.6)}

    .tb-sep{width:1px;height:26px;background:var(--mast-hair)}
    .range{display:flex;align-items:flex-end;gap:10px;margin-left:auto;flex-wrap:wrap}
    .rfield{display:flex;flex-direction:column;gap:3px}
    .rfield label{font-size:9.5px;font-weight:700;letter-spacing:.16em;text-transform:uppercase;color:var(--mast-sub)}
    /* Inputs date : posés sur la bande carbone → color-scheme:dark
       (icône calendrier native claire, jamais de boîte blanche flottante). */
    .range input[type="date"]{
      color-scheme:dark;
      appearance:none;-webkit-appearance:none;
      font:inherit;font-size:12.5px;font-variant-numeric:tabular-nums;
      color:#e2e8f0;background:rgba(148,163,184,.09);
      border:none;border-bottom:1px solid rgba(148,163,184,.4);
      border-radius:8px 8px 2px 2px;padding:7px 10px 6px;min-width:132px;
      transition:border-color .2s,background .2s;
    }
    .range input[type="date"]:hover{background:rgba(148,163,184,.14)}
    .range input[type="date"]:focus{outline:none;border-bottom-color:#818cf8;background:rgba(99,102,241,.14)}
    .range input[type="date"]::-webkit-calendar-picker-indicator{opacity:.75;cursor:pointer}
    .apply{
      border:none;border-radius:8px;padding:8px 16px;
      background:var(--primary-light);color:#fff;font-size:12px;font-weight:650;letter-spacing:.01em;
      box-shadow:0 6px 16px -6px rgba(99,102,241,.65);
      transition:transform .15s,box-shadow .2s,background .2s;
    }
    .apply:hover{background:#4f46e5;transform:translateY(-1px);box-shadow:0 10px 20px -6px rgba(99,102,241,.7)}
    .apply:active{transform:none}

    /* ── rangée KPI (filets) ── */
    .kpis{display:grid;grid-template-columns:repeat(6,1fr);padding-top:16px}
    /* Offre GPA : cinq KPI avec icône, sur une ligne */
    .kpis.k5{grid-template-columns:repeat(5,minmax(0,1fr))}
    .kpi.kpi-ic{display:flex;align-items:flex-start;gap:12px;cursor:default}
    .kic{
      width:34px;height:34px;border-radius:10px;flex:none;display:grid;place-items:center;margin-top:1px;
      background:color-mix(in srgb,var(--kc) 16%,transparent);
      border:1px solid color-mix(in srgb,var(--kc) 30%,transparent);
    }
    .kic svg{width:17px;height:17px;stroke:var(--kc)}
    .ktx{min-width:0;display:flex;flex-direction:column}
    .kpi-dec{font-size:.56em;font-weight:700;letter-spacing:-.01em;color:#cbd5e1}
    .kpi-na{color:var(--mast-sub);font-weight:600}
    .kpi{padding:2px 22px 4px;border-left:1px solid var(--mast-hair);min-width:0}
    .kpi:first-child{border-left:none;padding-left:2px}
    .kpi-l{display:flex;align-items:center;gap:6px;font-size:10px;font-weight:700;letter-spacing:.14em;text-transform:uppercase;color:var(--mast-sub);white-space:nowrap}
    .kpi-l .dot{width:6px;height:6px;border-radius:50%;flex:none}
    .kpi-v{
      display:flex;align-items:baseline;gap:5px;margin-top:6px;white-space:nowrap;
      font-size:clamp(21px,1.7vw,29px);font-weight:800;letter-spacing:-.03em;line-height:1.05;color:var(--mast-ink);
      font-variant-numeric:tabular-nums;
    }
    .kpi-u{font-size:.48em;font-weight:650;letter-spacing:.06em;color:var(--mast-sub)}
    .kpi-s{display:flex;align-items:center;gap:6px;margin-top:5px;font-size:11px;color:var(--mast-sub);white-space:nowrap}
    .kchip{display:inline-flex;align-items:center;gap:4px;padding:2px 8px;border-radius:999px;font-size:10.5px;font-weight:700}
    .kchip svg{flex:none}
    .kchip-danger{background:rgba(239,68,68,.16);color:#fca5a5}
    .kchip-ok{background:rgba(16,185,129,.14);color:#6ee7b7}

    /* ════ BENTO ════ */
    .bento{display:grid;grid-template-columns:repeat(12,1fr);gap:14px;margin-top:14px}
    .card{
      grid-column:span 4;
      background:var(--bg-card);
      border:1px solid var(--border-color);
      border-radius:20px;
      box-shadow:var(--card-shadow);
      padding:18px 20px;
      position:relative;overflow:hidden;min-width:0;
      transition:transform .22s cubic-bezier(.22,1,.36,1), box-shadow .22s;
      display:flex;flex-direction:column;
    }
    .card:hover{transform:translateY(-2px);box-shadow:var(--card-shadow-hover)}
    .card::before{ /* rail d'accent */
      content:"";position:absolute;left:0;top:20px;width:3px;height:26px;
      border-radius:0 3px 3px 0;background:var(--accent,var(--primary));
    }
    .span-6{grid-column:span 6}.span-8{grid-column:span 8}.span-12{grid-column:span 12}

    .acc-indigo{--accent:var(--acc-indigo);--accent-ink:var(--acc-indigo-ink)}
    .acc-green {--accent:var(--acc-green); --accent-ink:var(--acc-green-ink)}
    .acc-amber {--accent:var(--acc-amber); --accent-ink:var(--acc-amber-ink)}
    .acc-red   {--accent:var(--acc-red);   --accent-ink:var(--acc-red-ink)}
    .acc-cyan  {--accent:var(--acc-cyan);  --accent-ink:var(--acc-cyan-ink)}
    .acc-slate {--accent:var(--acc-slate); --accent-ink:var(--acc-slate-ink)}

    .card-head{display:flex;align-items:center;gap:11px;margin-bottom:14px}
    .icon-chip{
      width:32px;height:32px;border-radius:10px;flex:none;
      display:grid;place-items:center;
      background:color-mix(in srgb, var(--accent) 12%, transparent);
    }
    .icon-chip svg{stroke:var(--accent-ink);width:16px;height:16px}
    .head-txt{min-width:0}
    .eyebrow{font-size:10px;font-weight:700;letter-spacing:.16em;text-transform:uppercase;color:var(--accent-ink);line-height:1.2}
    .card-head h2{margin:0;font-size:14px;font-weight:700;letter-spacing:-.015em;color:var(--text-primary);line-height:1.25;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .head-right{margin-left:auto;display:flex;align-items:center;gap:6px;flex:none}

    .chip{
      display:inline-flex;align-items:center;gap:5px;
      font-size:11px;font-weight:700;padding:3.5px 9px;border-radius:999px;
      background:var(--chip-bg);border:1px solid var(--border-color);
      color:var(--text-secondary);white-space:nowrap;
    }
    .chip.ghost{border-style:dashed;color:var(--text-muted);font-weight:600}
    .chip.down{color:var(--bad-600);background:color-mix(in srgb,var(--acc-red) 10%,transparent);border-color:color-mix(in srgb,var(--acc-red) 26%,transparent)}
    .chip-dot{width:6px;height:6px;border-radius:50%;display:inline-block}
    .delta{display:inline-flex;align-items:center;gap:3px;font-size:10px;font-weight:700}
    .delta.bad{color:var(--bad-600)}
    .delta.good{color:var(--ok-600)}
    .delta svg{width:9px;height:9px}

    .pager{display:flex;align-items:center;gap:6px;font-size:11px;font-weight:600;color:var(--text-muted)}
    .pager button{
      width:22px;height:22px;border-radius:7px;display:grid;place-items:center;
      border:1px solid var(--border-color);background:var(--bg-card);color:var(--text-secondary);
      transition:background .15s;
    }
    .pager button:hover:not(:disabled){background:var(--bg-secondary)}
    .pager button:disabled{opacity:.35;cursor:default}
    .pager svg{width:11px;height:11px;stroke:currentColor}
    .pgr-foot{display:flex;justify-content:flex-end;margin-top:10px}

    /* ── carte Leaflet ── */
    .map-wrap{position:relative;border-radius:14px;overflow:hidden;flex:1;border:1px solid var(--border-color)}
    .fleet-map{height:360px;background:var(--bg-secondary)}
    :host-context([data-theme="dark"]) .fleet-map{background:#0b1220}
    :host-context([data-theme="dark"]) .fleet-map ::ng-deep .leaflet-tile{filter:invert(1) hue-rotate(180deg) brightness(.95) contrast(.9)}
    .map-legend{
      position:absolute;left:12px;bottom:12px;z-index:1000;
      display:flex;gap:4px;flex-wrap:wrap;
      background:color-mix(in srgb, var(--bg-card) 82%, transparent);
      backdrop-filter:blur(8px);
      border:1px solid var(--border-color);border-radius:11px;padding:6px 8px;
      pointer-events:none;
    }
    .map-legend span{display:inline-flex;align-items:center;gap:6px;font-size:10.5px;font-weight:600;color:var(--text-secondary);padding:2px 6px}
    .map-legend i{width:8px;height:8px;border-radius:50%;flex:none}

    /* ── rangées génériques ── */
    .rows{display:flex;flex-direction:column;flex:1}
    .row{display:flex;align-items:center;gap:10px;padding:9px 0;border-bottom:1px solid var(--border-color);min-width:0}
    .rows .row:last-child{border-bottom:0;padding-bottom:2px}
    .rows .row:first-child{padding-top:2px}
    .row.is-new{animation:flash .8s ease}
    @keyframes flash{0%{background:color-mix(in srgb,var(--primary) 8%,transparent)}100%{background:transparent}}
    .plate{
      font-size:11px;font-weight:700;font-variant-numeric:tabular-nums;letter-spacing:.02em;
      background:var(--chip-bg);border:1px solid var(--border-color);
      border-radius:7px;padding:2.5px 7px;color:var(--text-secondary);white-space:nowrap;flex:none;
    }
    .row .grow{flex:1;min-width:0}
    .row .t1{font-size:12px;font-weight:600;color:var(--text-primary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .row .t1 small{color:var(--text-muted);font-weight:600}
    .row .t2{font-size:11px;color:var(--text-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin-top:1px}
    .row .t2.nof{flex:none;overflow:visible}
    .row .val{font-size:12.5px;font-weight:700;color:var(--text-primary);white-space:nowrap;flex:none}
    .row .val small{font-weight:600;color:var(--text-muted);font-size:10.5px}

    .bar{height:7px;border-radius:99px;background:var(--track);overflow:hidden;position:relative}
    .bar i{position:absolute;left:0;top:0;bottom:0;border-radius:99px;transition:width .9s cubic-bezier(.16,1,.3,1)}

    .due{font-size:11px;font-weight:700;border-radius:999px;padding:3px 9px;white-space:nowrap;flex:none}
    .due.red{color:var(--bad-600);background:color-mix(in srgb,var(--acc-red) 11%,transparent)}
    .due.amber{color:var(--warn-600);background:color-mix(in srgb,var(--acc-amber) 12%,transparent)}
    .due.green{color:var(--ok-600);background:color-mix(in srgb,var(--acc-green) 11%,transparent)}

    .al-ic{width:28px;height:28px;border-radius:9px;display:grid;place-items:center;flex:none}
    .al-ic svg{width:14px;height:14px}
    .al-ic.warn{background:color-mix(in srgb,var(--acc-amber) 13%,transparent)}.al-ic.warn svg{stroke:var(--acc-amber-ink)}
    .al-ic.dang{background:color-mix(in srgb,var(--acc-red) 12%,transparent)}.al-ic.dang svg{stroke:var(--acc-red-ink)}
    .al-ic.info{background:color-mix(in srgb,var(--acc-cyan) 12%,transparent)}.al-ic.info svg{stroke:var(--acc-cyan-ink)}

    .rank{width:24px;height:24px;border-radius:50%;display:grid;place-items:center;flex:none;font-size:11px;font-weight:800;color:#fff}
    .rank.r1{background:linear-gradient(160deg,#fbbf24,#d97706);box-shadow:0 2px 6px rgba(217,119,6,.4)}
    .rank.r2{background:linear-gradient(160deg,#cbd5e1,#94a3b8);box-shadow:0 2px 6px rgba(100,116,139,.35)}
    .rank.r3{background:linear-gradient(160deg,#d6a06a,#b45309);box-shadow:0 2px 6px rgba(180,83,9,.35)}
    .rank.rx{background:var(--chip-bg);color:var(--text-muted);border:1px solid var(--border-color);font-weight:700}
    .score{font-size:14px;font-weight:800;letter-spacing:-.02em;flex:none}
    .score.sm{font-size:13px}
    .tint-ok{color:var(--ok-600)}.tint-warn{color:var(--warn-600)}.tint-bad{color:var(--bad-600)}

    .row .val.muted{color:var(--text-muted)}

    .drivers{display:grid;grid-template-columns:repeat(4,1fr);gap:10px}
    .driver{
      display:flex;align-items:center;gap:10px;min-width:0;
      border:1px solid var(--border-color);border-radius:13px;
      background:var(--bg-secondary);padding:10px 12px;
      transition:transform .18s, box-shadow .18s;
    }
    .driver:hover{transform:translateY(-1px);box-shadow:var(--card-shadow)}
    .driver .grow{flex:1;min-width:0}
    .avatar{
      width:34px;height:34px;border-radius:50%;flex:none;display:grid;place-items:center;
      font-size:11.5px;font-weight:800;letter-spacing:.02em;
      color:var(--accent-ink);
      background:color-mix(in srgb, var(--accent) 14%, transparent);
    }
    .badge-on{font-size:10px;font-weight:700;color:var(--ok-600);background:color-mix(in srgb,var(--acc-green) 11%,transparent);border-radius:999px;padding:2.5px 8px;white-space:nowrap;flex:none}
    .badge-off{font-size:10px;font-weight:700;color:var(--text-muted);background:var(--chip-bg);border:1px solid var(--border-color);border-radius:999px;padding:2px 8px;white-space:nowrap;flex:none}

    /* ── donut + jauge ── */
    .donut-flex,.gauge-flex{display:flex;align-items:center;gap:22px;flex:1}
    .donut,.gauge{position:relative;flex:none}
    .donut{width:150px;height:150px}
    .gauge{width:140px;height:140px}
    .donut svg,.gauge svg{width:100%;height:100%}
    .donut svg{transform:rotate(-90deg)}
    .donut-track{stroke:var(--track)}
    .donut-seg{transition:stroke-width .2s ease, stroke-dasharray .7s cubic-bezier(.16,1,.3,1);cursor:pointer}
    .donut-c{position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center}
    .donut-n{font-size:29px;font-weight:800;color:var(--text-primary);letter-spacing:-.04em;line-height:1;font-variant-numeric:tabular-nums}
    .gauge-n{font-size:26px;font-weight:800;color:var(--text-primary);letter-spacing:-.04em;line-height:1;font-variant-numeric:tabular-nums}
    .donut-l{font-size:9.5px;font-weight:700;color:var(--text-muted);text-transform:uppercase;letter-spacing:.14em;margin-top:4px}
    .gauge-ring{transition:stroke-dashoffset 1.2s cubic-bezier(.16,1,.3,1)}
    .legend{flex:1;display:flex;flex-direction:column;gap:2px;min-width:0}
    .legend .lg{display:flex;align-items:center;gap:8px;font-size:12px;padding:6px 8px;border-radius:8px;transition:background .15s}
    .legend .lg.hl,.legend .lg:hover{background:color-mix(in srgb,var(--primary) 7%,transparent)}
    .legend .lg i{width:9px;height:9px;border-radius:3px;flex:none}
    .legend .lg .lbl{flex:1;color:var(--text-secondary);font-weight:500;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .legend .lg b{font-weight:800;font-size:13px;color:var(--text-primary);padding-left:12px}

    .hbars{flex:1;display:flex;flex-direction:column;gap:13px;min-width:0}
    .hbar .hb-top{display:flex;justify-content:space-between;align-items:baseline;margin-bottom:5px;gap:8px}
    .hbar .hb-top span{font-size:11.5px;font-weight:600;color:var(--text-secondary);white-space:nowrap}
    .hbar .hb-top b{font-size:12.5px;font-weight:800;color:var(--text-primary)}

    /* ── graphique carburant ── */
    .chart-wrap{flex:1;min-height:0;position:relative}
    .chart-wrap svg{width:100%;height:auto;overflow:visible}
    .ax{font-size:10px;font-weight:600;fill:var(--text-muted)}
    .gridline{stroke:var(--grid-line);stroke-width:1}
    .chart-stop{stop-color:var(--primary)}
    .chart-line{stroke:var(--primary)}
    .chart-area{opacity:0;animation:fadeIn .6s ease forwards .5s}
    @keyframes fadeIn{to{opacity:1}}
    .chart-pt.halo{fill:var(--primary);opacity:.14}
    .chart-pt.ring{fill:var(--primary);stroke:var(--bg-card);stroke-width:2}
    .chart-cursor{stroke:var(--border-color);stroke-width:1;stroke-dasharray:3 4}
    .chart-tip{
      position:absolute;top:-4px;transform:translateX(-50%);
      background:var(--bg-card);border:1px solid var(--border-color);
      border-radius:9px;padding:5px 11px;box-shadow:var(--card-shadow-hover);pointer-events:none;z-index:5;
      display:flex;flex-direction:column;align-items:center;gap:1px;
    }
    .chart-tip span{font-size:10px;font-weight:700;color:var(--text-muted);letter-spacing:.08em}
    .chart-tip b{font-size:13px;font-weight:800;color:var(--text-primary);letter-spacing:-.02em;font-variant-numeric:tabular-nums}

    /* ── dépenses / types ── */
    .spend .lblc{display:flex;align-items:center;gap:8px;width:96px;flex:none}
    .spend .lblc i{width:9px;height:9px;border-radius:3px;flex:none}
    .spend .lblc span{font-size:11.5px;font-weight:600;color:var(--text-secondary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .spend .bar{flex:1}
    .spend .val{min-width:86px;text-align:right}

    /* ── géozones ── */
    .geo .gz-ic{width:30px;height:30px;border-radius:10px;display:grid;place-items:center;background:color-mix(in srgb,var(--acc-cyan) 11%,transparent);flex:none}
    .geo .gz-ic svg{width:15px;height:15px;stroke:var(--acc-cyan-ink)}
    .geo .cnt{font-size:16px;font-weight:800;letter-spacing:-.02em;flex:none}

    /* ── vide ── */
    .empty{display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;padding:30px 16px;color:var(--text-muted);flex:1}
    .empty svg{width:26px;height:26px;opacity:.6}
    .empty span{font-size:12px;font-weight:500}

    /* ════ OFFRE GPA (sans GPS) ════ */
    /* Répartition : colonne % et ligne TOTAL surlignée */
    .gpa-split.spend .val{min-width:92px}
    .gpa-split .row .pct{width:44px;flex:none;text-align:right;font-size:11px;font-weight:600;color:var(--text-muted)}
    .gpa-split .rows .row.tot{margin-top:6px;padding:8px 10px;border:0;border-radius:10px;background:color-mix(in srgb,var(--accent) 9%,transparent)}
    .gpa-split .rows .row.tot .lblc span{font-size:11px;font-weight:800;letter-spacing:.08em;text-transform:uppercase;color:var(--text-primary)}
    .gpa-split .rows .row.tot .val{font-size:13px;font-weight:800}
    .gpa-split .rows .row.tot .pct{color:var(--text-secondary);font-weight:700}

    /* Alertes et consommation : même hauteur que la répartition, défilement vertical interne */
    .gpa-list .scroll{
      flex:1 1 0;height:0;min-height:150px;overflow-y:auto;overflow-x:hidden;
      margin-right:-10px;padding-right:10px;
      scrollbar-width:thin;scrollbar-color:color-mix(in srgb,var(--text-muted) 40%,transparent) transparent;
    }
    .gpa-list .scroll::-webkit-scrollbar{width:6px}
    .gpa-list .scroll::-webkit-scrollbar-thumb{background:color-mix(in srgb,var(--text-muted) 40%,transparent);border-radius:6px}
    .gpa-list .rows .row{padding:7px 0}
    .gpa-list .rows .row:first-child{padding-top:2px}
    .gpa-list .plate{min-width:82px;text-align:center}
    .gpa-list .bar i{background:var(--acc-cyan)}
    .gpa-list .val{min-width:76px;text-align:right}
    .plate.xs{font-size:10px;padding:1px 5px;border-radius:5px;margin-right:6px;min-width:0}

    /* Évolution mensuelle : barres empilées en HTML (texte jamais déformé) */
    .gpa-evo .head-right{gap:12px}
    .mlegend{display:flex;align-items:center;gap:10px}
    .mlegend span{display:inline-flex;align-items:center;gap:5px;font-size:10.5px;font-weight:600;color:var(--text-secondary);white-space:nowrap}
    .mlegend i{width:8px;height:8px;border-radius:2.5px;flex:none}
    .mlegend i.hatch{background:repeating-linear-gradient(135deg,var(--text-muted) 0 1.5px,transparent 1.5px 3.5px);border:1px solid color-mix(in srgb,var(--text-muted) 55%,transparent)}
    .mchart{flex:1;min-height:170px;display:flex;position:relative}
    .m-axis{position:relative;width:40px;flex:none;margin:18px 0 20px}
    .m-axis span{position:absolute;right:8px;transform:translateY(50%);font-size:10px;font-weight:600;color:var(--text-muted);white-space:nowrap}
    .m-area{position:relative;flex:1;min-width:0;margin:18px 0 20px;display:flex;gap:6px;border-bottom:1px solid var(--border-color)}
    .m-grid{position:absolute;left:0;right:0;height:0;border-top:1px dashed var(--grid-line)}
    .m-col{position:relative;flex:1;min-width:0;display:flex;flex-direction:column;justify-content:flex-end;align-items:center;cursor:default}
    .m-col::before{content:"";position:absolute;inset:-18px -3px -20px;border-radius:8px;transition:background .15s}
    .m-col.on::before{background:color-mix(in srgb,var(--primary) 6%,transparent)}
    .m-bar{position:relative;width:min(34px,72%);display:flex;flex-direction:column-reverse;border-radius:5px 5px 2px 2px;overflow:hidden}
    .m-bar i{display:block;flex:0 0 0;min-height:0;border-top:1px solid var(--bg-card)}
    .m-col.partial .m-bar i{background-image:repeating-linear-gradient(135deg,rgba(255,255,255,.4) 0 2px,transparent 2px 5px)}
    .m-tot{position:absolute;left:50%;transform:translateX(-50%);font-size:10px;font-weight:700;color:var(--text-secondary);white-space:nowrap}
    .m-lbl{position:absolute;bottom:-17px;font-size:10px;font-weight:600;color:var(--text-muted);white-space:nowrap}
    .m-col.on .m-lbl,.m-col.on .m-tot{color:var(--text-primary)}
    .m-tip{
      position:absolute;top:-8px;transform:translateX(-50%);z-index:5;pointer-events:none;min-width:200px;
      background:var(--bg-card);border:1px solid var(--border-color);border-radius:10px;box-shadow:var(--card-shadow-hover);
      padding:8px 11px;display:flex;flex-direction:column;gap:3px;
    }
    .m-tip.al{transform:none}
    .m-tip.ar{transform:translateX(-100%)}
    .m-tip b{font-size:11.5px;font-weight:800;color:var(--text-primary);margin-bottom:2px}
    .m-tip span{display:flex;align-items:center;gap:6px;font-size:11px;color:var(--text-secondary)}
    .m-tip span i{width:8px;height:8px;border-radius:2.5px;flex:none}
    .m-tip em{margin-left:auto;padding-left:16px;font-style:normal;font-weight:700;color:var(--text-primary)}
    .m-tip .tt{border-top:1px solid var(--border-color);padding-top:4px;margin-top:2px;font-weight:800;color:var(--text-primary)}

    /* Top 5 : barre entretien + réparation */
    .gpa-top .rows .row{padding:9px 0}
    .gpa-top .plate{min-width:82px;text-align:center}
    .gpa-top .val{min-width:84px;text-align:right}
    /* La légende passe sous le titre quand la carte est trop étroite (titre jamais tronqué) */
    .gpa-top .card-head{flex-wrap:wrap;row-gap:4px}
    .gpa-top .head-right{gap:10px}
    .bar.stack i{border-radius:0}

    /* Dernières interventions : tableau compact, largeur fixe, jamais d'ascenseur horizontal */
    .tbl-wrap{max-height:344px;overflow-y:auto;overflow-x:hidden;scrollbar-width:thin}
    .itbl{width:100%;border-collapse:separate;border-spacing:0;table-layout:fixed;font-size:12px}
    .itbl th.c-date{width:96px}.itbl th.c-veh{width:116px}.itbl th.c-type{width:112px}
    .itbl th.c-sup{width:19%}.itbl th.c-km{width:118px}.itbl th.c-cost{width:112px}
    .itbl thead th{
      position:sticky;top:0;z-index:1;background:var(--bg-card);text-align:left;
      font-size:10px;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:var(--text-muted);
      padding:0 10px 8px;border-bottom:1px solid var(--border-color);white-space:nowrap;overflow:hidden;text-overflow:ellipsis;
    }
    .itbl tbody td{padding:7px 10px;border-bottom:1px solid var(--border-color);color:var(--text-secondary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .itbl tbody tr:last-child td{border-bottom:0}
    .itbl tbody tr:hover td{background:color-mix(in srgb,var(--primary) 4%,transparent)}
    .itbl thead th.r,.itbl tbody td.r{text-align:right}
    .itbl tbody td.b{font-weight:700;color:var(--text-primary)}
    .kind{display:inline-block;font-size:10.5px;font-weight:700;padding:2.5px 9px;border-radius:999px;color:var(--ok-600);background:color-mix(in srgb,var(--acc-green) 11%,transparent)}
    .kind.rep{color:var(--warn-600);background:color-mix(in srgb,var(--acc-amber) 12%,transparent)}

    /* Accès rapides */
    .gpa-quick .card-head{margin-bottom:12px}
    .quick{display:grid;grid-template-columns:repeat(var(--qn,6),minmax(0,1fr));gap:10px}
    .qk{
      display:flex;align-items:center;gap:10px;min-width:0;padding:10px 12px;border-radius:13px;
      border:1px solid var(--border-color);background:var(--bg-secondary);color:inherit;text-decoration:none;
      transition:transform .18s,box-shadow .18s,border-color .18s;
    }
    .qk:hover{transform:translateY(-1px);box-shadow:var(--card-shadow);border-color:color-mix(in srgb,var(--accent) 45%,var(--border-color))}
    .qk-ic{width:32px;height:32px;border-radius:10px;flex:none;display:grid;place-items:center;background:color-mix(in srgb,var(--accent) 13%,transparent)}
    .qk-ic svg{width:16px;height:16px;stroke:var(--accent-ink)}
    .qk-t{flex:1;min-width:0;display:flex;flex-direction:column}
    .qk-t b{font-size:12.5px;font-weight:700;color:var(--text-primary);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .qk-t small{font-size:11px;color:var(--text-muted);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
    .qk-go{width:14px;height:14px;stroke:var(--text-muted);flex:none;opacity:.6}

    /* ════ RESPONSIVE ════ */
    @media (max-width:1280px){
      .kpis.k5{grid-template-columns:repeat(3,minmax(0,1fr))}
      .card.gpa-split,.card.gpa-top{grid-column:span 12}
      .quick{grid-template-columns:repeat(3,minmax(0,1fr))}
      .shell{padding:16px 16px 48px}
      .span-8{grid-column:span 12}
      .card{grid-column:span 6}
      .span-6{grid-column:span 6}
      .span-12{grid-column:span 12}
      .kpis{grid-template-columns:repeat(3,1fr);row-gap:0}
      .kpi{padding:14px 22px}
      .kpi:nth-child(3n+1){border-left:none;padding-left:2px}
      .kpi:nth-child(-n+3){padding-top:2px}
      .kpi:nth-child(n+4){border-top:1px solid var(--mast-hair)}
      .drivers{grid-template-columns:repeat(2,1fr)}
    }
    @media (max-width:1024px){
      .card,.span-6{grid-column:span 6}
    }
    @media (max-width:760px){
      .card,.span-6,.span-8{grid-column:span 12}
      .drivers{grid-template-columns:1fr}
      .mast{padding:18px 16px}
      .mast-title{margin:12px 0 14px}
      .toolbar{gap:12px}
      .range{margin-left:0;width:100%}
      .range input[type="date"]{flex:1;min-width:0}
      .rfield{flex:1}
      .tb-sep{display:none}
      .kpis,.kpis.k5{grid-template-columns:repeat(2,minmax(0,1fr))}
      .quick{grid-template-columns:repeat(2,minmax(0,1fr))}
      .itbl .c-sup,.itbl .c-km{display:none}
      .gpa-evo .mlegend{display:none}
      .kpi{padding:13px 16px}
      .kpi:nth-child(3n+1){border-left:1px solid var(--mast-hair);padding-left:16px}
      .kpi:nth-child(2n+1){border-left:none;padding-left:2px}
      .kpi:nth-child(-n+3){padding-top:13px}
      .kpi:nth-child(-n+2){padding-top:2px;border-top:none}
      .kpi:nth-child(3){border-top:1px solid var(--mast-hair)}
      .donut-flex,.gauge-flex{flex-direction:column;align-items:stretch}
      .donut,.gauge{align-self:center}
      .fleet-map{height:300px}
    }
  `]
})
export class DashboardComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('fleetMap') mapRef?: ElementRef<HTMLDivElement>;
  private fleetMap?: L.Map;
  private fleetMarkers?: L.LayerGroup;
  private destroy$ = new Subject<void>();
  company: Company | null = null;
  // Le dashboard s'ouvre sur l'année en cours ; les opérateurs peuvent basculer
  // sur une période nommée ou une plage personnalisée Du/Au (selectedPeriod='custom')
  // — la plage est désormais transmise au backend (from/to sur /dashboard/all).
  selectedPeriod = 'year'; fromDate = ''; toDate = '';
  isConnected = false;
  todayLabel = '';
  Math = Math;

  periodsFr = [
    { value: 'today', label: "Aujourd'hui" },
    { value: 'yesterday', label: 'Hier' },
    { value: 'week', label: 'Semaine' },
    { value: 'month', label: 'Mois' },
    { value: 'year', label: 'Année' },
  ];

  motionData = {stationary:0,ignitionOn:0,moving:0,movingIgnition:0,lbs:0,wifi:0,noState:0,noCoords:0};
  healthData = {healthy:0,attention:0,unhealthy:0};
  geofences:{name:string;color:string;count:number}[] = [];
  topUnits:{name:string;color:string;mileage:number}[] = [];
  maxMileage = 1;
  totalFuelConsumed = 0;
  fuelEstimated = false;
  costTrend: number|null = null;
  distanceTrend: number|null = null;
  periodDistance = 0;
  typeBreakdown:{type:string;count:number;km:number;color:string}[]=[];
  maxTypeCount=1;
  // graphique carburant : géométrie 660×230, X∈[40,620], Y∈[66,190]
  cPath=''; cAreaPath='';
  cLabels:string[]=[]; cVals:number[]=[]; cPoints:{x:number;y:number}[]=[]; cIdx=-1;
  yTicks:{y:number;label:string}[]=[];
  xTicks:{x:number;label:string}[]=[];
  hSeg=-1;
  fuelCost=0; maintenanceCost=0; repairCost=0; otherCost=0; acquisitionCost=0; totalCost=0;
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
  maxExp=1;

  get totalMotion():number{return this.motionData.stationary+this.motionData.ignitionOn+this.motionData.movingIgnition+this.motionData.noState+this.motionData.noCoords;}
  get totalHealth():number{return this.healthData.healthy+this.healthData.attention+this.healthData.unhealthy;}
  get maxHealth():number{return Math.max(this.healthData.healthy,this.healthData.attention,this.healthData.unhealthy,1);}
  get hPct():number{return this.totalHealth?Math.round((this.healthData.healthy/this.totalHealth)*100):0;}
  get hColor():string{const p=this.hPct;return p>=80?'#059669':p>=50?'#d97706':'#dc2626';}
  get movingPct():number{return this.totalMotion?Math.round((this.motionData.movingIgnition/this.totalMotion)*100):0;}
  get stoppedPct():number{return this.totalMotion?Math.round((this.motionData.stationary/this.totalMotion)*100):0;}
  get activeDrivers():number{return this.drivers.filter(d=>d.active).length;}
  /** Coût compact : au-delà du million, "1,17 M" — la tuile absorbe les gros montants sans casse. */
  get costValue():string{ return this.compact(this.dCost); }
  compact(v:number):string{
    if(v>=1_000_000) return (v/1_000_000).toLocaleString('fr-FR',{minimumFractionDigits:2,maximumFractionDigits:2})+' M';
    return Math.round(v).toLocaleString('fr-FR');
  }

  // ── Offre GPA (sans GPS) : /dashboard/gpa + consommation réelle des pleins ──
  gpa:GpaDashboard|null=null;
  gpaErreur=false;
  private gpaSeq=0;
  private fuelSeq=0;
  realFuel:any=null;
  consoRows:{plate:string;name:string;c:number|null;pct:number;liters:number;km:number|null}[]=[];
  gpaSplit:{name:string;color:string;value:number;pct:number}[]=[];
  gpaMonths:any[]=[];
  gpaTicks:{pct:number;label:string}[]=[];
  gpaTotal12=0; gpaPartiel=false; mHover=-1;
  /** Mêmes teintes que la carte Dépenses de l'offre GPS. */
  readonly gpaSeries=[
    {key:'fuel',name:'Carburant',color:'#4f46e5'},
    {key:'maintenance',name:'Entretiens',color:'#059669'},
    {key:'repair',name:'Réparations',color:'#d97706'},
    {key:'other',name:'Autres',color:'#94a3b8'},
  ] as const;
  accesRapides:{label:string;sub:string;route:string;acc:string;d:string[]}[]=[];

  scP=0;unP=0;fuP=0;alertsP=0;trP=0;drP=0;
  get pScores(){return this.drivingScores.slice(this.scP*5,this.scP*5+5);}
  get pUnits(){return this.topUnits.slice(this.unP*5,this.unP*5+5);}
  get pFuel(){return this.vehicleFuelStats.slice(this.fuP*5,this.fuP*5+5);}
  get pagedAlerts(){return this.alerts.slice(this.alertsP*5,this.alertsP*5+5);}
  get pTrips(){return this.recentTrips.slice(this.trP*5,this.trP*5+5);}
  get pDrv(){return this.drivers.slice(this.drP*8,this.drP*8+8);}

  constructor(private router:Router,private apiService:ApiService,private signalrService:SignalRService,private cdr:ChangeDetectorRef,private userPrefs:UserPreferencesService,private permissionService:PermissionService){}

  get currencyCode():string{return this.userPrefs.current.currency;}
  get currencySym():string{return UserPreferencesService.currencySymbol(this.currencyCode);}
  /** GPS modules available? false for the "Gestion sans GPS" tier (moduleMonitoring off) → hide GPS-only widgets. */
  get hasGps():boolean{return this.permissionService.hasModuleAccess('monitoring');}

  // Échéances à venir (assurance / vignette / visite technique) — réutilise /documents/alerts.
  deadlines: VehicleExpiryDto[] = [];
  get deadlinesOverdue():number{ return this.deadlines.filter(d=>(d.daysUntilExpiry??0)<0).length; }
  docLabel(t:string):string{ return ({insurance:'Assurance',tax:'Vignette',technical_inspection:'Visite technique'} as Record<string,string>)[t] || t; }
  dueCls(d:VehicleExpiryDto):string{ const n=d.daysUntilExpiry??0; return n<0?'red':n<=30?'amber':'green'; }
  daysLabel(d:VehicleExpiryDto):string{ const n=d.daysUntilExpiry??0; return n<0?((-n)+' j retard'):(n+' j'); }
  loadDeadlines(){
    this.apiService.getExpiryAlerts(60).pipe(takeUntil(this.destroy$)).subscribe({
      next:(list)=>{ this.deadlines=(list||[]).sort((a,b)=>(a.daysUntilExpiry??0)-(b.daysUntilExpiry??0)).slice(0,8); this.cdr.detectChanges(); },
      error:()=>{}
    });
  }

  ngOnInit(){
    if(!this.apiService.isAuthenticated()){this.router.navigate(['/login']);return;}
    const u=this.apiService.getCurrentUserSync();
    if(u) this.company={id:u.companyId.toString(),name:u.companyName,type:'transport',subscriptionId:'1'} as Company;
    const today=new Date();
    this.todayLabel=today.toLocaleDateString('fr-FR',{weekday:'long',day:'numeric',month:'long',year:'numeric'});
    this.setRangeFor(this.selectedPeriod);
    this.loadAll();
    if(this.hasGps) this.loadDeadlines();
    else this.construireAccesRapides();
    this.wire();
  }

  ngAfterViewInit(){ this.initFleetMap(); }

  ngOnDestroy(){
    this.destroy$.next();this.destroy$.complete();
    this.fleetMap?.remove();
  }

  private initFleetMap(){
    const el=this.mapRef?.nativeElement;
    if(!el||this.fleetMap)return;
    this.fleetMap=L.map(el,{zoomControl:true,attributionControl:false}).setView([34.0,9.0],5);
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png',{maxZoom:19}).addTo(this.fleetMap); // Carto tatoue « API KEY REQUIRED » depuis 08/2026
    this.fleetMarkers=L.layerGroup().addTo(this.fleetMap);
    setTimeout(()=>this.fleetMap?.invalidateSize(),400);
    this.loadMapPositions();
  }

  private loadMapPositions(){
    if(!this.fleetMap||!this.fleetMarkers)return;
    this.apiService.getVehiclesWithPositions().pipe(takeUntil(this.destroy$)).subscribe({
      next:(vehicles:any[])=>{
        if(!this.fleetMarkers||!this.fleetMap)return;
        this.fleetMarkers.clearLayers();
        const pts:L.LatLngTuple[]=[];
        for(const v of vehicles||[]){
          const p=v?.lastPosition; if(!p)continue;
          const lat=Number(p.latitude),lon=Number(p.longitude);
          if((!lat&&!lon)||Math.abs(lat)>90||Math.abs(lon)>180)continue;
          const online=v.isOnline!==false;
          const moving=v.stats?.isMoving===true;
          const ign=p.ignitionOn===true;
          const color=!online?'#cbd5e1':moving?'#10b981':ign?'#f59e0b':'#64748b';
          const label=!online?'Hors ligne':moving?'En circulation':ign?'Moteur allumé':'À l\'arrêt';
          const mk=L.circleMarker([lat,lon],{radius:6,weight:2,color:'#ffffff',fillColor:color,fillOpacity:1});
          mk.bindPopup(`<b>${v.plate||v.name||'Véhicule'}</b><br>${label}`);
          this.fleetMarkers.addLayer(mk);
          pts.push([lat,lon]);
        }
        if(pts.length){try{this.fleetMap.fitBounds(L.latLngBounds(pts).pad(0.2),{maxZoom:13});}catch{}}
        this.cdr.detectChanges();
      },
      error:()=>{}
    });
  }

  /** Compte à rebours des KPI en UNE seule boucle rAF (un detectChanges par
   *  frame, ~550 ms), et UNIQUEMENT au premier chargement — les refresh 30 s
   *  et les alertes SignalR assignent directement (pas de tempête de rendus). */
  private firstLoad=true;
  private animateKpis(){
    const to={dVehicles:this.totalMotion,dMoving:this.motionData.movingIgnition,dStopped:this.motionData.stationary,dAlerts:this.alerts.length,dFuel:this.totalFuelConsumed,dCost:this.totalCost};
    if(!this.firstLoad){Object.assign(this,to);return;}
    this.firstLoad=false;
    const keys=Object.keys(to) as (keyof typeof to)[];
    const t0=performance.now(),dur=550;
    const step=(now:number)=>{
      const p=Math.min((now-t0)/dur,1),e=1-Math.pow(1-p,4);
      for(const k of keys)(this as any)[k]=Math.round(to[k]*e);
      this.cdr.detectChanges();
      if(p<1)requestAnimationFrame(step);
    };
    requestAnimationFrame(step);
  }

  private rebuild(){
    const tot=this.totalMotion||1;
    this.animateKpis();

    const segs=[
      {name:'En circulation',color:'#059669',value:this.motionData.movingIgnition},
      {name:'Moteur allumé',color:'#d97706',value:this.motionData.ignitionOn},
      {name:'À l\'arrêt',color:'#64748b',value:this.motionData.stationary},
      {name:'Maintenance',color:'#4f46e5',value:this.motionData.noState},
      {name:'Sans GPS',color:'#cbd5e1',value:this.motionData.noCoords},
    ];
    this.fleetSegs=segs.map(s=>({...s,pct:tot?Math.round((s.value/tot)*100):0}));
    // Espace de ~2 px entre les segments du donut (règle "surface gap" dataviz)
    // — seulement s'il y a ≥ 2 segments visibles.
    const visible=segs.filter(s=>s.value>0);
    const gap=visible.length>1?4:0;
    let cum=0;
    this.donutSegs=visible.map(s=>{
      const da2=Math.max((s.value/tot)*this.circ62-gap,1);
      const offset2=-(cum/tot)*this.circ62;
      cum+=s.value;
      return{...s,da2,offset2};
    });
    this.expItems=[
      {name:'Carburant',color:'#4f46e5',value:this.fuelCost},
      {name:'Entretien',color:'#059669',value:this.maintenanceCost},
      {name:'Réparation',color:'#d97706',value:this.repairCost},
      // Mensualités de crédit/leasing échues + apports/achats : calculés par le
      // serveur (AcquisitionSchedule), mêmes règles que l'écran Dépenses.
      {name:'Achats véhicule',color:'#0ea5e9',value:this.acquisitionCost},
      {name:'Autres',color:'#94a3b8',value:this.otherCost},
    ];
    this.maxExp=Math.max(this.fuelCost,this.maintenanceCost,this.repairCost,this.acquisitionCost,this.otherCost,1);
    this.hItems=[
      {name:'Bon état',color:'#059669',value:this.healthData.healthy},
      {name:'Attention',color:'#d97706',value:this.healthData.attention},
      {name:'Mauvais',color:'#dc2626',value:this.healthData.unhealthy},
    ];
  }

  /** Courbe lissée (Catmull-Rom → Bézier) dans la géométrie 660×230. */
  private buildChart(values:number[],days:string[]){
    this.cVals=values||[];
    const n=this.cVals.length;
    if(!n){this.cPath='';this.cAreaPath='';this.cPoints=[];this.yTicks=[];this.xTicks=[];return;}
    const mx=Math.max(...this.cVals,0.1);
    const X0=40,X1=620,Y0=66,Y1=190;
    this.cPoints=this.cVals.map((v,i)=>({x:n>1?X0+(i/(n-1))*(X1-X0):330,y:Y1-(v/mx)*(Y1-Y0)}));
    this.cPath=this.smoothPath(this.cPoints);
    this.cAreaPath=this.cPath?`${this.cPath} L${this.cPoints[n-1].x.toFixed(1)},190 L${this.cPoints[0].x.toFixed(1)},190 Z`:'';
    const fmtK=(v:number)=>v>=1000?((v/1000).toLocaleString('fr-FR',{maximumFractionDigits:1})+'k'):String(Math.round(v));
    this.yTicks=[
      {y:66,label:fmtK(mx)},
      {y:97,label:fmtK(mx*0.75)},
      {y:128,label:fmtK(mx*0.5)},
      {y:159,label:fmtK(mx*0.25)},
    ];
    const toLbl=(d:string)=>{const p=(d||'').split('-');return p.length>=3?`${p[2]}/${p[1]}`:d;};
    this.cLabels=(days||[]).map(toLbl);
    const step=Math.max(1,Math.ceil(n/7));
    this.xTicks=this.cPoints
      .map((p,i)=>({x:p.x,label:this.cLabels[i]||'',i}))
      .filter(t=>t.i%step===0||t.i===n-1)
      .map(t=>({x:t.x,label:t.label}));
  }

  private smoothPath(pts:{x:number;y:number}[]):string{
    if(pts.length<2)return '';
    let d=`M${pts[0].x.toFixed(1)},${pts[0].y.toFixed(1)}`;
    for(let i=0;i<pts.length-1;i++){
      const p0=pts[Math.max(0,i-1)],p1=pts[i],p2=pts[i+1],p3=pts[Math.min(pts.length-1,i+2)];
      const c1x=p1.x+(p2.x-p0.x)/6,c1y=p1.y+(p2.y-p0.y)/6;
      const c2x=p2.x-(p3.x-p1.x)/6,c2y=p2.y-(p3.y-p1.y)/6;
      d+=` C${c1x.toFixed(1)},${c1y.toFixed(1)} ${c2x.toFixed(1)},${c2y.toFixed(1)} ${p2.x.toFixed(1)},${p2.y.toFixed(1)}`;
    }
    return d;
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
        setTimeout(()=>{this.refreshPending=false;this.loadAll();this.loadMapPositions();},30000);
      }
    });
  }

  loadAll(){
    // Offre sans GPS : tableau de bord de gestion (/dashboard/gpa) et carburant
    // réel des pleins saisis ; les deux suivent la période affichée (Du/Au).
    if(!this.hasGps){ this.loadGpa(); this.loadFuelReal(); }
    const custom=this.selectedPeriod==='custom'&&!!this.fromDate&&!!this.toDate;
    this.apiService.getDashboardAll(custom?'custom':this.selectedPeriod,custom?this.fromDate:undefined,custom?this.toDate:undefined)
      .pipe(takeUntil(this.destroy$)).subscribe({
      next:(d:any)=>{
        if(d.vehicleStatus){const v=d.vehicleStatus;this.motionData={stationary:v.stopped??0,ignitionOn:v.ignitionOn??0,moving:0,movingIgnition:v.moving??0,lbs:0,wifi:0,noState:v.maintenance??0,noCoords:v.noGps??0};}
        if(d.expenses){const e=d.expenses;this.fuelCost=e.fuelCost??0;this.maintenanceCost=e.maintenanceCost??0;this.repairCost=e.repairCost??0;this.otherCost=e.otherCost??0;this.acquisitionCost=e.acquisitionCost??0;this.totalCost=e.totalCost??(this.fuelCost+this.maintenanceCost+this.repairCost+this.acquisitionCost+this.otherCost);}
        if(d.drivingScores?.length)this.drivingScores=d.drivingScores.map((s:any)=>({plate:s.plate,score:s.score})).sort((a:any,b:any)=>b.score-a.score);
        if(d.healthData)this.healthData={healthy:d.healthData.healthy??0,attention:d.healthData.attention??0,unhealthy:d.healthData.unhealthy??0};
        if(d.topUnits?.length){this.topUnits=d.topUnits.map((u:any)=>({name:u.name,color:u.color,mileage:Math.round(u.mileage??0)})).sort((a:any,b:any)=>b.mileage-a.mileage);this.maxMileage=Math.max(...this.topUnits.map(u=>u.mileage),1);}
        if(d.geofences)this.geofences=d.geofences.map((g:any)=>({name:g.name,color:g.color,count:g.count??0}));
        if(d.alerts)this.alerts=d.alerts.map((a:any)=>({message:a.message,severity:a.severity,time:a.time}));
        if(d.recentTrips)this.recentTrips=d.recentTrips.map((t:any)=>({plate:t.plate,distance:t.distance,duration:t.duration,date:t.date}));
        if(d.drivers)this.drivers=d.drivers.map((dr:any)=>({name:dr.name,initials:dr.initials,vehicle:dr.vehicle,active:dr.active}));
        if(d.fuelConsumption){
          const fc=d.fuelConsumption;
          this.fuelEstimated=fc.estimated===true;
          if(fc.vehicleStats?.length){this.vehicleFuelStats=fc.vehicleStats.map((v:any)=>({plate:v.plate||'Inconnu',consumption:Number(v.consumption)||0,totalLiters:Math.round(Number(v.totalLiters)||0),totalKm:Math.round(Number(v.totalKm)||0)}));this.maxFuelConsumption=Math.max(...this.vehicleFuelStats.map(v=>v.consumption),1);}
          if(fc.fleetTotalLiters>0)this.totalFuelConsumed=Math.round(fc.fleetTotalLiters);
          this.buildChart(fc.chartValues||[],fc.chartDays||[]);
        }
        if(d.trends){this.costTrend=d.trends.cost??null;this.distanceTrend=d.trends.distance??null;}
        if(d.periodDistance!=null)this.periodDistance=d.periodDistance;
        if(d.typeBreakdown){this.typeBreakdown=d.typeBreakdown;this.maxTypeCount=Math.max(...this.typeBreakdown.map(t=>t.count),1);}
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
    // X∈[40,620] dans un viewBox de 660 — on retranche les marges avant de mapper.
    const xView=(e.clientX-r.left)/r.width*660;
    const t=Math.max(0,Math.min(1,(xView-40)/580));
    this.cIdx=Math.round(t*(this.cPoints.length-1));
  }

  donutIdx(name:string):number{return this.donutSegs.findIndex(s=>s.name===name);}

  absPct(v:number):string{return Math.abs(v).toFixed(1);}

  scoreC(s:number):string{return s>=80?'#059669':s>=60?'#d97706':'#dc2626';}
  fuelC(c:number):string{return c<=6?'#059669':c<=8?'#d97706':'#dc2626';}
  tintCls(level:number):string{return level===0?'tint-ok':level===1?'tint-warn':'tint-bad';}

  // ── Chargements offre GPA (sans GPS) ──
  // Un compteur par requête : un clic rapide sur plusieurs périodes n'affiche
  // jamais la réponse d'une période précédente arrivée en retard.
  private loadGpa(){
    if(!this.fromDate||!this.toDate) return;
    const seq=++this.gpaSeq;
    this.apiService.getGpaDashboard(this.fromDate,this.toDate).pipe(takeUntil(this.destroy$)).subscribe({
      next:(d)=>{
        if(seq!==this.gpaSeq) return;
        this.gpa=d; this.gpaErreur=false; this.mHover=-1;
        this.construireGpa();
        this.cdr.detectChanges();
      },
      error:()=>{
        if(seq!==this.gpaSeq) return;
        this.gpa=null; this.gpaErreur=true;
        this.cdr.detectChanges();
      }
    });
  }

  private loadFuelReal(){
    const seq=++this.fuelSeq;
    this.apiService.getRealFuelConsumption(this.fromDate||undefined,this.toDate||undefined)
      .pipe(takeUntil(this.destroy$)).subscribe({
      next:(r:any)=>{
        if(seq!==this.fuelSeq) return;
        this.realFuel=r||{totalLiters:0,vehicles:[]};
        // Consommation par véhicule : litres achetés ÷ km mesurés (même calcul
        // que l'écran Carburant). La plus forte en tête ; sans relevé compteur
        // suffisant, le véhicule reste listé avec « — ».
        const vs:any[]=r?.vehicles||[];
        const lus=vs.map(v=>v.consumptionPer100Km!=null?Number(v.consumptionPer100Km):null);
        const max=Math.max(0,...lus.map(c=>c??0));
        this.consoRows=vs.map((v,i)=>{
          const c=lus[i];
          return {plate:v.plate||v.vehicleName||'—',name:v.vehicleName||'',c,pct:c!=null&&max>0?(c/max)*100:0,
            liters:Number(v.totalLiters)||0,km:v.distanceKm!=null?Number(v.distanceKm):null};
        }).sort((a,b)=>(b.c??-1)-(a.c??-1));
        this.cdr.detectChanges();
      },
      error:()=>{ if(seq!==this.fuelSeq) return; this.realFuel={totalLiters:0,vehicles:[]}; this.consoRows=[]; this.cdr.detectChanges(); }
    });
  }

  private static readonly MOIS_COURTS=['janv.','févr.','mars','avr.','mai','juin','juil.','août','sept.','oct.','nov.','déc.'];
  private static readonly MOIS_LONGS=['Janvier','Février','Mars','Avril','Mai','Juin','Juillet','Août','Septembre','Octobre','Novembre','Décembre'];

  /** Prépare la répartition et le graphique mensuel à partir de la réponse /dashboard/gpa. */
  private construireGpa(){
    const d=this.gpa; if(!d) return;
    const val=(o:any,k:string)=>Number(o?.[k])||0;
    const tot=d.costs.total;
    this.gpaSplit=this.gpaSeries.map(s=>{
      const v=val(d.costs,s.key);
      return {name:s.name,color:s.color,value:v,pct:tot>0?Math.max(0,Math.min(100,(v/tot)*100)):0};
    });

    const ms=d.monthly||[];
    // Hauteur des barres : somme des parts positives (un remboursement d'assurance
    // supérieur aux « autres » ne creuse pas la barre ; le total affiché reste exact).
    const hauteur=(m:any)=>this.gpaSeries.reduce((a,s)=>a+Math.max(0,val(m,s.key)),0);
    const {top,step}=this.echelle(Math.max(0,...ms.map(hauteur)));
    this.gpaTicks=top>0?Array.from({length:Math.round(top/step)+1},(_,i)=>({pct:(i*step/top)*100,label:this.axeLabel(i*step)})):[];
    this.gpaMonths=ms.map((m,i)=>{
      const detail=this.gpaSeries.map(s=>({name:s.name,color:s.color,v:val(m,s.key)}));
      const court=DashboardComponent.MOIS_COURTS[m.month-1]||m.label;
      return {
        ...m,
        detail,
        segs:detail.filter(s=>s.v>0),
        h:top>0?(hauteur(m)/top)*100:0,
        lbl:(i===0||m.month===1)?`${court} ${String(m.year).slice(-2)}`:court,
        full:`${DashboardComponent.MOIS_LONGS[m.month-1]||m.label} ${m.year}${m.isPartial?' (en cours)':''}`,
        totLabel:this.nombre(m.total),
      };
    });
    this.gpaTotal12=ms.reduce((a,m)=>a+(Number(m.total)||0),0);
    this.gpaPartiel=ms.some(m=>m.isPartial);
  }

  /** Graduation « ronde » (1, 2, 2,5 ou 5 × 10ⁿ) avec 4 à 5 intervalles. */
  private echelle(max:number):{top:number;step:number}{
    if(!(max>0)) return {top:0,step:0};
    const brut=max/4, mag=Math.pow(10,Math.floor(Math.log10(brut))), n=brut/mag;
    const step=(n<=1?1:n<=2?2:n<=2.5?2.5:n<=5?5:10)*mag;
    return {top:Math.ceil(max/step-1e-9)*step,step};
  }
  private axeLabel(v:number):string{
    return v>=100000?(v/1000).toLocaleString('fr-FR',{maximumFractionDigits:0})+' k':Math.round(v).toLocaleString('fr-FR');
  }
  tipCls(i:number):string{ const n=this.gpaMonths.length; return i<2?'al':i>=n-2?'ar':''; }

  // Formats français (le pipe number d'Angular sort en en-US faute de locale fr enregistrée).
  private montant2(v:number|null|undefined):string{ return (Number(v)||0).toLocaleString('fr-FR',{minimumFractionDigits:2,maximumFractionDigits:2}); }
  entier(v:number|null|undefined):string{ return this.montant2(v).split(',')[0]; }
  decimales(v:number|null|undefined):string{ const p=this.montant2(v).split(','); return p.length>1?','+p[1]:''; }
  nombre(v:number|null|undefined):string{ return Math.round(Number(v)||0).toLocaleString('fr-FR'); }
  l100(v:number|null|undefined):string{ return (Number(v)||0).toLocaleString('fr-FR',{minimumFractionDigits:2,maximumFractionDigits:2}); }
  pctLabel(p:number):string{ return p.toLocaleString('fr-FR',{maximumFractionDigits:1})+' %'; }
  joursLabel(n:number):string{ return n<0?`${-n} j retard`:n===0?"aujourd'hui":`${n} j`; }

  /** « 11 achetés · 1 financé » — le coût du parc ne dépend pas de la période. */
  get achatsSousTitre():string{
    const a=this.gpa?.acquisition; if(!a) return '';
    const parts:string[]=[];
    if(a.purchasedVehicles>0) parts.push(`${a.purchasedVehicles} acheté${a.purchasedVehicles>1?'s':''}`);
    if(a.financedVehicles>0) parts.push(`${a.financedVehicles} financé${a.financedVehicles>1?'s':''}`);
    return parts.length?parts.join(' · ')+' · tout le parc':'aucun achat renseigné';
  }
  private get gpaTopMax():number{ return Math.max(0,...(this.gpa?.top5||[]).map(v=>Number(v.total)||0)); }
  pctTop(v:number):number{ const m=this.gpaTopMax; return m>0?Math.max(0,(Number(v)||0)/m*100):0; }

  alerteInfoBulle(a:GpaDashboardAlert):string{
    const veh=[a.plate,a.vehicleName].filter(Boolean).join(' · ');
    // Minuit UTC côté serveur : afficher le jour UTC, sinon la veille à l'ouest de Greenwich.
    const date=a.date?new Date(a.date).toLocaleDateString('fr-FR',{timeZone:'UTC'}):'';
    return [a.title,veh,a.detail,date&&`Échéance : ${date}`].filter(Boolean).join('\n');
  }
  consoInfoBulle(v:{name:string;liters:number;km:number|null;c:number|null}):string{
    const l=`${this.l100(v.liters)} L achetés`;
    return [v.name,l,v.km!=null?`${this.nombre(v.km)} km mesurés`:'Relevés compteur insuffisants pour calculer la consommation'].filter(Boolean).join('\n');
  }
  top5InfoBulle(v:GpaDashboardTopVehicle):string{
    return [v.vehicleName,`Entretien : ${this.userPrefs.formatCurrency(v.maintenance,2)}`,`Réparation : ${this.userPrefs.formatCurrency(v.repair,2)}`].filter(Boolean).join('\n');
  }
  /** La colonne Type dit déjà « Entretien » ou « Réparation » : la description
   *  ne reprend que le sous-type saisi (« Réparation · Mécanique » → « Mécanique »). */
  interventionLibelle(r:GpaDashboardIntervention):string{
    const t=(r.typeLabel||''), i=t.indexOf('·');
    const sous=i>=0?t.slice(i+1).trim():'';
    const d=(r.description||'').trim();
    if(sous&&d&&d.toLowerCase()!==sous.toLowerCase()) return `${sous} — ${d}`;
    return d||sous||'—';
  }

  /** Raccourcis de l'offre GPA : chaque tuile suit le même droit que son entrée de menu. */
  private construireAccesRapides(){
    const tuiles:{label:string;sub:string;route:string;module:ModuleKey;acc:string;d:string[]}[]=[
      {label:'Entretiens',sub:'Plans et échéances',route:'/entretien-programmable',module:'maintenance',acc:'acc-green',
        d:['M14.7 6.3a4 4 0 0 0-5.4 5.2L3.8 17a1.8 1.8 0 0 0 2.6 2.6l5.5-5.5a4 4 0 0 0 5.2-5.4l-2.5 2.5-2.1-.5-.5-2.1z']},
      {label:'Réparations',sub:'Suivi des réparations',route:'/reparations',module:'maintenance',acc:'acc-amber',
        d:['M12 3l8 4.5v9L12 21l-8-4.5v-9z','M12 12l8-4.5M12 12v9M12 12 4 7.5']},
      {label:'Carburant',sub:'Pleins et consommation',route:'/carburant',module:'carburant',acc:'acc-cyan',
        d:['M5 20V6a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v14M3.5 20h11','M15 9h2.2a1.8 1.8 0 0 1 1.8 1.8V17a1.5 1.5 0 0 0 3 0v-6.4L19.5 8']},
      {label:'Dépenses',sub:'Toutes les dépenses',route:'/depenses',module:'costs',acc:'acc-indigo',
        d:['M12 2v20M17 6.5H9.8a2.8 2.8 0 0 0 0 5.6h4.4a2.8 2.8 0 0 1 0 5.6H6.5']},
      {label:'Rapports',sub:'Analyses et exports',route:'/reports',module:'reports',acc:'acc-red',
        d:['M4 20V10M10 20V4M16 20v-7M22 20H2']},
      {label:'Paramètres',sub:'Configuration du compte',route:'/settings',module:'settings',acc:'acc-slate',
        d:['M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z','M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z']},
    ];
    this.accesRapides=tuiles.filter(t=>this.permissionService.hasModuleAccess(t.module));
  }

  private readonly accCycle=['acc-indigo','acc-cyan','acc-green','acc-amber','acc-slate'];
  accFor(i:number):string{return this.accCycle[i%this.accCycle.length];}

  /** Reflète la période nommée dans les champs Du/Au (miroir de GetPeriodRange côté API). */
  private setRangeFor(p:string){
    const today=new Date();
    // Date LOCALE : toISOString() passe en UTC et, à UTC+1, le 1er janvier minuit
    // devenait le 31 décembre dans le champ « Du ».
    const iso=(d:Date)=>`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    switch(p){
      case 'today': this.fromDate=iso(today); this.toDate=iso(today); break;
      case 'yesterday': {const y=new Date(today.getTime()-86400000); this.fromDate=iso(y); this.toDate=iso(y); break;}
      case 'week': {const s=new Date(today.getTime()-today.getDay()*86400000); this.fromDate=iso(s); this.toDate=iso(today); break;}
      case 'month': this.fromDate=iso(new Date(today.getFullYear(),today.getMonth(),1)); this.toDate=iso(today); break;
      case 'year': this.fromDate=iso(new Date(today.getFullYear(),0,1)); this.toDate=iso(today); break;
    }
  }

  private resetPagers(){this.scP=0;this.unP=0;this.fuP=0;this.alertsP=0;this.trP=0;this.drP=0;}

  onPeriodClick(p:string){
    this.selectedPeriod=p;
    this.setRangeFor(p);
    this.resetPagers();
    this.loadAll();
  }

  applyCustom(){
    if(!this.fromDate||!this.toDate)return;
    if(this.fromDate>this.toDate){const t=this.fromDate;this.fromDate=this.toDate;this.toDate=t;}
    this.selectedPeriod='custom';
    this.resetPagers();
    this.loadAll();
  }
}
