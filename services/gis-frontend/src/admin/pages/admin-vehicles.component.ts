import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, AdminVehicle, Client } from '../services/admin.service';
import { VehiclePopupComponent } from '../../components/shared/vehicle-popup.component';

@Component({
  selector: 'admin-vehicles',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent, VehiclePopupComponent],
  template: `
    <admin-layout pageTitle="Vehicle Management">
      <div class="vehicles-page">
        <div class="page-header">
          <div class="header-left">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (input)="filterVehicles()" placeholder="Search vehicles..." />
            </div>
            <select class="filter-select" [(ngModel)]="statusFilter" (change)="filterVehicles()">
              <option value="all">All Status</option>
              <option value="available">Available</option>
              <option value="in_use">In Use</option>
              <option value="maintenance">Maintenance</option>
            </select>
          </div>
          <button class="add-btn" (click)="openAddModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Add Vehicle
          </button>
        </div>

        <div class="vehicles-grid">
          <div class="vehicle-card" *ngFor="let vehicle of filteredVehicles" [class]="vehicle.status">
            <div class="card-header">
              <div class="vehicle-avatar">
                <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="1" y="3" width="15" height="13" rx="2"/>
                  <path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/>
                  <circle cx="5.5" cy="18.5" r="2.5"/>
                  <circle cx="18.5" cy="18.5" r="2.5"/>
                </svg>
              </div>
              <div class="vehicle-info">
                <h3>{{ vehicle.name }}</h3>
                <span class="vehicle-plate">{{ vehicle.plate || 'N/A' }}</span>
              </div>
              <div class="status-badge" [class]="vehicle.status">{{ getStatusLabel(vehicle.status) }}</div>
            </div>

            <div class="card-body">
              <div class="info-row" *ngIf="vehicle.brand || vehicle.model">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/>
                  <path d="M12 6v6l4 2"/>
                </svg>
                <span>{{ vehicle.brand }} {{ vehicle.model }} {{ vehicle.year ? '(' + vehicle.year + ')' : '' }}</span>
              </div>

              <div class="stats-row">
                <div class="stat">
                  <span class="stat-value">{{ vehicle.type | titlecase }}</span>
                  <span class="stat-label">Type</span>
                </div>
                <div class="stat">
                  <span class="stat-value">{{ vehicle.mileage | number }} km</span>
                  <span class="stat-label">Mileage</span>
                </div>
                <div class="stat">
                  <span class="stat-value gps-status" [class.active]="vehicle.hasGps">{{ vehicle.hasGps ? 'Yes' : 'No' }}</span>
                  <span class="stat-label">GPS</span>
                </div>
              </div>

              <div class="gps-info" *ngIf="vehicle.hasGps && (vehicle.gpsImei || vehicle.gpsDeviceId || vehicle.gpsModel || vehicle.gpsFirmwareVersion)">
                <div class="gps-row" *ngIf="vehicle.gpsImei">
                  <span class="gps-label">IMEI:</span>
                  <span class="gps-value">{{ vehicle.gpsImei }}</span>
                </div>
                <div class="gps-row" *ngIf="vehicle.gpsDeviceId">
                  <span class="gps-label">Device ID:</span>
                  <span class="gps-value">{{ vehicle.gpsDeviceId }}</span>
                </div>
                <div class="gps-row" *ngIf="vehicle.gpsModel">
                  <span class="gps-label">Model:</span>
                  <span class="gps-value">
                    {{ vehicle.gpsModel }}
                    <span *ngIf="vehicle.gpsFirmwareVersion">({{ vehicle.gpsFirmwareVersion }})</span>
                  </span>
                </div>
              </div>
            </div>

            <div class="card-footer">
              <span class="created-date">Created {{ formatDate(vehicle.createdAt) }}</span>
              <div class="actions">
                <button class="action-btn edit" (click)="editVehicle(vehicle)" title="Edit">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="action-btn view" (click)="viewVehicle(vehicle)" title="View Details">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                  </svg>
                </button>
                <button class="action-btn delete" (click)="confirmDelete(vehicle)" title="Delete">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3,6 5,6 21,6"/>
                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                  </svg>
                </button>
              </div>
            </div>
          </div>
        </div>

        <div class="empty-state" *ngIf="filteredVehicles.length === 0">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <rect x="1" y="3" width="15" height="13" rx="2"/>
            <path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/>
            <circle cx="5.5" cy="18.5" r="2.5"/>
            <circle cx="18.5" cy="18.5" r="2.5"/>
          </svg>
          <h3>No vehicles found</h3>
          <p>Try adjusting your search or filters</p>
        </div>

        <!-- Vehicle Add/Edit Popup (reused from /vehicule) -->
        <app-vehicle-popup
          [isOpen]="showAddModal || showEditModal"
          [vehicle]="selectedVehicleForPopup"
          (closed)="closeModals()"
          (saved)="onVehicleSaved($event)">
        </app-vehicle-popup>

        <!-- View Modal (kept for viewing details) -->
        <div class="modal-overlay" *ngIf="showViewModal" (click)="closeModals()">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Vehicle Details</h2>
              <button class="close-btn" (click)="closeModals()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body view-mode" *ngIf="selectedVehicle">
              <div class="view-header">
                <div class="vehicle-avatar large">
                  <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="1" y="3" width="15" height="13" rx="2"/>
                    <path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-5"/>
                    <circle cx="5.5" cy="18.5" r="2.5"/>
                    <circle cx="18.5" cy="18.5" r="2.5"/>
                  </svg>
                </div>
                <div>
                  <h3>{{ selectedVehicle.name }}</h3>
                  <span class="status-badge" [class]="selectedVehicle.status">{{ getStatusLabel(selectedVehicle.status) }}</span>
                </div>
              </div>

              <div class="view-section">
                <h4>Vehicle Information</h4>
                <div class="view-row"><span>Plate:</span><span>{{ selectedVehicle.plate || 'N/A' }}</span></div>
                <div class="view-row"><span>Type:</span><span>{{ selectedVehicle.type | titlecase }}</span></div>
                <div class="view-row"><span>Brand:</span><span>{{ selectedVehicle.brand || 'N/A' }}</span></div>
                <div class="view-row"><span>Model:</span><span>{{ selectedVehicle.model || 'N/A' }}</span></div>
                <div class="view-row"><span>Year:</span><span>{{ selectedVehicle.year || 'N/A' }}</span></div>
                <div class="view-row"><span>Color:</span><span>{{ selectedVehicle.color || 'N/A' }}</span></div>
                <div class="view-row"><span>Mileage:</span><span>{{ selectedVehicle.mileage | number }} km</span></div>
              </div>

              <div class="view-section">
                <h4>Assignments</h4>
                <div class="view-row"><span>Assigned Driver:</span><span>{{ selectedVehicle.assignedDriverName || 'None' }}</span></div>
              </div>

              <div class="view-section" *ngIf="selectedVehicle.hasGps">
                <h4>GPS Information</h4>
                <div class="view-row"><span>IMEI:</span><span>{{ selectedVehicle.gpsImei || 'N/A' }}</span></div>
                <div class="view-row"><span>MAT:</span><span>{{ selectedVehicle.gpsMat || 'N/A' }}</span></div>
                <div class="view-row"><span>Device ID:</span><span>{{ selectedVehicle.gpsDeviceId || 'N/A' }}</span></div>
                <div class="view-row" *ngIf="selectedVehicle.gpsModel || selectedVehicle.gpsFirmwareVersion">
                  <span>Model / Firmware:</span>
                  <span>
                    {{ selectedVehicle.gpsModel || 'Unknown' }}
                    <span *ngIf="selectedVehicle.gpsFirmwareVersion">({{ selectedVehicle.gpsFirmwareVersion }})</span>
                  </span>
                </div>
              </div>

              <div class="view-section">
                <h4>Timestamps</h4>
                <div class="view-row"><span>Created:</span><span>{{ formatDate(selectedVehicle.createdAt) }}</span></div>
                <div class="view-row"><span>Updated:</span><span>{{ formatDate(selectedVehicle.updatedAt) }}</span></div>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModals()">Close</button>
              <button class="btn-primary" (click)="editVehicle(selectedVehicle!)">Edit Vehicle</button>
            </div>
          </div>
        </div>

        <div class="modal-overlay" *ngIf="showDeleteModal" (click)="closeDeleteModal()">
          <div class="modal delete-modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Delete Vehicle</h2>
              <button class="close-btn" (click)="closeDeleteModal()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>
            <div class="modal-body">
              <div class="delete-warning">
                <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/>
                  <line x1="12" y1="8" x2="12" y2="12"/>
                  <line x1="12" y1="16" x2="12.01" y2="16"/>
                </svg>
                <p>Are you sure you want to delete <strong>{{ vehicleToDelete?.name }}</strong>?</p>
                <p class="warning-text">This action cannot be undone.</p>
              </div>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeDeleteModal()">Cancel</button>
              <button class="btn-danger" (click)="deleteVehicle()">Delete Vehicle</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .vehicles-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .page-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: 16px;
    }

    .header-left {
      display: flex;
      align-items: center;
      gap: 12px;
      flex-wrap: wrap;
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      padding: 10px 14px;
      width: 280px;
    }

    .search-box svg { color: #64748b; }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: #1f2937;
      font-size: 14px;
      outline: none;
    }

    .filter-select {
      padding: 10px 14px;
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      color: #1f2937;
      font-size: 14px;
      outline: none;
      cursor: pointer;
    }

    .add-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .add-btn:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    .vehicles-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
      gap: 20px;
    }

    .vehicle-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      overflow: hidden;
      transition: all 0.3s;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
    }

    .vehicle-card:hover {
      border-color: #cbd5e1;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
      transform: translateY(-2px);
    }

    .vehicle-card.maintenance {
      border-left: 4px solid #f97316;
    }

    .card-header {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .vehicle-avatar {
      width: 48px;
      height: 48px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
    }

    .vehicle-avatar.large {
      width: 64px;
      height: 64px;
    }

    .vehicle-info {
      flex: 1;
    }

    .vehicle-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .vehicle-plate {
      font-size: 13px;
      color: #64748b;
      font-family: monospace;
    }

    .status-badge {
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-badge.available {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .status-badge.in_use {
      background: rgba(59, 130, 246, 0.15);
      color: #3b82f6;
    }

    .status-badge.maintenance {
      background: rgba(249, 115, 22, 0.15);
      color: #f97316;
    }

    .card-body {
      padding: 20px;
    }

    .info-row {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 12px;
      font-size: 13px;
      color: #64748b;
    }

    .info-row svg {
      color: #64748b;
      flex-shrink: 0;
    }

    .stats-row {
      display: flex;
      gap: 20px;
      margin-top: 16px;
      padding-top: 16px;
      border-top: 1px solid #e2e8f0;
    }

    .stat {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .stat-value {
      font-size: 14px;
      font-weight: 600;
      color: #1f2937;
    }

    .stat-value.gps-status.active {
      color: #22c55e;
    }

    .stat-label {
      font-size: 11px;
      color: #64748b;
      text-transform: uppercase;
    }

    .gps-info {
      margin-top: 12px;
      padding: 12px;
      background: #f8fafc;
      border-radius: 8px;
      border: 1px solid #e2e8f0;
    }

    .gps-row {
      display: flex;
      justify-content: space-between;
      font-size: 12px;
      padding: 4px 0;
    }

    .gps-label {
      color: #64748b;
    }

    .gps-value {
      color: #1f2937;
      font-family: monospace;
    }

    .card-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .created-date {
      font-size: 12px;
      color: #6b7280;
    }

    .actions {
      display: flex;
      gap: 8px;
    }

    .action-btn {
      width: 32px;
      height: 32px;
      border: none;
      border-radius: 8px;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .action-btn.edit {
      background: rgba(59, 130, 246, 0.15);
      color: #3b82f6;
    }

    .action-btn.view {
      background: #f1f5f9;
      color: #64748b;
    }

    .action-btn.delete {
      background: rgba(239, 68, 68, 0.15);
      color: #ef4444;
    }

    .action-btn:hover {
      transform: scale(1.1);
    }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 60px 20px;
      color: #64748b;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state h3 {
      margin: 0 0 8px 0;
      font-size: 18px;
      color: #1f2937;
    }

    .empty-state p {
      margin: 0;
      font-size: 14px;
    }

    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.7);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
    }

    .modal {
      background: white;
      border-radius: 18px;
      width: 760px;
      max-width: 96vw;
      max-height: 92vh;
      display: flex;
      flex-direction: column;
      overflow: hidden;
      box-shadow: 0 24px 70px rgba(15, 23, 42, 0.28);
      transform: translateY(0);
      transition: transform 0.3s ease, opacity 0.3s ease;
    }

    .modal.delete-modal {
      max-width: 420px;
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #1f2937;
    }

    .close-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: #f1f5f9;
      border-radius: 10px;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .close-btn:hover {
      background: #e2e8f0;
      color: #1f2937;
    }

    .modal-body {
      padding: 24px;
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .section-title {
      margin: 0 0 8px 0;
      font-size: 14px;
      font-weight: 600;
      color: #00a388;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      padding-bottom: 8px;
      border-bottom: 1px solid #e2e8f0;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-group label {
      font-size: 14px;
      font-weight: 500;
      color: #374151;
    }

    .form-group input, .form-group select {
      padding: 12px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      color: #1f2937;
      font-size: 14px;
      outline: none;
      transition: border-color 0.2s;
    }

    .form-group input:focus, .form-group select:focus {
      border-color: #00d4aa;
    }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .checkbox-group {
      flex-direction: row;
      align-items: center;
    }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 10px;
      cursor: pointer;
      font-size: 14px;
      color: #374151;
    }

    .checkbox-label input[type="checkbox"] {
      width: 18px;
      height: 18px;
      accent-color: #00d4aa;
    }

    .gps-fields {
      padding: 16px;
      background: #f8fafc;
      border-radius: 10px;
      border: 1px solid #e2e8f0;
      display: flex;
      flex-direction: column;
      gap: 16px;
    }

    .view-header {
      display: flex;
      align-items: center;
      gap: 16px;
      padding-bottom: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .view-header h3 {
      margin: 0 0 8px 0;
      font-size: 20px;
      color: #1f2937;
    }

    .view-section {
      padding: 16px 0;
      border-bottom: 1px solid #e2e8f0;
    }

    .view-section:last-child {
      border-bottom: none;
    }

    .view-section h4 {
      margin: 0 0 12px 0;
      font-size: 14px;
      font-weight: 600;
      color: #00a388;
    }

    .view-row {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      font-size: 14px;
    }

    .view-row span:first-child {
      color: #64748b;
    }

    .view-row span:last-child {
      color: #1f2937;
      font-weight: 500;
    }

    .gps-active {
      color: #22c55e !important;
    }

    .delete-warning {
      text-align: center;
      padding: 20px;
    }

    .delete-warning svg {
      color: #ef4444;
      margin-bottom: 16px;
    }

    .delete-warning p {
      margin: 0 0 8px 0;
      font-size: 16px;
      color: #1f2937;
    }

    .delete-warning .warning-text {
      font-size: 14px;
      color: #64748b;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 20px 24px;
      border-top: 1px solid #e2e8f0;
    }

    .btn-secondary {
      padding: 10px 20px;
      background: #f1f5f9;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      color: #1f2937;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: #e2e8f0;
    }

    .btn-primary {
      padding: 10px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    .btn-primary:disabled {
      opacity: 0.6;
      cursor: not-allowed;
    }

    .btn-danger {
      padding: 10px 20px;
      background: #ef4444;
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-danger:hover {
      background: #dc2626;
    }

    @media (max-width: 640px) {
      .form-row {
        grid-template-columns: 1fr;
      }

      .vehicles-grid {
        grid-template-columns: 1fr;
      }

      .header-left {
        width: 100%;
      }

      .search-box {
        width: 100%;
      }
    }
  `]
})
export class AdminVehiclesComponent implements OnInit {
  vehicles: AdminVehicle[] = [];
  filteredVehicles: AdminVehicle[] = [];
  companies: Client[] = [];

  searchQuery = '';
  statusFilter = 'all';
  companyFilter = 'all';

  showAddModal = false;
  showEditModal = false;
  showViewModal = false;
  showDeleteModal = false;
  selectedVehicle: AdminVehicle | null = null;
  vehicleToDelete: AdminVehicle | null = null;

  // Getter for the popup component - converts AdminVehicle to the format expected by VehiclePopupComponent
  get selectedVehicleForPopup(): any {
    if (!this.showEditModal || !this.selectedVehicle) return null;
    return {
      id: this.selectedVehicle.id,
      name: this.selectedVehicle.name,
      plate: this.selectedVehicle.plate,
      brand: this.selectedVehicle.brand,
      model: this.selectedVehicle.model,
      year: this.selectedVehicle.year,
      type: this.selectedVehicle.type,
      status: this.selectedVehicle.status,
      mileage: this.selectedVehicle.mileage,
      hasGPS: this.selectedVehicle.hasGps,
      gpsDeviceId: this.selectedVehicle.gpsDeviceId
    };
  }

  vehicleForm = {
    name: '',
    type: 'citadine',
    brand: '',
    model: '',
    plate: '',
    year: new Date().getFullYear(),
    color: '',
    status: 'available' as 'available' | 'in_use' | 'maintenance',
    mileage: 0,
    companyId: 1,
    hasGps: false,
    gpsDeviceId: undefined as number | undefined,
    gpsImei: '',
    gpsMat: '',
    gpsModel: '',
    gpsFirmwareVersion: ''
  };

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.loadData();
  }

  loadData() {
    this.adminService.getVehicles().subscribe(vehicles => {
      this.vehicles = vehicles;
      this.filterVehicles();
    });

    this.adminService.getClients().subscribe(clients => {
      this.companies = clients.filter(c => c.status === 'active');
    });
  }

  filterVehicles() {
    this.filteredVehicles = this.vehicles.filter(vehicle => {
      const matchesSearch = !this.searchQuery ||
        vehicle.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        (vehicle.plate && vehicle.plate.toLowerCase().includes(this.searchQuery.toLowerCase())) ||
        (vehicle.brand && vehicle.brand.toLowerCase().includes(this.searchQuery.toLowerCase()));

      const matchesStatus = this.statusFilter === 'all' || vehicle.status === this.statusFilter;

      const matchesCompany = this.companyFilter === 'all' ||
        vehicle.companyId.toString() === this.companyFilter;

      return matchesSearch && matchesStatus && matchesCompany;
    });
  }

  getStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      'available': 'Available',
      'in_use': 'In Use',
      'maintenance': 'Maintenance'
    };
    return labels[status] || status;
  }

  openAddModal() {
    this.resetForm();
    this.showAddModal = true;
  }

  editVehicle(vehicle: AdminVehicle) {
    this.selectedVehicle = vehicle;
    this.vehicleForm = {
      name: vehicle.name,
      type: vehicle.type,
      brand: vehicle.brand || '',
      model: vehicle.model || '',
      plate: vehicle.plate || '',
      year: vehicle.year || new Date().getFullYear(),
      color: vehicle.color || '',
      status: vehicle.status,
      mileage: vehicle.mileage,
      companyId: vehicle.companyId || 1,
      hasGps: vehicle.hasGps,
      gpsDeviceId: vehicle.gpsDeviceId,
      gpsImei: vehicle.gpsImei || '',
      gpsMat: vehicle.gpsMat || '',
      gpsModel: vehicle.gpsModel || '',
      gpsFirmwareVersion: vehicle.gpsFirmwareVersion || ''
    };
    this.showViewModal = false;
    this.showEditModal = true;
  }

  viewVehicle(vehicle: AdminVehicle) {
    this.selectedVehicle = vehicle;
    this.showViewModal = true;
  }

  confirmDelete(vehicle: AdminVehicle) {
    this.vehicleToDelete = vehicle;
    this.showDeleteModal = true;
  }

  deleteVehicle() {
    if (this.vehicleToDelete) {
      this.adminService.deleteVehicle(this.vehicleToDelete.id).subscribe(() => {
        this.loadData();
        this.closeDeleteModal();
      });
    }
  }

  // Handler for VehiclePopupComponent saved event
  onVehicleSaved(formData: any) {
    const vehicleData: Partial<AdminVehicle> = {
      name: formData.name,
      type: formData.type,
      brand: formData.brand || undefined,
      model: formData.model || undefined,
      plate: formData.plate || undefined,
      year: formData.year || undefined,
      status: formData.status,
      mileage: formData.mileage || 0,
      companyId: 1,
      hasGps: formData.hasGPS || false,
      gpsDeviceId: formData.hasGPS ? formData.gpsDeviceId : undefined,
      gpsImei: formData.hasGPS ? formData.gpsImei || undefined : undefined,
      gpsModel: formData.hasGPS ? formData.gpsBrand || undefined : undefined,
      gpsFirmwareVersion: formData.hasGPS ? formData.gpsModel || undefined : undefined
    };

    if (this.showEditModal && this.selectedVehicle) {
      this.adminService.updateVehicle(this.selectedVehicle.id, vehicleData).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error updating vehicle:', err);
          alert('Erreur lors de la mise à jour du véhicule');
        }
      });
    } else {
      this.adminService.createVehicle(vehicleData).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error creating vehicle:', err);
          alert('Erreur lors de la création du véhicule');
        }
      });
    }
  }

  saveVehicle() {
    if (!this.isFormValid()) return;

    const vehicleData: Partial<AdminVehicle> = {
      name: this.vehicleForm.name,
      type: this.vehicleForm.type,
      brand: this.vehicleForm.brand || undefined,
      model: this.vehicleForm.model || undefined,
      plate: this.vehicleForm.plate || undefined,
      year: this.vehicleForm.year || undefined,
      color: this.vehicleForm.color || undefined,
      status: this.vehicleForm.status,
      mileage: this.vehicleForm.mileage,
      companyId: 1,
      hasGps: this.vehicleForm.hasGps,
      gpsDeviceId: this.vehicleForm.hasGps ? this.vehicleForm.gpsDeviceId : undefined,
      gpsImei: this.vehicleForm.hasGps ? this.vehicleForm.gpsImei || undefined : undefined,
      gpsMat: this.vehicleForm.hasGps ? this.vehicleForm.gpsMat || undefined : undefined,
      gpsModel: this.vehicleForm.hasGps ? this.vehicleForm.gpsModel || undefined : undefined,
      gpsFirmwareVersion: this.vehicleForm.hasGps ? this.vehicleForm.gpsFirmwareVersion || undefined : undefined
    };

    if (this.showEditModal && this.selectedVehicle) {
      this.adminService.updateVehicle(this.selectedVehicle.id, vehicleData).subscribe(() => {
        this.loadData();
        this.closeModals();
      });
    } else {
      this.adminService.createVehicle(vehicleData).subscribe(() => {
        this.loadData();
        this.closeModals();
      });
    }
  }

  isFormValid(): boolean {
    return !!(this.vehicleForm.name && this.vehicleForm.type);
  }

  closeModals() {
    this.showAddModal = false;
    this.showEditModal = false;
    this.showViewModal = false;
    this.selectedVehicle = null;
    this.resetForm();
  }

  closeDeleteModal() {
    this.showDeleteModal = false;
    this.vehicleToDelete = null;
  }

  resetForm() {
    this.vehicleForm = {
      name: '',
      type: 'citadine',
      brand: '',
      model: '',
      plate: '',
      year: new Date().getFullYear(),
      color: '',
      status: 'available',
      mileage: 0,
      companyId: 1,
      hasGps: false,
      gpsDeviceId: undefined,
      gpsImei: '',
      gpsMat: '',
      gpsModel: '',
      gpsFirmwareVersion: ''
    };
  }

  formatDate(date: Date): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
