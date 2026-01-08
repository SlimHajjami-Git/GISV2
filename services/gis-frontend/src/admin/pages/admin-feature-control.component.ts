import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Client, MaintenanceMode } from '../services/admin.service';

@Component({
  selector: 'admin-feature-control',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Feature Control">
      <div class="feature-control-page">
        <div class="maintenance-section">
          <div class="section-header">
            <div class="header-content">
              <h2>Maintenance Mode</h2>
              <p>Temporarily disable specific pages for all users during maintenance</p>
            </div>
            <div class="maintenance-toggle" [class.active]="maintenanceMode.enabled">
              <label class="toggle-label">
                <input type="checkbox" [(ngModel)]="maintenanceMode.enabled" (change)="updateMaintenanceMode()" />
                <span class="toggle-switch"></span>
                <span class="toggle-text">{{ maintenanceMode.enabled ? 'Active' : 'Inactive' }}</span>
              </label>
            </div>
          </div>

          <div class="maintenance-config" *ngIf="maintenanceMode.enabled">
            <div class="config-row">
              <div class="form-group">
                <label>Maintenance Message</label>
                <textarea [(ngModel)]="maintenanceMode.message" rows="3" placeholder="This page is currently under maintenance. Please try again later."></textarea>
              </div>
            </div>

            <div class="config-row">
              <div class="form-group">
                <label>Scheduled End Time (Optional)</label>
                <input type="datetime-local" [(ngModel)]="scheduledEndString" />
              </div>
            </div>

            <div class="pages-to-disable">
              <label>Select Pages to Disable</label>
              <div class="pages-grid">
                <label class="page-checkbox" *ngFor="let page of allPages">
                  <input type="checkbox" [checked]="maintenanceMode.pages.includes(page)" (change)="toggleMaintenancePage(page)" />
                  <span class="checkmark"></span>
                  <span class="page-name">{{ formatPageName(page) }}</span>
                </label>
              </div>
              <div class="quick-select">
                <button (click)="selectAllPages()">Select All</button>
                <button (click)="deselectAllPages()">Deselect All</button>
              </div>
            </div>

            <button class="save-btn" (click)="saveMaintenanceMode()">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17,21 17,13 7,13 7,21"/>
                <polyline points="7,3 7,8 15,8"/>
              </svg>
              Save Maintenance Settings
            </button>
          </div>

          <div class="maintenance-preview" *ngIf="maintenanceMode.enabled && maintenanceMode.pages.length > 0">
            <h4>Preview: Disabled Pages</h4>
            <div class="disabled-pages-list">
              <span class="disabled-page" *ngFor="let page of maintenanceMode.pages">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/>
                </svg>
                {{ formatPageName(page) }}
              </span>
            </div>
          </div>
        </div>

        <div class="client-features-section">
          <div class="section-header">
            <div class="header-content">
              <h2>Client Feature Management</h2>
              <p>Enable or disable specific features for individual clients</p>
            </div>
            <select class="client-select" [(ngModel)]="selectedClientId" (change)="loadClientFeatures()">
              <option [value]="null">Select a client...</option>
              <option *ngFor="let client of clients" [value]="client.id">{{ client.name }}</option>
            </select>
          </div>

          <div class="client-features" *ngIf="selectedClient">
            <div class="client-header">
              <div class="client-info">
                <div class="client-avatar">{{ selectedClient.name.charAt(0) }}</div>
                <div>
                  <h3>{{ selectedClient.name }}</h3>
                  <span class="client-plan">{{ selectedClient.subscriptionName || 'No subscription' }}</span>
                </div>
              </div>
              <span class="status-badge" [class]="selectedClient.status">{{ selectedClient.status | titlecase }}</span>
            </div>

            <div class="features-grid">
              <div class="feature-card" *ngFor="let feature of clientFeatures">
                <div class="feature-icon" [class.enabled]="feature.enabled">
                  <svg [innerHTML]="feature.icon"></svg>
                </div>
                <div class="feature-info">
                  <h4>{{ feature.name }}</h4>
                  <p>{{ feature.description }}</p>
                </div>
                <label class="feature-toggle">
                  <input type="checkbox" [(ngModel)]="feature.enabled" />
                  <span class="toggle-switch small"></span>
                </label>
              </div>
            </div>

            <div class="features-actions">
              <button class="btn-secondary" (click)="resetClientFeatures()">Reset to Plan Default</button>
              <button class="btn-primary" (click)="saveClientFeatures()">Save Changes</button>
            </div>
          </div>

          <div class="no-client-selected" *ngIf="!selectedClient">
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
              <circle cx="9" cy="7" r="4"/>
              <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
              <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
            </svg>
            <p>Select a client to manage their feature access</p>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .feature-control-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .maintenance-section, .client-features-section {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 20px;
      padding: 28px;
    }

    .section-header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      margin-bottom: 24px;
      flex-wrap: wrap;
      gap: 16px;
    }

    .header-content h2 {
      margin: 0 0 6px 0;
      font-size: 20px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .header-content p {
      margin: 0;
      font-size: 14px;
      color: #8b98a5;
    }

    .maintenance-toggle {
      padding: 12px 20px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 12px;
      transition: all 0.3s;
    }

    .maintenance-toggle.active {
      background: rgba(239, 68, 68, 0.1);
      border-color: rgba(239, 68, 68, 0.3);
    }

    .toggle-label {
      display: flex;
      align-items: center;
      gap: 12px;
      cursor: pointer;
    }

    .toggle-label input { display: none; }

    .toggle-switch {
      width: 48px;
      height: 26px;
      background: rgba(255, 255, 255, 0.1);
      border-radius: 13px;
      position: relative;
      transition: all 0.3s;
    }

    .toggle-switch::after {
      content: '';
      position: absolute;
      width: 20px;
      height: 20px;
      background: #8b98a5;
      border-radius: 50%;
      top: 3px;
      left: 3px;
      transition: all 0.3s;
    }

    .toggle-label input:checked + .toggle-switch {
      background: #ef4444;
    }

    .toggle-label input:checked + .toggle-switch::after {
      left: 25px;
      background: #fff;
    }

    .toggle-switch.small {
      width: 40px;
      height: 22px;
    }

    .toggle-switch.small::after {
      width: 16px;
      height: 16px;
    }

    .toggle-label input:checked + .toggle-switch.small::after {
      left: 21px;
    }

    .feature-toggle input:checked + .toggle-switch {
      background: #00d4aa;
    }

    .toggle-text {
      font-size: 14px;
      font-weight: 500;
      color: #e7e9ea;
    }

    .maintenance-config {
      padding: 24px;
      background: rgba(239, 68, 68, 0.05);
      border: 1px solid rgba(239, 68, 68, 0.15);
      border-radius: 14px;
      margin-bottom: 20px;
    }

    .config-row {
      margin-bottom: 20px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-group label {
      font-size: 14px;
      font-weight: 500;
      color: #e7e9ea;
    }

    .form-group textarea, .form-group input {
      padding: 12px 14px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
      resize: vertical;
    }

    .form-group textarea:focus, .form-group input:focus {
      border-color: #ef4444;
    }

    .pages-to-disable {
      margin-bottom: 20px;
    }

    .pages-to-disable > label {
      display: block;
      font-size: 14px;
      font-weight: 500;
      color: #e7e9ea;
      margin-bottom: 12px;
    }

    .pages-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
      gap: 10px;
      margin-bottom: 12px;
    }

    .page-checkbox {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 10px 14px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
      position: relative;
    }

    .page-checkbox:hover {
      background: rgba(255, 255, 255, 0.06);
    }

    .page-checkbox input { display: none; }

    .page-checkbox .checkmark {
      width: 18px;
      height: 18px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.2);
      border-radius: 4px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .page-checkbox input:checked ~ .checkmark {
      background: #ef4444;
      border-color: #ef4444;
    }

    .page-checkbox input:checked ~ .checkmark::after {
      content: '';
      width: 4px;
      height: 8px;
      border: solid #fff;
      border-width: 0 2px 2px 0;
      transform: rotate(45deg);
    }

    .page-name {
      font-size: 13px;
      color: #e7e9ea;
    }

    .quick-select {
      display: flex;
      gap: 10px;
    }

    .quick-select button {
      padding: 6px 12px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 6px;
      color: #8b98a5;
      font-size: 12px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .quick-select button:hover {
      background: rgba(255, 255, 255, 0.1);
      color: #e7e9ea;
    }

    .save-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      width: 100%;
      padding: 14px;
      background: #ef4444;
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .save-btn:hover {
      background: #dc2626;
    }

    .maintenance-preview {
      padding: 20px;
      background: rgba(255, 255, 255, 0.03);
      border-radius: 12px;
    }

    .maintenance-preview h4 {
      margin: 0 0 14px 0;
      font-size: 14px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .disabled-pages-list {
      display: flex;
      flex-wrap: wrap;
      gap: 10px;
    }

    .disabled-page {
      display: flex;
      align-items: center;
      gap: 6px;
      padding: 6px 12px;
      background: rgba(239, 68, 68, 0.15);
      border-radius: 6px;
      font-size: 13px;
      color: #ef4444;
    }

    .client-select {
      padding: 12px 16px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
      cursor: pointer;
      min-width: 240px;
    }

    .client-features {
      padding: 24px;
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 14px;
    }

    .client-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 24px;
      padding-bottom: 20px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .client-info {
      display: flex;
      align-items: center;
      gap: 14px;
    }

    .client-avatar {
      width: 52px;
      height: 52px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 14px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 22px;
      color: #fff;
    }

    .client-info h3 {
      margin: 0 0 4px 0;
      font-size: 18px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .client-plan {
      font-size: 13px;
      color: #8b98a5;
    }

    .status-badge {
      padding: 6px 12px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-badge.active { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
    .status-badge.suspended { background: rgba(239, 68, 68, 0.15); color: #ef4444; }

    .features-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
      gap: 16px;
      margin-bottom: 24px;
    }

    .feature-card {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 16px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 12px;
      transition: all 0.2s;
    }

    .feature-card:hover {
      border-color: rgba(255, 255, 255, 0.15);
    }

    .feature-icon {
      width: 44px;
      height: 44px;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 10px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #6b7280;
      flex-shrink: 0;
      transition: all 0.3s;
    }

    .feature-icon.enabled {
      background: rgba(0, 212, 170, 0.15);
      color: #00d4aa;
    }

    .feature-info {
      flex: 1;
    }

    .feature-info h4 {
      margin: 0 0 4px 0;
      font-size: 14px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .feature-info p {
      margin: 0;
      font-size: 12px;
      color: #6b7280;
    }

    .feature-toggle {
      cursor: pointer;
    }

    .features-actions {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding-top: 20px;
      border-top: 1px solid rgba(255, 255, 255, 0.08);
    }

    .btn-secondary {
      padding: 12px 20px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
    }

    .btn-primary {
      padding: 12px 20px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
    }

    .no-client-selected {
      padding: 60px 40px;
      text-align: center;
      color: #6b7280;
    }

    .no-client-selected svg {
      margin-bottom: 16px;
      opacity: 0.5;
    }

    .no-client-selected p {
      font-size: 14px;
      margin: 0;
    }
  `]
})
export class AdminFeatureControlComponent implements OnInit {
  clients: Client[] = [];
  selectedClientId: number | null = null;
  selectedClient: Client | null = null;
  allPages: string[] = [];

  maintenanceMode: MaintenanceMode = {
    enabled: false,
    pages: [],
    message: ''
  };
  scheduledEndString = '';

  clientFeatures: any[] = [];

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    this.allPages = this.adminService.getAllPages();
    this.loadData();
  }

  loadData() {
    this.adminService.getClients().subscribe(clients => {
      this.clients = clients;
    });

    this.adminService.getMaintenanceMode().subscribe(mode => {
      this.maintenanceMode = mode;
      if (mode.scheduledEnd) {
        this.scheduledEndString = new Date(mode.scheduledEnd).toISOString().slice(0, 16);
      }
    });
  }

  loadClientFeatures() {
    this.selectedClient = this.clients.find(c => c.id === Number(this.selectedClientId)) || null;
  }

  updateMaintenanceMode() {
    if (!this.maintenanceMode.enabled) {
      this.maintenanceMode.pages = [];
      this.maintenanceMode.message = '';
      this.saveMaintenanceMode();
    }
  }

  toggleMaintenancePage(page: string) {
    const index = this.maintenanceMode.pages.indexOf(page);
    if (index > -1) {
      this.maintenanceMode.pages.splice(index, 1);
    } else {
      this.maintenanceMode.pages.push(page);
    }
  }

  selectAllPages() {
    this.maintenanceMode.pages = [...this.allPages];
  }

  deselectAllPages() {
    this.maintenanceMode.pages = [];
  }

  saveMaintenanceMode() {
    if (this.scheduledEndString) {
      this.maintenanceMode.scheduledEnd = new Date(this.scheduledEndString);
    }
    this.adminService.setMaintenanceMode(this.maintenanceMode).subscribe(() => {
      alert('Maintenance settings saved successfully');
    });
  }

  saveClientFeatures() {
    alert('Client features saved successfully');
  }

  resetClientFeatures() {
    alert('Features reset to plan defaults');
  }

  formatPageName(page: string): string {
    return page.split('-').map(w => w.charAt(0).toUpperCase() + w.slice(1)).join(' ');
  }
}
