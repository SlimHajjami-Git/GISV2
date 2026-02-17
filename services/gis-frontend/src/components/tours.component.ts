import { Component, OnInit, OnDestroy, ViewChild, ElementRef, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../services/api.service';
import { forkJoin } from 'rxjs';

declare let L: any;

@Component({
  selector: 'app-tours',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="tours-container">
      <!-- Header -->
      <div class="tours-header">
        <div class="header-left">
          <h1>Gestion des Tournées</h1>
          <p class="subtitle">Planifiez, suivez et analysez vos tournées</p>
        </div>
        <div class="header-actions">
          <button class="btn-primary" (click)="openCreateModal()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Nouvelle Tournée
          </button>
        </div>
      </div>

      <!-- Stats Cards -->
      <div class="stats-row">
        <div class="stat-card">
          <div class="stat-icon total"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M9 17H5a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm12-2h-4a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2z"/><polyline points="9 17 12 5 15 17"/></svg></div>
          <div class="stat-info"><span class="stat-value">{{stats.total}}</span><span class="stat-label">Total</span></div>
        </div>
        <div class="stat-card">
          <div class="stat-icon planned"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg></div>
          <div class="stat-info"><span class="stat-value">{{stats.planned}}</span><span class="stat-label">Planifiées</span></div>
        </div>
        <div class="stat-card">
          <div class="stat-icon progress"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg></div>
          <div class="stat-info"><span class="stat-value">{{stats.inProgress}}</span><span class="stat-label">En cours</span></div>
        </div>
        <div class="stat-card">
          <div class="stat-icon completed"><svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/></svg></div>
          <div class="stat-info"><span class="stat-value">{{stats.completed}}</span><span class="stat-label">Terminées</span></div>
        </div>
      </div>

      <!-- Filters -->
      <div class="filters-row">
        <div class="filter-group">
          <select [(ngModel)]="filterStatus" (change)="loadTours()">
            <option value="">Tous les statuts</option>
            <option value="planned">Planifiées</option>
            <option value="in_progress">En cours</option>
            <option value="completed">Terminées</option>
            <option value="cancelled">Annulées</option>
          </select>
        </div>
        <div class="filter-group">
          <select [(ngModel)]="filterVehicleId" (change)="loadTours()">
            <option [ngValue]="null">Tous les véhicules</option>
            <option *ngFor="let v of vehicles" [ngValue]="v.id">{{v.name}} ({{v.plate}})</option>
          </select>
        </div>
        <div class="filter-group">
          <select [(ngModel)]="filterDriverId" (change)="loadTours()">
            <option [ngValue]="null">Tous les chauffeurs</option>
            <option *ngFor="let d of drivers" [ngValue]="d.id">{{d.firstName}} {{d.lastName}}</option>
          </select>
        </div>
      </div>

      <!-- Tours List -->
      <div class="tours-list" *ngIf="!loading && tours.length > 0">
        <div class="tour-card" *ngFor="let tour of tours" (click)="openDetailModal(tour)">
          <div class="tour-status-bar" [class]="'status-' + tour.status"></div>
          <div class="tour-card-body">
            <div class="tour-card-header">
              <h3>{{tour.name}}</h3>
              <span class="status-badge" [class]="'badge-' + tour.status">{{getStatusLabel(tour.status)}}</span>
            </div>
            <div class="tour-route">
              <div class="route-point origin">
                <span class="dot"></span>
                <span class="address">{{tour.origin || 'Départ'}}</span>
              </div>
              <div class="route-line" *ngIf="tour.waypointCount > 2">
                <span class="waypoint-count">{{tour.waypointCount - 2}} arrêt(s)</span>
              </div>
              <div class="route-point destination">
                <span class="dot"></span>
                <span class="address">{{tour.destination || 'Arrivée'}}</span>
              </div>
            </div>
            <div class="tour-meta">
              <div class="meta-item">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
                <span>{{tour.vehicleName}}</span>
              </div>
              <div class="meta-item" *ngIf="tour.driverName">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>
                <span>{{tour.driverName}}</span>
              </div>
              <div class="meta-item">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                <span>{{formatDate(tour.scheduledStartTime)}}</span>
              </div>
              <div class="meta-item">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>
                <span>{{tour.estimatedDistanceKm | number:'1.1-1'}} km</span>
              </div>
              <div class="meta-item">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg>
                <span>~{{tour.estimatedFuelLiters | number:'1.1-1'}} L</span>
              </div>
              <div class="meta-item duration">
                <span>{{formatDuration(tour.estimatedDurationMinutes)}}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Empty state -->
      <div class="empty-state" *ngIf="!loading && tours.length === 0">
        <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.5"><path d="M9 17H5a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2zm12-2h-4a2 2 0 0 0-2 2 2 2 0 0 0 2 2h2a2 2 0 0 0 2-2z"/><polyline points="9 17 12 5 15 17"/></svg>
        <h3>Aucune tournée</h3>
        <p>Créez votre première tournée pour commencer</p>
        <button class="btn-primary" (click)="openCreateModal()">Créer une tournée</button>
      </div>

      <!-- Loading -->
      <div class="loading" *ngIf="loading">
        <div class="spinner"></div>
        <p>Chargement...</p>
      </div>

      <!-- ═══════ CREATE/EDIT MODAL ═══════ -->
      <div class="modal-overlay" *ngIf="showCreateModal" (click)="closeCreateModal()">
        <div class="modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <h2>{{editingTour ? 'Modifier la tournée' : 'Nouvelle Tournée'}}</h2>
            <button class="close-btn" (click)="closeCreateModal()">&times;</button>
          </div>
          <div class="modal-body">
            <div class="form-grid">
              <div class="form-group full-width">
                <label>Nom de la tournée</label>
                <input type="text" [(ngModel)]="tourForm.name" placeholder="Ex: Livraison Tunis-Sousse">
              </div>
              <div class="form-group">
                <label>Véhicule</label>
                <select [(ngModel)]="tourForm.vehicleId">
                  <option [ngValue]="null">Sélectionner...</option>
                  <option *ngFor="let v of vehicles" [ngValue]="v.id">{{v.name}} ({{v.plate}})</option>
                </select>
              </div>
              <div class="form-group">
                <label>Chauffeur</label>
                <select [(ngModel)]="tourForm.driverId">
                  <option [ngValue]="null">Sélectionner...</option>
                  <option *ngFor="let d of drivers" [ngValue]="d.id">{{d.firstName}} {{d.lastName}}</option>
                </select>
              </div>
              <div class="form-group">
                <label>Date & heure de départ</label>
                <input type="datetime-local" [(ngModel)]="tourForm.scheduledStartTime">
              </div>
              <div class="form-group">
                <label>Description</label>
                <input type="text" [(ngModel)]="tourForm.description" placeholder="Description optionnelle">
              </div>
            </div>

            <!-- Waypoints -->
            <div class="waypoints-section">
              <div class="section-header">
                <h3>Itinéraire</h3>
                <button class="btn-sm" (click)="addWaypoint()">+ Ajouter un arrêt</button>
              </div>
              <div class="waypoints-list">
                <div class="waypoint-item" *ngFor="let wp of tourForm.waypoints; let i = index; let first = first; let last = last">
                  <div class="waypoint-marker" [class.origin]="first" [class.destination]="last && tourForm.waypoints.length > 1">
                    <span class="marker-dot"></span>
                    <span class="marker-line" *ngIf="!last"></span>
                  </div>
                  <div class="waypoint-fields">
                    <div class="wp-row">
                      <input type="text" [(ngModel)]="wp.name" [placeholder]="first ? 'Point de départ' : (last && tourForm.waypoints.length > 1 ? 'Destination' : 'Arrêt ' + i)" class="wp-name">
                      <input type="text" [(ngModel)]="wp.address" placeholder="Adresse" class="wp-address">
                    </div>
                    <div class="wp-row coords">
                      <input type="number" [(ngModel)]="wp.latitude" placeholder="Latitude" step="0.0001" class="wp-coord">
                      <input type="number" [(ngModel)]="wp.longitude" placeholder="Longitude" step="0.0001" class="wp-coord">
                      <input type="number" [(ngModel)]="wp.plannedPauseMinutes" placeholder="Pause (min)" class="wp-pause" min="0">
                      <button class="btn-icon danger" *ngIf="!first && !(last && tourForm.waypoints.length <= 2)" (click)="removeWaypoint(i)" title="Supprimer">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              <!-- Map for waypoint selection -->
              <div class="map-container" id="tourMap" #tourMapEl></div>

              <!-- Route estimation result -->
              <div class="estimation-result" *ngIf="estimation">
                <div class="est-item">
                  <strong>Distance:</strong> {{estimation.distanceKm | number:'1.1-1'}} km
                </div>
                <div class="est-item">
                  <strong>Durée:</strong> {{formatDuration(estimation.durationMinutes)}}
                </div>
                <div class="est-item">
                  <strong>Durée + pauses:</strong> {{formatDuration(estimation.durationWithPausesMinutes)}}
                </div>
                <div class="est-item">
                  <strong>Carburant estimé:</strong> ~{{estimation.estimatedFuelLiters | number:'1.1-1'}} L
                </div>
              </div>
              <button class="btn-secondary" (click)="estimateRoute()" [disabled]="tourForm.waypoints.length < 2 || estimating">
                {{estimating ? 'Calcul en cours...' : 'Estimer l\\'itinéraire'}}
              </button>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn-cancel" (click)="closeCreateModal()">Annuler</button>
            <button class="btn-primary" (click)="saveTour()" [disabled]="saving">
              {{saving ? 'Enregistrement...' : (editingTour ? 'Modifier' : 'Créer la tournée')}}
            </button>
          </div>
        </div>
      </div>

      <!-- ═══════ DETAIL MODAL ═══════ -->
      <div class="modal-overlay" *ngIf="showDetailModal && selectedTour" (click)="closeDetailModal()">
        <div class="modal detail-modal" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <div>
              <h2>{{selectedTour.name}}</h2>
              <span class="status-badge" [class]="'badge-' + selectedTour.status">{{getStatusLabel(selectedTour.status)}}</span>
            </div>
            <button class="close-btn" (click)="closeDetailModal()">&times;</button>
          </div>
          <div class="modal-body">
            <!-- Detail Map -->
            <div class="detail-map-container" id="detailMap" #detailMapEl></div>

            <!-- Tour Info Grid -->
            <div class="detail-grid">
              <div class="detail-section">
                <h4>Informations</h4>
                <div class="info-row"><span class="label">Véhicule</span><span class="value">{{selectedTour.vehicleName}} ({{selectedTour.vehiclePlate}})</span></div>
                <div class="info-row" *ngIf="selectedTour.driverName"><span class="label">Chauffeur</span><span class="value">{{selectedTour.driverName}}</span></div>
                <div class="info-row"><span class="label">Départ prévu</span><span class="value">{{formatDate(selectedTour.scheduledStartTime)}}</span></div>
                <div class="info-row" *ngIf="selectedTour.scheduledEndTime"><span class="label">Arrivée prévue</span><span class="value">{{formatDate(selectedTour.scheduledEndTime)}}</span></div>
                <div class="info-row" *ngIf="selectedTour.actualStartTime"><span class="label">Départ réel</span><span class="value">{{formatDate(selectedTour.actualStartTime)}}</span></div>
                <div class="info-row" *ngIf="selectedTour.actualEndTime"><span class="label">Arrivée réelle</span><span class="value">{{formatDate(selectedTour.actualEndTime)}}</span></div>
              </div>

              <!-- Comparison table -->
              <div class="detail-section comparison" *ngIf="selectedTour.status === 'completed'">
                <h4>Comparaison Estimé vs Réel</h4>
                <table class="comparison-table">
                  <thead><tr><th></th><th>Estimé</th><th>Réel</th><th>Écart</th></tr></thead>
                  <tbody>
                    <tr>
                      <td>Distance</td>
                      <td>{{selectedTour.estimatedDistanceKm | number:'1.1-1'}} km</td>
                      <td>{{(selectedTour.actualDistanceKm || '-') | number:'1.1-1'}} km</td>
                      <td [class.positive]="selectedTour.distanceDiffKm > 0" [class.negative]="selectedTour.distanceDiffKm < 0">
                        {{selectedTour.distanceDiffKm !== null ? ((selectedTour.distanceDiffKm > 0 ? '+' : '') + (selectedTour.distanceDiffKm | number:'1.1-1') + ' km') : '-'}}
                      </td>
                    </tr>
                    <tr>
                      <td>Durée</td>
                      <td>{{formatDuration(selectedTour.estimatedDurationMinutes)}}</td>
                      <td>{{selectedTour.actualDurationMinutes ? formatDuration(selectedTour.actualDurationMinutes) : '-'}}</td>
                      <td [class.positive]="selectedTour.delayMinutes > 0" [class.negative]="selectedTour.delayMinutes < 0">
                        {{selectedTour.delayMinutes !== null ? ((selectedTour.delayMinutes > 0 ? '+' : '') + selectedTour.delayMinutes + ' min') : '-'}}
                      </td>
                    </tr>
                    <tr>
                      <td>Carburant</td>
                      <td>{{selectedTour.estimatedFuelLiters | number:'1.1-1'}} L</td>
                      <td>{{selectedTour.actualFuelLiters ? (selectedTour.actualFuelLiters | number:'1.1-1') + ' L' : '-'}}</td>
                      <td [class.positive]="selectedTour.fuelDiffLiters > 0" [class.negative]="selectedTour.fuelDiffLiters < 0">
                        {{selectedTour.fuelDiffLiters !== null ? ((selectedTour.fuelDiffLiters > 0 ? '+' : '') + (selectedTour.fuelDiffLiters | number:'1.1-1') + ' L') : '-'}}
                      </td>
                    </tr>
                    <tr>
                      <td>Pauses</td>
                      <td colspan="2">{{selectedTour.totalPauseMinutes}} min</td>
                      <td>{{selectedTour.pauses?.length || 0}} pause(s)</td>
                    </tr>
                  </tbody>
                </table>
              </div>

              <!-- Estimation only for non-completed -->
              <div class="detail-section" *ngIf="selectedTour.status !== 'completed'">
                <h4>Estimation</h4>
                <div class="info-row"><span class="label">Distance</span><span class="value">{{selectedTour.estimatedDistanceKm | number:'1.1-1'}} km</span></div>
                <div class="info-row"><span class="label">Durée estimée</span><span class="value">{{formatDuration(selectedTour.estimatedDurationMinutes)}}</span></div>
                <div class="info-row"><span class="label">Carburant estimé</span><span class="value">~{{selectedTour.estimatedFuelLiters | number:'1.1-1'}} L</span></div>
                <div class="info-row"><span class="label">Pauses prévues</span><span class="value">{{selectedTour.totalPauseMinutes}} min</span></div>
              </div>
            </div>

            <!-- Waypoints timeline -->
            <div class="detail-section">
              <h4>Points de passage</h4>
              <div class="timeline">
                <div class="timeline-item" *ngFor="let wp of selectedTour.waypoints" [class.completed]="wp.isCompleted">
                  <div class="timeline-marker" [class.origin]="wp.type === 'origin'" [class.destination]="wp.type === 'destination'">
                    <svg *ngIf="wp.isCompleted" width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><polyline points="20 6 9 17 4 12"/></svg>
                  </div>
                  <div class="timeline-content">
                    <div class="timeline-header">
                      <strong>{{wp.name || wp.address || getWaypointTypeLabel(wp.type)}}</strong>
                      <span class="timeline-time" *ngIf="wp.estimatedArrivalTime">{{formatTime(wp.estimatedArrivalTime)}}</span>
                    </div>
                    <div class="timeline-detail" *ngIf="wp.address">{{wp.address}}</div>
                    <div class="timeline-detail" *ngIf="wp.actualArrivalTime">
                      Arrivée réelle: {{formatTime(wp.actualArrivalTime)}}
                      <span class="delay" *ngIf="wp.arrivalDelay" [class.late]="wp.arrivalDelay > 0" [class.early]="wp.arrivalDelay < 0">
                        ({{wp.arrivalDelay > 0 ? '+' : ''}}{{wp.arrivalDelay}} min)
                      </span>
                    </div>
                    <div class="timeline-detail pause" *ngIf="wp.plannedPauseMinutes > 0">Pause prévue: {{wp.plannedPauseMinutes}} min</div>
                  </div>
                </div>
              </div>
            </div>

            <!-- Pauses -->
            <div class="detail-section" *ngIf="selectedTour.pauses?.length > 0">
              <h4>Pauses effectuées</h4>
              <div class="pauses-list">
                <div class="pause-item" *ngFor="let pause of selectedTour.pauses">
                  <div class="pause-icon">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/></svg>
                  </div>
                  <div class="pause-info">
                    <span class="pause-reason">{{getPauseLabel(pause.reason)}}</span>
                    <span class="pause-time">{{formatTime(pause.startTime)}} - {{pause.endTime ? formatTime(pause.endTime) : 'En cours'}}</span>
                    <span class="pause-duration" *ngIf="pause.durationMinutes">{{pause.durationMinutes}} min</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div class="modal-footer">
            <button class="btn-cancel" (click)="closeDetailModal()">Fermer</button>
            <button class="btn-secondary" *ngIf="selectedTour.status === 'planned'" (click)="editTour()">Modifier</button>
            <button class="btn-danger" *ngIf="selectedTour.status === 'planned' || selectedTour.status === 'in_progress'" (click)="cancelSelectedTour()">Annuler</button>
            <button class="btn-primary" *ngIf="selectedTour.status === 'planned'" (click)="startSelectedTour()">Démarrer</button>
            <button class="btn-success" *ngIf="selectedTour.status === 'in_progress'" (click)="completeSelectedTour()">Terminer</button>
          </div>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .tours-container { padding: 24px; max-width: 1400px; margin: 0 auto; }

    /* Header */
    .tours-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    .tours-header h1 { font-size: 24px; font-weight: 700; color: #1e293b; margin: 0; }
    .subtitle { color: #64748b; font-size: 14px; margin: 4px 0 0; }
    .btn-primary { display: flex; align-items: center; gap: 8px; background: #3b82f6; color: white; border: none; padding: 10px 20px; border-radius: 8px; font-size: 14px; font-weight: 500; cursor: pointer; transition: background 0.2s; }
    .btn-primary:hover { background: #2563eb; }
    .btn-primary:disabled { opacity: 0.6; cursor: not-allowed; }
    .btn-secondary { background: #f1f5f9; color: #475569; border: 1px solid #e2e8f0; padding: 8px 16px; border-radius: 8px; font-size: 13px; cursor: pointer; transition: all 0.2s; }
    .btn-secondary:hover { background: #e2e8f0; }
    .btn-secondary:disabled { opacity: 0.5; cursor: not-allowed; }
    .btn-danger { background: #fee2e2; color: #dc2626; border: 1px solid #fecaca; padding: 8px 16px; border-radius: 8px; font-size: 13px; cursor: pointer; }
    .btn-danger:hover { background: #fecaca; }
    .btn-success { background: #10b981; color: white; border: none; padding: 10px 20px; border-radius: 8px; font-size: 14px; font-weight: 500; cursor: pointer; }
    .btn-success:hover { background: #059669; }
    .btn-cancel { background: transparent; color: #64748b; border: 1px solid #e2e8f0; padding: 8px 16px; border-radius: 8px; font-size: 13px; cursor: pointer; }

    /* Stats */
    .stats-row { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; margin-bottom: 24px; }
    .stat-card { background: white; border-radius: 12px; padding: 16px; display: flex; align-items: center; gap: 16px; border: 1px solid #e2e8f0; }
    .stat-icon { width: 44px; height: 44px; border-radius: 10px; display: flex; align-items: center; justify-content: center; }
    .stat-icon.total { background: #eff6ff; color: #3b82f6; }
    .stat-icon.planned { background: #fef3c7; color: #f59e0b; }
    .stat-icon.progress { background: #dbeafe; color: #2563eb; }
    .stat-icon.completed { background: #d1fae5; color: #10b981; }
    .stat-value { font-size: 24px; font-weight: 700; color: #1e293b; display: block; }
    .stat-label { font-size: 12px; color: #94a3b8; }

    /* Filters */
    .filters-row { display: flex; gap: 12px; margin-bottom: 20px; }
    .filter-group select { padding: 8px 12px; border: 1px solid #e2e8f0; border-radius: 8px; font-size: 13px; color: #475569; background: white; min-width: 180px; }

    /* Tour cards */
    .tours-list { display: grid; gap: 12px; }
    .tour-card { background: white; border-radius: 12px; border: 1px solid #e2e8f0; cursor: pointer; transition: all 0.2s; display: flex; overflow: hidden; }
    .tour-card:hover { border-color: #3b82f6; box-shadow: 0 4px 12px rgba(59,130,246,0.1); transform: translateY(-1px); }
    .tour-status-bar { width: 4px; flex-shrink: 0; }
    .tour-status-bar.status-planned { background: #f59e0b; }
    .tour-status-bar.status-in_progress { background: #3b82f6; }
    .tour-status-bar.status-completed { background: #10b981; }
    .tour-status-bar.status-cancelled { background: #94a3b8; }
    .tour-card-body { padding: 16px 20px; flex: 1; }
    .tour-card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
    .tour-card-header h3 { font-size: 16px; font-weight: 600; color: #1e293b; margin: 0; }
    .status-badge { padding: 4px 10px; border-radius: 20px; font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.5px; }
    .badge-planned { background: #fef3c7; color: #b45309; }
    .badge-in_progress { background: #dbeafe; color: #1d4ed8; }
    .badge-completed { background: #d1fae5; color: #065f46; }
    .badge-cancelled { background: #f1f5f9; color: #64748b; }

    /* Route display */
    .tour-route { display: flex; align-items: center; gap: 8px; margin-bottom: 12px; padding: 8px 12px; background: #f8fafc; border-radius: 8px; }
    .route-point { display: flex; align-items: center; gap: 6px; }
    .route-point .dot { width: 8px; height: 8px; border-radius: 50%; }
    .route-point.origin .dot { background: #10b981; }
    .route-point.destination .dot { background: #ef4444; }
    .route-point .address { font-size: 13px; color: #475569; max-width: 200px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .route-line { flex: 1; border-top: 2px dashed #cbd5e1; position: relative; min-width: 40px; text-align: center; }
    .waypoint-count { font-size: 11px; color: #94a3b8; background: #f8fafc; padding: 0 6px; position: relative; top: -8px; }

    /* Meta */
    .tour-meta { display: flex; flex-wrap: wrap; gap: 16px; }
    .meta-item { display: flex; align-items: center; gap: 5px; font-size: 13px; color: #64748b; }
    .meta-item svg { color: #94a3b8; }
    .meta-item.duration { font-weight: 600; color: #3b82f6; }

    /* Empty & Loading */
    .empty-state { text-align: center; padding: 80px 20px; color: #94a3b8; }
    .empty-state h3 { margin: 16px 0 8px; color: #475569; }
    .empty-state .btn-primary { margin: 16px auto 0; }
    .loading { text-align: center; padding: 60px 20px; color: #94a3b8; }
    .spinner { width: 32px; height: 32px; border: 3px solid #e2e8f0; border-top-color: #3b82f6; border-radius: 50%; animation: spin 0.8s linear infinite; margin: 0 auto 12px; }
    @keyframes spin { to { transform: rotate(360deg); } }

    /* Modal */
    .modal-overlay { position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.5); backdrop-filter: blur(4px); z-index: 1000; display: flex; align-items: center; justify-content: center; }
    .modal { background: white; border-radius: 16px; width: 95%; max-width: 780px; max-height: 90vh; overflow-y: auto; box-shadow: 0 25px 50px rgba(0,0,0,0.25); }
    .detail-modal { max-width: 860px; }
    .modal-header { display: flex; justify-content: space-between; align-items: center; padding: 20px 24px; border-bottom: 1px solid #e2e8f0; }
    .modal-header h2 { font-size: 20px; font-weight: 700; color: #1e293b; margin: 0; }
    .modal-header .status-badge { margin-left: 12px; }
    .close-btn { background: none; border: none; font-size: 24px; color: #94a3b8; cursor: pointer; padding: 0 4px; }
    .modal-body { padding: 24px; }
    .modal-footer { display: flex; justify-content: flex-end; gap: 8px; padding: 16px 24px; border-top: 1px solid #e2e8f0; }

    /* Form */
    .form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 24px; }
    .form-group { display: flex; flex-direction: column; gap: 6px; }
    .form-group.full-width { grid-column: 1 / -1; }
    .form-group label { font-size: 13px; font-weight: 600; color: #475569; }
    .form-group input, .form-group select { padding: 8px 12px; border: 1px solid #e2e8f0; border-radius: 8px; font-size: 14px; color: #1e293b; }
    .form-group input:focus, .form-group select:focus { outline: none; border-color: #3b82f6; box-shadow: 0 0 0 3px rgba(59,130,246,0.1); }

    /* Waypoints */
    .waypoints-section { border-top: 1px solid #e2e8f0; padding-top: 20px; }
    .section-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; }
    .section-header h3 { font-size: 16px; font-weight: 600; color: #1e293b; margin: 0; }
    .btn-sm { background: #eff6ff; color: #3b82f6; border: none; padding: 6px 12px; border-radius: 6px; font-size: 12px; font-weight: 600; cursor: pointer; }
    .btn-sm:hover { background: #dbeafe; }
    .waypoints-list { display: flex; flex-direction: column; gap: 8px; margin-bottom: 16px; }
    .waypoint-item { display: flex; gap: 12px; }
    .waypoint-marker { display: flex; flex-direction: column; align-items: center; width: 20px; padding-top: 12px; }
    .marker-dot { width: 12px; height: 12px; border-radius: 50%; background: #94a3b8; border: 2px solid white; box-shadow: 0 0 0 2px #94a3b8; }
    .waypoint-marker.origin .marker-dot { background: #10b981; box-shadow: 0 0 0 2px #10b981; }
    .waypoint-marker.destination .marker-dot { background: #ef4444; box-shadow: 0 0 0 2px #ef4444; }
    .marker-line { flex: 1; width: 2px; background: #cbd5e1; margin-top: 4px; min-height: 30px; }
    .waypoint-fields { flex: 1; }
    .wp-row { display: flex; gap: 8px; margin-bottom: 6px; }
    .wp-row.coords { margin-bottom: 0; }
    .wp-name { flex: 1; padding: 6px 10px; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 13px; }
    .wp-address { flex: 2; padding: 6px 10px; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 13px; }
    .wp-coord { width: 110px; padding: 6px 10px; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 12px; }
    .wp-pause { width: 90px; padding: 6px 10px; border: 1px solid #e2e8f0; border-radius: 6px; font-size: 12px; }
    .btn-icon { border: none; background: none; cursor: pointer; padding: 4px; border-radius: 4px; }
    .btn-icon.danger { color: #ef4444; }
    .btn-icon.danger:hover { background: #fee2e2; }

    /* Map */
    .map-container { height: 280px; border-radius: 10px; border: 1px solid #e2e8f0; margin-bottom: 16px; }
    .detail-map-container { height: 300px; border-radius: 10px; border: 1px solid #e2e8f0; margin-bottom: 20px; }

    /* Estimation */
    .estimation-result { display: grid; grid-template-columns: repeat(4, 1fr); gap: 12px; padding: 12px; background: #f0fdf4; border-radius: 8px; border: 1px solid #bbf7d0; margin-bottom: 12px; }
    .est-item { font-size: 13px; color: #166534; }
    .est-item strong { display: block; font-size: 11px; color: #15803d; margin-bottom: 2px; }

    /* Detail grid */
    .detail-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; margin-bottom: 20px; }
    .detail-section { margin-bottom: 16px; }
    .detail-section h4 { font-size: 14px; font-weight: 700; color: #1e293b; margin: 0 0 12px; padding-bottom: 8px; border-bottom: 1px solid #e2e8f0; }
    .detail-section.comparison { grid-column: 1 / -1; }
    .info-row { display: flex; justify-content: space-between; padding: 6px 0; font-size: 13px; }
    .info-row .label { color: #64748b; }
    .info-row .value { color: #1e293b; font-weight: 500; }

    /* Comparison table */
    .comparison-table { width: 100%; border-collapse: collapse; font-size: 13px; }
    .comparison-table th { text-align: left; padding: 8px 12px; background: #f8fafc; color: #64748b; font-weight: 600; border-bottom: 1px solid #e2e8f0; }
    .comparison-table td { padding: 8px 12px; border-bottom: 1px solid #f1f5f9; color: #1e293b; }
    .comparison-table .positive { color: #ef4444; font-weight: 600; }
    .comparison-table .negative { color: #10b981; font-weight: 600; }

    /* Timeline */
    .timeline { padding-left: 8px; }
    .timeline-item { display: flex; gap: 12px; padding-bottom: 16px; position: relative; }
    .timeline-item:not(:last-child)::before { content: ''; position: absolute; left: 11px; top: 28px; bottom: 0; width: 2px; background: #e2e8f0; }
    .timeline-item.completed:not(:last-child)::before { background: #10b981; }
    .timeline-marker { width: 24px; height: 24px; border-radius: 50%; background: #e2e8f0; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .timeline-item.completed .timeline-marker { background: #10b981; color: white; }
    .timeline-marker.origin { background: #10b981; }
    .timeline-marker.destination { background: #ef4444; }
    .timeline-content { flex: 1; }
    .timeline-header { display: flex; justify-content: space-between; align-items: center; }
    .timeline-header strong { font-size: 14px; color: #1e293b; }
    .timeline-time { font-size: 12px; color: #94a3b8; }
    .timeline-detail { font-size: 12px; color: #64748b; margin-top: 2px; }
    .timeline-detail.pause { color: #f59e0b; }
    .delay.late { color: #ef4444; font-weight: 600; }
    .delay.early { color: #10b981; font-weight: 600; }

    /* Pauses list */
    .pauses-list { display: flex; flex-direction: column; gap: 8px; }
    .pause-item { display: flex; align-items: center; gap: 12px; padding: 8px 12px; background: #f8fafc; border-radius: 8px; }
    .pause-icon { color: #f59e0b; }
    .pause-info { display: flex; gap: 16px; font-size: 13px; color: #475569; }
    .pause-reason { font-weight: 600; color: #1e293b; }
    .pause-duration { color: #3b82f6; font-weight: 600; }

    @media (max-width: 768px) {
      .stats-row { grid-template-columns: repeat(2, 1fr); }
      .filters-row { flex-wrap: wrap; }
      .form-grid { grid-template-columns: 1fr; }
      .detail-grid { grid-template-columns: 1fr; }
      .estimation-result { grid-template-columns: repeat(2, 1fr); }
      .wp-row { flex-wrap: wrap; }
    }
  `]
})
export class ToursComponent implements OnInit, OnDestroy {
  @ViewChild('tourMapEl') tourMapEl!: ElementRef;
  @ViewChild('detailMapEl') detailMapEl!: ElementRef;

  tours: any[] = [];
  vehicles: any[] = [];
  drivers: any[] = [];
  stats = { total: 0, planned: 0, inProgress: 0, completed: 0, cancelled: 0 };

  loading = false;
  saving = false;
  estimating = false;

  filterStatus = '';
  filterVehicleId: number | null = null;
  filterDriverId: number | null = null;

  showCreateModal = false;
  showDetailModal = false;
  editingTour: any = null;
  selectedTour: any = null;
  estimation: any = null;

  tourForm = this.getEmptyForm();

  private tourMap: any = null;
  private detailMap: any = null;
  private mapMarkers: any[] = [];
  private routeLayer: any = null;

  constructor(private apiService: ApiService) {}

  ngOnInit() {
    this.loadData();
  }

  ngOnDestroy() {
    if (this.tourMap) { this.tourMap.remove(); this.tourMap = null; }
    if (this.detailMap) { this.detailMap.remove(); this.detailMap = null; }
  }

  loadData() {
    this.loading = true;
    forkJoin({
      vehicles: this.apiService.getVehicles(),
      drivers: this.apiService.getDrivers()
    }).subscribe({
      next: (data) => {
        this.vehicles = data.vehicles || [];
        this.drivers = data.drivers || [];
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
      next: (res) => {
        this.tours = res.items || [];
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  loadStats() {
    this.apiService.getTourStats().subscribe({
      next: (s) => { this.stats = s; },
      error: () => {}
    });
  }

  // ═══════ FORM ═══════

  getEmptyForm() {
    return {
      name: '',
      description: '',
      vehicleId: null as number | null,
      driverId: null as number | null,
      scheduledStartTime: '',
      notes: '',
      waypoints: [
        { name: '', address: '', latitude: 36.8065, longitude: 10.1815, plannedPauseMinutes: 0 },
        { name: '', address: '', latitude: 36.8338, longitude: 10.5958, plannedPauseMinutes: 0 }
      ]
    };
  }

  addWaypoint() {
    const lastIdx = this.tourForm.waypoints.length - 1;
    const last = this.tourForm.waypoints[lastIdx];
    this.tourForm.waypoints.splice(lastIdx, 0, {
      name: '',
      address: '',
      latitude: (this.tourForm.waypoints[0].latitude + last.latitude) / 2,
      longitude: (this.tourForm.waypoints[0].longitude + last.longitude) / 2,
      plannedPauseMinutes: 15
    });
  }

  removeWaypoint(index: number) {
    if (this.tourForm.waypoints.length > 2) {
      this.tourForm.waypoints.splice(index, 1);
    }
  }

  // ═══════ ESTIMATION ═══════

  estimateRoute() {
    if (this.tourForm.waypoints.length < 2) return;
    this.estimating = true;

    const selectedVehicle = this.vehicles.find(v => v.id === this.tourForm.vehicleId);
    this.apiService.estimateRoute({
      waypoints: this.tourForm.waypoints,
      fuelType: selectedVehicle?.fuelType || 'diesel'
    }).subscribe({
      next: (est) => {
        this.estimation = est;
        this.estimating = false;
        this.drawRouteOnMap(est.routePoints);
      },
      error: () => {
        this.estimating = false;
        this.estimation = null;
      }
    });
  }

  // ═══════ CREATE / EDIT ═══════

  openCreateModal() {
    this.editingTour = null;
    this.tourForm = this.getEmptyForm();
    this.estimation = null;
    this.showCreateModal = true;
    setTimeout(() => this.initTourMap(), 100);
  }

  closeCreateModal() {
    this.showCreateModal = false;
    if (this.tourMap) { this.tourMap.remove(); this.tourMap = null; }
  }

  editTour() {
    if (!this.selectedTour) return;
    this.editingTour = this.selectedTour;
    this.tourForm = {
      name: this.selectedTour.name,
      description: this.selectedTour.description || '',
      vehicleId: this.selectedTour.vehicleId,
      driverId: this.selectedTour.driverId,
      scheduledStartTime: this.toLocalDatetime(this.selectedTour.scheduledStartTime),
      notes: this.selectedTour.notes || '',
      waypoints: this.selectedTour.waypoints.map((w: any) => ({
        name: w.name || '',
        address: w.address || '',
        latitude: w.latitude,
        longitude: w.longitude,
        plannedPauseMinutes: w.plannedPauseMinutes || 0
      }))
    };
    this.estimation = null;
    this.showDetailModal = false;
    this.showCreateModal = true;
    setTimeout(() => this.initTourMap(), 100);
  }

  saveTour() {
    if (!this.tourForm.name || !this.tourForm.vehicleId || this.tourForm.waypoints.length < 2) return;
    this.saving = true;

    const payload = {
      name: this.tourForm.name,
      description: this.tourForm.description || null,
      vehicleId: this.tourForm.vehicleId,
      driverId: this.tourForm.driverId,
      scheduledStartTime: this.tourForm.scheduledStartTime,
      notes: this.tourForm.notes || null,
      waypoints: this.tourForm.waypoints
    };

    const obs = this.editingTour
      ? this.apiService.updateTour(this.editingTour.id, payload)
      : this.apiService.createTour(payload);

    obs.subscribe({
      next: () => {
        this.closeCreateModal();
        this.loadTours();
        this.loadStats();
        this.saving = false;
      },
      error: () => { this.saving = false; }
    });
  }

  // ═══════ DETAIL ═══════

  openDetailModal(tour: any) {
    this.apiService.getTour(tour.id).subscribe({
      next: (detail) => {
        this.selectedTour = detail;
        this.showDetailModal = true;
        setTimeout(() => this.initDetailMap(), 100);
      }
    });
  }

  closeDetailModal() {
    this.showDetailModal = false;
    this.selectedTour = null;
    if (this.detailMap) { this.detailMap.remove(); this.detailMap = null; }
  }

  // ═══════ ACTIONS ═══════

  startSelectedTour() {
    if (!this.selectedTour) return;
    this.apiService.startTour(this.selectedTour.id).subscribe({
      next: () => { this.closeDetailModal(); this.loadTours(); this.loadStats(); }
    });
  }

  completeSelectedTour() {
    if (!this.selectedTour) return;
    this.apiService.completeTour(this.selectedTour.id).subscribe({
      next: () => { this.closeDetailModal(); this.loadTours(); this.loadStats(); }
    });
  }

  cancelSelectedTour() {
    if (!this.selectedTour) return;
    if (!confirm('Annuler cette tournée ?')) return;
    this.apiService.cancelTour(this.selectedTour.id).subscribe({
      next: () => { this.closeDetailModal(); this.loadTours(); this.loadStats(); }
    });
  }

  deleteTour(tour: any) {
    if (!confirm('Supprimer cette tournée ?')) return;
    this.apiService.deleteTour(tour.id).subscribe({
      next: () => { this.loadTours(); this.loadStats(); }
    });
  }

  // ═══════ MAP ═══════

  initTourMap() {
    if (this.tourMap) { this.tourMap.remove(); this.tourMap = null; }
    if (!this.tourMapEl?.nativeElement) return;

    try {
      this.tourMap = L.map(this.tourMapEl.nativeElement).setView([36.8, 10.18], 7);
      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap'
      }).addTo(this.tourMap);

      this.tourMap.on('click', (e: any) => {
        // Add clicked point as a waypoint
      });

      this.updateMapMarkers();
    } catch (e) {}
  }

  initDetailMap() {
    if (this.detailMap) { this.detailMap.remove(); this.detailMap = null; }
    if (!this.detailMapEl?.nativeElement || !this.selectedTour) return;

    try {
      this.detailMap = L.map(this.detailMapEl.nativeElement).setView([36.8, 10.18], 7);
      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '&copy; OpenStreetMap'
      }).addTo(this.detailMap);

      const wps = this.selectedTour.waypoints || [];
      if (wps.length > 0) {
        const bounds: any[] = [];
        wps.forEach((wp: any, i: number) => {
          const color = wp.type === 'origin' ? '#10b981' : wp.type === 'destination' ? '#ef4444' : '#3b82f6';
          const marker = L.circleMarker([wp.latitude, wp.longitude], {
            radius: 8, color: color, fillColor: color, fillOpacity: 0.8, weight: 2
          }).addTo(this.detailMap);
          marker.bindPopup(`<b>${wp.name || wp.address || wp.type}</b>`);
          bounds.push([wp.latitude, wp.longitude]);
        });

        // Draw route line
        if (bounds.length >= 2) {
          L.polyline(bounds, { color: '#3b82f6', weight: 3, dashArray: '8 4' }).addTo(this.detailMap);
          this.detailMap.fitBounds(bounds, { padding: [30, 30] });
        }
      }
    } catch (e) {}
  }

  updateMapMarkers() {
    if (!this.tourMap) return;
    this.mapMarkers.forEach(m => m.remove());
    this.mapMarkers = [];

    const bounds: any[] = [];
    this.tourForm.waypoints.forEach((wp, i) => {
      if (wp.latitude && wp.longitude) {
        const isFirst = i === 0;
        const isLast = i === this.tourForm.waypoints.length - 1;
        const color = isFirst ? '#10b981' : isLast ? '#ef4444' : '#3b82f6';
        const marker = L.circleMarker([wp.latitude, wp.longitude], {
          radius: 8, color: color, fillColor: color, fillOpacity: 0.8, weight: 2
        }).addTo(this.tourMap);
        marker.bindPopup(wp.name || wp.address || `Point ${i + 1}`);
        this.mapMarkers.push(marker);
        bounds.push([wp.latitude, wp.longitude]);
      }
    });

    if (bounds.length >= 2) {
      this.tourMap.fitBounds(bounds, { padding: [30, 30] });
    }
  }

  drawRouteOnMap(points: any[]) {
    if (!this.tourMap || !points || points.length < 2) return;
    if (this.routeLayer) { this.tourMap.removeLayer(this.routeLayer); }

    const latlngs = points.map((p: any) => [p.lat, p.lng]);
    this.routeLayer = L.polyline(latlngs, { color: '#3b82f6', weight: 4 }).addTo(this.tourMap);
    this.tourMap.fitBounds(this.routeLayer.getBounds(), { padding: [30, 30] });
    this.updateMapMarkers();
  }

  // ═══════ HELPERS ═══════

  getStatusLabel(status: string): string {
    const labels: any = { planned: 'Planifiée', in_progress: 'En cours', completed: 'Terminée', cancelled: 'Annulée' };
    return labels[status] || status;
  }

  getWaypointTypeLabel(type: string): string {
    const labels: any = { origin: 'Départ', waypoint: 'Arrêt', destination: 'Arrivée' };
    return labels[type] || type;
  }

  getPauseLabel(reason: string): string {
    const labels: any = { break: 'Pause', fuel: 'Carburant', delivery: 'Livraison', rest: 'Repos', other: 'Autre' };
    return labels[reason] || reason;
  }

  formatDate(dateStr: string): string {
    if (!dateStr) return '-';
    const d = new Date(dateStr);
    return d.toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
  }

  formatTime(dateStr: string): string {
    if (!dateStr) return '-';
    return new Date(dateStr).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  formatDuration(minutes: number): string {
    if (!minutes) return '0 min';
    const h = Math.floor(minutes / 60);
    const m = minutes % 60;
    return h > 0 ? `${h}h${m > 0 ? m.toString().padStart(2, '0') : ''}` : `${m} min`;
  }

  toLocalDatetime(dateStr: string): string {
    if (!dateStr) return '';
    const d = new Date(dateStr);
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }
}
