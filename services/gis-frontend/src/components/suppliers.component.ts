import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AppLayoutComponent } from './shared/app-layout.component';
import { GaragePopupComponent } from './shared/garage-popup.component';
import { ApiService } from '../services/api.service';
import { trigger, transition, style, animate } from '@angular/animations';

// Interface pour les garages/fournisseurs
export interface Garage {
  id?: string;
  name: string;
  address: string;
  city: string;
  postalCode: string;
  phone: string;
  email: string;
  contactName: string;
  services: string[];
  rating: number;
  isActive: boolean;
  notes?: string;
  createdAt?: Date;
  updatedAt?: Date;
}

@Component({
  selector: 'app-suppliers',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, GaragePopupComponent],
  animations: [
    trigger('fadeIn', [
      transition(':enter', [
        style({ opacity: 0 }),
        animate('200ms ease-out', style({ opacity: 1 }))
      ])
    ]),
    trigger('scaleIn', [
      transition(':enter', [
        style({ opacity: 0, transform: 'scale(0.95)' }),
        animate('200ms ease-out', style({ opacity: 1, transform: 'scale(1)' }))
      ])
    ])
  ],
  template: `
    <app-layout>
      <div class="suppliers-page">
        <!-- Barre de filtres -->
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher un garage..." [(ngModel)]="searchQuery" (input)="filterGarages()">
          </div>
          <select class="filter-select" [(ngModel)]="filterService" (change)="filterGarages()">
            <option value="">Tous les services</option>
            <option value="mecanique">Mécanique générale</option>
            <option value="carrosserie">Carrosserie</option>
            <option value="electricite">Électricité auto</option>
            <option value="pneumatique">Pneumatiques</option>
            <option value="vidange">Vidange & Entretien</option>
            <option value="climatisation">Climatisation</option>
            <option value="diagnostic">Diagnostic électronique</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterStatus" (change)="filterGarages()">
            <option value="">Tous les statuts</option>
            <option value="active">Actif</option>
            <option value="inactive">Inactif</option>
          </select>
          <button class="btn-add" (click)="openAddPopup()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouveau garage
          </button>
        </div>

        <!-- Barre de statistiques -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon info">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M3 21h18"/>
                <path d="M9 8h1"/>
                <path d="M9 12h1"/>
                <path d="M9 16h1"/>
                <path d="M14 8h1"/>
                <path d="M14 12h1"/>
                <path d="M14 16h1"/>
                <path d="M5 21V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ allGarages.length }}</span>
              <span class="stat-label">Total garages</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon active">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getActiveGarages().length }}</span>
              <span class="stat-label">Actifs</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon warning">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/>
                <line x1="12" y1="8" x2="12" y2="12"/>
                <line x1="12" y1="16" x2="12.01" y2="16"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getInactiveGarages().length }}</span>
              <span class="stat-label">Inactifs</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon available">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
              </svg>
            </div>
            <div class="stat-content">
              <span class="stat-value">{{ getAverageRating() }}</span>
              <span class="stat-label">Note moyenne</span>
            </div>
          </div>
        </div>

        <!-- Grille des garages -->
        <div class="grid-container">
          <div class="garages-grid" @fadeIn>
            <div class="garage-card" *ngFor="let garage of filteredGarages" @scaleIn>
              <!-- Header de la carte -->
              <div class="card-top" [class.active]="garage.isActive" [class.inactive]="!garage.isActive">
                <div class="card-top-left">
                  <div class="garage-icon">🔧</div>
                  <div class="card-top-info">
                    <span class="garage-city">{{ garage.city }}</span>
                    <span class="garage-rating">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor">
                        <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>
                      </svg>
                      {{ garage.rating.toFixed(1) }}
                    </span>
                  </div>
                </div>
                <div class="card-top-right">
                  <div class="status-badge-small" [class.active]="garage.isActive" [class.inactive]="!garage.isActive">
                    <span class="status-dot"></span>
                  </div>
                </div>
              </div>

              <!-- Contenu de la carte -->
              <div class="card-content">
                <div class="card-header">
                  <h3 class="garage-name">{{ garage.name }}</h3>
                </div>
                <p class="garage-address">{{ garage.address }}, {{ garage.postalCode }} {{ garage.city }}</p>

                <!-- Contact -->
                <div class="contact-info">
                  <div class="contact-item">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z"/>
                    </svg>
                    <span>{{ garage.phone }}</span>
                  </div>
                  <div class="contact-item" *ngIf="garage.contactName">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/>
                      <circle cx="12" cy="7" r="4"/>
                    </svg>
                    <span>{{ garage.contactName }}</span>
                  </div>
                </div>

                <!-- Services -->
                <div class="services-list">
                  <span class="service-tag" *ngFor="let service of garage.services.slice(0, 3)">
                    {{ getServiceLabel(service) }}
                  </span>
                  <span class="service-tag more" *ngIf="garage.services.length > 3">
                    +{{ garage.services.length - 3 }}
                  </span>
                </div>
              </div>

              <!-- Footer avec actions -->
              <div class="card-footer">
                <button class="btn-action edit" (click)="openEditPopup(garage); $event.stopPropagation()" title="Modifier">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="btn-action call" (click)="callGarage(garage); $event.stopPropagation()" title="Appeler">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72 12.84 12.84 0 0 0 .7 2.81 2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45 12.84 12.84 0 0 0 2.81.7A2 2 0 0 1 22 16.92z"/>
                  </svg>
                </button>
                <button class="btn-action delete" (click)="confirmDelete(garage); $event.stopPropagation()" title="Supprimer">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3 6 5 6 21 6"/>
                    <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                  </svg>
                </button>
                <button class="btn-detail" (click)="viewGarageDetails(garage)">
                  Voir détails
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="m9 18 6-6-6-6"/>
                  </svg>
                </button>
              </div>
            </div>

            <!-- État vide -->
            <div class="empty-state" *ngIf="filteredGarages.length === 0">
              <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                <path d="M3 21h18"/>
                <path d="M9 8h1"/>
                <path d="M9 12h1"/>
                <path d="M9 16h1"/>
                <path d="M14 8h1"/>
                <path d="M14 12h1"/>
                <path d="M14 16h1"/>
                <path d="M5 21V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v16"/>
              </svg>
              <h3>Aucun garage trouvé</h3>
              <p>Modifiez vos filtres ou ajoutez un nouveau garage</p>
            </div>
          </div>
        </div>
      </div>

      <!-- Popup ajout/modification garage -->
      <app-garage-popup
        [isOpen]="isPopupOpen"
        [garage]="selectedGarage"
        (closed)="closePopup()"
        (saved)="saveGarage($event)"
      />

      <!-- Modal de confirmation suppression -->
      <div class="modal-overlay" *ngIf="showDeleteConfirm" (click)="cancelDelete()">
        <div class="modal-content" (click)="$event.stopPropagation()">
          <div class="modal-header">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="#dc2626" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <line x1="12" y1="8" x2="12" y2="12"/>
              <line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
            <h3>Confirmer la suppression</h3>
          </div>
          <p>Êtes-vous sûr de vouloir supprimer le garage <strong>{{ garageToDelete?.name }}</strong> ?</p>
          <p class="warning-text">Cette action est irréversible.</p>
          <div class="modal-actions">
            <button class="btn-cancel" (click)="cancelDelete()">Annuler</button>
            <button class="btn-delete" (click)="deleteGarage()">Supprimer</button>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    /* ===== PAGE LAYOUT ===== */
    .suppliers-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    /* ===== FILTER BAR ===== */
    .filter-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .search-wrapper {
      position: relative;
      flex: 1;
      max-width: 300px;
    }

    .search-icon {
      position: absolute;
      left: 10px;
      top: 50%;
      transform: translateY(-50%);
      color: #94a3b8;
    }

    .search-input {
      width: 100%;
      padding: 6px 10px 6px 32px;
      font-family: var(--font-family);
      font-size: 12px;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      background: white;
      color: #1e293b;
    }

    .search-input:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .filter-select {
      padding: 6px 10px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 3px;
      color: #1e293b;
      font-family: var(--font-family);
      font-size: 12px;
      cursor: pointer;
    }

    .filter-select:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .btn-add {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 3px;
      font-family: var(--font-family);
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      margin-left: auto;
    }

    .btn-add:hover {
      background: #2563eb;
    }

    /* ===== STATS BAR ===== */
    .stats-bar {
      display: flex;
      gap: 16px;
      padding: 12px 14px;
      background: white;
      border-bottom: 1px solid #e2e8f0;
    }

    .stat-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px 14px;
      background: #f8fafc;
      border-radius: 6px;
    }

    .stat-icon {
      width: 32px;
      height: 32px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.active { background: #dcfce7; color: #16a34a; }
    .stat-icon.available { background: #fef3c7; color: #d97706; }
    .stat-icon.warning { background: #fee2e2; color: #dc2626; }
    .stat-icon.info { background: #dbeafe; color: #2563eb; }

    .stat-content {
      display: flex;
      flex-direction: column;
    }

    .stat-value {
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
    }

    .stat-label {
      font-size: 11px;
      color: #64748b;
    }

    /* ===== GRID CONTAINER ===== */
    .grid-container {
      flex: 1;
      padding: 12px;
      overflow-y: auto;
    }

    .garages-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
      gap: 16px;
    }

    /* ===== GARAGE CARD ===== */
    .garage-card {
      background: white;
      border-radius: 8px;
      overflow: hidden;
      box-shadow: 0 1px 3px rgba(0,0,0,0.08);
      transition: all 0.2s;
      border: 1px solid #e2e8f0;
    }

    .garage-card:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 12px rgba(0,0,0,0.1);
    }

    /* ===== CARD TOP ===== */
    .card-top {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 14px;
      border-bottom: 1px solid #e2e8f0;
    }

    .card-top.active { 
      background: linear-gradient(135deg, #f0fdf4 0%, #dcfce7 100%); 
      border-left: 3px solid #22c55e; 
    }
    .card-top.inactive { 
      background: linear-gradient(135deg, #fef2f2 0%, #fee2e2 100%); 
      border-left: 3px solid #ef4444; 
    }

    .card-top-left {
      display: flex;
      align-items: center;
      gap: 10px;
    }

    .garage-icon {
      font-size: 24px;
      line-height: 1;
    }

    .card-top-info {
      display: flex;
      flex-direction: column;
    }

    .garage-city {
      font-size: 12px;
      font-weight: 600;
      color: #1e293b;
    }

    .garage-rating {
      display: flex;
      align-items: center;
      gap: 4px;
      font-size: 11px;
      color: #f59e0b;
    }

    .card-top-right {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .status-badge-small {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      position: relative;
    }

    .status-badge-small.active { background: #22c55e; }
    .status-badge-small.inactive { background: #ef4444; }

    .status-badge-small .status-dot {
      position: absolute;
      inset: 0;
      border-radius: 50%;
      animation: pulse-ring 2s infinite;
    }

    .status-badge-small.active .status-dot { background: rgba(34, 197, 94, 0.4); }
    .status-badge-small.inactive .status-dot { background: rgba(239, 68, 68, 0.4); }

    @keyframes pulse-ring {
      0% { transform: scale(1); opacity: 1; }
      50% { transform: scale(1.8); opacity: 0; }
      100% { transform: scale(1); opacity: 0; }
    }

    /* ===== CARD CONTENT ===== */
    .card-content {
      padding: 16px;
    }

    .card-header {
      margin-bottom: 8px;
    }

    .garage-name {
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
      margin: 0;
    }

    .garage-address {
      color: #64748b;
      font-size: 12px;
      margin: 0 0 12px;
      line-height: 1.4;
    }

    .contact-info {
      display: flex;
      flex-direction: column;
      gap: 6px;
      margin-bottom: 12px;
    }

    .contact-item {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: #64748b;
    }

    .contact-item svg {
      color: #94a3b8;
    }

    .services-list {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .service-tag {
      padding: 4px 8px;
      background: #f1f5f9;
      border-radius: 4px;
      font-size: 10px;
      font-weight: 500;
      color: #475569;
    }

    .service-tag.more {
      background: #e2e8f0;
      color: #64748b;
    }

    /* ===== CARD FOOTER ===== */
    .card-footer {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 16px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .btn-action {
      width: 32px;
      height: 32px;
      border-radius: 6px;
      border: 1px solid #e2e8f0;
      background: white;
      color: #64748b;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .btn-action:hover {
      border-color: #cbd5e1;
      color: #1e293b;
    }

    .btn-action.edit:hover { color: #3b82f6; border-color: #3b82f6; }
    .btn-action.call:hover { color: #22c55e; border-color: #22c55e; }
    .btn-action.delete:hover { color: #ef4444; border-color: #ef4444; }

    .btn-detail {
      margin-left: auto;
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: #1e3a5f;
      color: white;
      border: none;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-detail:hover {
      background: #2d5a87;
    }

    /* ===== EMPTY STATE ===== */
    .empty-state {
      grid-column: 1 / -1;
      text-align: center;
      padding: 60px 40px;
      color: #64748b;
    }

    .empty-state svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .empty-state h3 {
      margin: 0 0 8px;
      color: #1e293b;
    }

    .empty-state p {
      margin: 0;
      font-size: 14px;
    }

    /* ===== DELETE MODAL ===== */
    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(0,0,0,0.5);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .modal-content {
      background: white;
      border-radius: 12px;
      padding: 24px;
      max-width: 400px;
      width: 90%;
      box-shadow: 0 20px 40px rgba(0,0,0,0.2);
    }

    .modal-header {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 16px;
    }

    .modal-header h3 {
      margin: 0;
      font-size: 18px;
      color: #1e293b;
    }

    .modal-content p {
      margin: 0 0 8px;
      color: #64748b;
      font-size: 14px;
    }

    .warning-text {
      color: #dc2626 !important;
      font-size: 12px !important;
    }

    .modal-actions {
      display: flex;
      gap: 12px;
      margin-top: 20px;
      justify-content: flex-end;
    }

    .btn-cancel {
      padding: 8px 16px;
      background: #f1f5f9;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      color: #64748b;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-cancel:hover {
      background: #e2e8f0;
    }

    .btn-delete {
      padding: 8px 16px;
      background: #dc2626;
      border: none;
      border-radius: 6px;
      color: white;
      font-size: 13px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-delete:hover {
      background: #b91c1c;
    }

    /* ===== RESPONSIVE ===== */
    @media (max-width: 768px) {
      .filter-bar {
        flex-wrap: wrap;
      }
      
      .search-wrapper {
        max-width: 100%;
        width: 100%;
      }
      
      .garages-grid {
        grid-template-columns: 1fr;
      }
      
      .stats-bar {
        flex-wrap: wrap;
      }
    }
  `]
})
export class SuppliersComponent implements OnInit {
  // Données
  allGarages: Garage[] = [];
  filteredGarages: Garage[] = [];
  
  // Filtres
  searchQuery = '';
  filterService = '';
  filterStatus = '';
  
  // Popup
  isPopupOpen = false;
  selectedGarage: Garage | null = null;
  
  // Suppression
  showDeleteConfirm = false;
  garageToDelete: Garage | null = null;

  constructor(private apiService: ApiService) {}

  ngOnInit(): void {
    this.loadGarages();
  }

  loadGarages(): void {
    // Données de démonstration
    this.allGarages = [
      {
        id: '1',
        name: 'Garage Central Tunis',
        address: '45 Avenue Habib Bourguiba',
        city: 'Tunis',
        postalCode: '1000',
        phone: '+216 71 123 456',
        email: 'contact@garagecentral.tn',
        contactName: 'Mohamed Ben Ali',
        services: ['mecanique', 'carrosserie', 'electricite', 'diagnostic'],
        rating: 4.5,
        isActive: true,
        createdAt: new Date('2023-01-15')
      },
      {
        id: '2',
        name: 'Auto Service Sfax',
        address: '120 Route de Gabes',
        city: 'Sfax',
        postalCode: '3000',
        phone: '+216 74 456 789',
        email: 'info@autoservicesfax.tn',
        contactName: 'Ahmed Trabelsi',
        services: ['mecanique', 'pneumatique', 'vidange'],
        rating: 4.2,
        isActive: true,
        createdAt: new Date('2023-03-20')
      },
      {
        id: '3',
        name: 'Garage Express Sousse',
        address: '78 Boulevard de la Corniche',
        city: 'Sousse',
        postalCode: '4000',
        phone: '+216 73 789 012',
        email: 'express@garagesousse.tn',
        contactName: 'Karim Saidi',
        services: ['vidange', 'climatisation', 'diagnostic'],
        rating: 3.8,
        isActive: false,
        createdAt: new Date('2022-11-10')
      },
      {
        id: '4',
        name: 'Méca Pro Bizerte',
        address: '25 Rue de la République',
        city: 'Bizerte',
        postalCode: '7000',
        phone: '+216 72 345 678',
        email: 'contact@mecapro.tn',
        contactName: 'Sami Boussaidi',
        services: ['mecanique', 'electricite', 'pneumatique', 'carrosserie', 'vidange'],
        rating: 4.8,
        isActive: true,
        createdAt: new Date('2023-06-01')
      }
    ];
    this.filterGarages();
  }

  filterGarages(): void {
    this.filteredGarages = this.allGarages.filter(garage => {
      const matchesSearch = !this.searchQuery || 
        garage.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        garage.city.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        garage.address.toLowerCase().includes(this.searchQuery.toLowerCase());
      
      const matchesService = !this.filterService || garage.services.includes(this.filterService);
      
      const matchesStatus = !this.filterStatus || 
        (this.filterStatus === 'active' && garage.isActive) ||
        (this.filterStatus === 'inactive' && !garage.isActive);
      
      return matchesSearch && matchesService && matchesStatus;
    });
  }

  getActiveGarages(): Garage[] {
    return this.allGarages.filter(g => g.isActive);
  }

  getInactiveGarages(): Garage[] {
    return this.allGarages.filter(g => !g.isActive);
  }

  getAverageRating(): string {
    if (this.allGarages.length === 0) return '0.0';
    const sum = this.allGarages.reduce((acc, g) => acc + g.rating, 0);
    return (sum / this.allGarages.length).toFixed(1);
  }

  getServiceLabel(service: string): string {
    const labels: { [key: string]: string } = {
      'mecanique': 'Mécanique',
      'carrosserie': 'Carrosserie',
      'electricite': 'Électricité',
      'pneumatique': 'Pneus',
      'vidange': 'Vidange',
      'climatisation': 'Clim',
      'diagnostic': 'Diagnostic'
    };
    return labels[service] || service;
  }

  openAddPopup(): void {
    this.selectedGarage = null;
    this.isPopupOpen = true;
  }

  openEditPopup(garage: Garage): void {
    this.selectedGarage = { ...garage };
    this.isPopupOpen = true;
  }

  closePopup(): void {
    this.isPopupOpen = false;
    this.selectedGarage = null;
  }

  saveGarage(garage: Garage): void {
    if (garage.id) {
      // Mise à jour
      const index = this.allGarages.findIndex(g => g.id === garage.id);
      if (index !== -1) {
        this.allGarages[index] = { ...garage, updatedAt: new Date() };
      }
    } else {
      // Création
      garage.id = Date.now().toString();
      garage.createdAt = new Date();
      this.allGarages.push(garage);
    }
    this.filterGarages();
    this.closePopup();
  }

  confirmDelete(garage: Garage): void {
    this.garageToDelete = garage;
    this.showDeleteConfirm = true;
  }

  cancelDelete(): void {
    this.garageToDelete = null;
    this.showDeleteConfirm = false;
  }

  deleteGarage(): void {
    if (this.garageToDelete) {
      this.allGarages = this.allGarages.filter(g => g.id !== this.garageToDelete!.id);
      this.filterGarages();
    }
    this.cancelDelete();
  }

  callGarage(garage: Garage): void {
    window.open(`tel:${garage.phone}`, '_self');
  }

  viewGarageDetails(garage: Garage): void {
    this.openEditPopup(garage);
  }
}
