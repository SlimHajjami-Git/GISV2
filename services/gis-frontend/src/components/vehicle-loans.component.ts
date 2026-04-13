import { Component, OnInit, OnDestroy } from '@angular/core';
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
  firstName: string;
  lastName: string;
  fullName: string;
}

@Component({
  selector: 'app-vehicle-loans',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout pageTitle="Emprunts Véhicules">
      <div class="loans-page">

        <!-- Stats -->
        <div class="stats-row">
          <div class="stat-card">
            <div class="stat-icon total">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ vehicles.length }}</span>
              <span class="stat-label">Total véhicules</span>
            </div>
          </div>
          <div class="stat-card">
            <div class="stat-icon available">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
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
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
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
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ borrowedCount }}</span>
              <span class="stat-label">Empruntés</span>
            </div>
          </div>
        </div>

        <!-- Tabs -->
        <div class="tabs-bar">
          <button class="tab-btn" [class.active]="activeTab === 'vehicles'" (click)="activeTab = 'vehicles'">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
            </svg>
            Statut Véhicules
          </button>
          <button class="tab-btn" [class.active]="activeTab === 'active'" (click)="activeTab = 'active'">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
            </svg>
            Emprunts en cours
            <span class="tab-badge" *ngIf="activeReservations.length">{{ activeReservations.length }}</span>
          </button>
          <button class="tab-btn" [class.active]="activeTab === 'history'" (click)="activeTab = 'history'; loadHistory()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14,2 14,8 20,8"/>
            </svg>
            Historique
          </button>
        </div>

        <!-- Tab: Vehicles Status -->
        <div class="tab-content" *ngIf="activeTab === 'vehicles'">
          <div class="section-header">
            <div class="search-box">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" placeholder="Rechercher un véhicule..." [(ngModel)]="vehicleSearch" />
            </div>
          </div>

          <div class="loading-state" *ngIf="loadingVehicles">
            <div class="spinner"></div>
            <span>Chargement des véhicules...</span>
          </div>

          <div class="table-card" *ngIf="!loadingVehicles && filteredVehicles.length">
            <table>
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
                <tr *ngFor="let v of filteredVehicles">
                  <td class="cell-name">{{ v.name }}</td>
                  <td><span class="plate-tag">{{ v.plate || '-' }}</span></td>
                  <td>{{ v.mileage | number }} km</td>
                  <td>
                    <span class="gps-dot" [class.active]="v.hasGps"></span>
                    {{ v.hasGps ? 'Oui' : 'Non' }}
                  </td>
                  <td>
                    <span class="badge rented" *ngIf="v.isRented">Loué</span>
                    <span class="badge borrowed" *ngIf="!v.isRented && v.hasActiveReservation">Emprunté</span>
                    <span class="badge available" *ngIf="!v.isRented && !v.hasActiveReservation">Disponible</span>
                  </td>
                  <td class="cell-actions">
                    <button class="btn-sm btn-outline"
                            [disabled]="v.hasActiveReservation || togglingRental === v.id"
                            (click)="toggleRentalStatus(v)">
                      {{ v.isRented ? 'Libérer' : 'Marquer Loué' }}
                    </button>
                    <button class="btn-sm btn-primary"
                            *ngIf="!v.isRented && !v.hasActiveReservation"
                            (click)="openBorrowModal(v)">
                      Emprunter
                    </button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="empty-state" *ngIf="!loadingVehicles && !filteredVehicles.length">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>
            </svg>
            <p>Aucun véhicule trouvé</p>
          </div>
        </div>

        <!-- Tab: Active Reservations -->
        <div class="tab-content" *ngIf="activeTab === 'active'">
          <div class="section-header">
            <button class="btn-primary" (click)="openBorrowModal()">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
              </svg>
              Nouvel Emprunt
            </button>
          </div>

          <div class="loading-state" *ngIf="loadingReservations">
            <div class="spinner"></div>
            <span>Chargement...</span>
          </div>

          <div class="table-card" *ngIf="!loadingReservations && activeReservations.length">
            <table>
              <thead>
                <tr>
                  <th>Véhicule</th>
                  <th>Plaque</th>
                  <th>Employé</th>
                  <th>Motif</th>
                  <th>Début</th>
                  <th>Km départ</th>
                  <th>Durée</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let r of activeReservations">
                  <td class="cell-name">{{ r.vehicleName }}</td>
                  <td><span class="plate-tag">{{ r.vehiclePlate || '-' }}</span></td>
                  <td>{{ r.assignedDriverName || r.requestedByUserName || '-' }}</td>
                  <td>{{ r.purpose || '-' }}</td>
                  <td>{{ r.startDateTime | date:'dd/MM/yy HH:mm' }}</td>
                  <td>{{ r.startMileage ? (r.startMileage | number) + ' km' : '-' }}</td>
                  <td><span class="duration-tag">{{ getDuration(r.startDateTime) }}</span></td>
                  <td class="cell-actions">
                    <button class="btn-sm btn-success" (click)="confirmReturn(r)">Retourner</button>
                    <button class="btn-sm btn-danger" (click)="confirmCancel(r)">Annuler</button>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="empty-state" *ngIf="!loadingReservations && !activeReservations.length">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
            </svg>
            <p>Aucun emprunt en cours</p>
          </div>
        </div>

        <!-- Tab: History -->
        <div class="tab-content" *ngIf="activeTab === 'history'">
          <div class="section-header">
            <div class="filters-row">
              <select [(ngModel)]="historyStatus" (ngModelChange)="loadHistory()">
                <option value="">Tous les statuts</option>
                <option value="completed">Terminés</option>
                <option value="cancelled">Annulés</option>
              </select>
              <input type="date" [(ngModel)]="historyFrom" (ngModelChange)="loadHistory()" />
              <input type="date" [(ngModel)]="historyTo" (ngModelChange)="loadHistory()" />
            </div>
          </div>

          <div class="loading-state" *ngIf="loadingHistory">
            <div class="spinner"></div>
            <span>Chargement...</span>
          </div>

          <div class="table-card" *ngIf="!loadingHistory && historyReservations.length">
            <table>
              <thead>
                <tr>
                  <th>Véhicule</th>
                  <th>Employé</th>
                  <th>Motif</th>
                  <th>Période</th>
                  <th>Km départ</th>
                  <th>Km arrivée</th>
                  <th class="col-highlight">Km parcourus</th>
                  <th>Statut</th>
                </tr>
              </thead>
              <tbody>
                <tr *ngFor="let r of historyReservations">
                  <td class="cell-name">{{ r.vehicleName }}</td>
                  <td>{{ r.assignedDriverName || r.requestedByUserName || '-' }}</td>
                  <td>{{ r.purpose || '-' }}</td>
                  <td class="cell-period">
                    {{ r.startDateTime | date:'dd/MM' }} — {{ r.endDateTime | date:'dd/MM/yy' }}
                  </td>
                  <td>{{ r.startMileage ? (r.startMileage | number) : '-' }}</td>
                  <td>{{ r.endMileage ? (r.endMileage | number) : '-' }}</td>
                  <td class="col-highlight cell-km">
                    <strong *ngIf="r.actualKm != null">{{ r.actualKm | number }} km</strong>
                    <span *ngIf="r.actualKm == null">-</span>
                  </td>
                  <td>
                    <span class="badge completed" *ngIf="r.status === 'completed'">Terminé</span>
                    <span class="badge cancelled" *ngIf="r.status === 'cancelled'">Annulé</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="empty-state" *ngIf="!loadingHistory && !historyReservations.length">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"/><polyline points="14,2 14,8 20,8"/>
            </svg>
            <p>Aucun historique</p>
          </div>
        </div>

        <!-- Borrow Modal -->
        <div class="modal-overlay" *ngIf="showBorrowModal" (click)="closeBorrowModal()">
          <div class="modal-card" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>Nouvel Emprunt</h3>
              <button class="modal-close" (click)="closeBorrowModal()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
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
                  <option *ngFor="let u of users" [ngValue]="u.id">{{ u.fullName || (u.firstName + ' ' + u.lastName) }}</option>
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
              <div class="form-row">
                <div class="form-group">
                  <label>Km estimé</label>
                  <input type="number" [(ngModel)]="borrowForm.estimatedKm" placeholder="0" />
                </div>
              </div>
              <div class="form-group">
                <label>Notes</label>
                <textarea [(ngModel)]="borrowForm.notes" rows="2" placeholder="Notes supplémentaires..."></textarea>
              </div>
              <p class="form-hint">Le kilométrage de départ sera capturé automatiquement depuis le GPS.</p>
            </div>
            <div class="modal-footer">
              <button class="btn-outline" (click)="closeBorrowModal()">Annuler</button>
              <button class="btn-primary" [disabled]="!borrowForm.vehicleId || submitting" (click)="submitBorrow()">
                {{ submitting ? 'Création...' : 'Confirmer l\\'emprunt' }}
              </button>
            </div>
          </div>
        </div>

        <!-- Confirm Dialog -->
        <div class="modal-overlay" *ngIf="confirmDialog" (click)="confirmDialog = null">
          <div class="modal-card confirm-card" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h3>{{ confirmDialog.title }}</h3>
            </div>
            <div class="modal-body">
              <p>{{ confirmDialog.message }}</p>
            </div>
            <div class="modal-footer">
              <button class="btn-outline" (click)="confirmDialog = null">Annuler</button>
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
      max-width: 1200px;
    }

    /* ── Stats ── */
    .stats-row {
      display: grid;
      grid-template-columns: repeat(4, 1fr);
      gap: 16px;
      margin-bottom: 24px;
    }

    .stat-card {
      background: #fff;
      border: 1px solid #e5e7eb;
      border-radius: 12px;
      padding: 18px 20px;
      display: flex;
      align-items: center;
      gap: 16px;
    }

    .stat-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .stat-icon.total { background: #f0f4ff; color: #4f6ef7; }
    .stat-icon.available { background: #ecfdf5; color: #059669; }
    .stat-icon.rented { background: #fff7ed; color: #d97706; }
    .stat-icon.borrowed { background: #eff6ff; color: #3b82f6; }

    .stat-info {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 26px;
      font-weight: 700;
      color: #111827;
      line-height: 1;
    }

    .stat-label {
      font-size: 13px;
      color: #6b7280;
      margin-top: 4px;
    }

    /* ── Tabs ── */
    .tabs-bar {
      display: flex;
      gap: 4px;
      background: #fff;
      border: 1px solid #e5e7eb;
      border-radius: 12px;
      padding: 4px;
      margin-bottom: 20px;
    }

    .tab-btn {
      flex: 1;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      padding: 10px 16px;
      border: none;
      background: transparent;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      color: #6b7280;
      cursor: pointer;
      transition: all .2s;
    }

    .tab-btn:hover { background: #f3f4f6; color: #374151; }

    .tab-btn.active {
      background: #00d4aa;
      color: #fff;
      box-shadow: 0 1px 3px rgba(0, 212, 170, .3);
    }

    .tab-badge {
      background: #fff;
      color: #00a388;
      font-size: 11px;
      font-weight: 700;
      padding: 2px 7px;
      border-radius: 10px;
      min-width: 20px;
      text-align: center;
    }

    /* ── Section Header ── */
    .section-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 16px;
      gap: 12px;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: #fff;
      border: 1px solid #e5e7eb;
      border-radius: 10px;
      padding: 8px 14px;
      width: 320px;
    }

    .search-box svg { color: #9ca3af; flex-shrink: 0; }

    .search-box input {
      border: none;
      background: transparent;
      outline: none;
      font-size: 14px;
      color: #111827;
      width: 100%;
    }

    .search-box input::placeholder { color: #9ca3af; }

    .filters-row {
      display: flex;
      gap: 10px;
      align-items: center;
    }

    .filters-row select,
    .filters-row input[type="date"] {
      padding: 8px 12px;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      font-size: 13px;
      color: #374151;
      background: #fff;
      outline: none;
    }

    .filters-row select:focus,
    .filters-row input:focus {
      border-color: #00d4aa;
    }

    /* ── Table ── */
    .table-card {
      background: #fff;
      border: 1px solid #e5e7eb;
      border-radius: 12px;
      overflow: hidden;
    }

    table {
      width: 100%;
      border-collapse: collapse;
    }

    thead {
      background: #f9fafb;
    }

    th {
      padding: 12px 16px;
      text-align: left;
      font-size: 12px;
      font-weight: 600;
      color: #6b7280;
      text-transform: uppercase;
      letter-spacing: .04em;
      border-bottom: 1px solid #e5e7eb;
    }

    td {
      padding: 14px 16px;
      font-size: 14px;
      color: #374151;
      border-bottom: 1px solid #f3f4f6;
    }

    tr:last-child td { border-bottom: none; }
    tr:hover td { background: #fafbfc; }

    .cell-name { font-weight: 600; color: #111827; }

    .cell-actions {
      display: flex;
      gap: 8px;
      align-items: center;
    }

    .cell-period { white-space: nowrap; font-size: 13px; }

    .col-highlight { background: rgba(0, 212, 170, .04); }

    .cell-km strong {
      color: #00a388;
      font-size: 15px;
    }

    .plate-tag {
      background: #f3f4f6;
      padding: 3px 8px;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
      font-family: monospace;
    }

    .gps-dot {
      display: inline-block;
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #d1d5db;
      margin-right: 6px;
    }

    .gps-dot.active { background: #10b981; }

    .duration-tag {
      background: #eff6ff;
      color: #3b82f6;
      padding: 3px 10px;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 500;
    }

    /* ── Badges ── */
    .badge {
      display: inline-block;
      padding: 4px 10px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
      white-space: nowrap;
    }

    .badge.available { background: #ecfdf5; color: #059669; }
    .badge.rented { background: #fff7ed; color: #d97706; }
    .badge.borrowed { background: #eff6ff; color: #3b82f6; }
    .badge.completed { background: #ecfdf5; color: #059669; }
    .badge.cancelled { background: #fef2f2; color: #dc2626; }

    /* ── Buttons ── */
    .btn-primary {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 10px 18px;
      background: #00d4aa;
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all .2s;
    }

    .btn-primary:hover { background: #00a388; }
    .btn-primary:disabled { opacity: .5; cursor: not-allowed; }

    .btn-outline {
      padding: 8px 14px;
      background: #fff;
      color: #374151;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all .2s;
    }

    .btn-outline:hover { border-color: #d1d5db; background: #f9fafb; }
    .btn-outline:disabled { opacity: .5; cursor: not-allowed; }

    .btn-sm {
      padding: 6px 12px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      border: none;
      transition: all .2s;
    }

    .btn-sm.btn-primary { background: #00d4aa; color: #fff; }
    .btn-sm.btn-primary:hover { background: #00a388; }

    .btn-sm.btn-outline {
      background: #fff;
      color: #374151;
      border: 1px solid #e5e7eb;
    }

    .btn-sm.btn-outline:hover { background: #f9fafb; }
    .btn-sm.btn-outline:disabled { opacity: .4; }

    .btn-sm.btn-success { background: #059669; color: #fff; }
    .btn-sm.btn-success:hover { background: #047857; }

    .btn-sm.btn-danger { background: #fff; color: #dc2626; border: 1px solid #fecaca; }
    .btn-sm.btn-danger:hover { background: #fef2f2; }

    .btn-danger-solid {
      padding: 10px 18px;
      background: #dc2626;
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-danger-solid:hover { background: #b91c1c; }
    .btn-danger-solid:disabled { opacity: .5; cursor: not-allowed; }

    .btn-success-solid {
      padding: 10px 18px;
      background: #059669;
      color: #fff;
      border: none;
      border-radius: 8px;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-success-solid:hover { background: #047857; }
    .btn-success-solid:disabled { opacity: .5; cursor: not-allowed; }

    /* ── Loading / Empty ── */
    .loading-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 60px 0;
      color: #9ca3af;
    }

    .spinner {
      width: 32px;
      height: 32px;
      border: 3px solid #e5e7eb;
      border-top-color: #00d4aa;
      border-radius: 50%;
      animation: spin .7s linear infinite;
    }

    @keyframes spin { to { transform: rotate(360deg); } }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 60px 0;
      color: #9ca3af;
    }

    .empty-state p { font-size: 15px; }

    /* ── Modal ── */
    .modal-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(0, 0, 0, .4);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      backdrop-filter: blur(2px);
    }

    .modal-card {
      background: #fff;
      border-radius: 16px;
      width: 520px;
      max-height: 85vh;
      overflow-y: auto;
      box-shadow: 0 20px 60px rgba(0, 0, 0, .15);
    }

    .confirm-card { width: 420px; }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid #f3f4f6;
    }

    .modal-header h3 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #111827;
    }

    .modal-close {
      background: none;
      border: none;
      color: #9ca3af;
      cursor: pointer;
      padding: 4px;
      border-radius: 6px;
    }

    .modal-close:hover { background: #f3f4f6; color: #374151; }

    .modal-body {
      padding: 24px;
    }

    .modal-body p {
      color: #6b7280;
      font-size: 14px;
      line-height: 1.6;
      margin: 0;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding: 16px 24px;
      border-top: 1px solid #f3f4f6;
    }

    /* ── Forms ── */
    .form-group {
      margin-bottom: 16px;
    }

    .form-group label {
      display: block;
      font-size: 13px;
      font-weight: 500;
      color: #374151;
      margin-bottom: 6px;
    }

    .form-group input,
    .form-group select,
    .form-group textarea {
      width: 100%;
      padding: 9px 12px;
      border: 1px solid #e5e7eb;
      border-radius: 8px;
      font-size: 14px;
      color: #111827;
      background: #fff;
      outline: none;
      transition: border-color .2s;
      box-sizing: border-box;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      border-color: #00d4aa;
      box-shadow: 0 0 0 3px rgba(0, 212, 170, .1);
    }

    .form-group textarea { resize: vertical; }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
    }

    .form-hint {
      font-size: 12px;
      color: #9ca3af;
      margin: 0;
      font-style: italic;
    }

    /* ── Responsive ── */
    @media (max-width: 768px) {
      .stats-row { grid-template-columns: repeat(2, 1fr); }
      .tabs-bar { flex-wrap: wrap; }
      .search-box { width: 100%; }
      .filters-row { flex-wrap: wrap; }
      .form-row { grid-template-columns: 1fr; }
      .cell-actions { flex-direction: column; }
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
  loadingVehicles = false;
  loadingReservations = false;
  loadingHistory = false;
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

  constructor(private api: ApiService) {}

  ngOnInit() {
    this.loadData();
    // Auto-refresh active reservations every 30s
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

  // ── Data Loading ──

  loadData() {
    this.loadVehicles();
    this.loadActiveReservations();
    this.loadUsers();
  }

  loadVehicles() {
    this.loadingVehicles = true;
    this.api.getAvailableVehiclesForBorrowing()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.vehicles = data;
          this.loadingVehicles = false;
        },
        error: () => { this.loadingVehicles = false; }
      });
  }

  loadActiveReservations(silent = false) {
    if (!silent) this.loadingReservations = true;
    this.api.getReservations({ status: 'in_progress' })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => {
          this.activeReservations = data;
          this.loadingReservations = false;
        },
        error: () => { this.loadingReservations = false; }
      });
  }

  loadHistory() {
    this.loadingHistory = true;
    const params: any = {};
    if (this.historyStatus) params.status = this.historyStatus;
    if (this.historyFrom) params.from = this.historyFrom;
    if (this.historyTo) params.to = this.historyTo;
    // Load completed + cancelled
    if (!this.historyStatus) {
      // Load all non-active
      this.api.getReservations(params)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (data) => {
            this.historyReservations = data.filter(r => r.status === 'completed' || r.status === 'cancelled');
            this.loadingHistory = false;
          },
          error: () => { this.loadingHistory = false; }
        });
    } else {
      this.api.getReservations(params)
        .pipe(takeUntil(this.destroy$))
        .subscribe({
          next: (data) => {
            this.historyReservations = data;
            this.loadingHistory = false;
          },
          error: () => { this.loadingHistory = false; }
        });
    }
  }

  loadUsers() {
    this.api.getCompanyUsers()
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (data) => { this.users = data; },
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
          v.isRented = !v.isRented;
          this.togglingRental = null;
        },
        error: () => { this.togglingRental = null; }
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
        this.submitting = false;
        this.showBorrowModal = false;
        this.loadData();
        this.activeTab = 'active';
      },
      error: () => { this.submitting = false; }
    });
  }

  confirmReturn(r: Reservation) {
    this.confirmDialog = {
      title: 'Retourner le véhicule',
      message: `Confirmer le retour de "${r.vehicleName}" ? Le kilométrage d'arrivée sera capturé automatiquement depuis le GPS.`,
      btnText: 'Confirmer le retour',
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
          this.submitting = false;
          this.confirmDialog = null;
          this.loadData();
        },
        error: () => { this.submitting = false; }
      });
  }

  confirmCancel(r: Reservation) {
    this.confirmDialog = {
      title: 'Annuler l\'emprunt',
      message: `Êtes-vous sûr de vouloir annuler l'emprunt de "${r.vehicleName}" ?`,
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
          this.submitting = false;
          this.confirmDialog = null;
          this.loadData();
        },
        error: () => { this.submitting = false; }
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
