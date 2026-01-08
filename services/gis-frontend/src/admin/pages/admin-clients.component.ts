import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Client } from '../services/admin.service';

@Component({
  selector: 'admin-clients',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Client Management">
      <div class="clients-page">
        <div class="page-header">
          <div class="header-left">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (input)="filterClients()" placeholder="Search clients..." />
            </div>
            <select class="filter-select" [(ngModel)]="statusFilter" (change)="filterClients()">
              <option value="all">All Status</option>
              <option value="active">Active</option>
              <option value="suspended">Suspended</option>
              <option value="pending">Pending</option>
            </select>
          </div>
          <button class="add-btn" (click)="showAddModal = true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            Add Client
          </button>
        </div>

        <div class="clients-grid">
          <div class="client-card" *ngFor="let client of filteredClients" [class]="client.status">
            <div class="card-header">
              <div class="client-avatar">{{ client.name.charAt(0) }}</div>
              <div class="client-info">
                <h3>{{ client.name }}</h3>
                <span class="client-type">{{ client.type | titlecase }}</span>
              </div>
              <div class="status-badge" [class]="client.status">{{ client.status | titlecase }}</div>
            </div>

            <div class="card-body">
              <div class="info-row">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                  <polyline points="22,6 12,13 2,6"/>
                </svg>
                <span>{{ client.email }}</span>
              </div>
              <div class="info-row" *ngIf="client.phone">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72"/>
                </svg>
                <span>{{ client.phone }}</span>
              </div>

              <div class="stats-row">
                <div class="stat">
                  <span class="stat-value">{{ client.currentVehicles }}/{{ client.maxVehicles }}</span>
                  <span class="stat-label">Vehicles</span>
                </div>
                <div class="stat">
                  <span class="stat-value">{{ client.currentUsers }}</span>
                  <span class="stat-label">Users</span>
                </div>
                <div class="stat">
                  <span class="stat-value">{{ client.subscriptionName || 'None' }}</span>
                  <span class="stat-label">Plan</span>
                </div>
              </div>
            </div>

            <div class="card-footer">
              <span class="joined-date">Joined {{ formatDate(client.createdAt) }}</span>
              <div class="actions">
                <button class="action-btn edit" (click)="editClient(client)" title="Edit">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="action-btn" [class.suspend]="client.status === 'active'" [class.activate]="client.status !== 'active'"
                        (click)="toggleClientStatus(client)" [title]="client.status === 'active' ? 'Suspend' : 'Activate'">
                  <svg *ngIf="client.status === 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/>
                  </svg>
                  <svg *ngIf="client.status !== 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/>
                    <polyline points="22,4 12,14.01 9,11.01"/>
                  </svg>
                </button>
                <button class="action-btn view" (click)="viewClient(client)" title="View Details">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                  </svg>
                </button>
              </div>
            </div>
          </div>
        </div>

        <div class="modal-overlay" *ngIf="showAddModal || showEditModal || showViewModal" (click)="closeModals()">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>{{ showViewModal ? 'Client Details' : (showEditModal ? 'Edit Client' : 'Add New Client') }}</h2>
              <button class="close-btn" (click)="closeModals()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body" *ngIf="!showViewModal">
              <h4 class="section-title">Informations société</h4>
              <div class="form-group">
                <label>Nom de la société *</label>
                <input type="text" [(ngModel)]="clientForm.name" placeholder="Nom de la société" />
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Email société</label>
                  <input type="email" [(ngModel)]="clientForm.email" placeholder="contact@societe.tn" />
                </div>
                <div class="form-group">
                  <label>Téléphone</label>
                  <input type="tel" [(ngModel)]="clientForm.phone" placeholder="+216 XX XXX XXX" />
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Type de société</label>
                  <select [(ngModel)]="clientForm.type">
                    <option value="transport">Transport</option>
                    <option value="location">Location</option>
                    <option value="other">Autre</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Abonnement</label>
                  <select [(ngModel)]="clientForm.subscriptionId">
                    <option [value]="null">Aucun abonnement</option>
                    <option *ngFor="let sub of subscriptions" [value]="sub.id">{{ sub.name }} - {{ sub.price }} TND/mois</option>
                  </select>
                </div>
              </div>
              
              <h4 class="section-title" *ngIf="!showEditModal">Administrateur de la société</h4>
              <div class="form-row" *ngIf="!showEditModal">
                <div class="form-group">
                  <label>Nom de l'admin *</label>
                  <input type="text" [(ngModel)]="clientForm.adminName" placeholder="Nom complet" />
                </div>
                <div class="form-group">
                  <label>Email admin *</label>
                  <input type="email" [(ngModel)]="clientForm.adminEmail" placeholder="admin@societe.tn" />
                </div>
              </div>
              <div class="form-group" *ngIf="!showEditModal">
                <label>Mot de passe *</label>
                <input type="password" [(ngModel)]="clientForm.adminPassword" placeholder="Mot de passe" />
              </div>
            </div>

            <div class="modal-body view-mode" *ngIf="showViewModal && selectedClient">
              <div class="view-header">
                <div class="client-avatar large">{{ selectedClient.name.charAt(0) }}</div>
                <div>
                  <h3>{{ selectedClient.name }}</h3>
                  <span class="status-badge" [class]="selectedClient.status">{{ selectedClient.status | titlecase }}</span>
                </div>
              </div>
              <div class="view-section">
                <h4>Contact Information</h4>
                <div class="view-row"><span>Email:</span><span>{{ selectedClient.email }}</span></div>
                <div class="view-row"><span>Phone:</span><span>{{ selectedClient.phone || 'N/A' }}</span></div>
              </div>
              <div class="view-section">
                <h4>Subscription Details</h4>
                <div class="view-row"><span>Plan:</span><span>{{ selectedClient.subscriptionName || 'None' }}</span></div>
                <div class="view-row"><span>Vehicles:</span><span>{{ selectedClient.currentVehicles }} / {{ selectedClient.maxVehicles }}</span></div>
                <div class="view-row"><span>Users:</span><span>{{ selectedClient.currentUsers }}</span></div>
              </div>
              <div class="view-section">
                <h4>Account</h4>
                <div class="view-row"><span>Created:</span><span>{{ formatDate(selectedClient.createdAt) }}</span></div>
                <div class="view-row"><span>Last Activity:</span><span>{{ selectedClient.lastActivity ? formatDate(selectedClient.lastActivity) : 'N/A' }}</span></div>
              </div>
            </div>

            <div class="modal-footer" *ngIf="!showViewModal">
              <button class="btn-secondary" (click)="closeModals()">Cancel</button>
              <button class="btn-primary" (click)="saveClient()">{{ showEditModal ? 'Update' : 'Create' }} Client</button>
            </div>
            <div class="modal-footer" *ngIf="showViewModal">
              <button class="btn-secondary" (click)="closeModals()">Close</button>
              <button class="btn-primary" (click)="editClient(selectedClient!)">Edit Client</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .clients-page {
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
    }

    .search-box {
      display: flex;
      align-items: center;
      gap: 10px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      padding: 10px 14px;
      width: 280px;
    }

    .search-box svg { color: #8b98a5; }

    .search-box input {
      flex: 1;
      border: none;
      background: transparent;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
    }

    .filter-select {
      padding: 10px 14px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
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

    .clients-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
      gap: 20px;
    }

    .client-card {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 16px;
      overflow: hidden;
      transition: all 0.3s;
    }

    .client-card:hover {
      border-color: rgba(255, 255, 255, 0.15);
      transform: translateY(-2px);
    }

    .client-card.suspended {
      opacity: 0.7;
    }

    .card-header {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 20px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    }

    .client-avatar {
      width: 48px;
      height: 48px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 20px;
      color: #fff;
    }

    .client-avatar.large {
      width: 64px;
      height: 64px;
      font-size: 28px;
    }

    .client-info {
      flex: 1;
    }

    .client-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .client-type {
      font-size: 13px;
      color: #8b98a5;
    }

    .status-badge {
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-badge.active {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .status-badge.suspended {
      background: rgba(239, 68, 68, 0.15);
      color: #ef4444;
    }

    .status-badge.pending {
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
      color: #8b98a5;
    }

    .info-row svg {
      color: #6b7280;
    }

    .stats-row {
      display: flex;
      gap: 20px;
      margin-top: 16px;
      padding-top: 16px;
      border-top: 1px solid rgba(255, 255, 255, 0.05);
    }

    .stat {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .stat-value {
      font-size: 16px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .stat-label {
      font-size: 11px;
      color: #6b7280;
      text-transform: uppercase;
    }

    .card-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: rgba(255, 255, 255, 0.02);
      border-top: 1px solid rgba(255, 255, 255, 0.05);
    }

    .joined-date {
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

    .action-btn.suspend {
      background: rgba(239, 68, 68, 0.15);
      color: #ef4444;
    }

    .action-btn.activate {
      background: rgba(34, 197, 94, 0.15);
      color: #22c55e;
    }

    .action-btn.view {
      background: rgba(255, 255, 255, 0.05);
      color: #8b98a5;
    }

    .action-btn:hover {
      transform: scale(1.1);
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
      background: linear-gradient(135deg, #1a1f2e 0%, #141824 100%);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 20px;
      width: 100%;
      max-width: 520px;
      max-height: 90vh;
      overflow-y: auto;
    }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .close-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 10px;
      color: #8b98a5;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .close-btn:hover {
      background: rgba(255, 255, 255, 0.1);
      color: #e7e9ea;
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
      color: #00d4aa;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      padding-bottom: 8px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.1);
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

    .form-group input, .form-group select {
      padding: 12px 14px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
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

    .view-header {
      display: flex;
      align-items: center;
      gap: 16px;
      padding-bottom: 20px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .view-header h3 {
      margin: 0 0 8px 0;
      font-size: 20px;
      color: #e7e9ea;
    }

    .view-section {
      padding: 16px 0;
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    }

    .view-section:last-child {
      border-bottom: none;
    }

    .view-section h4 {
      margin: 0 0 12px 0;
      font-size: 14px;
      font-weight: 600;
      color: #00d4aa;
    }

    .view-row {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      font-size: 14px;
    }

    .view-row span:first-child {
      color: #8b98a5;
    }

    .view-row span:last-child {
      color: #e7e9ea;
      font-weight: 500;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 20px 24px;
      border-top: 1px solid rgba(255, 255, 255, 0.08);
    }

    .btn-secondary {
      padding: 10px 20px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: rgba(255, 255, 255, 0.1);
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
  `]
})
export class AdminClientsComponent implements OnInit {
  clients: Client[] = [];
  filteredClients: Client[] = [];
  subscriptions: any[] = [];

  searchQuery = '';
  statusFilter = 'all';

  showAddModal = false;
  showEditModal = false;
  showViewModal = false;
  selectedClient: Client | null = null;

  clientForm = {
    name: '',
    email: '',
    phone: '',
    type: 'transport',
    subscriptionId: undefined as number | undefined,
    maxVehicles: 10,
    adminName: '',
    adminEmail: '',
    adminPassword: ''
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
    this.adminService.getClients().subscribe(clients => {
      this.clients = clients;
      this.filterClients();
    });

    this.adminService.getSubscriptions().subscribe(subs => {
      this.subscriptions = subs;
    });
  }

  filterClients() {
    this.filteredClients = this.clients.filter(client => {
      const matchesSearch = !this.searchQuery ||
        client.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        client.email.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesStatus = this.statusFilter === 'all' || client.status === this.statusFilter;
      return matchesSearch && matchesStatus;
    });
  }

  editClient(client: Client) {
    this.selectedClient = client;
    this.clientForm = {
      name: client.name,
      email: client.email,
      phone: client.phone || '',
      type: client.type,
      subscriptionId: client.subscriptionId,
      maxVehicles: client.maxVehicles,
      adminName: '',
      adminEmail: '',
      adminPassword: ''
    };
    this.showViewModal = false;
    this.showEditModal = true;
  }

  viewClient(client: Client) {
    this.selectedClient = client;
    this.showViewModal = true;
  }

  toggleClientStatus(client: Client) {
    if (client.status === 'active') {
      this.adminService.suspendClient(client.id).subscribe(() => {
        client.status = 'suspended';
      });
    } else {
      this.adminService.activateClient(client.id).subscribe(() => {
        client.status = 'active';
      });
    }
  }

  saveClient() {
    const sub = this.subscriptions.find(s => s.id === this.clientForm.subscriptionId);
    const data = {
      ...this.clientForm,
      subscriptionName: sub?.name
    };

    if (this.showEditModal && this.selectedClient) {
      this.adminService.updateClient(this.selectedClient.id, data).subscribe(() => {
        this.loadData();
        this.closeModals();
      });
    } else {
      this.adminService.createClient(data).subscribe(() => {
        this.loadData();
        this.closeModals();
      });
    }
  }

  closeModals() {
    this.showAddModal = false;
    this.showEditModal = false;
    this.showViewModal = false;
    this.selectedClient = null;
    this.clientForm = { name: '', email: '', phone: '', type: 'transport', subscriptionId: undefined, maxVehicles: 10, adminName: '', adminEmail: '', adminPassword: '' };
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }
}
