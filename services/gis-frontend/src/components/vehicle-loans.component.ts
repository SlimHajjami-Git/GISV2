import { Component, OnInit, OnDestroy, NgZone, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil, interval } from 'rxjs';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService } from '../services/api.service';

interface VehicleStatus {
  id: number;
  name: string;
  plate?: string;
  mileage: number;
  hasGps: boolean;
  isRented: boolean;
  hasActiveReservation: boolean;
}

interface Reservation {
  id: number;
  vehicleId: number;
  vehicleName: string;
  vehiclePlate?: string;
  requestedByUserId?: number;
  requestedByUserName?: string;
  assignedDriverId?: number;
  assignedDriverName?: string;
  purpose?: string;
  destination?: string;
  startDateTime: string;
  endDateTime: string;
  estimatedKm?: number;
  actualKm?: number;
  startMileage?: number;
  endMileage?: number;
  status: string;
  notes?: string;
  createdAt: string;
}

interface CompanyUser {
  id: number;
  name: string;
}

@Component({
  selector: 'app-vehicle-loans',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout pageTitle="Emprunts Véhicules">
      <div class="loans-page">

        <!-- Page Header -->
        <div class="page-header">
          <div class="header-left">
            <div class="header-icon">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
            </div>
            <div>
              <h1 class="page-title">Emprunts Véhicules</h1>
              <p class="page-subtitle">Gestion des locations et emprunts de la flotte</p>
            </div>
          </div>
          <button class="btn-add" (click)="openBorrowModal()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouvel Emprunt
          </button>
        </div>

        <!-- Stats Row -->
        <div class="stats-row">
          <div class="stat-card">
            <div class="stat-icon total">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ vehicles.length }}</span>
              <span class="stat-label">Total</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon available">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ availableCount }}</span>
              <span class="stat-label">Disponibles</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon rented">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 4H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h18c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2z"/><path d="M1 10h22"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ rentedCount }}</span>
              <span class="stat-label">Loués</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon borrowed">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ borrowedCount }}</span>
              <span class="stat-label">Empruntés</span>
            </div>
          </div>
        </div>

        <!-- Filter Bar -->
        <div class="filter-bar">
          <div class="tabs-group">
            <button class="tab-btn" [class.active]="activeTab === 'vehicles'" (click)="switchTab('vehicles')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
              Véhicules
            </button>
            <button class="tab-btn" [class.active]="activeTab === 'active'" (click)="switchTab('active')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
              </svg>
              En cours
              <span class="tab-count" *ngIf="activeReservations.length">{{ activeReservations.length }}</span>
            </button>
            <button class="tab-btn" [class.active]="activeTab === 'history'" (click)="switchTab('history')">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14,2 14,8 20,8"/>
              </svg>
              Historique
            </button>
          </div>
          <div class="filter-right">
            <div class="search-wrapper" *ngIf="activeTab === 'vehicles'">
              <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input class="search-input" type="text" placeholder="Rechercher..." [(ngModel)]="vehicleSearch" />
            </div>
            <select class="filter-select" *ngIf="activeTab === 'history'" [(ngModel)]="historyStatus" (ngModelChange)="loadHistory()">
              <option value="">Tous les statuts</option>
              <option value="completed">Terminés</option>
              <option value="cancelled">Annulés</option>
            </select>
            <input class="filter-select" type="date" *ngIf="activeTab === 'history'" [(ngModel)]="historyFrom" (ngModelChange)="loadHistory()" />
            <input class="filter-select" type="date" *ngIf="activeTab === 'history'" [(ngModel)]="historyTo" (ngModelChange)="loadHistory()" />
          </div>
        </div>

        <!-- Content -->
        <div class="table-container">

          <!-- Loading -->
          <div class="loading-state" *ngIf="(activeTab === 'vehicles' && loadingVehicles) || (activeTab === 'active' && loadingReservations) || (activeTab === 'history' && loadingHistory)">
            <div class="spinner"></div>
          </div>

          <!-- Tab: Vehicles -->
          <table class="data-table" *ngIf="activeTab === 'vehicles' && !loadingVehicles && filteredVehicles.length">
            <thead>
              <tr>
                <th>Véhicule</th>
                <th>Plaque</th>
                <th>Kilométrage</th>
                <th>GPS</th>
                <th>Statut</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let v of filteredVehicles" class="data-row">
                <td>
                  <div class="vehicle-cell">
                    <div class="vehicle-avatar" [class.rented]="v.isRented" [class.borrowed]="v.hasActiveReservation">
                      {{ v.name.charAt(0).toUpperCase() }}
                    </div>
                    <span class="cell-name">{{ v.name }}</span>
                  </div>
                </td>
                <td><span class="plate-tag" *ngIf="v.plate">{{ v.plate }}</span><span *ngIf="!v.plate" class="text-muted">-</span></td>
                <!-- Calypso 6 (P11): no thousand separator -->
                <td class="text-mono">{{ v.mileage }} km</td>
                <td>
                  <span class="status-dot" [class.active]="v.hasGps" [class.inactive]="!v.hasGps"></span>
                  {{ v.hasGps ? 'Actif' : 'Non' }}
                </td>
                <td>
                  <span class="badge badge-success" *ngIf="!v.isRented && !v.hasActiveReservation">Disponible</span>
                  <span class="badge badge-warning" *ngIf="v.isRented">Loué</span>
                  <span class="badge badge-info" *ngIf="!v.isRented && v.hasActiveReservation">Emprunté</span>
                </td>
                <td>
                  <div class="action-group">
                    <button class="action-btn" [class.toggling]="togglingRental === v.id"
                            [disabled]="v.hasActiveReservation || togglingRental === v.id"
                            (click)="toggleRentalStatus(v)"
                            [title]="v.isRented ? 'Marquer disponible' : 'Marquer loué'">
                      <svg *ngIf="!v.isRented" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>
                      </svg>
                      <svg *ngIf="v.isRented" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 9.9-1"/>
                      </svg>
                    </button>
                    <button class="action-btn primary"
                            *ngIf="!v.isRented && !v.hasActiveReservation"
                            (click)="openBorrowModal(v)"
                            title="Emprunter">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="8.5" cy="7" r="4"/><line x1="20" y1="8" x2="20" y2="14"/><line x1="23" y1="11" x2="17" y2="11"/>
                      </svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>

          <!-- Tab: Active Reservations -->
          <table class="data-table" *ngIf="activeTab === 'active' && !loadingReservations && activeReservations.length">
            <thead>
              <tr>
                <th>Véhicule</th>
                <th>Employé</th>
                <th>Motif</th>
                <th>Début</th>
                <th>Km départ</th>
                <th>Durée</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of activeReservations" class="data-row">
                <td>
                  <div class="vehicle-cell">
                    <div class="vehicle-avatar borrowed">{{ r.vehicleName.charAt(0).toUpperCase() }}</div>
                    <div class="cell-info">
                      <span class="cell-name">{{ r.vehicleName }}</span>
                      <span class="cell-sub" *ngIf="r.vehiclePlate">{{ r.vehiclePlate }}</span>
                    </div>
                  </div>
                </td>
                <td>{{ r.assignedDriverName || r.requestedByUserName || '-' }}</td>
                <td><span class="text-muted">{{ r.purpose || '-' }}</span></td>
                <td class="text-mono-sm">{{ r.startDateTime | date:'dd/MM/yy HH:mm' }}</td>
                <!-- Calypso 6 (P11): no thousand separator -->
                <td class="text-mono-sm">{{ r.startMileage ? r.startMileage + ' km' : '-' }}</td>
                <td><span class="duration-badge">{{ getDuration(r.startDateTime) }}</span></td>
                <td>
                  <div class="action-group">
                    <button class="action-btn save" (click)="confirmReturn(r)" title="Retourner">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polyline points="20 6 9 17 4 12"/>
                      </svg>
                    </button>
                    <button class="action-btn danger" (click)="confirmCancel(r)" title="Annuler">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                      </svg>
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>

          <!-- Tab: History -->
          <table class="data-table" *ngIf="activeTab === 'history' && !loadingHistory && historyReservations.length">
            <thead>
              <tr>
                <th>Véhicule</th>
                <th>Employé</th>
                <th>Motif</th>
                <th>Période</th>
                <th>Km départ</th>
                <th>Km arrivée</th>
                <th>Km parcourus</th>
                <th>Statut</th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let r of historyReservations" class="data-row">
                <td>
                  <div class="vehicle-cell">
                    <div class="vehicle-avatar">{{ r.vehicleName.charAt(0).toUpperCase() }}</div>
                    <span class="cell-name">{{ r.vehicleName }}</span>
                  </div>
                </td>
                <td>{{ r.assignedDriverName || r.requestedByUserName || '-' }}</td>
                <td><span class="text-muted">{{ r.purpose || '-' }}</span></td>
                <td class="text-mono-sm">{{ r.startDateTime | date:'dd/MM' }} — {{ r.endDateTime | date:'dd/MM/yy' }}</td>
                <!-- Calypso 6 (P11): no thousand separator -->
                <td class="text-mono-sm">{{ r.startMileage != null ? r.startMileage : '-' }}</td>
                <td class="text-mono-sm">{{ r.endMileage != null ? r.endMileage : '-' }}</td>
                <td>
                  <span class="km-value" *ngIf="r.actualKm != null">{{ r.actualKm }} km</span>
                  <span class="text-muted" *ngIf="r.actualKm == null">-</span>
                </td>
                <td>
                  <span class="badge badge-success" *ngIf="r.status === 'completed'">Terminé</span>
                  <span class="badge badge-danger" *ngIf="r.status === 'cancelled'">Annulé</span>
                </td>
              </tr>
            </tbody>
          </table>

          <!-- Empty States -->
          <div class="empty-state" *ngIf="activeTab === 'vehicles' && !loadingVehicles && !filteredVehicles.length">
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
            <p>Aucun véhicule trouvé</p>
          </div>
          <div class="empty-state" *ngIf="activeTab === 'active' && !loadingReservations && !activeReservations.length">
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
            <p>Aucun emprunt en cours</p>
          </div>
          <div class="empty-state" *ngIf="activeTab === 'history' && !loadingHistory && !historyReservations.length">
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14,2 14,8 20,8"/></svg>
            <p>Aucun historique</p>
          </div>
        </div>

        <!-- Borrow Modal -->
        <div class="modal-overlay" *ngIf="showBorrowModal" (click)="closeBorrowModal()">
          <div class="modal-card" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Nouvel Emprunt</h3>
              <button class="modal-close" (click)="closeBorrowModal()">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>
            <div class="modal-body">
              <div class="form-group">
                <label>Véhicule *</label>
                <select [(ngModel)]="borrowForm.vehicleId" required>
                  <option [ngValue]="null">-- Sélectionner --</option>
                  <option *ngFor="let v of availableForBorrow" [ngValue]="v.id">
                    {{ v.name }} {{ v.plate ? '(' + v.plate + ')' : '' }}
                  </option>
                </select>
              </div>
              <div class="form-group">
                <label>Employé</label>
                <select [(ngModel)]="borrowForm.assignedDriverId">
                  <option [ngValue]="null">-- Sélectionner --</option>
                  <option *ngFor="let u of users" [ngValue]="u.id">{{ u.name }}</option>
                </select>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Motif</label>
                  <input type="text" [(ngModel)]="borrowForm.purpose" placeholder="Ex: Livraison client" />
                </div>
                <div class="form-group">
                  <label>Destination</label>
                  <input type="text" [(ngModel)]="borrowForm.destination" placeholder="Ex: Tunis" />
                </div>
              </div>
              <div class="form-group">
                <label>Km estimé</label>
                <input type="number" [(ngModel)]="borrowForm.estimatedKm" placeholder="0" />
              </div>
              <div class="form-group">
                <label>Notes</label>
                <textarea [(ngModel)]="borrowForm.notes" rows="2" placeholder="Notes supplémentaires..."></textarea>
              </div>
              <div class="form-hint">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/></svg>
                Le kilométrage de départ sera capturé automatiquement depuis le GPS.
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-cancel" (click)="closeBorrowModal()">Annuler</button>
              <button class="btn-confirm" [disabled]="!borrowForm.vehicleId || submitting" (click)="submitBorrow()">
                {{ submitting ? 'Création...' : 'Confirmer' }}
              </button>
            </div>
          </div>
        </div>

        <!-- Confirm Dialog -->
        <div class="modal-overlay" *ngIf="confirmDialog" (click)="confirmDialog = null">
          <div class="modal-card modal-sm" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>{{ confirmDialog.title }}</h3>
            </div>
            <div class="modal-body">
              <p class="confirm-text">{{ confirmDialog.message }}</p>
            </div>
            <div class="modal-footer">
              <button class="btn-cancel" (click)="confirmDialog = null">Annuler</button>
              <button [class]="confirmDialog.btnClass" [disabled]="submitting" (click)="confirmDialog.action()">
                {{ submitting ? 'En cours...' : confirmDialog.btnText }}
              </button>
            </div>
          </div>
        </div>

      </div>
    </app-layout>
  `,
  styles: [`
    .loans-page {
      flex: 1;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
      background: var(--bg-page, #f1f5f9);
    }

    /* ── Page Header ── */
    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 18px 24px;
      background: var(--bg-card, #fff);
      border-bottom: 1px solid var(--border-color, #e2e8f0);
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .header-icon {
      width: 42px;
      height: 42px;
      background: linear-gradient(135deg, #6366f1, #4f46e5);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
      flex-shrink: 0;
    }

    .page-title {
      margin: 0;
      font-size: 18px;
      font-weight: 700;
      color: var(--text-primary, #0f172a);
      letter-spacing: -0.3px;
    }

    .page-subtitle {
      margin: 2px 0 0;
      font-size: 12px;
      color: var(--text-muted, #64748b);
    }

    .btn-add {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 8px 16px;
      background: linear-gradient(135deg, #6366f1, #4f46e5);
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 12px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
      box-shadow: 0 2px 8px rgba(99, 102, 241, 0.3);
    }

    .btn-add:hover {
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(99, 102, 241, 0.4);
    }

    /* ── Stats Row ── */
    .stats-row {
      display: flex;
      gap: 14px;
      padding: 16px 24px;
      background: var(--bg-card, #fff);
      border-bottom: 1px solid var(--border-color, #e2e8f0);
    }

    .stat-card {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 18px;
      background: var(--bg-secondary, #f8fafc);
      border: 1px solid var(--border-color, #e2e8f0);
      border-radius: 10px;
      min-width: 130px;
    }

    .stat-icon {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .stat-icon.total { background: #ede9fe; color: #7c3aed; }
    .stat-icon.available { background: #dcfce7; color: #16a34a; }
    .stat-icon.rented { background: #fef3c7; color: #d97706; }
    .stat-icon.borrowed { background: #dbeafe; color: #2563eb; }

    .stat-info {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 20px;
      font-weight: 800;
      color: var(--text-primary, #0f172a);
      line-height: 1;
    }

    .stat-label {
      font-size: 11px;
      color: var(--text-muted, #64748b);
      margin-top: 2px;
    }

    /* ── Filter Bar ── */
    .filter-bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 12px 24px;
      background: var(--bg-card, #fff);
      border-bottom: 1px solid var(--border-color, #e2e8f0);
    }

    .tabs-group {
      display: flex;
      gap: 2px;
      background: var(--bg-secondary, #f1f5f9);
      border-radius: 8px;
      padding: 3px;
    }

    .tab-btn {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 7px 14px;
      border: none;
      background: transparent;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      color: var(--text-secondary, #64748b);
      cursor: pointer;
      transition: all 0.15s;
      white-space: nowrap;
    }

    .tab-btn:hover {
      color: var(--text-primary, #1e293b);
      background: var(--bg-card, rgba(255,255,255,0.6));
    }

    .tab-btn.active {
      background: #6366f1;
      color: #fff;
      box-shadow: 0 1px 3px rgba(99, 102, 241, 0.3);
    }

    .tab-count {
      background: rgba(255,255,255,0.25);
      font-size: 10px;
      font-weight: 700;
      padding: 1px 6px;
      border-radius: 10px;
      min-width: 18px;
      text-align: center;
    }

    .filter-right {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .search-wrapper {
      position: relative;
    }

    .search-icon {
      position: absolute;
      left: 10px;
      top: 50%;
      transform: translateY(-50%);
      color: #94a3b8;
      pointer-events: none;
    }

    .search-input {
      padding: 7px 12px 7px 30px;
      font-size: 12px;
      border: 1px solid var(--border-color, #e2e8f0);
      border-radius: 8px;
      background: var(--bg-secondary, #f8fafc);
      color: var(--text-primary, #1e293b);
      width: 200px;
      transition: all 0.15s;
    }

    .search-input::placeholder { color: #94a3b8; }

    .search-input:focus {
      outline: none;
      border-color: #6366f1;
      background: var(--bg-card, #fff);
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.08);
    }

    .filter-select {
      padding: 7px 12px;
      background: var(--bg-secondary, #f8fafc);
      border: 1px solid var(--border-color, #e2e8f0);
      border-radius: 8px;
      color: var(--text-primary, #1e293b);
      font-size: 12px;
      cursor: pointer;
      transition: all 0.15s;
    }

    .filter-select:focus {
      outline: none;
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.08);
    }

    /* ── Table ── */
    .table-container {
      flex: 1;
      padding: 16px 24px;
      overflow-x: auto;
    }

    .data-table {
      width: 100%;
      border-collapse: separate;
      border-spacing: 0;
      background: var(--bg-card, #fff);
      border-radius: 10px;
      border: 1px solid var(--border-color, #e2e8f0);
      overflow: hidden;
    }

    .data-table thead {
      background: var(--bg-secondary, #f8fafc);
    }

    .data-table th {
      padding: 10px 16px;
      font-size: 10px;
      font-weight: 600;
      color: var(--text-muted, #64748b);
      text-transform: uppercase;
      letter-spacing: 0.5px;
      text-align: left;
      border-bottom: 1px solid var(--border-color, #e2e8f0);
      white-space: nowrap;
    }

    .data-table td {
      padding: 12px 16px;
      font-size: 12px;
      color: var(--text-secondary, #334155);
      border-bottom: 1px solid var(--border-light, #f1f5f9);
      vertical-align: middle;
    }

    .data-row {
      cursor: default;
      transition: background 0.15s;
    }

    .data-row:hover {
      background: var(--bg-secondary, #f8fafc);
    }

    .data-row:last-child td {
      border-bottom: none;
    }

    /* ── Cell Styles ── */
    .vehicle-cell {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .vehicle-avatar {
      width: 32px;
      height: 32px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 12px;
      color: #fff;
      background: linear-gradient(135deg, #94a3b8, #64748b);
      flex-shrink: 0;
    }

    .vehicle-avatar.rented {
      background: linear-gradient(135deg, #f59e0b, #d97706);
    }

    .vehicle-avatar.borrowed {
      background: linear-gradient(135deg, #6366f1, #4f46e5);
    }

    .cell-name {
      font-size: 13px;
      font-weight: 600;
      color: var(--text-primary, #0f172a);
    }

    .cell-info {
      display: flex;
      flex-direction: column;
      gap: 1px;
    }

    .cell-sub {
      font-size: 11px;
      color: var(--text-muted, #94a3b8);
      font-family: 'JetBrains Mono', ui-monospace, monospace;
    }

    .text-muted { color: var(--text-muted, #94a3b8); }

    .text-mono {
      font-family: 'JetBrains Mono', ui-monospace, monospace;
      font-size: 12px;
    }

    .text-mono-sm {
      font-family: 'JetBrains Mono', ui-monospace, monospace;
      font-size: 11px;
      color: var(--text-secondary, #475569);
    }

    .plate-tag {
      background: var(--bg-tertiary, #f1f5f9);
      padding: 3px 8px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 600;
      font-family: 'JetBrains Mono', ui-monospace, monospace;
      color: var(--text-secondary, #475569);
      letter-spacing: 0.3px;
    }

    .status-dot {
      display: inline-block;
      width: 7px;
      height: 7px;
      border-radius: 50%;
      margin-right: 5px;
    }

    .status-dot.active { background: #10b981; }
    .status-dot.inactive { background: #d1d5db; }

    .duration-badge {
      background: #ede9fe;
      color: #6366f1;
      padding: 3px 10px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 600;
      font-family: 'JetBrains Mono', ui-monospace, monospace;
    }

    .km-value {
      font-weight: 700;
      font-size: 13px;
      color: #059669;
      font-family: 'JetBrains Mono', ui-monospace, monospace;
    }

    /* ── Badges ── */
    .badge {
      display: inline-block;
      padding: 4px 10px;
      border-radius: 20px;
      font-size: 11px;
      font-weight: 600;
      white-space: nowrap;
    }

    .badge-success { background: #dcfce7; color: #16a34a; }
    .badge-warning { background: #fef3c7; color: #d97706; }
    .badge-info { background: #dbeafe; color: #2563eb; }
    .badge-danger { background: #fee2e2; color: #dc2626; }

    /* ── Action Buttons ── */
    .action-group {
      display: flex;
      gap: 4px;
    }

    .action-btn {
      width: 30px;
      height: 30px;
      border: 1px solid transparent;
      background: transparent;
      border-radius: 6px;
      color: var(--text-muted, #64748b);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.15s;
    }

    .action-btn:hover {
      background: var(--bg-tertiary, #e2e8f0);
      color: var(--text-primary, #1e293b);
    }

    .action-btn.primary:hover {
      color: #6366f1;
      background: #ede9fe;
      border-color: #c7d2fe;
    }

    .action-btn.save:hover {
      color: #16a34a;
      background: #dcfce7;
      border-color: #bbf7d0;
    }

    .action-btn.danger:hover {
      color: #dc2626;
      background: #fee2e2;
      border-color: #fecaca;
    }

    .action-btn:disabled {
      opacity: 0.35;
      cursor: not-allowed;
    }

    .action-btn.toggling {
      animation: pulse-toggle 0.8s ease-in-out infinite;
    }

    @keyframes pulse-toggle {
      0%, 100% { opacity: 0.5; }
      50% { opacity: 1; }
    }

    /* ── Loading / Empty ── */
    .loading-state {
      display: flex;
      align-items: center;
      justify-content: center;
      padding: 80px 0;
    }

    .spinner {
      width: 28px;
      height: 28px;
      border: 3px solid var(--border-color, #e2e8f0);
      border-top-color: #6366f1;
      border-radius: 50%;
      animation: spin 0.7s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 80px 0;
      color: var(--text-muted, #94a3b8);
    }

    .empty-state p {
      font-size: 13px;
      margin: 0;
    }

    /* ── Modal ── */
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      animation: fadeIn 0.15s ease;
    }

    @keyframes fadeIn {
      from { opacity: 0; }
      to { opacity: 1; }
    }

    .modal-card {
      background: var(--bg-card, #fff);
      border-radius: 12px;
      width: 480px;
      max-width: 95vw;
      max-height: 85vh;
      overflow-y: auto;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1);
      animation: slideUp 0.2s ease;
    }

    .modal-sm { width: 400px; }

    @keyframes slideUp {
      from { opacity: 0; transform: translateY(10px); }
      to { opacity: 1; transform: translateY(0); }
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      border-bottom: 1px solid var(--border-color, #e5e7eb);
    }

    .modal-header h3 {
      margin: 0;
      font-size: 16px;
      font-weight: 600;
      color: var(--text-primary, #1f2937);
    }

    .modal-close {
      background: none;
      border: none;
      color: var(--text-muted, #9ca3af);
      cursor: pointer;
      padding: 4px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      transition: all 0.15s;
    }

    .modal-close:hover { background: var(--bg-tertiary, #f3f4f6); color: var(--text-primary, #374151); }

    .modal-body {
      padding: 20px;
    }

    .confirm-text {
      color: var(--text-secondary, #6b7280);
      font-size: 13px;
      line-height: 1.6;
      margin: 0;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding: 16px 20px;
      border-top: 1px solid var(--border-color, #e5e7eb);
    }

    .btn-cancel {
      padding: 8px 16px;
      background: var(--bg-card, #fff);
      color: var(--text-secondary, #374151);
      border: 1px solid var(--border-color, #d1d5db);
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-cancel:hover { background: var(--bg-secondary, #f9fafb); }

    .btn-confirm {
      padding: 8px 16px;
      background: linear-gradient(135deg, #6366f1, #4f46e5);
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.15s;
      box-shadow: 0 1px 3px rgba(99, 102, 241, 0.3);
    }

    .btn-confirm:hover { box-shadow: 0 4px 8px rgba(99, 102, 241, 0.4); }
    .btn-confirm:disabled { opacity: 0.5; cursor: not-allowed; }

    .btn-success-solid {
      padding: 8px 16px;
      background: #059669;
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-success-solid:hover { background: #047857; }
    .btn-success-solid:disabled { opacity: 0.5; cursor: not-allowed; }

    .btn-danger-solid {
      padding: 8px 16px;
      background: #dc2626;
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.15s;
    }

    .btn-danger-solid:hover { background: #b91c1c; }
    .btn-danger-solid:disabled { opacity: 0.5; cursor: not-allowed; }

    /* ── Forms ── */
    .form-group {
      margin-bottom: 14px;
    }

    .form-group label {
      display: block;
      font-size: 12px;
      font-weight: 600;
      color: var(--text-secondary, #374151);
      margin-bottom: 5px;
    }

    .form-group input,
    .form-group select,
    .form-group textarea {
      width: 100%;
      padding: 8px 12px;
      border: 1px solid var(--border-color, #e2e8f0);
      border-radius: 8px;
      font-size: 13px;
      color: var(--text-primary, #1e293b);
      background: var(--bg-card, #fff);
      outline: none;
      transition: all 0.15s;
      box-sizing: border-box;
      font-family: inherit;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      border-color: #6366f1;
      box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.08);
    }

    .form-group textarea { resize: vertical; }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .form-hint {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: var(--text-muted, #94a3b8);
      margin-top: 4px;
      padding: 8px 10px;
      background: var(--bg-secondary, #f8fafc);
      border-radius: 6px;
    }

    /* ── Responsive ── */
    @media (max-width: 768px) {
      .page-header { flex-direction: column; gap: 12px; align-items: flex-start; }
      .stats-row { flex-wrap: wrap; }
      .stat-card { min-width: 110px; flex: 1; }
      .filter-bar { flex-direction: column; align-items: stretch; }
      .tabs-group { overflow-x: auto; }
      .filter-right { flex-wrap: wrap; }
      .search-input { width: 100%; }
      .form-row { grid-template-columns: 1fr; }
      .action-group { flex-direction: column; }
    }
  `]
})
export class VehicleLoansComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  activeTab: 'vehicles' | 'active' | 'history' = 'vehicles';

  // Data
  vehicles: VehicleStatus[] = [];
  activeReservations: Reservation[] = [];
  historyReservations: Reservation[] = [];
  users: CompanyUser[] = [];

  // Loading
  loadingVehicles = true;
  loadingReservations = true;
  loadingHistory = true;
  submitting = false;
  togglingRental: number | null = null;

  // Filters
  vehicleSearch = '';
  historyStatus = '';
  historyFrom = '';
  historyTo = '';

  // Modal
  showBorrowModal = false;
  borrowForm: {
    vehicleId: number | null;
    assignedDriverId: number | null;
    purpose: string;
    destination: string;
    estimatedKm: number | null;
    notes: string;
  } = this.resetBorrowForm();

  confirmDialog: {
    title: string;
    message: string;
    btnText: string;
    btnClass: string;
    action: () => void;
  } | null = null;

  constructor(
    private api: ApiService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit() {
    this.loadAllData();
    interval(30000)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        if (this.activeTab === 'active') {
          this.loadActiveReservations(true);
        }
      });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }

  switchTab(tab: 'vehicles' | 'active' | 'history') {
    this.activeTab = tab;
  }

  // ── Data Loading ──

  loadAllData() {
    this.loadVehicles();
    this.loadActiveReservations();
    this.loadHistory();
    this.loadUsers();
  }

  loadVehicles() {
    this.loadingVehicles = true;
    this.api.getAvailableVehiclesForBorrowing()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.ngZone.run(() => {
            this.vehicles = data;
            this.loadingVehicles = false;
            this.cdr.detectChanges();
          });
        },
        error: () => {
          this.ngZone.run(() => {
            this.loadingVehicles = false;
            this.cdr.detectChanges();
          });
        }
      });
  }

  loadActiveReservations(silent = false) {
    if (!silent) this.loadingReservations = true;
    this.api.getReservations({ status: 'in_progress' })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.ngZone.run(() => {
            this.activeReservations = data;
            this.loadingReservations = false;
            this.cdr.detectChanges();
          });
        },
        error: () => {
          this.ngZone.run(() => {
            this.loadingReservations = false;
            this.cdr.detectChanges();
          });
        }
      });
  }

  loadHistory() {
    this.loadingHistory = true;
    const params: any = {};
    if (this.historyStatus) params.status = this.historyStatus;
    if (this.historyFrom) params.from = this.historyFrom;
    if (this.historyTo) params.to = this.historyTo;

    this.api.getReservations(params)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.ngZone.run(() => {
            this.historyReservations = data.filter(
              (r: Reservation) => r.status === 'completed' || r.status === 'cancelled'
            );
            this.loadingHistory = false;
            this.cdr.detectChanges();
          });
        },
        error: () => {
          this.ngZone.run(() => {
            this.loadingHistory = false;
            this.cdr.detectChanges();
          });
        }
      });
  }

  loadUsers() {
    this.api.getCompanyUsers()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.ngZone.run(() => {
            this.users = data;
            this.cdr.detectChanges();
          });
        },
        error: () => {}
      });
  }

  // ── Computed ──

  get filteredVehicles(): VehicleStatus[] {
    if (!this.vehicleSearch) return this.vehicles;
    const q = this.vehicleSearch.toLowerCase();
    return this.vehicles.filter(v =>
      v.name.toLowerCase().includes(q) ||
      (v.plate && v.plate.toLowerCase().includes(q))
    );
  }

  get availableCount(): number {
    return this.vehicles.filter(v => !v.isRented && !v.hasActiveReservation).length;
  }

  get rentedCount(): number {
    return this.vehicles.filter(v => v.isRented).length;
  }

  get borrowedCount(): number {
    return this.vehicles.filter(v => v.hasActiveReservation).length;
  }

  get availableForBorrow(): VehicleStatus[] {
    return this.vehicles.filter(v => !v.isRented && !v.hasActiveReservation);
  }

  // ── Actions ──

  toggleRentalStatus(v: VehicleStatus) {
    this.togglingRental = v.id;
    this.api.setVehicleRentalStatus(v.id, !v.isRented)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.ngZone.run(() => {
            v.isRented = !v.isRented;
            this.togglingRental = null;
            this.cdr.detectChanges();
          });
        },
        error: () => {
          this.ngZone.run(() => {
            this.togglingRental = null;
            this.cdr.detectChanges();
          });
        }
      });
  }

  openBorrowModal(vehicle?: VehicleStatus) {
    this.borrowForm = this.resetBorrowForm();
    if (vehicle) this.borrowForm.vehicleId = vehicle.id;
    this.showBorrowModal = true;
  }

  closeBorrowModal() {
    this.showBorrowModal = false;
  }

  submitBorrow() {
    if (!this.borrowForm.vehicleId) return;
    this.submitting = true;
    this.api.createReservation({
      vehicleId: this.borrowForm.vehicleId,
      assignedDriverId: this.borrowForm.assignedDriverId || undefined,
      purpose: this.borrowForm.purpose || undefined,
      destination: this.borrowForm.destination || undefined,
      estimatedKm: this.borrowForm.estimatedKm || undefined,
      notes: this.borrowForm.notes || undefined,
    })
    .pipe(takeUntil(this.destroy$))
    .subscribe({
      next: () => {
        this.ngZone.run(() => {
          this.submitting = false;
          this.showBorrowModal = false;
          this.loadAllData();
          this.activeTab = 'active';
          this.cdr.detectChanges();
        });
      },
      error: () => {
        this.ngZone.run(() => {
          this.submitting = false;
          this.cdr.detectChanges();
        });
      }
    });
  }

  confirmReturn(r: Reservation) {
    this.confirmDialog = {
      title: 'Retourner le véhicule',
      message: `Confirmer le retour de "${r.vehicleName}" ? Le kilométrage sera capturé depuis le GPS.`,
      btnText: 'Confirmer',
      btnClass: 'btn-success-solid',
      action: () => this.doReturn(r)
    };
  }

  doReturn(r: Reservation) {
    this.submitting = true;
    this.api.completeReservation(r.id)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.ngZone.run(() => {
            this.submitting = false;
            this.confirmDialog = null;
            this.loadAllData();
            this.cdr.detectChanges();
          });
        },
        error: () => {
          this.ngZone.run(() => {
            this.submitting = false;
            this.cdr.detectChanges();
          });
        }
      });
  }

  confirmCancel(r: Reservation) {
    this.confirmDialog = {
      title: 'Annuler l\'emprunt',
      message: `Annuler l'emprunt de "${r.vehicleName}" ?`,
      btnText: 'Annuler l\'emprunt',
      btnClass: 'btn-danger-solid',
      action: () => this.doCancel(r)
    };
  }

  doCancel(r: Reservation) {
    this.submitting = true;
    this.api.cancelReservation(r.id, 'Annulé manuellement')
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.ngZone.run(() => {
            this.submitting = false;
            this.confirmDialog = null;
            this.loadAllData();
            this.cdr.detectChanges();
          });
        },
        error: () => {
          this.ngZone.run(() => {
            this.submitting = false;
            this.cdr.detectChanges();
          });
        }
      });
  }

  // ── Helpers ──

  getDuration(startStr: string): string {
    const start = new Date(startStr);
    const now = new Date();
    const diffMs = now.getTime() - start.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    if (diffMins < 60) return `${diffMins}min`;
    const hours = Math.floor(diffMins / 60);
    if (hours < 24) return `${hours}h ${diffMins % 60}min`;
    const days = Math.floor(hours / 24);
    return `${days}j ${hours % 24}h`;
  }

  private resetBorrowForm() {
    return {
      vehicleId: null as number | null,
      assignedDriverId: null as number | null,
      purpose: '',
      destination: '',
      estimatedKm: null as number | null,
      notes: ''
    };
  }
}
