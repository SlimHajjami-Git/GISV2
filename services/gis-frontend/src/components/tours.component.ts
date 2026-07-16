import { Component, OnInit, OnDestroy, ViewChild, ElementRef, ChangeDetectorRef, NgZone } from '@angular/core';
import { Router } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { ApiService } from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { USER_PREF_PIPES } from '../pipes/user-preference-pipes';
import { forkJoin, Subject, of, Subscription } from 'rxjs';
import { debounceTime, switchMap, catchError } from 'rxjs/operators';

declare let L: any;

@Component({
  selector: 'app-tours',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, ...USER_PREF_PIPES],
  template: `
    <app-layout>

    <!-- ══════════ MAIN LIST VIEW ══════════ -->
    <div class="page" *ngIf="currentView === 'list'">
      <div class="page-top">
        <div class="page-title">
          <h1>Tournees</h1>
          <span class="title-count">{{tours.length}}</span>
        </div>
        <button class="btn-create" (click)="openCreate()">
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          Nouvelle tournee
        </button>
      </div>

      <!-- Stats row -->
      <div class="kpi-row">
        <div class="kpi" (click)="filterStatus='';loadTours()" [class.kpi-active]="filterStatus===''">
          <span class="kpi-val">{{stats.total}}</span><span class="kpi-lbl">Total</span>
        </div>
        <div class="kpi kpi-amber" (click)="filterStatus='planned';loadTours()" [class.kpi-active]="filterStatus==='planned'">
          <span class="kpi-val">{{stats.planned}}</span><span class="kpi-lbl">Planifiees</span>
        </div>
        <div class="kpi kpi-blue" (click)="filterStatus='in_progress';loadTours()" [class.kpi-active]="filterStatus==='in_progress'">
          <span class="kpi-val">{{stats.inProgress}}</span><span class="kpi-lbl">En cours</span>
        </div>
        <div class="kpi kpi-green" (click)="filterStatus='completed';loadTours()" [class.kpi-active]="filterStatus==='completed'">
          <span class="kpi-val">{{stats.completed}}</span><span class="kpi-lbl">Terminees</span>
        </div>
      </div>

      <!-- Filters -->
      <div class="filter-row">
        <div class="filter-group">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="2"><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/></svg>
          <select [(ngModel)]="filterVehicleId" (ngModelChange)="loadTours()">
            <option [ngValue]="null">Tous les vehicules</option>
            <option *ngFor="let v of vehicles" [ngValue]="v.id">{{v.name || v.plate}}</option>
          </select>
          <select [(ngModel)]="filterDriverId" (ngModelChange)="loadTours()">
            <option [ngValue]="null">Tous les chauffeurs</option>
            <option *ngFor="let d of drivers" [ngValue]="d.id">{{d.firstName}} {{d.lastName}}</option>
          </select>
        </div>
      </div>

      <!-- Loading -->
      <div class="loading-state" *ngIf="loading">
        <div class="lds-ring"><div></div><div></div><div></div><div></div></div>
      </div>

      <!-- Empty -->
      <div class="empty-state" *ngIf="!loading && tours.length === 0">
        <div class="empty-icon">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><path d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-4 0a1 1 0 01-1-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 01-1 1"/></svg>
        </div>
        <h3>Aucune tournee trouvee</h3>
        <p>Commencez par creer votre premiere tournee</p>
        <button class="btn-create sm" (click)="openCreate()">
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          Creer une tournee
        </button>
      </div>

      <!-- Tour table -->
      <div class="tour-table" *ngIf="!loading && tours.length > 0">
        <table>
          <thead>
            <tr>
              <th class="col-status"></th>
              <th>Nom</th>
              <th>Trajet</th>
              <th>Vehicule</th>
              <th>Chauffeur</th>
              <th>Depart</th>
              <th>Distance</th>
              <th>Duree</th>
              <th class="col-actions"></th>
            </tr>
          </thead>
          <tbody>
            <tr *ngFor="let t of tours" (click)="openDetail(t)" class="tour-row">
              <td class="col-status"><span class="status-dot" [class]="'sd-'+t.status"></span></td>
              <td>
                <div class="cell-name">{{t.name}}</div>
                <span class="badge-sm" [class]="'bs-'+t.status">{{getStatusLabel(t.status)}}</span>
              </td>
              <td>
                <div class="cell-route">
                  <span class="route-tag origin">{{truncate(t.origin, 20) || 'Depart'}}</span>
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg>
                  <span class="route-tag dest">{{truncate(t.destination, 20) || 'Arrivee'}}</span>
                  <span class="stop-count" *ngIf="t.waypointCount > 2">+{{t.waypointCount-2}}</span>
                </div>
              </td>
              <td><span class="cell-sub">{{t.vehicleName}}</span></td>
              <td><span class="cell-sub">{{t.driverName || '-'}}</span></td>
              <td><span class="cell-sub">{{formatDateShort(t.scheduledStartTime)}}</span></td>
              <td><strong class="cell-km">{{t.estimatedDistanceKm | appDistance:0}}</strong></td>
              <td><span class="cell-dur">{{formatDuration(t.estimatedDurationMinutes)}}</span></td>
              <td class="col-actions action-group">
                <button class="row-action" (click)="$event.stopPropagation();quickEdit(t)" title="Modifier" *ngIf="t.status==='planned'">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                </button>
                <button class="row-action" (click)="$event.stopPropagation();quickDelete(t)" title="Supprimer">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
                </button>
                <button class="row-action" (click)="$event.stopPropagation();openDetail(t)" title="Details">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg>
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- ══════════ CREATE / EDIT VIEW (Full screen split) ══════════ -->
    <div class="create-view" *ngIf="currentView === 'create'">

      <!-- Left panel: form -->
      <div class="cv-left">
        <div class="cv-left-head">
          <button class="back-btn" (click)="closeCreate()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"/></svg>
          </button>
          <h2>{{editingTour ? 'Modifier la tournee' : 'Planifier une tournee'}}</h2>
        </div>

        <div class="cv-left-scroll">
          <!-- Step 1: Basic info -->
          <div class="form-section">
            <div class="fs-title">
              <span class="step-num">1</span>
              Informations
            </div>
            <div class="field">
              <label>Nom *</label>
              <input type="text" [(ngModel)]="tourForm.name" placeholder="Ex: Livraison Tunis - Sousse">
            </div>
            <div class="field-row">
              <div class="field">
                <label>Vehicule *</label>
                <select [(ngModel)]="tourForm.vehicleId" (ngModelChange)="onVehicleChange($event)">
                  <option [ngValue]="null">Choisir...</option>
                  <option *ngFor="let v of vehicles" [ngValue]="v.id">{{v.name}} ({{v.plate}})</option>
                </select>
              </div>
              <div class="field">
                <label>Chauffeur</label>
                <div class="driver-chip" [class.driver-assigned]="tourForm.vehicleId && getVehicleDriver()" [class.driver-empty]="!tourForm.vehicleId || !getVehicleDriver()">
                  <svg class="driver-chip-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
                  <span class="driver-chip-text" *ngIf="tourForm.vehicleId && getVehicleDriver()">{{getVehicleDriver()}}</span>
                  <span class="driver-chip-text" *ngIf="tourForm.vehicleId && !getVehicleDriver()">Aucun chauffeur</span>
                  <span class="driver-chip-text" *ngIf="!tourForm.vehicleId">—</span>
                </div>
              </div>
            </div>
            <div class="field-row">
              <div class="field">
                <label>Depart prevu *</label>
                <input type="datetime-local" [(ngModel)]="tourForm.scheduledStartTime">
              </div>
              <div class="field">
                <label>Recurrence</label>
                <select [(ngModel)]="tourForm.recurrence" (ngModelChange)="onRecurrenceChange($event)">
                  <option value="none">Aucune (unique)</option>
                  <option value="weekly">Hebdomadaire</option>
                  <option value="custom">Personnalisee</option>
                </select>
              </div>
            </div>
            <div class="recurrence-config" *ngIf="tourForm.recurrence === 'weekly'">
              <label class="rc-label">Jours de la semaine</label>
              <div class="day-chips">
                <button *ngFor="let d of weekDays" class="day-chip" [class.day-active]="tourForm.recurrenceDays.includes(d.value)" (click)="toggleDay(d.value)" type="button">{{d.label}}</button>
              </div>
            </div>
            <div class="recurrence-config" *ngIf="tourForm.recurrence === 'custom'">
              <div class="field-row">
                <div class="field">
                  <label>Repeter chaque</label>
                  <div class="custom-interval">
                    <input type="number" [(ngModel)]="tourForm.customInterval" min="1" max="365" class="interval-input">
                    <select [(ngModel)]="tourForm.customUnit">
                      <option value="days">jour(s)</option>
                      <option value="weeks">semaine(s)</option>
                      <option value="months">mois</option>
                    </select>
                  </div>
                </div>
              </div>
            </div>
            <div class="field">
              <label>Notes</label>
              <input type="text" [(ngModel)]="tourForm.description" placeholder="Optionnel">
            </div>
          </div>

          <!-- Step 2: Itinerary -->
          <div class="form-section">
            <div class="fs-title">
              <span class="step-num">2</span>
              Itineraire
              <button class="add-stop-btn" (click)="addWaypoint()">+ Arret</button>
            </div>

            <div class="wp-timeline">
              <div class="wp-node" *ngFor="let wp of tourForm.waypoints; let i = index; let first = first; let last = last">
                <div class="wp-rail">
                  <div class="wp-circle" [class.wpc-green]="first" [class.wpc-red]="last && !first" [class.wpc-blue]="!first && !last"
                       [class.wpc-filled]="wp.latitude">
                    <span *ngIf="!wp.latitude">{{i+1}}</span>
                    <svg *ngIf="wp.latitude" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="3"><polyline points="20 6 9 17 4 12"/></svg>
                  </div>
                  <div class="wp-rail-line" *ngIf="!last"></div>
                </div>
                <div class="wp-content">
                  <div class="wp-label">{{first ? 'Depart' : (last && !first ? 'Destination' : 'Arret ' + i)}}</div>
                  <div class="wp-search-wrap">
                    <input type="text"
                      [(ngModel)]="wp.searchText"
                      (ngModelChange)="onSearchChange($event, i)"
                      (focus)="onSearchFocus(i)"
                      (blur)="onSearchBlur()"
                      [placeholder]="first ? 'Rechercher une adresse de depart...' : (last && !first ? 'Rechercher la destination...' : 'Rechercher une adresse...')"
                      class="wp-search-input"
                      [class.wp-filled]="wp.latitude"
                      autocomplete="off">
                    <div class="wp-autocomplete" *ngIf="activeSearchIdx === i && searchResults.length > 0">
                      <div class="wp-ac-item" *ngFor="let r of searchResults" (mousedown)="pickResult(r, i)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/></svg>
                        <div class="wp-ac-text">
                          <span class="wp-ac-main">{{getShortName(r.display_name)}}</span>
                          <span class="wp-ac-sub">{{r.display_name}}</span>
                        </div>
                      </div>
                    </div>
                  </div>
                  <div class="wp-meta" *ngIf="wp.latitude">
                    <span class="wp-coord-tag">{{wp.latitude.toFixed(4)}}, {{wp.longitude.toFixed(4)}}</span>
                    <div class="wp-opts">
                      <label class="wp-pause-lbl" *ngIf="!first">
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                        <input type="number" [(ngModel)]="wp.plannedPauseMinutes" min="0" class="pause-input"> min
                      </label>
                      <button class="wp-clear" (click)="clearWaypoint(i)" title="Modifier ce point">
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>
                      </button>
                      <button class="wp-remove" *ngIf="!first && !(last && tourForm.waypoints.length <= 2)" (click)="removeWaypoint(i)">
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                      </button>
                    </div>
                    <div class="wp-geofence-row" *ngIf="!first">
                      <select [(ngModel)]="wp.geofenceId" class="wp-geofence-select">
                        <option [ngValue]="null">— Zone géofence (optionnel) —</option>
                        <option *ngFor="let gf of geofences" [ngValue]="gf.id">{{gf.name}}</option>
                      </select>
                      <label class="wp-margin-lbl" title="Marge de retard (minutes)">
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
                        <input type="number" [(ngModel)]="wp.deadlineMarginMinutes" min="5" max="480" class="margin-input"> min marge
                      </label>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div class="map-tip">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2"><path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0118 0z"/><circle cx="12" cy="10" r="3"/></svg>
              Cliquez sur la carte pour placer des points
            </div>
          </div>

          <!-- Step 3: Estimation -->
          <div class="form-section" *ngIf="canEstimate()">
            <div class="fs-title">
              <span class="step-num">3</span>
              Estimation
            </div>
            <button class="est-btn" (click)="estimateRoute()" [disabled]="estimating">
              <svg *ngIf="!estimating" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M13 2L3 14h9l-1 8 10-12h-9l1-8z"/></svg>
              <div class="mini-spin" *ngIf="estimating"></div>
              {{estimating ? 'Calcul...' : 'Calculer l\\'itineraire'}}
            </button>
            <div class="est-grid" *ngIf="estimation">
              <div class="est-card">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
                <div><span class="est-v">{{estimation.distanceKm | appDistance:1}}</span><span class="est-l">Distance</span></div>
              </div>
              <div class="est-card">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#8b5cf6" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                <div><span class="est-v">{{formatDuration(estimation.durationMinutes)}}</span><span class="est-l">Duree</span></div>
              </div>
              <div class="est-card">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                <div><span class="est-v">{{formatDuration(estimation.durationWithPausesMinutes)}}</span><span class="est-l">Avec pauses</span></div>
              </div>
              <div class="est-card">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
                <div><span class="est-v">~{{estimation.estimatedFuelLiters | number:'1.1-1'}} L</span><span class="est-l">Carburant</span></div>
              </div>
            </div>
          </div>
        </div>

        <!-- Bottom bar -->
        <div class="cv-left-foot">
          <button class="btn-cancel" (click)="closeCreate()">Annuler</button>
          <button class="btn-save" (click)="saveTour()" [disabled]="saving || !tourForm.name || !tourForm.vehicleId">
            <div class="mini-spin" *ngIf="saving"></div>
            {{saving ? 'Enregistrement...' : (editingTour ? 'Modifier' : 'Creer la tournee')}}
          </button>
        </div>
      </div>

      <!-- Right panel: map -->
      <div class="cv-right">
        <div class="cv-map" #tourMapEl></div>
      </div>
    </div>

    <!-- ══════════ DETAIL VIEW ══════════ -->
    <div class="detail-view" *ngIf="currentView === 'detail' && selectedTour">
      <div class="dv-left">
        <div class="cv-left-head">
          <button class="back-btn" (click)="closeDetail()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"/></svg>
          </button>
          <h2>{{selectedTour.name}}</h2>
          <span class="badge-sm" [class]="'bs-'+selectedTour.status" style="margin-left:8px">{{getStatusLabel(selectedTour.status)}}</span>
        </div>

        <div class="cv-left-scroll">
          <!-- Live tracking bar -->
          <div class="tracking-bar" *ngIf="selectedTour.status === 'in_progress' && trackingData">
            <div class="track-header">
              <span class="track-live-dot"></span>
              <strong>Suivi en direct</strong>
              <span class="track-speed" *ngIf="trackingData.vehicle">{{trackingData.vehicle.speedKph | appSpeed:0}}</span>
            </div>
            <div class="track-progress">
              <div class="track-progress-bar" [style.width.%]="trackingData.progress?.percentComplete || 0"></div>
            </div>
            <div class="track-info">
              <span>{{trackingData.progress?.completedWaypoints}}/{{trackingData.progress?.totalWaypoints}} points</span>
              <span *ngIf="trackingData.progress?.nextWaypointName">Prochain: <strong>{{trackingData.progress.nextWaypointName}}</strong></span>
              <span *ngIf="trackingData.progress?.distanceToNextMeters">{{trackingData.progress.distanceToNextMeters >= 1000 ? ((trackingData.progress.distanceToNextMeters / 1000 | number:'1.1-1') + ' km') : (trackingData.progress.distanceToNextMeters + ' m')}}</span>
            </div>
          </div>
          <div class="tracking-bar track-offline" *ngIf="selectedTour.status === 'in_progress' && !trackingData?.vehicle">
            <div class="track-header">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="2"><path d="M1 1l22 22M16.72 11.06A10.94 10.94 0 0 1 19 12.55M5 12.55a10.94 10.94 0 0 1 5.17-2.39M10.71 5.05A16 16 0 0 1 22.56 9M1.42 9a15.91 15.91 0 0 1 4.7-2.88M8.53 16.11a6 6 0 0 1 6.95 0M12 20h.01"/></svg>
              <span>Position GPS indisponible</span>
            </div>
          </div>

          <!-- Info section -->
          <div class="detail-section">
            <h4>Informations</h4>
            <div class="info-grid">
              <div class="info-item"><span class="info-lbl">Vehicule</span><span class="info-val">{{selectedTour.vehicleName}}</span></div>
              <div class="info-item" *ngIf="selectedTour.driverName"><span class="info-lbl">Chauffeur</span><span class="info-val">{{selectedTour.driverName}}</span></div>
              <div class="info-item"><span class="info-lbl">Depart prevu</span><span class="info-val">{{formatDate(selectedTour.scheduledStartTime)}}</span></div>
              <div class="info-item" *ngIf="selectedTour.actualDepartureTime || selectedTour.actualStartTime"><span class="info-lbl">Depart reel</span><span class="info-val">{{formatDate(selectedTour.actualDepartureTime || selectedTour.actualStartTime)}}</span></div>
              <div class="info-item" *ngIf="selectedTour.waitBeforeDepartureMinutes >= 1"><span class="info-lbl">Attente avant depart</span><span class="info-val c-orange">{{selectedTour.waitBeforeDepartureMinutes}} min</span></div>
              <div class="info-item"><span class="info-lbl">Distance</span><span class="info-val">{{selectedTour.estimatedDistanceKm | appDistance:1}}</span></div>
              <div class="info-item"><span class="info-lbl">Duree</span><span class="info-val">{{formatDuration(selectedTour.estimatedDurationMinutes)}}</span></div>
              <div class="info-item"><span class="info-lbl">Carburant</span><span class="info-val">~{{selectedTour.estimatedFuelLiters | number:'1.1-1'}} L</span></div>
              <div class="info-item"><span class="info-lbl">Pauses</span><span class="info-val">{{selectedTour.totalPauseMinutes}} min</span></div>
            </div>
          </div>

          <!-- Compare -->
          <div class="detail-section" *ngIf="selectedTour.status === 'completed'">
            <h4>Comparaison Estime vs Reel</h4>
            <table class="cmp-table">
              <thead><tr><th></th><th>Estime</th><th>Reel</th><th>Ecart</th></tr></thead>
              <tbody>
                <tr>
                  <td>Distance</td>
                  <td>{{selectedTour.estimatedDistanceKm | appDistance:1}}</td>
                  <td>{{selectedTour.actualDistanceKm ? (selectedTour.actualDistanceKm | appDistance:1) : '-'}}</td>
                  <td [class.c-red]="selectedTour.distanceDiffKm > 0" [class.c-green]="selectedTour.distanceDiffKm < 0">
                    {{selectedTour.distanceDiffKm != null ? ((selectedTour.distanceDiffKm > 0 ? '+' : '') + (selectedTour.distanceDiffKm | appDistance:1)) : '-'}}
                  </td>
                </tr>
                <tr>
                  <td>Duree</td>
                  <td>{{formatDuration(selectedTour.estimatedDurationMinutes)}}</td>
                  <td>{{selectedTour.actualDurationMinutes ? formatDuration(selectedTour.actualDurationMinutes) : '-'}}</td>
                  <td [class.c-red]="selectedTour.delayMinutes > 0" [class.c-green]="selectedTour.delayMinutes < 0">
                    {{selectedTour.delayMinutes != null ? ((selectedTour.delayMinutes > 0 ? '+' : '') + selectedTour.delayMinutes + ' min') : '-'}}
                  </td>
                </tr>
                <tr>
                  <td>Carburant</td>
                  <td>{{selectedTour.estimatedFuelLiters | number:'1.1-1'}} L</td>
                  <td>{{selectedTour.actualFuelLiters ? (selectedTour.actualFuelLiters | number:'1.1-1') + ' L' : '-'}}</td>
                  <td [class.c-red]="selectedTour.fuelDiffLiters > 0" [class.c-green]="selectedTour.fuelDiffLiters < 0">
                    {{selectedTour.fuelDiffLiters != null ? ((selectedTour.fuelDiffLiters > 0 ? '+' : '') + (selectedTour.fuelDiffLiters | number:'1.1-1') + ' L') : '-'}}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Waypoints timeline -->
          <div class="detail-section">
            <h4>Points de passage</h4>
            <div class="d-timeline">
              <div class="dt-node" *ngFor="let wp of selectedTour.waypoints; let last = last" [class.dt-done]="wp.isCompleted">
                <div class="dt-rail">
                  <div class="dt-dot" [class.dt-origin]="wp.type==='origin'" [class.dt-dest]="wp.type==='destination'">
                    <svg *ngIf="wp.isCompleted" width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="3"><polyline points="20 6 9 17 4 12"/></svg>
                  </div>
                  <div class="dt-line" *ngIf="!last"></div>
                </div>
                <div class="dt-body">
                  <div class="dt-main">
                    <strong>{{wp.name || wp.address || getWaypointTypeLabel(wp.type)}}</strong>
                    <span class="dt-time" *ngIf="wp.estimatedArrivalTime">{{formatTime(wp.estimatedArrivalTime)}}</span>
                  </div>
                  <div class="dt-sub" *ngIf="wp.address && wp.name">{{wp.address}}</div>
                  <div class="dt-sub" *ngIf="wp.actualArrivalTime">
                    Arrivee: {{formatTime(wp.actualArrivalTime)}}
                    <span *ngIf="wp.arrivalDelay" [class.c-red]="wp.arrivalDelay>0" [class.c-green]="wp.arrivalDelay<0">({{wp.arrivalDelay>0?'+':''}}{{wp.arrivalDelay}} min)</span>
                  </div>
                  <div class="dt-sub dt-pause" *ngIf="wp.plannedPauseMinutes > 0">Pause: {{wp.plannedPauseMinutes}} min</div>
                </div>
              </div>
            </div>
          </div>

          <!-- Pauses -->
          <div class="detail-section" *ngIf="selectedTour.pauses?.length > 0">
            <h4>Pauses</h4>
            <div class="pause-cards">
              <div class="p-card" *ngFor="let p of selectedTour.pauses">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></svg>
                <strong>{{getPauseLabel(p.reason)}}</strong>
                <span>{{formatTime(p.startTime)}} - {{p.endTime ? formatTime(p.endTime) : 'En cours'}}</span>
                <span class="p-dur" *ngIf="p.durationMinutes">{{p.durationMinutes}} min</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Actions footer -->
        <div class="cv-left-foot">
          <button class="btn-outline-sm" *ngIf="selectedTour.status==='completed'||selectedTour.status==='in_progress'" (click)="replaySelectedTour()" title="Rejouer le trajet sur la carte">
            &#9654; Replay
          </button>
          <button class="btn-cancel" (click)="closeDetail()">Retour</button>
          <button class="btn-outline-sm" *ngIf="selectedTour.status==='planned'" (click)="editTour()">Modifier</button>
          <button class="btn-danger-sm" *ngIf="selectedTour.status==='planned'||selectedTour.status==='in_progress'" (click)="cancelSelectedTour()">Annuler</button>
          <button class="btn-save" *ngIf="selectedTour.status==='planned'" (click)="startSelectedTour()">Demarrer</button>
          <button class="btn-save green" *ngIf="selectedTour.status==='in_progress'" (click)="completeSelectedTour()">Terminer</button>
          <button class="btn-danger-sm" (click)="deleteSelectedTour()">Supprimer</button>
        </div>
      </div>
      <div class="cv-right">
        <div class="cv-map" #detailMapEl></div>
      </div>
    </div>

    </app-layout>
  `,
  styles: [`
    :host { display: block; height: 100%; overflow: hidden; }

    /* ════ PAGE LIST ════ */
    .page { padding: 24px 28px; height: 100%; box-sizing: border-box; overflow-y: auto; }
    .page-top { display: flex; justify-content: space-between; align-items: center; margin-bottom: 22px; }
    .page-title { display: flex; align-items: center; gap: 10px; }
    .page-title h1 { font-size: 20px; font-weight: 700; color: #0f172a; margin: 0; }
    .title-count { background: #eff6ff; color: #3b82f6; font-size: 12px; font-weight: 700; padding: 2px 8px; border-radius: 10px; }

    .btn-create { display: flex; align-items: center; gap: 7px; padding: 9px 20px; background: #3b82f6; color: white; border: none; border-radius: 9px; font-size: 13px; font-weight: 600; cursor: pointer; transition: all .15s; box-shadow: 0 1px 3px rgba(59,130,246,0.3); }
    .btn-create:hover { background: #2563eb; box-shadow: 0 4px 12px rgba(59,130,246,0.3); transform: translateY(-1px); }
    .btn-create.sm { padding: 8px 16px; font-size: 12px; }

    /* KPIs */
    .kpi-row { display: flex; gap: 10px; margin-bottom: 18px; }
    .kpi { padding: 12px 20px; background: white; border: 1px solid #e2e8f0; border-radius: 9px; cursor: pointer; transition: all .15s; min-width: 100px; text-align: center; }
    .kpi:hover { border-color: #93c5fd; }
    .kpi-active { border-color: #3b82f6; background: #eff6ff; }
    .kpi-val { display: block; font-size: 22px; font-weight: 800; color: #0f172a; line-height: 1.1; }
    .kpi-lbl { font-size: 11px; color: #64748b; font-weight: 500; }
    .kpi-amber .kpi-val { color: #d97706; }
    .kpi-blue .kpi-val { color: #2563eb; }
    .kpi-green .kpi-val { color: #16a34a; }

    /* Filters */
    .filter-row { display: flex; align-items: center; gap: 10px; margin-bottom: 16px; }
    .filter-group { display: flex; align-items: center; gap: 8px; }
    .filter-group select { padding: 6px 10px; border: 1px solid #e2e8f0; border-radius: 7px; font-size: 12px; color: #475569; background: white; cursor: pointer; }
    .filter-group select:focus { outline: none; border-color: #93c5fd; }

    /* Loading / Empty */
    .loading-state { display: flex; justify-content: center; padding: 60px 0; }
    .lds-ring { display: inline-block; position: relative; width: 32px; height: 32px; }
    .lds-ring div { box-sizing: border-box; display: block; position: absolute; width: 28px; height: 28px; margin: 2px; border: 3px solid #3b82f6; border-radius: 50%; animation: ldsring .8s cubic-bezier(.5,.1,.5,1) infinite; border-color: #3b82f6 transparent transparent transparent; }
    .lds-ring div:nth-child(1) { animation-delay: -.3s; } .lds-ring div:nth-child(2) { animation-delay: -.2s; } .lds-ring div:nth-child(3) { animation-delay: -.1s; }
    @keyframes ldsring { 0% { transform: rotate(0); } 100% { transform: rotate(360deg); } }

    .empty-state { text-align: center; padding: 60px 20px; }
    .empty-icon { margin-bottom: 12px; }
    .empty-state h3 { font-size: 16px; color: #475569; margin: 0 0 6px; font-weight: 600; }
    .empty-state p { font-size: 13px; color: #94a3b8; margin: 0 0 18px; }

    /* Tour Table */
    .tour-table { background: white; border: 1px solid #e2e8f0; border-radius: 10px; overflow: hidden; }
    .tour-table table { width: 100%; border-collapse: collapse; }
    .tour-table thead { background: #f8fafc; }
    .tour-table th { padding: 10px 14px; font-size: 11px; font-weight: 600; color: #64748b; text-align: left; text-transform: uppercase; letter-spacing: .4px; border-bottom: 1px solid #e2e8f0; }
    .tour-table td { padding: 12px 14px; font-size: 13px; color: #1e293b; border-bottom: 1px solid #f1f5f9; }
    .tour-row { cursor: pointer; transition: background .1s; }
    .tour-row:hover { background: #f8fafc; }
    .tour-row:last-child td { border-bottom: none; }
    .col-status { width: 24px; padding-right: 0 !important; }
    .col-actions { width: 40px; }
    .status-dot { width: 8px; height: 8px; border-radius: 50%; display: inline-block; }
    .sd-planned { background: #f59e0b; }
    .sd-in_progress { background: #3b82f6; animation: pulse 2s infinite; }
    .sd-completed { background: #22c55e; }
    .sd-cancelled { background: #94a3b8; }
    @keyframes pulse { 0%,100% { opacity:1; } 50% { opacity:.4; } }
    .cell-name { font-weight: 600; color: #0f172a; margin-bottom: 2px; }
    .badge-sm { display: inline-block; padding: 1px 7px; border-radius: 10px; font-size: 9px; font-weight: 700; text-transform: uppercase; letter-spacing: .3px; }
    .bs-planned { background: #fef3c7; color: #92400e; }
    .bs-in_progress { background: #dbeafe; color: #1e40af; }
    .bs-completed { background: #dcfce7; color: #166534; }
    .bs-cancelled { background: #f1f5f9; color: #64748b; }
    .cell-route { display: flex; align-items: center; gap: 4px; }
    .route-tag { font-size: 11px; padding: 2px 6px; border-radius: 4px; max-width: 130px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .route-tag.origin { background: #f0fdf4; color: #15803d; }
    .route-tag.dest { background: #fef2f2; color: #b91c1c; }
    .stop-count { font-size: 10px; color: #64748b; background: #f1f5f9; padding: 1px 5px; border-radius: 8px; font-weight: 600; }
    .cell-sub { font-size: 12px; color: #64748b; }
    .cell-km { font-size: 13px; color: #0f172a; }
    .cell-dur { font-size: 12px; color: #3b82f6; font-weight: 600; }
    .action-group { display: flex; gap: 2px; justify-content: flex-end; }
    .row-action { background: none; border: none; cursor: pointer; color: #94a3b8; padding: 4px; border-radius: 4px; display: flex; align-items: center; }
    .row-action:hover { background: #f1f5f9; color: #475569; }

    /* ════ CREATE / DETAIL VIEW ════ */
    .create-view, .detail-view { display: flex; height: calc(100vh - 56px); }
    .cv-left, .dv-left { width: 440px; min-width: 440px; display: flex; flex-direction: column; border-right: 1px solid #e2e8f0; background: white; height: 100%; }
    .cv-right { flex: 1; position: relative; }
    .cv-map { width: 100%; height: 100%; }
    .cv-left-head { display: flex; align-items: center; gap: 8px; padding: 14px 18px; border-bottom: 1px solid #f1f5f9; flex-shrink: 0; }
    .cv-left-head h2 { font-size: 15px; font-weight: 700; color: #0f172a; margin: 0; white-space: nowrap; }
    .back-btn { background: none; border: none; cursor: pointer; color: #64748b; padding: 4px; border-radius: 6px; display: flex; }
    .back-btn:hover { background: #f1f5f9; color: #0f172a; }
    .cv-left-scroll { flex: 1; overflow-y: auto; padding: 0; }
    .cv-left-foot { display: flex; justify-content: flex-end; gap: 8px; padding: 12px 18px; border-top: 1px solid #f1f5f9; flex-shrink: 0; background: white; }
    .btn-cancel { padding: 8px 16px; background: transparent; border: 1px solid #e2e8f0; border-radius: 8px; font-size: 12px; color: #64748b; cursor: pointer; font-weight: 500; }
    .btn-cancel:hover { background: #f8fafc; }
    .btn-save { padding: 8px 20px; background: #3b82f6; color: white; border: none; border-radius: 8px; font-size: 12px; font-weight: 600; cursor: pointer; display: flex; align-items: center; gap: 6px; }
    .btn-save:hover { background: #2563eb; }
    .btn-save:disabled { opacity: .5; cursor: not-allowed; }
    .btn-save.green { background: #16a34a; }
    .btn-save.green:hover { background: #15803d; }
    .btn-outline-sm { padding: 8px 14px; background: white; border: 1px solid #e2e8f0; border-radius: 8px; font-size: 12px; color: #475569; cursor: pointer; font-weight: 500; }
    .btn-outline-sm:hover { background: #f8fafc; }
    .btn-danger-sm { padding: 8px 14px; background: #fef2f2; border: 1px solid #fecaca; border-radius: 8px; font-size: 12px; color: #dc2626; cursor: pointer; font-weight: 500; }
    .btn-danger-sm:hover { background: #fee2e2; }

    /* Form sections */
    .form-section { padding: 16px 18px; border-bottom: 1px solid #f1f5f9; }
    .fs-title { display: flex; align-items: center; gap: 8px; font-size: 13px; font-weight: 700; color: #0f172a; margin-bottom: 14px; }
    .step-num { width: 22px; height: 22px; border-radius: 50%; background: #3b82f6; color: white; font-size: 11px; font-weight: 700; display: flex; align-items: center; justify-content: center; }
    .add-stop-btn { margin-left: auto; padding: 4px 10px; background: #eff6ff; color: #3b82f6; border: none; border-radius: 6px; font-size: 11px; font-weight: 600; cursor: pointer; }
    .add-stop-btn:hover { background: #dbeafe; }
    .field { display: flex; flex-direction: column; gap: 4px; margin-bottom: 10px; }
    .field label { font-size: 11px; font-weight: 600; color: #64748b; }
    .field input, .field select { padding: 8px 11px; border: 1px solid #e2e8f0; border-radius: 7px; font-size: 13px; color: #0f172a; background: #fff; }
    .field input:focus, .field select:focus { outline: none; border-color: #93c5fd; box-shadow: 0 0 0 2px rgba(59,130,246,.12); }
    .field-row { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }

    .driver-chip { display: flex; align-items: center; gap: 8px; padding: 8px 12px; border-radius: 7px; font-size: 13px; min-height: 18px; transition: all .2s; }
    .driver-assigned { background: #f0fdf4; border: 1px solid #bbf7d0; color: #15803d; }
    .driver-assigned .driver-chip-icon { color: #22c55e; }
    .driver-empty { background: #f8fafc; border: 1px dashed #cbd5e1; color: #94a3b8; }
    .driver-empty .driver-chip-icon { color: #cbd5e1; }
    .driver-chip-text { font-weight: 500; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

    .recurrence-config { padding: 10px 0 4px; }
    .rc-label { font-size: 11px; font-weight: 600; color: #64748b; margin-bottom: 6px; display: block; }
    .day-chips { display: flex; gap: 6px; flex-wrap: wrap; }
    .day-chip { padding: 6px 12px; border-radius: 20px; border: 1.5px solid #e2e8f0; background: #fff; font-size: 12px; font-weight: 600; color: #64748b; cursor: pointer; transition: all .15s; }
    .day-chip:hover { border-color: #93c5fd; color: #3b82f6; }
    .day-active { background: #3b82f6 !important; color: white !important; border-color: #3b82f6 !important; }
    .custom-interval { display: flex; gap: 8px; align-items: center; }
    .interval-input { width: 70px !important; text-align: center; }

    /* Waypoint timeline */
    .wp-timeline { display: flex; flex-direction: column; }
    .wp-node { display: flex; gap: 12px; }
    .wp-rail { display: flex; flex-direction: column; align-items: center; width: 22px; }
    .wp-circle { width: 22px; height: 22px; border-radius: 50%; background: #e2e8f0; display: flex; align-items: center; justify-content: center; font-size: 10px; font-weight: 700; color: #94a3b8; flex-shrink: 0; transition: all .2s; }
    .wpc-green { background: #dcfce7; color: #16a34a; }
    .wpc-red { background: #fef2f2; color: #dc2626; }
    .wpc-blue { background: #dbeafe; color: #2563eb; }
    .wpc-filled { color: white !important; }
    .wpc-filled.wpc-green { background: #16a34a; }
    .wpc-filled.wpc-red { background: #dc2626; }
    .wpc-filled.wpc-blue { background: #2563eb; }
    .wp-rail-line { flex: 1; width: 2px; background: #e2e8f0; min-height: 16px; }
    .wp-content { flex: 1; padding-bottom: 12px; min-width: 0; }
    .wp-label { font-size: 10px; font-weight: 600; color: #94a3b8; text-transform: uppercase; letter-spacing: .4px; margin-bottom: 4px; }

    .wp-search-wrap { position: relative; }
    .wp-search-input { width: 100%; padding: 8px 11px; border: 1.5px solid #e2e8f0; border-radius: 8px; font-size: 12px; color: #0f172a; box-sizing: border-box; transition: all .15s; background: #f8fafc; }
    .wp-search-input:focus { outline: none; border-color: #93c5fd; box-shadow: 0 0 0 3px rgba(59,130,246,.1); background: white; }
    .wp-search-input.wp-filled { border-color: #86efac; background: #f0fdf4; }
    .wp-search-input::placeholder { color: #94a3b8; font-size: 12px; }

    .wp-autocomplete { position: absolute; top: calc(100% + 3px); left: 0; right: 0; background: white; border: 1px solid #e2e8f0; border-radius: 9px; box-shadow: 0 10px 30px rgba(0,0,0,.12); z-index: 100; max-height: 220px; overflow-y: auto; }
    .wp-ac-item { display: flex; align-items: flex-start; gap: 8px; padding: 9px 11px; cursor: pointer; border-bottom: 1px solid #f8fafc; transition: background .1s; }
    .wp-ac-item:last-child { border-bottom: none; }
    .wp-ac-item:hover { background: #eff6ff; }
    .wp-ac-item svg { flex-shrink: 0; margin-top: 2px; }
    .wp-ac-text { display: flex; flex-direction: column; min-width: 0; }
    .wp-ac-main { font-size: 12px; font-weight: 600; color: #1e293b; }
    .wp-ac-sub { font-size: 10px; color: #94a3b8; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }

    .wp-meta { display: flex; align-items: center; gap: 6px; margin-top: 4px; flex-wrap: wrap; }
    .wp-coord-tag { font-size: 9px; color: #94a3b8; background: #f1f5f9; padding: 1px 5px; border-radius: 3px; font-family: monospace; }
    .wp-opts { display: flex; align-items: center; gap: 6px; margin-left: auto; }
    .wp-pause-lbl { display: flex; align-items: center; gap: 3px; font-size: 10px; color: #94a3b8; }
    .pause-input { width: 36px; padding: 2px 4px; border: 1px solid #e2e8f0; border-radius: 4px; font-size: 11px; text-align: center; }
    .pause-input:focus { outline: none; border-color: #93c5fd; }
    .wp-clear { background: none; border: none; cursor: pointer; color: #3b82f6; padding: 2px; border-radius: 4px; display: flex; opacity: .6; }
    .wp-clear:hover { opacity: 1; background: #eff6ff; }
    .wp-remove { background: none; border: none; cursor: pointer; color: #ef4444; padding: 2px; border-radius: 4px; display: flex; opacity: .6; }
    .wp-remove:hover { opacity: 1; background: #fef2f2; }
    .wp-geofence-row { display: flex; align-items: center; gap: 6px; margin-top: 4px; width: 100%; }
    .wp-geofence-select { flex: 1; padding: 4px 6px; border: 1px solid #e2e8f0; border-radius: 5px; font-size: 11px; color: #475569; background: #f8fafc; }
    .wp-geofence-select:focus { outline: none; border-color: #93c5fd; }
    .wp-margin-lbl { display: flex; align-items: center; gap: 3px; font-size: 10px; color: #f59e0b; white-space: nowrap; }
    .margin-input { width: 40px; padding: 2px 4px; border: 1px solid #fde68a; border-radius: 4px; font-size: 11px; text-align: center; background: #fffbeb; }
    .margin-input:focus { outline: none; border-color: #f59e0b; }

    .map-tip { display: flex; align-items: center; gap: 6px; font-size: 11px; color: #3b82f6; padding: 8px 18px; background: #eff6ff; margin: 0; border-top: 1px solid #dbeafe; }

    /* Estimation */
    .est-btn { display: flex; align-items: center; gap: 6px; padding: 8px 16px; background: #0f172a; color: white; border: none; border-radius: 8px; font-size: 12px; font-weight: 600; cursor: pointer; margin-bottom: 12px; }
    .est-btn:hover { background: #1e293b; }
    .est-btn:disabled { opacity: .5; cursor: not-allowed; }
    .mini-spin { width: 14px; height: 14px; border: 2px solid rgba(255,255,255,.3); border-top-color: white; border-radius: 50%; animation: ldsring .6s linear infinite; }
    .est-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .est-card { display: flex; align-items: center; gap: 10px; padding: 10px 12px; background: #f8fafc; border-radius: 8px; border: 1px solid #f1f5f9; }
    .est-v { font-size: 14px; font-weight: 700; color: #0f172a; display: block; line-height: 1.1; }
    .est-l { font-size: 10px; color: #94a3b8; }

    /* ════ DETAIL VIEW ════ */
    .detail-section { padding: 16px 18px; border-bottom: 1px solid #f1f5f9; }
    .detail-section h4 { font-size: 12px; font-weight: 700; color: #0f172a; margin: 0 0 12px; text-transform: uppercase; letter-spacing: .4px; }
    .info-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
    .info-item { display: flex; flex-direction: column; }
    .info-lbl { font-size: 10px; color: #94a3b8; font-weight: 500; }
    .info-val { font-size: 13px; color: #0f172a; font-weight: 500; }
    .cmp-table { width: 100%; border-collapse: collapse; font-size: 12px; }
    .cmp-table th { text-align: left; padding: 6px 8px; background: #f8fafc; color: #64748b; font-weight: 600; font-size: 11px; }
    .cmp-table td { padding: 6px 8px; border-bottom: 1px solid #f8fafc; color: #1e293b; }
    .c-red { color: #ef4444; font-weight: 600; }
    .c-green { color: #22c55e; font-weight: 600; }
    .c-orange { color: #f97316; font-weight: 600; }

    /* Detail timeline */
    .d-timeline { display: flex; flex-direction: column; }
    .dt-node { display: flex; gap: 10px; }
    .dt-rail { display: flex; flex-direction: column; align-items: center; width: 20px; }
    .dt-dot { width: 20px; height: 20px; border-radius: 50%; background: #e2e8f0; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .dt-done .dt-dot { background: #22c55e; }
    .dt-origin { background: #22c55e !important; }
    .dt-dest { background: #ef4444 !important; }
    .dt-line { flex: 1; width: 2px; background: #e2e8f0; min-height: 12px; }
    .dt-done .dt-line { background: #86efac; }
    .dt-body { flex: 1; padding-bottom: 14px; min-width: 0; }
    .dt-main { display: flex; justify-content: space-between; align-items: center; font-size: 13px; }
    .dt-main strong { color: #0f172a; }
    .dt-time { font-size: 10px; color: #94a3b8; }
    .dt-sub { font-size: 11px; color: #64748b; margin-top: 2px; }
    .dt-pause { color: #f59e0b; }

    .pause-cards { display: flex; flex-direction: column; gap: 4px; }
    .p-card { display: flex; align-items: center; gap: 8px; padding: 8px 10px; background: #fffbeb; border: 1px solid #fef3c7; border-radius: 7px; font-size: 12px; color: #78350f; }
    .p-dur { font-weight: 700; margin-left: auto; color: #92400e; }

    /* ════ VEHICLE LIVE ICON (map) ════ */
    :host ::ng-deep .vehicle-live-icon { background: none !important; border: none !important; }
    :host ::ng-deep .vlive-dot { width: 16px; height: 16px; background: #2563eb; border-radius: 50%; border: 3px solid white; box-shadow: 0 0 8px rgba(37,99,235,.5); position: relative; }
    :host ::ng-deep .vlive-pulse { position: absolute; inset: -6px; border-radius: 50%; border: 2px solid #3b82f6; animation: vpulse 2s infinite; }
    @keyframes vpulse { 0% { transform: scale(.8); opacity: 1; } 100% { transform: scale(2); opacity: 0; } }

    /* ════ TRACKING BAR ════ */
    .tracking-bar { padding: 12px 18px; background: linear-gradient(135deg, #eff6ff, #f0fdf4); border-bottom: 1px solid #dbeafe; }
    .track-offline { background: #f8fafc; border-bottom: 1px solid #f1f5f9; }
    .track-offline .track-header { color: #94a3b8; gap: 6px; }
    .track-header { display: flex; align-items: center; gap: 8px; font-size: 12px; color: #1e293b; margin-bottom: 8px; }
    .track-header strong { font-weight: 700; }
    .track-live-dot { width: 8px; height: 8px; border-radius: 50%; background: #22c55e; animation: pulse 2s infinite; }
    .track-speed { margin-left: auto; font-size: 14px; font-weight: 800; color: #2563eb; }
    .track-progress { height: 4px; background: #e2e8f0; border-radius: 2px; overflow: hidden; margin-bottom: 6px; }
    .track-progress-bar { height: 100%; background: linear-gradient(90deg, #3b82f6, #22c55e); border-radius: 2px; transition: width .5s ease; }
    .track-info { display: flex; justify-content: space-between; font-size: 10px; color: #64748b; }
    .track-info strong { color: #1e293b; }

    /* ════ RESPONSIVE ════ */
    @media (max-width: 900px) {
      .cv-left, .dv-left { width: 100%; min-width: 100%; border-right: none; }
      .cv-right { display: none; }
      .kpi-row { flex-wrap: wrap; }
      .tour-table { overflow-x: auto; }
    }
  `]
})
export class ToursComponent implements OnInit, OnDestroy {
  @ViewChild('tourMapEl') tourMapEl!: ElementRef;
  @ViewChild('detailMapEl') detailMapEl!: ElementRef;

  currentView: 'list' | 'create' | 'detail' = 'list';

  tours: any[] = [];
  vehicles: any[] = [];
  drivers: any[] = [];
  geofences: any[] = [];
  stats = { total: 0, planned: 0, inProgress: 0, completed: 0, cancelled: 0 };

  loading = false;
  saving = false;
  estimating = false;

  filterStatus = '';
  filterVehicleId: number | null = null;
  filterDriverId: number | null = null;

  editingTour: any = null;
  selectedTour: any = null;
  estimation: any = null;
  showCreateModal = false;
  showDetailModal = false;
  trackingData: any = null;
  private trackingInterval: any = null;
  private vehicleMarker: any = null;

  tourForm = this.getEmptyForm();

  searchResults: any[] = [];
  activeSearchIdx = -1;
  private searchSubject = new Subject<{ query: string; index: number }>();
  private searchSub!: Subscription;
  private searchCache = new Map<string, any[]>();

  private tourMap: any = null;
  private detailMap: any = null;
  private mapMarkers: any[] = [];
  private routeLayer: any = null;
  private routeLines: any[] = [];
  private detailRouteLines: any[] = [];

  constructor(
    private apiService: ApiService,
    private http: HttpClient,
    private cdr: ChangeDetectorRef,
    private zone: NgZone,
    private router: Router
  ) {}

  /// Ouvre le lecteur de trajet (playback) borné à la fenêtre de la tournée :
  /// 5 min avant le démarrage → 5 min après la fin (ou maintenant si en cours).
  /// Permet de voir le détail réel : attente avant départ, arrêts, vitesses.
  replaySelectedTour() {
    const t = this.selectedTour;
    if (!t?.vehicleId || !t.actualStartTime) return;
    const from = new Date(new Date(t.actualStartTime).getTime() - 5 * 60000);
    const to = t.actualEndTime
      ? new Date(new Date(t.actualEndTime).getTime() + 5 * 60000)
      : new Date();
    this.router.navigate(['/playback'], {
      queryParams: {
        vehicleId: t.vehicleId,
        from: from.toISOString(),
        to: to.toISOString()
      }
    });
  }

  ngOnInit() {
    this.loadData();
    this.searchSub = this.searchSubject.pipe(
      debounceTime(200),
      switchMap(({ query, index }) => {
        if (!query || query.length < 2) return of([]);
        const cacheKey = query.toLowerCase().trim();
        if (this.searchCache.has(cacheKey)) return of(this.searchCache.get(cacheKey)!);
        return this.http.get<any[]>(
          `/api/nominatim/search?q=${encodeURIComponent(query)}&format=json&limit=5&countrycodes=tn,dz,ly,ma&accept-language=fr`
        ).pipe(
          switchMap(results => {
            this.searchCache.set(cacheKey, results);
            return of(results);
          }),
          catchError(() => of([]))
        );
      })
    ).subscribe(results => {
      this.searchResults = results || [];
      this.cdr.detectChanges();
    });
  }

  ngOnDestroy() {
    if (this.searchSub) this.searchSub.unsubscribe();
    this.stopTracking();
    this.destroyMaps();
  }

  destroyMaps() {
    if (this.tourMap) { this.tourMap.remove(); this.tourMap = null; }
    if (this.detailMap) { this.detailMap.remove(); this.detailMap = null; }
  }

  stopTracking() {
    if (this.trackingInterval) { clearInterval(this.trackingInterval); this.trackingInterval = null; }
    this.trackingData = null;
    this.vehicleMarker = null;
  }

  loadData() {
    this.loading = true;
    forkJoin({
      vehicles: this.apiService.getVehicles(),
      drivers: this.apiService.getDrivers(),
      geofences: this.apiService.getGeofences()
    }).subscribe({
      next: (data) => {
        this.vehicles = data.vehicles || [];
        this.drivers = data.drivers || [];
        this.geofences = data.geofences || [];
        this.loadTours();
        this.loadStats();
      },
      error: () => { this.loading = false; }
    });
  }

  loadTours() {
    this.loading = true;
    const filters: any = {};
    if (this.filterStatus) filters.status = this.filterStatus;
    if (this.filterVehicleId) filters.vehicleId = this.filterVehicleId;
    if (this.filterDriverId) filters.driverId = this.filterDriverId;
    this.apiService.getTours(filters).subscribe({
      next: (res) => { this.tours = res.items || []; this.loading = false; this.cdr.detectChanges(); },
      error: () => { this.loading = false; }
    });
  }

  loadStats() {
    this.apiService.getTourStats().subscribe({
      next: (s) => { this.stats = s; this.cdr.detectChanges(); },
      error: () => {}
    });
  }

  // ═══════ NAVIGATION ═══════

  openCreate() {
    this.editingTour = null;
    this.tourForm = this.getEmptyForm();
    this.estimation = null;
    this.currentView = 'create';
    setTimeout(() => this.initTourMap(), 200);
  }

  closeCreate() {
    this.currentView = 'list';
    this.destroyMaps();
  }

  openDetail(tour: any) {
    this.apiService.getTour(tour.id).subscribe({
      next: (detail) => {
        this.selectedTour = detail;
        this.currentView = 'detail';
        this.cdr.detectChanges();
        setTimeout(() => this.initDetailMap(), 200);
        if (detail.status === 'in_progress') {
          this.startTracking(detail.id);
        }
      }
    });
  }

  closeDetail() {
    this.stopTracking();
    this.currentView = 'list';
    this.selectedTour = null;
    this.destroyMaps();
  }

  startTracking(tourId: number) {
    this.stopTracking();
    this.fetchTracking(tourId);
    this.trackingInterval = setInterval(() => this.fetchTracking(tourId), 10000);
  }

  fetchTracking(tourId: number) {
    this.apiService.getTourTracking(tourId).subscribe({
      next: (data) => {
        this.trackingData = data;
        this.updateVehicleMarker(data);
        this.cdr.detectChanges();
      },
      error: () => {}
    });
  }

  updateVehicleMarker(data: any) {
    if (!this.detailMap || !data?.vehicle) return;
    const { latitude, longitude, headingDeg } = data.vehicle;
    if (this.vehicleMarker) {
      this.vehicleMarker.setLatLng([latitude, longitude]);
    } else {
      const vehicleIcon = L.divIcon({
        className: 'vehicle-live-icon',
        html: '<div class="vlive-dot"><div class="vlive-pulse"></div></div>',
        iconSize: [24, 24],
        iconAnchor: [12, 12]
      });
      this.vehicleMarker = L.marker([latitude, longitude], { icon: vehicleIcon, zIndexOffset: 1000 })
        .addTo(this.detailMap)
        .bindPopup(`<b>Position actuelle</b><br>${data.vehicle.speedKph?.toFixed(0) || 0} km/h`);
    }
    this.vehicleMarker.setPopupContent(`<b>Position actuelle</b><br>${data.vehicle.speedKph?.toFixed(0) || 0} km/h`);
  }

  // ═══════ FORM ═══════

  weekDays = [
    { value: 'mon', label: 'Lun' }, { value: 'tue', label: 'Mar' }, { value: 'wed', label: 'Mer' },
    { value: 'thu', label: 'Jeu' }, { value: 'fri', label: 'Ven' }, { value: 'sat', label: 'Sam' }, { value: 'sun', label: 'Dim' }
  ];

  getEmptyForm(): any {
    return {
      name: '',
      description: '',
      vehicleId: null,
      driverId: null,
      scheduledStartTime: '',
      recurrence: 'none',
      recurrenceDays: [] as string[],
      customInterval: 1,
      customUnit: 'weeks',
      notes: '',
      waypoints: [
        { searchText: '', resolvedName: '', address: '', latitude: null, longitude: null, plannedPauseMinutes: 0, geofenceId: null, deadlineMarginMinutes: 60 },
        { searchText: '', resolvedName: '', address: '', latitude: null, longitude: null, plannedPauseMinutes: 0, geofenceId: null, deadlineMarginMinutes: 60 }
      ]
    };
  }

  onVehicleChange(vehicleId: number) {
    const v = this.vehicles.find((veh: any) => veh.id === vehicleId);
    this.tourForm.driverId = v?.assignedDriverId || null;
  }

  getVehicleDriver(): string | null {
    const v = this.vehicles.find((veh: any) => veh.id === this.tourForm.vehicleId);
    return v?.assignedDriverName || v?.driverName || null;
  }

  onRecurrenceChange(val: string) {
    if (val === 'weekly') { this.tourForm.recurrenceDays = []; }
  }

  toggleDay(day: string) {
    const idx = this.tourForm.recurrenceDays.indexOf(day);
    if (idx >= 0) this.tourForm.recurrenceDays.splice(idx, 1);
    else this.tourForm.recurrenceDays.push(day);
  }

  addWaypoint() {
    const lastIdx = this.tourForm.waypoints.length - 1;
    this.tourForm.waypoints.splice(lastIdx, 0, {
      searchText: '', resolvedName: '', address: '', latitude: null, longitude: null, plannedPauseMinutes: 0, geofenceId: null, deadlineMarginMinutes: 60
    });
  }

  removeWaypoint(index: number) {
    if (this.tourForm.waypoints.length > 2) {
      this.tourForm.waypoints.splice(index, 1);
      this.updateMapMarkers();
    }
  }

  clearWaypoint(index: number) {
    const wp = this.tourForm.waypoints[index];
    wp.latitude = null;
    wp.longitude = null;
    wp.address = '';
    wp.resolvedName = '';
    wp.searchText = '';
    this.updateMapMarkers();
    this.cdr.detectChanges();
  }

  // ═══════ ADDRESS SEARCH ═══════

  onSearchChange(query: string, index: number) {
    this.activeSearchIdx = index;
    if (!query || query.length < 2) { this.searchResults = []; return; }
    this.searchSubject.next({ query, index });
  }

  onSearchFocus(index: number) { this.activeSearchIdx = index; }

  onSearchBlur() {
    setTimeout(() => { this.activeSearchIdx = -1; this.searchResults = []; this.cdr.detectChanges(); }, 250);
  }

  pickResult(result: any, index: number) {
    const wp = this.tourForm.waypoints[index];
    wp.latitude = parseFloat(result.lat);
    wp.longitude = parseFloat(result.lon);
    wp.address = result.display_name;
    wp.resolvedName = this.getShortName(result.display_name);
    wp.searchText = wp.resolvedName;
    this.searchResults = [];
    this.activeSearchIdx = -1;
    this.updateMapMarkers();
    this.cdr.detectChanges();
  }

  getShortName(displayName: string): string {
    if (!displayName) return '';
    return displayName.split(',')[0]?.trim() || displayName;
  }

  canEstimate(): boolean {
    return this.tourForm.waypoints.length >= 2 &&
      this.tourForm.waypoints.every((wp: any) => wp.latitude && wp.longitude);
  }

  reverseGeocodeWaypoint(index: number, lat: number, lon: number) {
    this.http.get<any>(`/api/nominatim/reverse?lat=${lat}&lon=${lon}&format=json&accept-language=fr`).pipe(
      catchError(() => of(null))
    ).subscribe(result => {
      if (result && this.tourForm.waypoints[index]) {
        const wp = this.tourForm.waypoints[index];
        wp.address = result.display_name || `${lat.toFixed(4)}, ${lon.toFixed(4)}`;
        wp.resolvedName = this.getShortName(wp.address);
        wp.searchText = wp.resolvedName;
        this.cdr.detectChanges();
      }
    });
  }

  // ═══════ ESTIMATION ═══════

  estimateRoute() {
    if (!this.canEstimate()) return;
    this.estimating = true;
    const selectedVehicle = this.vehicles.find((v: any) => v.id === this.tourForm.vehicleId);
    this.apiService.estimateRoute({
      waypoints: this.tourForm.waypoints.map((wp: any) => ({
        latitude: wp.latitude, longitude: wp.longitude, name: wp.resolvedName, address: wp.address, plannedPauseMinutes: wp.plannedPauseMinutes
      })),
      fuelType: selectedVehicle?.fuelType || 'diesel'
    }).subscribe({
      next: (est) => {
        this.estimation = est;
        this.estimating = false;
        if (est.routePoints) this.drawRouteOnMap(est.routePoints);
        this.cdr.detectChanges();
      },
      error: () => { this.estimating = false; this.estimation = null; this.cdr.detectChanges(); }
    });
  }

  // ═══════ SAVE ═══════

  saveTour() {
    if (!this.tourForm.name || !this.tourForm.vehicleId || this.tourForm.waypoints.length < 2) return;
    this.saving = true;
    let recurrence = this.tourForm.recurrence || 'none';
    if (recurrence === 'weekly' && this.tourForm.recurrenceDays.length > 0) {
      recurrence = 'weekly:' + this.tourForm.recurrenceDays.join(',');
    } else if (recurrence === 'custom') {
      recurrence = 'custom:' + this.tourForm.customInterval + ':' + this.tourForm.customUnit;
    }
    const payload = {
      name: this.tourForm.name,
      description: this.tourForm.description || null,
      vehicleId: this.tourForm.vehicleId,
      driverId: this.tourForm.driverId,
      scheduledStartTime: new Date(this.tourForm.scheduledStartTime).toISOString(),
      recurrence: recurrence,
      notes: this.tourForm.notes || null,
      waypoints: this.tourForm.waypoints.map((wp: any) => ({
        name: wp.resolvedName || wp.searchText, address: wp.address,
        latitude: wp.latitude, longitude: wp.longitude, plannedPauseMinutes: wp.plannedPauseMinutes,
        geofenceId: wp.geofenceId || null, deadlineMarginMinutes: wp.deadlineMarginMinutes || 60
      }))
    };
    const obs = this.editingTour
      ? this.apiService.updateTour(this.editingTour.id, payload)
      : this.apiService.createTour(payload);
    obs.subscribe({
      next: () => { this.saving = false; this.closeCreate(); this.loadTours(); this.loadStats(); },
      error: () => { this.saving = false; }
    });
  }

  editTour() {
    if (!this.selectedTour) return;
    this.editingTour = this.selectedTour;
    const rec = this.selectedTour.recurrence || 'none';
    let recType = 'none', recDays: string[] = [], customInt = 1, customUnit = 'weeks';
    if (rec.startsWith('weekly:')) { recType = 'weekly'; recDays = rec.split(':')[1]?.split(',') || []; }
    else if (rec.startsWith('custom:')) { recType = 'custom'; const parts = rec.split(':'); customInt = parseInt(parts[1]) || 1; customUnit = parts[2] || 'weeks'; }
    else if (rec === 'weekly') { recType = 'weekly'; }
    this.tourForm = {
      name: this.selectedTour.name,
      description: this.selectedTour.description || '',
      vehicleId: this.selectedTour.vehicleId,
      driverId: this.selectedTour.driverId,
      scheduledStartTime: this.toLocalDatetime(this.selectedTour.scheduledStartTime),
      recurrence: recType,
      recurrenceDays: recDays,
      customInterval: customInt,
      customUnit: customUnit,
      notes: this.selectedTour.notes || '',
      waypoints: this.selectedTour.waypoints.map((w: any) => ({
        searchText: w.name || w.address || '', resolvedName: w.name || '', address: w.address || '',
        latitude: w.latitude, longitude: w.longitude, plannedPauseMinutes: w.plannedPauseMinutes || 0,
        geofenceId: w.geofenceId || null, deadlineMarginMinutes: w.deadlineMarginMinutes || 60
      }))
    };
    this.estimation = null;
    this.currentView = 'create';
    this.cdr.detectChanges();
    setTimeout(() => this.initTourMap(), 200);
  }

  // ═══════ ACTIONS ═══════

  startSelectedTour() {
    if (!this.selectedTour) return;
    this.apiService.startTour(this.selectedTour.id).subscribe({
      next: () => { this.closeDetail(); this.loadTours(); this.loadStats(); }
    });
  }

  completeSelectedTour() {
    if (!this.selectedTour) return;
    this.apiService.completeTour(this.selectedTour.id).subscribe({
      next: () => { this.closeDetail(); this.loadTours(); this.loadStats(); }
    });
  }

  cancelSelectedTour() {
    if (!this.selectedTour) return;
    if (!confirm('Annuler cette tournee ?')) return;
    this.apiService.cancelTour(this.selectedTour.id).subscribe({
      next: () => { this.closeDetail(); this.loadTours(); this.loadStats(); }
    });
  }

  deleteSelectedTour() {
    if (!this.selectedTour) return;
    if (!confirm('Supprimer définitivement cette tournée ?')) return;
    this.apiService.deleteTour(this.selectedTour.id).subscribe({
      next: () => { this.closeDetail(); this.loadTours(); this.loadStats(); },
      error: (err: any) => { console.error('Error deleting tour:', err); alert('Erreur lors de la suppression'); }
    });
  }

  quickEdit(tour: any) {
    this.selectedTour = tour;
    this.editTour();
  }

  quickDelete(tour: any) {
    if (!confirm('Supprimer définitivement cette tournée ?')) return;
    this.apiService.deleteTour(tour.id).subscribe({
      next: () => { this.loadTours(); this.loadStats(); },
      error: (err: any) => { console.error('Error deleting tour:', err); alert('Erreur lors de la suppression'); }
    });
  }

  // ═══════ MAP ═══════

  initTourMap() {
    if (this.tourMap) { this.tourMap.remove(); this.tourMap = null; }
    if (!this.tourMapEl?.nativeElement) return;
    try {
      this.tourMap = L.map(this.tourMapEl.nativeElement).setView([34.5, 9.5], 6);
      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { attribution: '© OpenStreetMap' }).addTo(this.tourMap);
      this.tourMap.on('click', (e: any) => {
        this.zone.run(() => {
          const lat = e.latlng.lat;
          const lng = e.latlng.lng;
          const emptyIdx = this.tourForm.waypoints.findIndex((wp: any) => !wp.latitude);
          if (emptyIdx >= 0) {
            this.tourForm.waypoints[emptyIdx].latitude = lat;
            this.tourForm.waypoints[emptyIdx].longitude = lng;
            this.reverseGeocodeWaypoint(emptyIdx, lat, lng);
          } else {
            const lastIdx = this.tourForm.waypoints.length - 1;
            this.tourForm.waypoints.splice(lastIdx, 0, {
              searchText: '', resolvedName: '', address: '', latitude: lat, longitude: lng, plannedPauseMinutes: 0
            });
            this.reverseGeocodeWaypoint(lastIdx, lat, lng);
          }
          this.updateMapMarkers();
          this.cdr.detectChanges();
        });
      });
      this.updateMapMarkers();
    } catch (e) { console.error('Map init error', e); }
  }

  initDetailMap() {
    if (this.detailMap) { this.detailMap.remove(); this.detailMap = null; }
    if (!this.detailMapEl?.nativeElement || !this.selectedTour) return;
    try {
      this.detailMap = L.map(this.detailMapEl.nativeElement).setView([34.5, 9.5], 6);
      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { attribution: '© OpenStreetMap' }).addTo(this.detailMap);
      const wps = this.selectedTour.waypoints || [];
      if (wps.length > 0) {
        const bounds: any[] = [];
        wps.forEach((wp: any) => {
          const color = wp.type === 'origin' ? '#22c55e' : wp.type === 'destination' ? '#ef4444' : '#3b82f6';
          L.circleMarker([wp.latitude, wp.longitude], { radius: 7, color, fillColor: color, fillOpacity: 0.9, weight: 2 })
            .addTo(this.detailMap).bindPopup(`<b>${wp.name || wp.address || wp.type}</b>`);
          bounds.push([wp.latitude, wp.longitude]);
        });
        if (bounds.length >= 2) {
          L.polyline(bounds, { color: '#3b82f6', weight: 3, dashArray: '6 4' }).addTo(this.detailMap);
          this.detailMap.fitBounds(bounds, { padding: [40, 40] });
        }
      }
    } catch (e) { console.error('Detail map error', e); }
  }

  updateMapMarkers() {
    if (!this.tourMap) return;
    this.mapMarkers.forEach(m => m.remove());
    this.mapMarkers = [];
    this.routeLines.forEach(l => l.remove());
    this.routeLines = [];
    const bounds: any[] = [];
    this.tourForm.waypoints.forEach((wp: any, i: number) => {
      if (wp.latitude && wp.longitude) {
        const isFirst = i === 0;
        const isLast = i === this.tourForm.waypoints.length - 1;
        const color = isFirst ? '#22c55e' : isLast ? '#ef4444' : '#3b82f6';
        const marker = L.circleMarker([wp.latitude, wp.longitude], { radius: 8, color, fillColor: color, fillOpacity: 0.9, weight: 2 })
          .addTo(this.tourMap).bindPopup(wp.resolvedName || wp.searchText || `Point ${i + 1}`);
        this.mapMarkers.push(marker);
        bounds.push([wp.latitude, wp.longitude]);
      }
    });
    if (bounds.length >= 2) {
      const line = L.polyline(bounds, { color: '#3b82f6', weight: 3, dashArray: '6 4', opacity: .7 }).addTo(this.tourMap);
      this.routeLines.push(line);
      this.tourMap.fitBounds(bounds, { padding: [40, 40] });
    } else if (bounds.length === 1) {
      this.tourMap.setView(bounds[0], 13);
    }
  }

  drawRouteOnMap(points: any[]) {
    if (!this.tourMap || !points || points.length < 2) return;
    if (this.routeLayer) this.tourMap.removeLayer(this.routeLayer);
    this.routeLines.forEach(l => l.remove());
    this.routeLines = [];
    const latlngs = points.map((p: any) => [p.lat, p.lng]);
    this.routeLayer = L.polyline(latlngs, { color: '#3b82f6', weight: 4 }).addTo(this.tourMap);
    this.tourMap.fitBounds(this.routeLayer.getBounds(), { padding: [40, 40] });
    // Re-add markers on top
    this.mapMarkers.forEach(m => m.bringToFront());
  }

  // ═══════ HELPERS ═══════

  getStatusLabel(s: string): string {
    return ({ planned: 'Planifiee', in_progress: 'En cours', completed: 'Terminee', cancelled: 'Annulee' } as any)[s] || s;
  }
  getWaypointTypeLabel(t: string): string {
    return ({ origin: 'Depart', waypoint: 'Arret', destination: 'Arrivee' } as any)[t] || t;
  }
  getPauseLabel(r: string): string {
    return ({ break: 'Pause', fuel: 'Carburant', delivery: 'Livraison', rest: 'Repos', other: 'Autre' } as any)[r] || r;
  }
  formatDate(d: string): string {
    if (!d) return '-';
    return new Date(d).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }
  formatDateShort(d: string): string {
    if (!d) return '-';
    return new Date(d).toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' });
  }
  formatTime(d: string): string {
    if (!d) return '-';
    return new Date(d).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }
  formatDuration(m: number): string {
    if (!m) return '0 min';
    const h = Math.floor(m / 60);
    const mm = m % 60;
    return h > 0 ? `${h}h${mm > 0 ? mm.toString().padStart(2, '0') : ''}` : `${mm} min`;
  }
  truncate(s: string, len: number): string {
    if (!s) return '';
    return s.length > len ? s.substring(0, len) + '...' : s;
  }
  toLocalDatetime(d: string): string {
    if (!d) return '';
    const dt = new Date(d);
    const p = (n: number) => n.toString().padStart(2, '0');
    return `${dt.getFullYear()}-${p(dt.getMonth() + 1)}-${p(dt.getDate())}T${p(dt.getHours())}:${p(dt.getMinutes())}`;
  }
}
