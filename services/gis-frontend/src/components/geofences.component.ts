import { Component, OnInit, AfterViewInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';
import { Geofence, GeofenceEvent, GeofencePoint, Vehicle, Company, GeofenceGroup } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import * as L from 'leaflet';

@Component({
  selector: 'app-geofences',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="geofences-page">
        <!-- Filter Bar -->
        <div class="filter-bar">
          <div class="search-wrapper">
            <svg class="search-icon" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
            </svg>
            <input type="text" class="search-input" placeholder="Rechercher une zone..." [(ngModel)]="searchQuery" (input)="filterGeofences()">
          </div>
          <select class="filter-select" [(ngModel)]="filterType" (change)="filterGeofences()">
            <option value="">Tous les types</option>
            <option value="circle">Cercle</option>
            <option value="polygon">Polygone</option>
          </select>
          <select class="filter-select" [(ngModel)]="filterStatus" (change)="filterGeofences()">
            <option value="">Tous les statuts</option>
            <option value="active">Actives</option>
            <option value="inactive">Inactives</option>
          </select>
          <button class="btn-add" (click)="openAddPopup()">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/>
              <line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Nouvelle zone
          </button>
        </div>

        <!-- Stats Bar -->
        <div class="stats-bar">
          <div class="stat-item">
            <div class="stat-icon total">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                <circle cx="12" cy="10" r="3"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ allGeofences.length }}</span>
              <span class="stat-label">Total zones</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon active">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                <polyline points="22 4 12 14.01 9 11.01"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getActiveCount() }}</span>
              <span class="stat-label">Actives</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon circles">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="12" cy="12" r="10"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getCircleCount() }}</span>
              <span class="stat-label">Cercles</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon polygons">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polygon points="12 2 22 8.5 22 15.5 12 22 2 15.5 2 8.5 12 2"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ getPolygonCount() }}</span>
              <span class="stat-label">Polygones</span>
            </div>
          </div>
          <div class="stat-item">
            <div class="stat-icon events">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
              </svg>
            </div>
            <div class="stat-info">
              <span class="stat-value">{{ events.length }}</span>
              <span class="stat-label">Événements</span>
            </div>
          </div>
        </div>

        <!-- Main Content -->
        <div class="main-content">
          <!-- Map Overview -->
          <div class="map-container">
            <div id="overview-map" class="overview-map"></div>
          </div>

          <!-- Geofences List -->
          <div class="geofences-list">
            @for (geofence of geofences; track geofence.id) {
              <div class="geofence-card" [class.inactive]="!geofence.isActive">
                <div class="card-header">
                  <div class="zone-icon" [style.background]="geofence.color + '20'" [style.color]="geofence.color">
                    <svg *ngIf="geofence.type === 'circle'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <circle cx="12" cy="12" r="10"/>
                    </svg>
                    <svg *ngIf="geofence.type === 'polygon'" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polygon points="12 2 22 8.5 22 15.5 12 22 2 15.5 2 8.5 12 2"/>
                    </svg>
                  </div>
                  <div class="card-title-section">
                    <h3>{{ geofence.name }}</h3>
                    <span class="zone-type">{{ geofence.type === 'circle' ? 'Cercle' : 'Polygone' }}</span>
                  </div>
                  <div class="status-badge" [class.active]="geofence.isActive" [class.inactive]="!geofence.isActive">
                    {{ geofence.isActive ? 'Active' : 'Inactive' }}
                  </div>
                </div>

                <div class="card-body">
                  <p class="description" *ngIf="geofence.description">{{ geofence.description }}</p>
                  
                  <div class="zone-details">
                    <div class="detail-row" *ngIf="geofence.type === 'circle' && geofence.radius">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/>
                        <line x1="12" y1="12" x2="12" y2="6"/>
                      </svg>
                      <span>Rayon: {{ geofence.radius >= 1000 ? (geofence.radius / 1000) + ' km' : geofence.radius + ' m' }}</span>
                    </div>
                    <div class="detail-row" *ngIf="geofence.type === 'polygon' && geofence.coordinates">
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <polygon points="12 2 22 8.5 22 15.5 12 22 2 15.5 2 8.5 12 2"/>
                      </svg>
                      <span>{{ geofence.coordinates.length }} points</span>
                    </div>
                  </div>

                  <div class="alerts-config">
                    <span class="alert-badge" [class.enabled]="geofence.alertOnEntry">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M15 3h4a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2h-4"/>
                        <polyline points="10 17 15 12 10 7"/>
                        <line x1="15" y1="12" x2="3" y2="12"/>
                      </svg>
                      Entrée
                    </span>
                    <span class="alert-badge" [class.enabled]="geofence.alertOnExit">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>
                        <polyline points="16 17 21 12 16 7"/>
                        <line x1="21" y1="12" x2="9" y2="12"/>
                      </svg>
                      Sortie
                    </span>
                    <span class="alert-badge speed" *ngIf="geofence.alertSpeed" [class.enabled]="true">
                      <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"/>
                        <polyline points="12 6 12 12 16 14"/>
                      </svg>
                      {{ geofence.alertSpeed }} km/h
                    </span>
                  </div>

                  <div class="assigned-vehicles" *ngIf="geofence.assignedVehicleIds?.length">
                    <span class="vehicles-label">{{ geofence.assignedVehicleIds?.length }} véhicule(s) assigné(s)</span>
                  </div>
                </div>

                <div class="card-footer">
                  <button class="btn-action" (click)="toggleActive(geofence)">
                    <svg *ngIf="geofence.isActive" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="6" y="4" width="4" height="16"/>
                      <rect x="14" y="4" width="4" height="16"/>
                    </svg>
                    <svg *ngIf="!geofence.isActive" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polygon points="5 3 19 12 5 21 5 3"/>
                    </svg>
                    {{ geofence.isActive ? 'Désactiver' : 'Activer' }}
                  </button>
                  <button class="btn-action edit" (click)="editGeofence(geofence)">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                      <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                    </svg>
                    Modifier
                  </button>
                  <button class="btn-action danger" (click)="deleteGeofence(geofence)">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polyline points="3 6 5 6 21 6"/>
                      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                    </svg>
                  </button>
                </div>
              </div>
            }

            @if (geofences.length === 0) {
              <div class="empty-state-list">
                <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                  <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                  <circle cx="12" cy="10" r="3"/>
                </svg>
                <p>Aucune zone</p>
                <button class="btn-add-small" (click)="openAddPopup()">+ Créer</button>
              </div>
            }
          </div>
        </div>
      </div>

      <!-- Add/Edit Popup -->
      <div class="popup-overlay" *ngIf="showPopup" (click)="onOverlayClick($event)">
        <div class="popup-container">
          <div class="popup-header">
            <h3>{{ editingGeofence ? 'Modifier la zone' : 'Nouvelle zone géographique' }}</h3>
            <button class="close-btn" (click)="closePopup()">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"/>
                <line x1="6" y1="6" x2="18" y2="18"/>
              </svg>
            </button>
          </div>
          <div class="popup-body">
            <form (ngSubmit)="saveGeofence()">
              <div class="form-row">
                <div class="form-group flex-1">
                  <label for="name">Nom de la zone *</label>
                  <input type="text" id="name" name="name" [(ngModel)]="geofenceForm.name" required placeholder="Ex: Siège social">
                </div>
                <div class="form-group">
                  <label for="type">Type</label>
                  <select id="type" name="type" [(ngModel)]="geofenceForm.type" (change)="onTypeChange()">
                    <option value="circle">Cercle</option>
                    <option value="polygon">Polygone</option>
                  </select>
                </div>
              </div>

              <div class="form-group">
                <label for="description">Description</label>
                <input type="text" id="description" name="description" [(ngModel)]="geofenceForm.description" placeholder="Description optionnelle...">
              </div>

              <!-- Map for drawing -->
              <div class="map-draw-section">
                <div class="map-toolbar">
                  <span class="toolbar-label">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
                      <circle cx="12" cy="10" r="3"/>
                    </svg>
                    {{ geofenceForm.type === 'circle' ? 'Cliquez pour placer le centre du cercle' : 'Cliquez pour ajouter des points (min. 3)' }}
                  </span>
                  <button type="button" class="btn-clear" (click)="clearDrawing()" *ngIf="hasDrawing()">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <polyline points="3 6 5 6 21 6"/>
                      <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                    </svg>
                    Effacer
                  </button>
                </div>
                <div id="draw-map" class="draw-map"></div>
                
                <!-- Radius slider for circles -->
                <div class="radius-control" *ngIf="geofenceForm.type === 'circle' && geofenceForm.centerLat">
                  <label>Rayon: {{ geofenceForm.radius }} m</label>
                  <input type="range" min="50" max="5000" step="50" [(ngModel)]="geofenceForm.radius" name="radius" (input)="updateCircleRadius()">
                </div>

                <!-- Polygon points info -->
                <div class="points-info" *ngIf="geofenceForm.type === 'polygon' && geofenceForm.coordinates.length > 0">
                  <span>{{ geofenceForm.coordinates.length }} point(s) défini(s)</span>
                  <button type="button" class="btn-undo" (click)="undoLastPoint()" *ngIf="geofenceForm.coordinates.length > 0">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13"/>
                    </svg>
                    Annuler dernier
                  </button>
                </div>
              </div>

              <div class="form-row">
                <div class="form-group">
                  <label>Couleur</label>
                  <div class="color-picker">
                    @for (c of colors; track c) {
                      <button type="button" class="color-option" [style.background]="c" [class.selected]="geofenceForm.color === c" (click)="setColor(c)"></button>
                    }
                  </div>
                </div>
                <div class="form-group">
                  <label class="toggle-label">
                    <input type="checkbox" [(ngModel)]="geofenceForm.isActive" name="isActive">
                    <span class="toggle-text">{{ geofenceForm.isActive ? 'Active' : 'Inactive' }}</span>
                  </label>
                </div>
              </div>

              <div class="alerts-section">
                <span class="section-label">Alertes</span>
                <div class="alerts-row">
                  <label class="checkbox-label">
                    <input type="checkbox" [(ngModel)]="geofenceForm.alertOnEntry" name="alertOnEntry">
                    <span>Entrée</span>
                  </label>
                  <label class="checkbox-label">
                    <input type="checkbox" [(ngModel)]="geofenceForm.alertOnExit" name="alertOnExit">
                    <span>Sortie</span>
                  </label>
                  <div class="speed-input">
                    <label>Vitesse max:</label>
                    <input type="number" [(ngModel)]="geofenceForm.alertSpeed" name="alertSpeed" min="0" step="5" placeholder="km/h">
                  </div>
                </div>
                <div class="cooldown-row">
                  <label>Cooldown notifications:</label>
                  <input type="number" [(ngModel)]="geofenceForm.notificationCooldownMinutes" name="cooldown" min="0" step="1" placeholder="min"> min
                </div>
              </div>

              <!-- Active Hours Section -->
              <div class="active-hours-section">
                <span class="section-label">Heures d'activité (optionnel)</span>
                <div class="hours-row">
                  <div class="time-input">
                    <label>Début:</label>
                    <input type="time" [(ngModel)]="geofenceForm.activeStartTime" name="startTime">
                  </div>
                  <div class="time-input">
                    <label>Fin:</label>
                    <input type="time" [(ngModel)]="geofenceForm.activeEndTime" name="endTime">
                  </div>
                </div>
                <div class="days-row">
                  <label class="day-checkbox" *ngFor="let day of weekDays">
                    <input type="checkbox" [checked]="isDayActive(day.value)" (change)="toggleDay(day.value)">
                    <span>{{ day.label }}</span>
                  </label>
                </div>
              </div>

              <!-- Group Selection -->
              <div class="form-group" *ngIf="groups.length > 0">
                <label>Groupe (optionnel)</label>
                <select [(ngModel)]="geofenceForm.groupId" name="groupId">
                  <option [ngValue]="null">Aucun groupe</option>
                  <option *ngFor="let group of groups" [ngValue]="group.id">{{ group.name }}</option>
                </select>
              </div>

              <div class="popup-footer">
                <button type="button" class="btn-cancel" (click)="closePopup()">Annuler</button>
                <button type="submit" class="btn-save" [disabled]="!canSave()">
                  {{ editingGeofence ? 'Mettre à jour' : 'Créer la zone' }}
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .geofences-page {
      flex: 1;
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

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
      max-width: 280px;
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
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 12px;
    }

    .search-input:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .filter-select {
      padding: 6px 10px;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 12px;
      background: white;
    }

    .btn-add {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 14px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 4px;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
      margin-left: auto;
    }

    .btn-add:hover { background: #2563eb; }

    /* Stats Bar */
    .stats-bar {
      display: flex;
      gap: 1px;
      background: #e2e8f0;
    }

    .stat-item {
      flex: 1;
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 16px;
      background: white;
    }

    .stat-icon {
      width: 36px;
      height: 36px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .stat-icon.total { background: #eff6ff; color: #3b82f6; }
    .stat-icon.active { background: #dcfce7; color: #16a34a; }
    .stat-icon.circles { background: #fef3c7; color: #d97706; }
    .stat-icon.polygons { background: #f3e8ff; color: #9333ea; }
    .stat-icon.events { background: #fee2e2; color: #dc2626; }

    .stat-info { display: flex; flex-direction: column; }
    .stat-value { font-size: 18px; font-weight: 700; color: #1e293b; }
    .stat-label { font-size: 10px; color: #64748b; }

    /* Main Content */
    .main-content {
      flex: 1;
      display: flex;
      gap: 1px;
      background: #e2e8f0;
      min-height: 0;
    }

    .map-container {
      flex: 1;
      position: relative;
      background: #f1f5f9;
    }

    .overview-map {
      width: 100%;
      height: 100%;
      min-height: 400px;
    }

    .geofences-list {
      width: 340px;
      background: white;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 1px;
      background: #f1f5f9;
    }

    .geofence-card {
      background: white;
      border-radius: 0;
      overflow: hidden;
      cursor: pointer;
      transition: background 0.15s;
    }

    .geofence-card:hover { background: #f8fafc; }
    .geofence-card.inactive { opacity: 0.7; }

    .card-header {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 14px;
      border-bottom: 1px solid #f1f5f9;
    }

    .zone-icon {
      width: 40px;
      height: 40px;
      border-radius: 8px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .card-title-section { flex: 1; }
    .card-title-section h3 {
      margin: 0;
      font-size: 14px;
      font-weight: 600;
      color: #1e293b;
    }
    .zone-type {
      font-size: 11px;
      color: #94a3b8;
    }

    .status-badge {
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 10px;
      font-weight: 500;
    }

    .status-badge.active { background: #dcfce7; color: #16a34a; }
    .status-badge.inactive { background: #f1f5f9; color: #64748b; }

    .card-body { padding: 14px; }

    .description {
      font-size: 12px;
      color: #64748b;
      margin: 0 0 12px 0;
      line-height: 1.4;
    }

    .zone-details {
      display: flex;
      flex-direction: column;
      gap: 6px;
      margin-bottom: 12px;
    }

    .detail-row {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: #64748b;
    }

    .detail-row svg { color: #94a3b8; }

    .alerts-config {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
      margin-bottom: 10px;
    }

    .alert-badge {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 4px 8px;
      border-radius: 4px;
      font-size: 10px;
      background: #f1f5f9;
      color: #94a3b8;
    }

    .alert-badge.enabled { background: #dbeafe; color: #2563eb; }
    .alert-badge.speed.enabled { background: #fef3c7; color: #d97706; }

    .assigned-vehicles {
      padding-top: 10px;
      border-top: 1px solid #f1f5f9;
    }

    .vehicles-label {
      font-size: 11px;
      color: #64748b;
    }

    .card-footer {
      display: flex;
      gap: 6px;
      padding: 10px 14px;
      border-top: 1px solid #f1f5f9;
      background: #fafafa;
    }

    .btn-action {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 6px 10px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 11px;
      color: #64748b;
      cursor: pointer;
    }

    .btn-action:hover { background: #f8fafc; color: #1e293b; }
    .btn-action.edit:hover { background: #eff6ff; color: #3b82f6; border-color: #bfdbfe; }
    .btn-action.danger { padding: 6px; margin-left: auto; }
    .btn-action.danger:hover { background: #fee2e2; color: #dc2626; border-color: #fecaca; }

    /* Events Panel */
    .events-panel {
      width: 320px;
      background: white;
      display: flex;
      flex-direction: column;
    }

    .events-header {
      padding: 12px 14px;
      border-bottom: 1px solid #f1f5f9;
    }

    .events-header h4 {
      margin: 0;
      font-size: 13px;
      font-weight: 600;
      color: #1e293b;
    }

    .events-list {
      flex: 1;
      overflow-y: auto;
    }

    .event-item {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 14px;
      border-bottom: 1px solid #f8fafc;
    }

    .event-icon {
      width: 28px;
      height: 28px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .event-icon.entry { background: #dcfce7; color: #16a34a; }
    .event-icon.exit { background: #fee2e2; color: #dc2626; }
    .event-icon.speed_violation { background: #fef3c7; color: #d97706; }

    .event-info { flex: 1; }
    .event-title { display: block; font-size: 11px; font-weight: 500; color: #1e293b; }
    .event-details { font-size: 10px; color: #94a3b8; }
    .event-time { font-size: 10px; color: #94a3b8; white-space: nowrap; }

    /* Empty State List */
    .empty-state-list {
      display: flex;
      flex-direction: column;
      align-items: center;
      padding: 40px 20px;
      color: #94a3b8;
      background: white;
    }

    .empty-state-list svg { margin-bottom: 12px; opacity: 0.5; }
    .empty-state-list p { font-size: 13px; font-weight: 500; color: #64748b; margin: 0 0 12px 0; }

    .btn-add-small {
      padding: 6px 14px;
      background: #3b82f6;
      color: white;
      border: none;
      border-radius: 4px;
      font-size: 12px;
      cursor: pointer;
    }

    .btn-add-small:hover { background: #2563eb; }

    /* Popup */
    .popup-overlay {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      bottom: 0;
      background: rgba(15, 23, 42, 0.6);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
    }

    .popup-container {
      background: white;
      border-radius: 8px;
      width: 90%;
      max-width: 700px;
      max-height: 90vh;
      overflow-y: auto;
    }

    .popup-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 14px 18px;
      border-bottom: 1px solid #e2e8f0;
    }

    .popup-header h3 { margin: 0; font-size: 15px; font-weight: 600; color: #1e293b; }

    .close-btn {
      background: transparent;
      border: none;
      color: #64748b;
      cursor: pointer;
      padding: 4px;
    }

    .close-btn:hover { color: #1e293b; }

    .popup-body { padding: 18px; }

    .form-row {
      display: flex;
      gap: 12px;
      margin-bottom: 12px;
    }

    .form-group { display: flex; flex-direction: column; gap: 4px; margin-bottom: 12px; }
    .form-group.flex-1 { flex: 1; }

    .form-group label {
      font-size: 11px;
      font-weight: 500;
      color: #64748b;
    }

    .form-group input,
    .form-group select,
    .form-group textarea {
      padding: 8px 10px;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 13px;
    }

    .form-group input:focus,
    .form-group select:focus,
    .form-group textarea:focus {
      outline: none;
      border-color: #3b82f6;
    }

    .color-picker {
      display: flex;
      gap: 6px;
    }

    .color-option {
      width: 24px;
      height: 24px;
      border-radius: 4px;
      border: 2px solid transparent;
      cursor: pointer;
    }

    .color-option.selected { border-color: #1e293b; }

    .checkbox-label {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 12px;
      color: #1e293b;
      cursor: pointer;
    }

    .toggle-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
    }

    .toggle-text {
      font-size: 12px;
      color: #1e293b;
    }

    /* Map Drawing Section */
    .map-draw-section {
      margin-bottom: 14px;
      border: 1px solid #e2e8f0;
      border-radius: 6px;
      overflow: hidden;
    }

    .map-toolbar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 8px 12px;
      background: #f8fafc;
      border-bottom: 1px solid #e2e8f0;
    }

    .toolbar-label {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 11px;
      color: #64748b;
    }

    .toolbar-label svg { color: #94a3b8; }

    .btn-clear, .btn-undo {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 4px 8px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 10px;
      color: #64748b;
      cursor: pointer;
    }

    .btn-clear:hover, .btn-undo:hover { 
      background: #fee2e2; 
      color: #dc2626; 
      border-color: #fecaca; 
    }

    .draw-map {
      height: 280px;
      width: 100%;
      cursor: crosshair;
    }

    .radius-control {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .radius-control label {
      font-size: 11px;
      color: #64748b;
      white-space: nowrap;
      min-width: 100px;
    }

    .radius-control input[type="range"] {
      flex: 1;
      height: 4px;
      -webkit-appearance: none;
      background: #e2e8f0;
      border-radius: 2px;
    }

    .radius-control input[type="range"]::-webkit-slider-thumb {
      -webkit-appearance: none;
      width: 14px;
      height: 14px;
      border-radius: 50%;
      background: #3b82f6;
      cursor: pointer;
    }

    .points-info {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 8px 12px;
      background: #eff6ff;
      border-top: 1px solid #e2e8f0;
    }

    .points-info span {
      font-size: 11px;
      color: #3b82f6;
      font-weight: 500;
    }

    /* Alerts Section */
    .alerts-section {
      padding: 12px;
      background: #f8fafc;
      border-radius: 6px;
      margin-bottom: 14px;
    }

    .section-label {
      display: block;
      font-size: 11px;
      font-weight: 600;
      color: #64748b;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      margin-bottom: 10px;
    }

    .alerts-row {
      display: flex;
      align-items: center;
      gap: 16px;
      flex-wrap: wrap;
    }

    .speed-input {
      display: flex;
      align-items: center;
      gap: 6px;
      margin-left: auto;
    }

    .speed-input label {
      font-size: 11px;
      color: #64748b;
    }

    .speed-input input {
      width: 70px;
      padding: 4px 8px;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 12px;
    }

    .popup-footer {
      display: flex;
      justify-content: flex-end;
      gap: 10px;
      padding-top: 18px;
      margin-top: 18px;
      border-top: 1px solid #e2e8f0;
    }

    .btn-cancel {
      padding: 8px 16px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 12px;
      color: #64748b;
      cursor: pointer;
    }

    .btn-cancel:hover { background: #f8fafc; }

    .btn-save {
      padding: 8px 20px;
      background: #3b82f6;
      border: none;
      border-radius: 4px;
      color: white;
      font-size: 12px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-save:hover { background: #2563eb; }

    /* Cooldown Row */
    .cooldown-row {
      display: flex;
      align-items: center;
      gap: 8px;
      margin-top: 10px;
      font-size: 12px;
      color: #64748b;
    }

    .cooldown-row input {
      width: 60px;
      padding: 4px 8px;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 12px;
    }

    /* Active Hours Section */
    .active-hours-section {
      padding: 12px;
      background: #f8fafc;
      border-radius: 6px;
      margin-bottom: 14px;
    }

    .hours-row {
      display: flex;
      gap: 16px;
      margin-bottom: 10px;
    }

    .time-input {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .time-input label {
      font-size: 11px;
      color: #64748b;
    }

    .time-input input[type="time"] {
      padding: 4px 8px;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 12px;
    }

    .days-row {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }

    .day-checkbox {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 4px 8px;
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 4px;
      font-size: 11px;
      color: #64748b;
      cursor: pointer;
    }

    .day-checkbox:has(input:checked) {
      background: #dbeafe;
      border-color: #3b82f6;
      color: #1e40af;
    }

    .day-checkbox input {
      width: 12px;
      height: 12px;
    }

    @media (max-width: 900px) {
      .events-panel { display: none; }
    }
  `]
})
export class GeofencesComponent implements OnInit, AfterViewInit, OnDestroy {
  geofences: Geofence[] = [];
  allGeofences: Geofence[] = [];
  events: GeofenceEvent[] = [];
  vehicles: Vehicle[] = [];
  groups: GeofenceGroup[] = [];
  company: Company | null = null;

  weekDays = [
    { value: 'monday', label: 'Lun' },
    { value: 'tuesday', label: 'Mar' },
    { value: 'wednesday', label: 'Mer' },
    { value: 'thursday', label: 'Jeu' },
    { value: 'friday', label: 'Ven' },
    { value: 'saturday', label: 'Sam' },
    { value: 'sunday', label: 'Dim' }
  ];

  searchQuery = '';
  filterType = '';
  filterStatus = '';

  showPopup = false;
  editingGeofence: Geofence | null = null;

  colors = ['#22c55e', '#3b82f6', '#f97316', '#8b5cf6', '#06b6d4', '#ec4899', '#eab308', '#ef4444'];

  geofenceForm: any = {
    name: '',
    description: '',
    type: 'circle',
    color: '#3b82f6',
    isActive: true,
    centerLat: null,
    centerLng: null,
    radius: 500,
    coordinates: [] as GeofencePoint[],
    alertOnEntry: true,
    alertOnExit: true,
    alertSpeed: null,
    notificationCooldownMinutes: 5,
    activeStartTime: null as string | null,
    activeEndTime: null as string | null,
    activeDays: [] as string[],
    groupId: null as number | null
  };

  // Leaflet maps
  private overviewMap: L.Map | null = null;
  private drawMap: L.Map | null = null;
  private overviewLayers: L.FeatureGroup = L.featureGroup();
  private drawingLayer: L.Circle | L.Polygon | null = null;
  private markers: L.CircleMarker[] = [];
  private defaultCenter: L.LatLngExpression = [36.8065, 10.1815]; // Tunis, Tunisie

  constructor(
    private router: Router,
    private apiService: ApiService
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadData();
  }

  loadData() {
    this.apiService.getGeofences().subscribe({
      next: (geofences) => {
        this.allGeofences = geofences;
        this.geofences = [...this.allGeofences];
        this.renderGeofencesOnMap();
      },
      error: (err) => console.error('Error loading geofences:', err)
    });

    this.apiService.getVehicles().subscribe({
      next: (vehicles) => this.vehicles = vehicles,
      error: (err) => console.error('Error loading vehicles:', err)
    });

    this.apiService.getGeofenceGroups().subscribe({
      next: (groups) => this.groups = groups,
      error: (err) => console.error('Error loading groups:', err)
    });

    this.apiService.getGeofenceEvents().subscribe({
      next: (events) => this.events = events.slice(0, 50),
      error: (err) => console.error('Error loading events:', err)
    });
  }

  isDayActive(day: string): boolean {
    return this.geofenceForm.activeDays?.includes(day) || false;
  }

  toggleDay(day: string) {
    if (!this.geofenceForm.activeDays) {
      this.geofenceForm.activeDays = [];
    }
    const index = this.geofenceForm.activeDays.indexOf(day);
    if (index > -1) {
      this.geofenceForm.activeDays.splice(index, 1);
    } else {
      this.geofenceForm.activeDays.push(day);
    }
  }

  ngAfterViewInit() {
    setTimeout(() => this.initOverviewMap(), 100);
  }

  ngOnDestroy() {
    this.destroyMaps();
  }

  private initOverviewMap() {
    const mapEl = document.getElementById('overview-map');
    if (!mapEl || this.overviewMap) return;

    this.overviewMap = L.map('overview-map', {
      center: this.defaultCenter,
      zoom: 11,
      zoomControl: true
    });

    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap'
    }).addTo(this.overviewMap);

    this.overviewLayers.addTo(this.overviewMap);
    this.renderGeofencesOnMap();
  }

  private renderGeofencesOnMap() {
    this.overviewLayers.clearLayers();

    this.allGeofences.forEach(g => {
      if (g.type === 'circle' && g.center) {
        const circle = L.circle([g.center.lat, g.center.lng], {
          radius: g.radius || 500,
          color: g.color,
          fillColor: g.color,
          fillOpacity: g.isActive ? 0.3 : 0.1,
          weight: 2,
          dashArray: g.isActive ? '' : '5,5'
        });
        circle.bindTooltip(g.name, { permanent: false });
        circle.on('click', () => this.focusGeofence(g));
        this.overviewLayers.addLayer(circle);
      } else if (g.type === 'polygon' && g.coordinates?.length) {
        const latLngs = g.coordinates.map(c => [c.lat, c.lng] as L.LatLngExpression);
        const polygon = L.polygon(latLngs, {
          color: g.color,
          fillColor: g.color,
          fillOpacity: g.isActive ? 0.3 : 0.1,
          weight: 2,
          dashArray: g.isActive ? '' : '5,5'
        });
        polygon.bindTooltip(g.name, { permanent: false });
        polygon.on('click', () => this.focusGeofence(g));
        this.overviewLayers.addLayer(polygon);
      }
    });

    // Fit bounds if we have geofences
    if (this.allGeofences.length > 0 && this.overviewMap) {
      const bounds = this.overviewLayers.getBounds();
      if (bounds.isValid()) {
        this.overviewMap.fitBounds(bounds, { padding: [30, 30] });
      }
    }
  }

  private focusGeofence(geofence: Geofence) {
    if (!this.overviewMap) return;
    
    if (geofence.type === 'circle' && geofence.center) {
      this.overviewMap.setView([geofence.center.lat, geofence.center.lng], 14);
    } else if (geofence.coordinates?.length) {
      const latLngs = geofence.coordinates.map(c => [c.lat, c.lng] as L.LatLngExpression);
      this.overviewMap.fitBounds(L.latLngBounds(latLngs), { padding: [50, 50] });
    }
  }

  private initDrawMap() {
    setTimeout(() => {
      const mapEl = document.getElementById('draw-map');
      if (!mapEl) return;

      if (this.drawMap) {
        this.drawMap.remove();
        this.drawMap = null;
      }

      this.drawMap = L.map('draw-map', {
        center: this.defaultCenter,
        zoom: 12,
        zoomControl: true
      });

      L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
        attribution: '© OpenStreetMap'
      }).addTo(this.drawMap);

      // Set click handler for drawing
      this.drawMap.on('click', (e: L.LeafletMouseEvent) => this.onMapClick(e));

      // If editing, show existing geometry
      if (this.editingGeofence) {
        this.loadExistingGeometry();
      }
    }, 100);
  }

  private loadExistingGeometry() {
    if (!this.editingGeofence || !this.drawMap) return;

    if (this.editingGeofence.type === 'circle' && this.editingGeofence.center) {
      this.geofenceForm.centerLat = this.editingGeofence.center.lat;
      this.geofenceForm.centerLng = this.editingGeofence.center.lng;
      this.geofenceForm.radius = this.editingGeofence.radius || 500;
      this.updateDrawingCircle();
      this.drawMap.setView([this.editingGeofence.center.lat, this.editingGeofence.center.lng], 14);
    } else if (this.editingGeofence.coordinates?.length) {
      this.geofenceForm.coordinates = [...this.editingGeofence.coordinates];
      this.updateDrawingPolygon();
      const latLngs = this.editingGeofence.coordinates.map(c => [c.lat, c.lng] as L.LatLngExpression);
      this.drawMap.fitBounds(L.latLngBounds(latLngs), { padding: [50, 50] });
    }
  }

  private onMapClick(e: L.LeafletMouseEvent) {
    const { lat, lng } = e.latlng;

    if (this.geofenceForm.type === 'circle') {
      this.geofenceForm.centerLat = lat;
      this.geofenceForm.centerLng = lng;
      this.updateDrawingCircle();
    } else {
      this.geofenceForm.coordinates.push({ lat, lng });
      this.updateDrawingPolygon();
    }
  }

  private updateDrawingCircle() {
    if (!this.drawMap) return;

    // Remove existing
    if (this.drawingLayer) {
      this.drawMap.removeLayer(this.drawingLayer);
    }
    this.markers.forEach(m => this.drawMap?.removeLayer(m));
    this.markers = [];

    if (this.geofenceForm.centerLat && this.geofenceForm.centerLng) {
      // Draw circle
      this.drawingLayer = L.circle(
        [this.geofenceForm.centerLat, this.geofenceForm.centerLng],
        {
          radius: this.geofenceForm.radius,
          color: this.geofenceForm.color,
          fillColor: this.geofenceForm.color,
          fillOpacity: 0.3,
          weight: 2
        }
      ).addTo(this.drawMap);

      // Center marker
      const marker = L.circleMarker(
        [this.geofenceForm.centerLat, this.geofenceForm.centerLng],
        { radius: 6, color: this.geofenceForm.color, fillColor: '#fff', fillOpacity: 1, weight: 2 }
      ).addTo(this.drawMap);
      this.markers.push(marker);
    }
  }

  private updateDrawingPolygon() {
    if (!this.drawMap) return;

    // Remove existing
    if (this.drawingLayer) {
      this.drawMap.removeLayer(this.drawingLayer);
      this.drawingLayer = null;
    }
    this.markers.forEach(m => this.drawMap?.removeLayer(m));
    this.markers = [];

    if (this.geofenceForm.coordinates.length > 0) {
      // Draw markers for each point
      this.geofenceForm.coordinates.forEach((coord: GeofencePoint, index: number) => {
        const marker = L.circleMarker(
          [coord.lat, coord.lng],
          { 
            radius: 6, 
            color: this.geofenceForm.color, 
            fillColor: index === 0 ? this.geofenceForm.color : '#fff', 
            fillOpacity: 1, 
            weight: 2 
          }
        ).addTo(this.drawMap!);
        this.markers.push(marker);
      });

      // Draw polygon if we have at least 3 points
      if (this.geofenceForm.coordinates.length >= 3) {
        const latLngs = this.geofenceForm.coordinates.map((c: GeofencePoint) => [c.lat, c.lng] as L.LatLngExpression);
        this.drawingLayer = L.polygon(latLngs, {
          color: this.geofenceForm.color,
          fillColor: this.geofenceForm.color,
          fillOpacity: 0.3,
          weight: 2
        }).addTo(this.drawMap);
      } else if (this.geofenceForm.coordinates.length === 2) {
        // Draw line between 2 points
        const latLngs = this.geofenceForm.coordinates.map((c: GeofencePoint) => [c.lat, c.lng] as L.LatLngExpression);
        L.polyline(latLngs, { color: this.geofenceForm.color, weight: 2, dashArray: '5,5' }).addTo(this.drawMap);
      }
    }
  }

  private destroyMaps() {
    if (this.overviewMap) {
      this.overviewMap.remove();
      this.overviewMap = null;
    }
    if (this.drawMap) {
      this.drawMap.remove();
      this.drawMap = null;
    }
  }

  filterGeofences() {
    this.geofences = this.allGeofences.filter(g => {
      const matchesSearch = !this.searchQuery ||
        g.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        (g.description || '').toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesType = !this.filterType || g.type === this.filterType;
      const matchesStatus = !this.filterStatus ||
        (this.filterStatus === 'active' && g.isActive) ||
        (this.filterStatus === 'inactive' && !g.isActive);
      return matchesSearch && matchesType && matchesStatus;
    });
  }

  getActiveCount(): number {
    return this.allGeofences.filter(g => g.isActive).length;
  }

  getCircleCount(): number {
    return this.allGeofences.filter(g => g.type === 'circle').length;
  }

  getPolygonCount(): number {
    return this.allGeofences.filter(g => g.type === 'polygon').length;
  }

  getVehicleName(vehicleId: string): string {
    const vehicle = this.vehicles.find(v => v.id === vehicleId);
    return vehicle ? vehicle.name : vehicleId;
  }

  getGeofenceName(geofenceId: string): string {
    const geofence = this.allGeofences.find(g => g.id === geofenceId);
    return geofence ? geofence.name : geofenceId;
  }

  getEventLabel(type: string): string {
    const labels: any = {
      entry: 'Entrée dans la zone',
      exit: 'Sortie de la zone',
      speed_violation: 'Excès de vitesse'
    };
    return labels[type] || type;
  }

  formatTime(date: Date): string {
    return new Date(date).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  toggleActive(geofence: Geofence) {
    this.apiService.updateGeofence(parseInt(geofence.id), { isActive: !geofence.isActive }).subscribe({
      next: () => this.refreshData(),
      error: (err) => console.error('Error toggling geofence:', err)
    });
  }

  openAddPopup() {
    this.editingGeofence = null;
    this.resetForm();
    this.showPopup = true;
    this.initDrawMap();
  }

  editGeofence(geofence: Geofence) {
    this.editingGeofence = geofence;
    this.geofenceForm = {
      name: geofence.name,
      description: geofence.description || '',
      type: geofence.type,
      color: geofence.color,
      isActive: geofence.isActive,
      centerLat: geofence.center?.lat || null,
      centerLng: geofence.center?.lng || null,
      radius: geofence.radius || 500,
      coordinates: geofence.coordinates ? [...geofence.coordinates] : [],
      alertOnEntry: geofence.alertOnEntry,
      alertOnExit: geofence.alertOnExit,
      alertSpeed: geofence.alertSpeed || null
    };
    this.showPopup = true;
    this.initDrawMap();
  }

  deleteGeofence(geofence: Geofence) {
    if (confirm(`Êtes-vous sûr de vouloir supprimer la zone "${geofence.name}" ?`)) {
      this.apiService.deleteGeofence(parseInt(geofence.id)).subscribe({
        next: () => this.refreshData(),
        error: (err) => console.error('Error deleting geofence:', err)
      });
    }
  }

  resetForm() {
    this.geofenceForm = {
      name: '',
      description: '',
      type: 'circle',
      color: '#3b82f6',
      isActive: true,
      centerLat: null,
      centerLng: null,
      radius: 500,
      coordinates: [] as GeofencePoint[],
      alertOnEntry: true,
      alertOnExit: true,
      alertSpeed: null,
      notificationCooldownMinutes: 5,
      activeStartTime: null,
      activeEndTime: null,
      activeDays: [],
      groupId: null
    };
  }

  closePopup() {
    this.showPopup = false;
    this.editingGeofence = null;
    if (this.drawMap) {
      this.drawMap.remove();
      this.drawMap = null;
    }
    this.drawingLayer = null;
    this.markers = [];
  }

  onOverlayClick(event: MouseEvent) {
    if ((event.target as HTMLElement).classList.contains('popup-overlay')) {
      this.closePopup();
    }
  }

  // Helper methods for the form
  canSave(): boolean {
    if (!this.geofenceForm.name) return false;
    if (this.geofenceForm.type === 'circle') {
      return this.geofenceForm.centerLat !== null && this.geofenceForm.centerLng !== null;
    } else {
      return this.geofenceForm.coordinates.length >= 3;
    }
  }

  hasDrawing(): boolean {
    if (this.geofenceForm.type === 'circle') {
      return this.geofenceForm.centerLat !== null;
    }
    return this.geofenceForm.coordinates.length > 0;
  }

  clearDrawing() {
    if (this.geofenceForm.type === 'circle') {
      this.geofenceForm.centerLat = null;
      this.geofenceForm.centerLng = null;
    } else {
      this.geofenceForm.coordinates = [];
    }
    if (this.drawingLayer && this.drawMap) {
      this.drawMap.removeLayer(this.drawingLayer);
      this.drawingLayer = null;
    }
    this.markers.forEach(m => this.drawMap?.removeLayer(m));
    this.markers = [];
  }

  setColor(color: string) {
    this.geofenceForm.color = color;
    if (this.geofenceForm.type === 'circle') {
      this.updateDrawingCircle();
    } else {
      this.updateDrawingPolygon();
    }
  }

  updateCircleRadius() {
    this.updateDrawingCircle();
  }

  undoLastPoint() {
    if (this.geofenceForm.coordinates.length > 0) {
      this.geofenceForm.coordinates.pop();
      this.updateDrawingPolygon();
    }
  }

  onTypeChange() {
    this.clearDrawing();
  }

  saveGeofence() {
    if (!this.canSave()) return;

    const geofenceData: Partial<Geofence> = {
      companyId: this.company?.id || '',
      name: this.geofenceForm.name,
      description: this.geofenceForm.description,
      type: this.geofenceForm.type,
      color: this.geofenceForm.color,
      isActive: this.geofenceForm.isActive,
      alertOnEntry: this.geofenceForm.alertOnEntry,
      alertOnExit: this.geofenceForm.alertOnExit,
      alertSpeed: this.geofenceForm.alertSpeed || undefined
    };

    if (this.geofenceForm.type === 'circle') {
      geofenceData.center = {
        lat: this.geofenceForm.centerLat,
        lng: this.geofenceForm.centerLng
      };
      geofenceData.radius = this.geofenceForm.radius;
      geofenceData.coordinates = undefined;
    } else {
      geofenceData.coordinates = [...this.geofenceForm.coordinates];
      geofenceData.center = undefined;
      geofenceData.radius = undefined;
    }

    if (this.editingGeofence) {
      this.apiService.updateGeofence(parseInt(this.editingGeofence.id), geofenceData).subscribe({
        next: () => {
          this.refreshData();
          this.renderGeofencesOnMap();
          this.closePopup();
        },
        error: (err) => console.error('Error updating geofence:', err)
      });
    } else {
      this.apiService.createGeofence({
        ...geofenceData,
        createdAt: new Date()
      }).subscribe({
        next: () => {
          this.refreshData();
          this.renderGeofencesOnMap();
          this.closePopup();
        },
        error: (err) => console.error('Error creating geofence:', err)
      });
    }
  }

  refreshData() {
    this.apiService.getGeofences().subscribe({
      next: (geofences) => {
        this.allGeofences = geofences;
        this.geofences = [...this.allGeofences];
        this.filterGeofences();
      },
      error: (err) => console.error('Error refreshing geofences:', err)
    });
  }
}
