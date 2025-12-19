import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Estimate, EstimateItem, Client } from '../services/admin.service';

@Component({
  selector: 'admin-estimates',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Estimates">
      <div class="estimates-page">
        <div class="page-header">
          <div class="header-stats">
            <div class="stat-item">
              <span class="stat-value">{{ estimates.length }}</span>
              <span class="stat-label">Total Estimates</span>
            </div>
            <div class="stat-item">
              <span class="stat-value">{{ getPendingCount() }}</span>
              <span class="stat-label">Pending</span>
            </div>
            <div class="stat-item">
              <span class="stat-value">{{ formatCurrency(getTotalValue()) }}</span>
              <span class="stat-label">Total Value</span>
            </div>
          </div>
          <button class="create-btn" (click)="showCreateModal = true">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            New Estimate
          </button>
        </div>

        <div class="estimates-list">
          <div class="estimate-card" *ngFor="let estimate of estimates">
            <div class="card-header">
              <div class="estimate-id">{{ estimate.id }}</div>
              <span class="status-badge" [class]="estimate.status">{{ estimate.status | titlecase }}</span>
            </div>

            <div class="card-body">
              <div class="client-info">
                <h3>{{ estimate.clientName }}</h3>
                <span class="client-email">{{ estimate.clientEmail }}</span>
              </div>

              <div class="estimate-items">
                <div class="item" *ngFor="let item of estimate.items.slice(0, 2)">
                  <span class="item-desc">{{ item.description }}</span>
                  <span class="item-total">{{ formatCurrency(item.total) }}</span>
                </div>
                <div class="more-items" *ngIf="estimate.items.length > 2">
                  +{{ estimate.items.length - 2 }} more items
                </div>
              </div>

              <div class="estimate-total">
                <div class="total-row subtotal">
                  <span>Subtotal</span>
                  <span>{{ formatCurrency(estimate.subtotal) }}</span>
                </div>
                <div class="total-row tax">
                  <span>Tax (19%)</span>
                  <span>{{ formatCurrency(estimate.tax) }}</span>
                </div>
                <div class="total-row total">
                  <span>Total</span>
                  <span>{{ formatCurrency(estimate.total) }}</span>
                </div>
              </div>
            </div>

            <div class="card-footer">
              <div class="footer-info">
                <span class="created">Created {{ formatDate(estimate.createdAt) }}</span>
                <span class="valid-until" [class.expiring]="isExpiringSoon(estimate)">
                  Valid until {{ formatDate(estimate.validUntil) }}
                </span>
              </div>
              <div class="actions">
                <button class="action-btn view" (click)="viewEstimate(estimate)" title="View">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                  </svg>
                </button>
                <button class="action-btn edit" (click)="editEstimate(estimate)" title="Edit">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="action-btn send" *ngIf="estimate.status === 'draft'" (click)="sendEstimate(estimate)" title="Send">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="22" y1="2" x2="11" y2="13"/><polygon points="22,2 15,22 11,13 2,9"/>
                  </svg>
                </button>
                <button class="action-btn download" (click)="downloadPdf(estimate)" title="Download PDF">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/>
                    <polyline points="7,10 12,15 17,10"/><line x1="12" y1="15" x2="12" y2="3"/>
                  </svg>
                </button>
              </div>
            </div>
          </div>
        </div>

        <div class="modal-overlay" *ngIf="showCreateModal" (click)="closeModal()">
          <div class="modal estimate-modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Create New Estimate</h2>
              <button class="close-btn" (click)="closeModal()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body">
              <div class="form-section">
                <h3>Client Information</h3>
                <div class="form-row">
                  <div class="form-group">
                    <label>Select Existing Client</label>
                    <select [(ngModel)]="newEstimate.clientId" (change)="onClientSelect()">
                      <option [value]="null">New Client</option>
                      <option *ngFor="let client of clients" [value]="client.id">{{ client.name }}</option>
                    </select>
                  </div>
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label>Client Name</label>
                    <input type="text" [(ngModel)]="newEstimate.clientName" placeholder="Company name" />
                  </div>
                  <div class="form-group">
                    <label>Client Email</label>
                    <input type="email" [(ngModel)]="newEstimate.clientEmail" placeholder="email@company.com" />
                  </div>
                </div>
              </div>

              <div class="form-section">
                <h3>Estimate Items</h3>
                <div class="items-list">
                  <div class="item-row" *ngFor="let item of newEstimate.items; let i = index">
                    <input type="text" [(ngModel)]="item.description" placeholder="Description" class="desc-input" />
                    <input type="number" [(ngModel)]="item.quantity" min="1" placeholder="Qty" class="qty-input" (input)="calculateItemTotal(item)" />
                    <input type="number" [(ngModel)]="item.unitPrice" min="0" placeholder="Unit Price" class="price-input" (input)="calculateItemTotal(item)" />
                    <span class="item-total">{{ formatCurrency(item.total) }}</span>
                    <button class="remove-item" (click)="removeItem(i)">
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                      </svg>
                    </button>
                  </div>
                </div>
                <button class="add-item-btn" (click)="addItem()">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
                  </svg>
                  Add Item
                </button>
              </div>

              <div class="form-section totals-section">
                <div class="total-row">
                  <span>Subtotal</span>
                  <span>{{ formatCurrency(calculateSubtotal()) }}</span>
                </div>
                <div class="total-row">
                  <span>Tax (19%)</span>
                  <span>{{ formatCurrency(calculateTax()) }}</span>
                </div>
                <div class="total-row grand-total">
                  <span>Total</span>
                  <span>{{ formatCurrency(calculateTotal()) }}</span>
                </div>
              </div>

              <div class="form-section">
                <div class="form-row">
                  <div class="form-group">
                    <label>Valid Until</label>
                    <input type="date" [(ngModel)]="validUntilString" />
                  </div>
                </div>
                <div class="form-group">
                  <label>Notes (Optional)</label>
                  <textarea [(ngModel)]="newEstimate.notes" rows="3" placeholder="Additional notes..."></textarea>
                </div>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModal()">Cancel</button>
              <button class="btn-secondary" (click)="saveAsDraft()">Save as Draft</button>
              <button class="btn-primary" (click)="createAndSend()">Create & Send</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .estimates-page {
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

    .header-stats {
      display: flex;
      gap: 32px;
    }

    .stat-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .stat-value {
      font-size: 24px;
      font-weight: 700;
      color: #e7e9ea;
    }

    .stat-label {
      font-size: 13px;
      color: #8b98a5;
    }

    .create-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 12px 24px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .create-btn:hover {
      transform: translateY(-2px);
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    .estimates-list {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(400px, 1fr));
      gap: 20px;
    }

    .estimate-card {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 16px;
      overflow: hidden;
      transition: all 0.3s;
    }

    .estimate-card:hover {
      transform: translateY(-2px);
      border-color: rgba(255, 255, 255, 0.15);
    }

    .card-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: rgba(255, 255, 255, 0.03);
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    }

    .estimate-id {
      font-size: 14px;
      font-weight: 600;
      color: #00d4aa;
    }

    .status-badge {
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-badge.draft { background: rgba(107, 114, 128, 0.2); color: #9ca3af; }
    .status-badge.sent { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }
    .status-badge.accepted { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
    .status-badge.rejected { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
    .status-badge.expired { background: rgba(249, 115, 22, 0.15); color: #f97316; }

    .card-body {
      padding: 20px;
    }

    .client-info {
      margin-bottom: 16px;
    }

    .client-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .client-email {
      font-size: 13px;
      color: #8b98a5;
    }

    .estimate-items {
      padding: 14px;
      background: rgba(255, 255, 255, 0.02);
      border-radius: 10px;
      margin-bottom: 16px;
    }

    .item {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);
    }

    .item:last-child {
      border-bottom: none;
    }

    .item-desc {
      font-size: 13px;
      color: #e7e9ea;
    }

    .item-total {
      font-size: 13px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .more-items {
      font-size: 12px;
      color: #8b98a5;
      text-align: center;
      padding-top: 8px;
    }

    .estimate-total {
      padding-top: 12px;
      border-top: 1px solid rgba(255, 255, 255, 0.08);
    }

    .total-row {
      display: flex;
      justify-content: space-between;
      padding: 6px 0;
      font-size: 13px;
    }

    .total-row.subtotal, .total-row.tax {
      color: #8b98a5;
    }

    .total-row.total {
      font-size: 16px;
      font-weight: 700;
      color: #e7e9ea;
      padding-top: 10px;
      margin-top: 4px;
      border-top: 1px dashed rgba(255, 255, 255, 0.1);
    }

    .card-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: rgba(255, 255, 255, 0.02);
      border-top: 1px solid rgba(255, 255, 255, 0.05);
    }

    .footer-info {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .created, .valid-until {
      font-size: 12px;
      color: #6b7280;
    }

    .valid-until.expiring {
      color: #f97316;
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

    .action-btn.view { background: rgba(255, 255, 255, 0.05); color: #8b98a5; }
    .action-btn.edit { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }
    .action-btn.send { background: rgba(0, 212, 170, 0.15); color: #00d4aa; }
    .action-btn.download { background: rgba(249, 115, 22, 0.15); color: #f97316; }

    .action-btn:hover { transform: scale(1.1); }

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
      max-width: 700px;
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
    }

    .modal-body {
      padding: 24px;
    }

    .form-section {
      margin-bottom: 24px;
    }

    .form-section h3 {
      margin: 0 0 16px 0;
      font-size: 15px;
      font-weight: 600;
      color: #00d4aa;
    }

    .form-row {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 16px;
      margin-bottom: 16px;
    }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-group label {
      font-size: 13px;
      font-weight: 500;
      color: #e7e9ea;
    }

    .form-group input, .form-group select, .form-group textarea {
      padding: 12px 14px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #e7e9ea;
      font-size: 14px;
      outline: none;
    }

    .form-group input:focus, .form-group select:focus, .form-group textarea:focus {
      border-color: #00d4aa;
    }

    .items-list {
      margin-bottom: 12px;
    }

    .item-row {
      display: flex;
      gap: 10px;
      align-items: center;
      margin-bottom: 10px;
    }

    .item-row .desc-input { flex: 2; }
    .item-row .qty-input { width: 70px; }
    .item-row .price-input { width: 100px; }
    .item-row .item-total { width: 90px; text-align: right; font-weight: 600; color: #e7e9ea; }

    .remove-item {
      width: 32px;
      height: 32px;
      border: none;
      background: rgba(239, 68, 68, 0.15);
      border-radius: 8px;
      color: #ef4444;
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .add-item-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      width: 100%;
      padding: 10px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px dashed rgba(255, 255, 255, 0.15);
      border-radius: 10px;
      color: #8b98a5;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .add-item-btn:hover {
      background: rgba(255, 255, 255, 0.06);
      color: #e7e9ea;
    }

    .totals-section {
      padding: 16px;
      background: rgba(255, 255, 255, 0.03);
      border-radius: 12px;
    }

    .totals-section .total-row {
      padding: 8px 0;
    }

    .totals-section .grand-total {
      font-size: 18px;
      font-weight: 700;
      color: #00d4aa;
      border-top: 1px solid rgba(255, 255, 255, 0.1);
      margin-top: 8px;
      padding-top: 12px;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 20px 24px;
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
  `]
})
export class AdminEstimatesComponent implements OnInit {
  estimates: Estimate[] = [];
  clients: Client[] = [];
  showCreateModal = false;
  validUntilString = '';

  newEstimate: Partial<Estimate> = {
    clientId: undefined,
    clientName: '',
    clientEmail: '',
    items: [{ description: '', quantity: 1, unitPrice: 0, total: 0 }],
    notes: ''
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

    const futureDate = new Date();
    futureDate.setDate(futureDate.getDate() + 30);
    this.validUntilString = futureDate.toISOString().split('T')[0];
  }

  loadData() {
    this.adminService.getEstimates().subscribe(estimates => {
      this.estimates = estimates;
    });

    this.adminService.getClients().subscribe(clients => {
      this.clients = clients;
    });
  }

  onClientSelect() {
    if (this.newEstimate.clientId) {
      const client = this.clients.find(c => c.id === Number(this.newEstimate.clientId));
      if (client) {
        this.newEstimate.clientName = client.name;
        this.newEstimate.clientEmail = client.email;
      }
    }
  }

  addItem() {
    this.newEstimate.items!.push({ description: '', quantity: 1, unitPrice: 0, total: 0 });
  }

  removeItem(index: number) {
    if (this.newEstimate.items!.length > 1) {
      this.newEstimate.items!.splice(index, 1);
    }
  }

  calculateItemTotal(item: EstimateItem) {
    item.total = item.quantity * item.unitPrice;
  }

  calculateSubtotal(): number {
    return this.newEstimate.items!.reduce((sum, item) => sum + item.total, 0);
  }

  calculateTax(): number {
    return this.calculateSubtotal() * 0.19;
  }

  calculateTotal(): number {
    return this.calculateSubtotal() + this.calculateTax();
  }

  saveAsDraft() {
    this.newEstimate.subtotal = this.calculateSubtotal();
    this.newEstimate.tax = this.calculateTax();
    this.newEstimate.total = this.calculateTotal();
    this.newEstimate.validUntil = new Date(this.validUntilString);

    this.adminService.createEstimate(this.newEstimate).subscribe(() => {
      this.loadData();
      this.closeModal();
    });
  }

  createAndSend() {
    this.saveAsDraft();
    alert('Estimate created and sent to client');
  }

  viewEstimate(estimate: Estimate) {
    alert('View estimate: ' + estimate.id);
  }

  editEstimate(estimate: Estimate) {
    alert('Edit estimate: ' + estimate.id);
  }

  sendEstimate(estimate: Estimate) {
    estimate.status = 'sent';
    alert('Estimate sent to: ' + estimate.clientEmail);
  }

  downloadPdf(estimate: Estimate) {
    alert('Downloading PDF for: ' + estimate.id);
  }

  closeModal() {
    this.showCreateModal = false;
    this.newEstimate = {
      clientId: undefined,
      clientName: '',
      clientEmail: '',
      items: [{ description: '', quantity: 1, unitPrice: 0, total: 0 }],
      notes: ''
    };
  }

  getPendingCount(): number {
    return this.estimates.filter(e => e.status === 'sent' || e.status === 'draft').length;
  }

  getTotalValue(): number {
    return this.estimates.reduce((sum, e) => sum + e.total, 0);
  }

  isExpiringSoon(estimate: Estimate): boolean {
    const daysUntilExpiry = (new Date(estimate.validUntil).getTime() - Date.now()) / (1000 * 60 * 60 * 24);
    return daysUntilExpiry < 7;
  }

  formatCurrency(amount: number): string {
    return amount.toLocaleString('fr-TN', { style: 'currency', currency: 'TND' });
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }
}
