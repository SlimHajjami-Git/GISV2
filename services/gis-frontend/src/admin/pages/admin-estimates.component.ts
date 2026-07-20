import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Estimate, EstimateItem, Client } from '../services/admin.service';
import { environment } from '../../environments/environment';

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

        <div class="modal-overlay" *ngIf="showCreateModal" (mousedown)="closeModal()">
          <div class="modal estimate-modal" (mousedown)="$event.stopPropagation()">
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
                <h3><span class="section-dash"></span>Client Information</h3>
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
                <h3><span class="section-dash"></span>Estimate Items</h3>
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
      padding: 24px;
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
      gap: 16px;
      flex-wrap: wrap;
    }

    .stat-item {
      display: flex;
      flex-direction: column;
      gap: 4px;
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-left: 3px solid var(--adm-indigo);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      padding: 14px 18px;
      min-width: 140px;
    }

    .stat-item:nth-child(2) { border-left-color: var(--adm-amber); }
    .stat-item:nth-child(3) { border-left-color: var(--adm-green); }

    .stat-value {
      font-size: 25px;
      font-weight: 800;
      font-variant-numeric: tabular-nums;
      color: var(--adm-ink);
    }

    .stat-label {
      font-size: 11px;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--adm-sub);
    }

    .create-btn {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 9px 18px;
      background: var(--adm-indigo);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .create-btn:hover {
      background: var(--adm-indigo-ink);
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }

    .estimates-list {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(400px, 100%), 1fr));
      gap: 20px;
    }

    .estimate-card {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 16px;
      box-shadow: var(--adm-shadow);
      overflow: hidden;
      transition: all 0.2s;
      animation: rise 0.25s ease both;
    }

    .estimate-card:hover {
      transform: translateY(-1px);
      box-shadow: var(--adm-shadow-hover);
    }

    @keyframes rise {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: none; }
    }

    @media (prefers-reduced-motion: reduce) {
      .estimate-card { animation: none; }
      .estimate-card:hover, .create-btn:hover { transform: none; }
    }

    .card-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: #f8fafc;
      border-bottom: 1px solid var(--adm-border);
    }

    .estimate-id {
      font-size: 14px;
      font-weight: 700;
      font-variant-numeric: tabular-nums;
      color: var(--adm-indigo-ink);
    }

    .status-badge {
      padding: 3px 10px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 700;
    }

    .status-badge.draft { background: rgba(100, 116, 139, 0.12); color: var(--adm-slate-ink); }
    .status-badge.sent { background: rgba(8, 145, 178, 0.10); color: var(--adm-cyan-ink); }
    .status-badge.accepted { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }
    .status-badge.rejected { background: rgba(220, 38, 38, 0.10); color: var(--adm-red-ink); }
    .status-badge.expired { background: rgba(220, 38, 38, 0.10); color: var(--adm-red-ink); }

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
      color: var(--adm-ink);
    }

    .client-email {
      font-size: 13px;
      color: var(--adm-sub);
    }

    .estimate-items {
      padding: 14px;
      background: var(--adm-track);
      border-radius: 10px;
      margin-bottom: 16px;
    }

    .item {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      border-bottom: 1px solid var(--adm-border);
    }

    .item:last-child {
      border-bottom: none;
    }

    .item-desc {
      font-size: 13px;
      color: var(--adm-ink);
    }

    .item-total {
      font-size: 13px;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
      color: var(--adm-ink);
    }

    .more-items {
      font-size: 12px;
      color: var(--adm-sub);
      text-align: center;
      padding-top: 8px;
    }

    .estimate-total {
      padding-top: 12px;
      border-top: 1px solid var(--adm-border);
    }

    .total-row {
      display: flex;
      justify-content: space-between;
      padding: 6px 0;
      font-size: 13px;
      font-variant-numeric: tabular-nums;
    }

    .total-row.subtotal, .total-row.tax {
      color: var(--adm-sub);
    }

    .total-row.total {
      font-size: 16px;
      font-weight: 700;
      color: var(--adm-ink);
      padding-top: 10px;
      margin-top: 4px;
      border-top: 1px dashed var(--adm-border);
    }

    .card-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid var(--adm-border);
    }

    .footer-info {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .created, .valid-until {
      font-size: 12px;
      color: var(--adm-sub);
    }

    .valid-until.expiring {
      color: var(--adm-amber-ink);
      font-weight: 600;
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

    .action-btn.view { background: rgba(100, 116, 139, 0.10); color: var(--adm-slate-ink); }
    .action-btn.edit { background: rgba(79, 70, 229, 0.10); color: var(--adm-indigo-ink); }
    .action-btn.send { background: rgba(5, 150, 105, 0.10); color: var(--adm-green-ink); }
    .action-btn.download { background: rgba(8, 145, 178, 0.10); color: var(--adm-cyan-ink); }

    .action-btn:hover { transform: scale(1.08); }

    .modal-overlay {
      position: fixed;
      inset: 0;
      background: rgba(15, 23, 42, 0.55);
      backdrop-filter: blur(4px);
      display: flex;
      align-items: center;
      justify-content: center;
      z-index: 1000;
      padding: 20px;
    }

    .modal {
      background: var(--adm-card);
      border: 1px solid var(--adm-border);
      border-radius: 18px;
      box-shadow: 0 24px 60px -24px rgba(2, 6, 23, 0.45);
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
      border-bottom: 1px solid var(--adm-border);
    }

    .modal-header h2 {
      margin: 0;
      font-size: 18px;
      font-weight: 700;
      color: var(--adm-ink);
    }

    .close-btn {
      width: 36px;
      height: 36px;
      border: none;
      background: var(--adm-track);
      border-radius: 10px;
      color: var(--adm-sub);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .close-btn:hover {
      background: var(--adm-border);
      color: var(--adm-ink);
    }

    .modal-body {
      padding: 24px;
    }

    .form-section {
      margin-bottom: 24px;
    }

    .form-section h3 {
      display: flex;
      align-items: center;
      gap: 8px;
      margin: 0 0 16px 0;
      font-size: 12px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.08em;
      color: var(--adm-sub);
    }

    .section-dash {
      width: 3px;
      height: 12px;
      border-radius: 2px;
      background: var(--adm-indigo);
      flex-shrink: 0;
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
      font-weight: 600;
      color: var(--adm-ink);
    }

    .form-group input, .form-group select, .form-group textarea,
    .item-row input {
      padding: 10px 14px;
      background: #fff;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 14px;
      outline: none;
      transition: border-color 0.2s, box-shadow 0.2s;
    }

    .form-group input:focus, .form-group select:focus, .form-group textarea:focus,
    .item-row input:focus {
      border-color: var(--adm-indigo);
      box-shadow: 0 0 0 3px rgba(79, 70, 229, 0.12);
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
    .item-row .item-total { width: 90px; text-align: right; font-weight: 600; font-variant-numeric: tabular-nums; color: var(--adm-ink); }

    .remove-item {
      width: 32px;
      height: 32px;
      border: none;
      background: rgba(220, 38, 38, 0.10);
      border-radius: 8px;
      color: var(--adm-red);
      cursor: pointer;
      display: flex;
      align-items: center;
      justify-content: center;
      transition: all 0.2s;
    }

    .remove-item:hover {
      background: rgba(220, 38, 38, 0.16);
    }

    .add-item-btn {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 8px;
      width: 100%;
      padding: 10px;
      background: #fff;
      border: 1px dashed var(--adm-border);
      border-radius: 10px;
      color: var(--adm-sub);
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .add-item-btn:hover {
      border-color: var(--adm-indigo);
      color: var(--adm-indigo-ink);
      background: rgba(79, 70, 229, 0.04);
    }

    .totals-section {
      padding: 16px;
      background: var(--adm-track);
      border-radius: 12px;
    }

    .totals-section .total-row {
      padding: 8px 0;
      color: var(--adm-sub);
    }

    .totals-section .grand-total {
      font-size: 18px;
      font-weight: 800;
      font-variant-numeric: tabular-nums;
      color: var(--adm-indigo-ink);
      border-top: 1px solid var(--adm-border);
      margin-top: 8px;
      padding-top: 12px;
    }

    .modal-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 16px 24px;
      border-top: 1px solid var(--adm-border);
      background: #f8fafc;
      border-radius: 0 0 18px 18px;
    }

    .btn-secondary {
      padding: 9px 18px;
      background: #fff;
      border: 1px solid var(--adm-border);
      border-radius: 10px;
      color: var(--adm-ink);
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      border-color: var(--adm-indigo);
      color: var(--adm-indigo-ink);
    }

    .btn-primary {
      padding: 9px 18px;
      background: var(--adm-indigo);
      border: none;
      border-radius: 10px;
      color: #fff;
      font-size: 14px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
    }

    .btn-primary:hover {
      background: var(--adm-indigo-ink);
    }
  `]
})
export class AdminEstimatesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();
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
    this.adminService.getEstimates().pipe(takeUntil(this.destroy$)).subscribe(estimates => {
      this.estimates = estimates;
    });

    this.adminService.getClients().pipe(takeUntil(this.destroy$)).subscribe(clients => {
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

    this.adminService.createEstimate(this.newEstimate).pipe(takeUntil(this.destroy$)).subscribe(() => {
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

  get currencyCode(): string {
    return (environment as { defaultCurrency?: string }).defaultCurrency || 'TND';
  }

  formatCurrency(amount: number): string {
    return amount.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + this.currencyCode;
  }

  formatDate(date: Date): string {
    return new Date(date).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
