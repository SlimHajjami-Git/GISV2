import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Campaign, CampaignAccessRights } from '../services/admin.service';

@Component({
  selector: 'admin-campaigns',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Campaign Management">
      <div class="campaigns-page">
        <div class="page-header">
          <div class="header-left">
            <div class="search-box">
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
              </svg>
              <input type="text" [(ngModel)]="searchQuery" (input)="filterCampaigns()" placeholder="Search campaigns..." />
            </div>
            <select class="filter-select" [(ngModel)]="statusFilter" (change)="filterCampaigns()">
              <option value="all">All Status</option>
              <option value="draft">Draft</option>
              <option value="active">Active</option>
              <option value="paused">Paused</option>
              <option value="ended">Ended</option>
            </select>
            <select class="filter-select" [(ngModel)]="typeFilter" (change)="filterCampaigns()">
              <option value="all">All Types</option>
              <option value="standard">Standard</option>
              <option value="promotional">Promotional</option>
              <option value="enterprise">Enterprise</option>
              <option value="trial">Trial</option>
            </select>
          </div>
          <button class="add-btn" (click)="openAddModal()">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
            </svg>
            New Campaign
          </button>
        </div>

        <div class="campaigns-grid">
          <div class="campaign-card" *ngFor="let campaign of filteredCampaigns" [class]="campaign.status">
            <div class="card-header">
              <div class="campaign-icon" [class]="campaign.type">
                <svg *ngIf="campaign.type === 'standard'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/>
                </svg>
                <svg *ngIf="campaign.type === 'promotional'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"/>
                </svg>
                <svg *ngIf="campaign.type === 'enterprise'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M9 21V9"/>
                </svg>
                <svg *ngIf="campaign.type === 'trial'" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <circle cx="12" cy="12" r="10"/><path d="M12 6v6l4 2"/>
                </svg>
              </div>
              <div class="campaign-info">
                <h3>{{ campaign.name }}</h3>
                <span class="campaign-type">{{ campaign.type | titlecase }}</span>
              </div>
              <div class="status-badge" [class]="campaign.status">{{ campaign.status | titlecase }}</div>
            </div>

            <div class="card-body">
              <p class="description" *ngIf="campaign.description">{{ campaign.description }}</p>
              
              <div class="info-row" *ngIf="campaign.discountPercentage">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="19" y1="5" x2="5" y2="19"/><circle cx="6.5" cy="6.5" r="2.5"/><circle cx="17.5" cy="17.5" r="2.5"/>
                </svg>
                <span>{{ campaign.discountPercentage }}% discount</span>
              </div>

              <div class="info-row" *ngIf="campaign.startDate || campaign.endDate">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/>
                </svg>
                <span>{{ formatDateRange(campaign) }}</span>
              </div>

              <div class="stats-row">
                <div class="stat">
                  <span class="stat-value">{{ campaign.enrolledCompanies }}</span>
                  <span class="stat-label">Enrolled</span>
                </div>
                <div class="stat" *ngIf="campaign.maxSubscriptions">
                  <span class="stat-value">{{ campaign.currentSubscriptions }}/{{ campaign.maxSubscriptions }}</span>
                  <span class="stat-label">Capacity</span>
                </div>
                <div class="stat">
                  <span class="stat-value">{{ campaign.targetSubscriptionName || 'Any' }}</span>
                  <span class="stat-label">Target Plan</span>
                </div>
              </div>
            </div>

            <div class="card-footer">
              <span class="created-date">Created {{ formatDate(campaign.createdAt) }}</span>
              <div class="actions">
                <button class="action-btn edit" (click)="editCampaign(campaign)" title="Edit">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                  </svg>
                </button>
                <button class="action-btn" [class.pause]="campaign.status === 'active'" [class.play]="campaign.status !== 'active'"
                        (click)="toggleCampaignStatus(campaign)" [title]="campaign.status === 'active' ? 'Pause' : 'Activate'">
                  <svg *ngIf="campaign.status === 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <rect x="6" y="4" width="4" height="16"/><rect x="14" y="4" width="4" height="16"/>
                  </svg>
                  <svg *ngIf="campaign.status !== 'active'" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polygon points="5,3 19,12 5,21"/>
                  </svg>
                </button>
                <button class="action-btn view" (click)="viewCampaign(campaign)" title="View Details">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                  </svg>
                </button>
                <button class="action-btn delete" (click)="confirmDelete(campaign)" title="Delete">
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3,6 5,6 21,6"/><path d="M19,6v14a2,2,0,0,1-2,2H7a2,2,0,0,1-2-2V6m3,0V4a2,2,0,0,1,2-2h4a2,2,0,0,1,2,2v2"/>
                  </svg>
                </button>
              </div>
            </div>
          </div>
        </div>

        <div class="empty-state" *ngIf="filteredCampaigns.length === 0">
          <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/>
          </svg>
          <h3>No campaigns found</h3>
          <p>Create a new campaign to get started</p>
        </div>

        <!-- Add/Edit Modal -->
        <div class="modal-overlay" *ngIf="showAddModal || showEditModal" (click)="closeModals()">
          <div class="modal large" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>{{ showEditModal ? 'Edit Campaign' : 'Create New Campaign' }}</h2>
              <button class="close-btn" (click)="closeModals()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body">
              <div class="form-tabs">
                <button [class.active]="activeTab === 'general'" (click)="activeTab = 'general'">General</button>
                <button [class.active]="activeTab === 'access'" (click)="activeTab = 'access'">Access Rights</button>
              </div>

              <div class="tab-content" *ngIf="activeTab === 'general'">
                <h4 class="section-title">Campaign Information</h4>
                <div class="form-group">
                  <label>Campaign Name *</label>
                  <input type="text" [(ngModel)]="campaignForm.name" placeholder="Enter campaign name" />
                </div>
                <div class="form-group">
                  <label>Description</label>
                  <textarea [(ngModel)]="campaignForm.description" rows="3" placeholder="Campaign description..."></textarea>
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label>Type</label>
                    <select [(ngModel)]="campaignForm.type">
                      <option value="standard">Standard</option>
                      <option value="promotional">Promotional</option>
                      <option value="enterprise">Enterprise</option>
                      <option value="trial">Trial</option>
                    </select>
                  </div>
                  <div class="form-group">
                    <label>Status</label>
                    <select [(ngModel)]="campaignForm.status">
                      <option value="draft">Draft</option>
                      <option value="active">Active</option>
                      <option value="paused">Paused</option>
                      <option value="ended">Ended</option>
                    </select>
                  </div>
                </div>

                <h4 class="section-title">Duration & Limits</h4>
                <div class="form-row">
                  <div class="form-group">
                    <label>Start Date</label>
                    <input type="date" [(ngModel)]="campaignForm.startDate" />
                  </div>
                  <div class="form-group">
                    <label>End Date</label>
                    <input type="date" [(ngModel)]="campaignForm.endDate" />
                  </div>
                </div>
                <div class="form-row">
                  <div class="form-group">
                    <label>Discount (%)</label>
                    <input type="number" [(ngModel)]="campaignForm.discountPercentage" min="0" max="100" placeholder="0-100" />
                  </div>
                  <div class="form-group">
                    <label>Max Subscriptions</label>
                    <input type="number" [(ngModel)]="campaignForm.maxSubscriptions" min="1" placeholder="Unlimited if empty" />
                  </div>
                </div>

                <h4 class="section-title">Target Subscription</h4>
                <div class="form-group">
                  <label>Target Plan</label>
                  <select [(ngModel)]="campaignForm.targetSubscriptionId">
                    <option [ngValue]="null">Any subscription</option>
                    <option *ngFor="let sub of subscriptions" [ngValue]="sub.id">{{ sub.name }} - {{ sub.price }} TND</option>
                  </select>
                </div>
              </div>

              <div class="tab-content" *ngIf="activeTab === 'access'">
                <h4 class="section-title">Page Access</h4>
                <div class="access-grid">
                  <label class="toggle-item" *ngFor="let right of pageAccessRights">
                    <input type="checkbox" [ngModel]="getAccessRight(right.key)" (ngModelChange)="setAccessRight(right.key, $event)" />
                    <span class="toggle-label">{{ right.label }}</span>
                  </label>
                </div>

                <h4 class="section-title">Feature Access</h4>
                <div class="access-grid">
                  <label class="toggle-item" *ngFor="let right of featureAccessRights">
                    <input type="checkbox" [ngModel]="getAccessRight(right.key)" (ngModelChange)="setAccessRight(right.key, $event)" />
                    <span class="toggle-label">{{ right.label }}</span>
                  </label>
                </div>

                <h4 class="section-title">Limits</h4>
                <div class="limits-grid">
                  <div class="form-group">
                    <label>Max Vehicles</label>
                    <input type="number" [(ngModel)]="campaignForm.accessRights.maxVehicles" min="1" />
                  </div>
                  <div class="form-group">
                    <label>Max Users</label>
                    <input type="number" [(ngModel)]="campaignForm.accessRights.maxUsers" min="1" />
                  </div>
                  <div class="form-group">
                    <label>Max GPS Devices</label>
                    <input type="number" [(ngModel)]="campaignForm.accessRights.maxGpsDevices" min="1" />
                  </div>
                  <div class="form-group">
                    <label>Max Geofences</label>
                    <input type="number" [(ngModel)]="campaignForm.accessRights.maxGeofences" min="1" />
                  </div>
                  <div class="form-group">
                    <label>History Retention (days)</label>
                    <input type="number" [(ngModel)]="campaignForm.accessRights.historyRetentionDays" min="1" />
                  </div>
                </div>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModals()">Cancel</button>
              <button class="btn-primary" (click)="saveCampaign()" [disabled]="!campaignForm.name">
                {{ showEditModal ? 'Update' : 'Create' }} Campaign
              </button>
            </div>
          </div>
        </div>

        <!-- View Modal -->
        <div class="modal-overlay" *ngIf="showViewModal && selectedCampaign" (click)="closeModals()">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Campaign Details</h2>
              <button class="close-btn" (click)="closeModals()">
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>
                </svg>
              </button>
            </div>

            <div class="modal-body view-mode">
              <div class="view-header">
                <div class="campaign-icon large" [class]="selectedCampaign.type">
                  <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/>
                  </svg>
                </div>
                <div>
                  <h3>{{ selectedCampaign.name }}</h3>
                  <span class="status-badge" [class]="selectedCampaign.status">{{ selectedCampaign.status | titlecase }}</span>
                </div>
              </div>

              <div class="view-section" *ngIf="selectedCampaign.description">
                <h4>Description</h4>
                <p>{{ selectedCampaign.description }}</p>
              </div>

              <div class="view-section">
                <h4>Campaign Details</h4>
                <div class="view-row"><span>Type:</span><span>{{ selectedCampaign.type | titlecase }}</span></div>
                <div class="view-row"><span>Discount:</span><span>{{ selectedCampaign.discountPercentage || 0 }}%</span></div>
                <div class="view-row"><span>Target Plan:</span><span>{{ selectedCampaign.targetSubscriptionName || 'Any' }}</span></div>
                <div class="view-row"><span>Enrolled:</span><span>{{ selectedCampaign.enrolledCompanies }} companies</span></div>
                <div class="view-row" *ngIf="selectedCampaign.maxSubscriptions"><span>Capacity:</span><span>{{ selectedCampaign.currentSubscriptions }}/{{ selectedCampaign.maxSubscriptions }}</span></div>
              </div>

              <div class="view-section">
                <h4>Duration</h4>
                <div class="view-row"><span>Start:</span><span>{{ selectedCampaign.startDate ? formatDate(selectedCampaign.startDate) : 'Not set' }}</span></div>
                <div class="view-row"><span>End:</span><span>{{ selectedCampaign.endDate ? formatDate(selectedCampaign.endDate) : 'Not set' }}</span></div>
              </div>

              <div class="view-section">
                <h4>Timestamps</h4>
                <div class="view-row"><span>Created:</span><span>{{ formatDate(selectedCampaign.createdAt) }}</span></div>
                <div class="view-row"><span>Updated:</span><span>{{ formatDate(selectedCampaign.updatedAt) }}</span></div>
                <div class="view-row" *ngIf="selectedCampaign.createdByName"><span>Created by:</span><span>{{ selectedCampaign.createdByName }}</span></div>
              </div>
            </div>

            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeModals()">Close</button>
              <button class="btn-primary" (click)="editCampaign(selectedCampaign)">Edit Campaign</button>
            </div>
          </div>
        </div>

        <!-- Delete Confirmation Modal -->
        <div class="modal-overlay" *ngIf="showDeleteModal && campaignToDelete" (click)="closeDeleteModal()">
          <div class="modal delete-modal" (click)="$event.stopPropagation()">
            <div class="modal-header">
              <h2>Delete Campaign</h2>
            </div>
            <div class="modal-body">
              <p>Are you sure you want to delete <strong>{{ campaignToDelete.name }}</strong>?</p>
              <p class="warning" *ngIf="campaignToDelete.enrolledCompanies > 0">
                This campaign has {{ campaignToDelete.enrolledCompanies }} enrolled companies.
              </p>
            </div>
            <div class="modal-footer">
              <button class="btn-secondary" (click)="closeDeleteModal()">Cancel</button>
              <button class="btn-danger" (click)="deleteCampaign()">Delete</button>
            </div>
          </div>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .campaigns-page {
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

    .campaigns-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(380px, 1fr));
      gap: 20px;
    }

    .campaign-card {
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 16px;
      overflow: hidden;
      transition: all 0.3s;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
    }

    .campaign-card:hover {
      border-color: #cbd5e1;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
      transform: translateY(-2px);
    }

    .campaign-card.paused, .campaign-card.ended { opacity: 0.7; }

    .card-header {
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .campaign-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      color: #fff;
    }

    .campaign-icon.standard { background: linear-gradient(135deg, #3b82f6, #1d4ed8); }
    .campaign-icon.promotional { background: linear-gradient(135deg, #f59e0b, #d97706); }
    .campaign-icon.enterprise { background: linear-gradient(135deg, #8b5cf6, #6d28d9); }
    .campaign-icon.trial { background: linear-gradient(135deg, #10b981, #059669); }

    .campaign-icon.large {
      width: 64px;
      height: 64px;
    }

    .campaign-info { flex: 1; }

    .campaign-info h3 {
      margin: 0 0 4px 0;
      font-size: 16px;
      font-weight: 600;
      color: #1f2937;
    }

    .campaign-type {
      font-size: 13px;
      color: #64748b;
    }

    .status-badge {
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 500;
    }

    .status-badge.active { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
    .status-badge.draft { background: rgba(100, 116, 139, 0.15); color: #64748b; }
    .status-badge.paused { background: rgba(249, 115, 22, 0.15); color: #f97316; }
    .status-badge.ended { background: rgba(239, 68, 68, 0.15); color: #ef4444; }

    .card-body { padding: 20px; }

    .description {
      margin: 0 0 12px 0;
      font-size: 13px;
      color: #64748b;
      line-height: 1.5;
    }

    .info-row {
      display: flex;
      align-items: center;
      gap: 10px;
      margin-bottom: 8px;
      font-size: 13px;
      color: #64748b;
    }

    .info-row svg { color: #64748b; }

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

    .stat-value { font-size: 16px; font-weight: 600; color: #1f2937; }
    .stat-label { font-size: 11px; color: #64748b; text-transform: uppercase; }

    .card-footer {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px;
      background: #f8fafc;
      border-top: 1px solid #e2e8f0;
    }

    .created-date { font-size: 12px; color: #6b7280; }

    .actions { display: flex; gap: 8px; }

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

    .action-btn.edit { background: rgba(59, 130, 246, 0.15); color: #3b82f6; }
    .action-btn.pause { background: rgba(249, 115, 22, 0.15); color: #f97316; }
    .action-btn.play { background: rgba(34, 197, 94, 0.15); color: #22c55e; }
    .action-btn.view { background: #f1f5f9; color: #64748b; }
    .action-btn.delete { background: rgba(239, 68, 68, 0.15); color: #ef4444; }
    .action-btn:hover { transform: scale(1.1); }

    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 60px 20px;
      text-align: center;
      color: #64748b;
    }

    .empty-state svg { margin-bottom: 16px; opacity: 0.5; }
    .empty-state h3 { margin: 0 0 8px 0; color: #1f2937; }
    .empty-state p { margin: 0; }

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
      background: #ffffff;
      border: 1px solid #e2e8f0;
      border-radius: 20px;
      width: 100%;
      max-width: 520px;
      max-height: 90vh;
      overflow-y: auto;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.2);
    }

    .modal.large { max-width: 700px; }

    .modal-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 20px 24px;
      border-bottom: 1px solid #e2e8f0;
    }

    .modal-header h2 { margin: 0; font-size: 18px; font-weight: 600; color: #1f2937; }

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

    .close-btn:hover { background: #e2e8f0; color: #1f2937; }

    .modal-body {
      padding: 24px;
      display: flex;
      flex-direction: column;
      gap: 20px;
    }

    .form-tabs {
      display: flex;
      gap: 8px;
      margin-bottom: 16px;
    }

    .form-tabs button {
      padding: 8px 16px;
      border: 1px solid #e2e8f0;
      background: #f8fafc;
      border-radius: 8px;
      color: #64748b;
      font-size: 14px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .form-tabs button.active {
      background: #00d4aa;
      border-color: #00d4aa;
      color: #fff;
    }

    .section-title {
      margin: 16px 0 12px 0;
      font-size: 14px;
      font-weight: 600;
      color: #00a388;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      padding-bottom: 8px;
      border-bottom: 1px solid #e2e8f0;
    }

    .section-title:first-child { margin-top: 0; }

    .form-group {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .form-group label { font-size: 14px; font-weight: 500; color: #374151; }

    .form-group input, .form-group select, .form-group textarea {
      padding: 12px 14px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 10px;
      color: #1f2937;
      font-size: 14px;
      outline: none;
      transition: border-color 0.2s;
    }

    .form-group input:focus, .form-group select:focus, .form-group textarea:focus {
      border-color: #00d4aa;
    }

    .form-group textarea { resize: vertical; font-family: inherit; }

    .form-row {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 16px;
    }

    .access-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 12px;
    }

    .toggle-item {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 8px 12px;
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .toggle-item:hover { background: #f1f5f9; }

    .toggle-item input[type="checkbox"] {
      width: 16px;
      height: 16px;
      accent-color: #00d4aa;
    }

    .toggle-label { font-size: 13px; color: #374151; }

    .limits-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 16px;
    }

    .view-header {
      display: flex;
      align-items: center;
      gap: 16px;
      padding-bottom: 20px;
      border-bottom: 1px solid #e2e8f0;
    }

    .view-header h3 { margin: 0 0 8px 0; font-size: 20px; color: #1f2937; }

    .view-section {
      padding: 16px 0;
      border-bottom: 1px solid #e2e8f0;
    }

    .view-section:last-child { border-bottom: none; }

    .view-section h4 { margin: 0 0 12px 0; font-size: 14px; font-weight: 600; color: #00a388; }

    .view-section p { margin: 0; font-size: 14px; color: #64748b; line-height: 1.5; }

    .view-row {
      display: flex;
      justify-content: space-between;
      padding: 8px 0;
      font-size: 14px;
    }

    .view-row span:first-child { color: #64748b; }
    .view-row span:last-child { color: #1f2937; font-weight: 500; }

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

    .btn-secondary:hover { background: #e2e8f0; }

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

    .btn-primary:hover { box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3); }
    .btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }

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

    .btn-danger:hover { background: #dc2626; }

    .delete-modal { max-width: 400px; }
    .delete-modal .modal-body { text-align: center; }
    .delete-modal .warning { color: #f97316; font-size: 13px; margin-top: 8px; }

    @media (max-width: 768px) {
      .form-row, .access-grid, .limits-grid { grid-template-columns: 1fr; }
      .campaigns-grid { grid-template-columns: 1fr; }
    }
  `]
})
export class AdminCampaignsComponent implements OnInit {
  campaigns: Campaign[] = [];
  filteredCampaigns: Campaign[] = [];
  subscriptions: any[] = [];

  searchQuery = '';
  statusFilter = 'all';
  typeFilter = 'all';

  showAddModal = false;
  showEditModal = false;
  showViewModal = false;
  showDeleteModal = false;
  selectedCampaign: Campaign | null = null;
  campaignToDelete: Campaign | null = null;

  activeTab: 'general' | 'access' = 'general';

  // Cache access rights lists to avoid infinite loop in template
  pageAccessRights: { key: string; label: string; category: string }[] = [];
  featureAccessRights: { key: string; label: string; category: string }[] = [];

  campaignForm: {
    name: string;
    description: string;
    type: string;
    status: string;
    startDate: string;
    endDate: string;
    discountPercentage: number | null;
    maxSubscriptions: number | null;
    targetSubscriptionId: number | null;
    accessRights: CampaignAccessRights;
  } = this.getEmptyForm();

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {}

  ngOnInit() {
    if (!this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/login']);
      return;
    }
    // Cache access rights lists once to avoid infinite loop
    const allRights = this.adminService.getAccessRightsList();
    this.pageAccessRights = allRights.filter(r => r.category === 'Pages');
    this.featureAccessRights = allRights.filter(r => r.category === 'Fonctionnalités');
    this.loadData();
  }

  loadData() {
    this.adminService.getCampaigns().subscribe(campaigns => {
      this.campaigns = campaigns;
      this.filterCampaigns();
    });

    this.adminService.getSubscriptions().subscribe(subs => {
      this.subscriptions = subs;
    });
  }

  filterCampaigns() {
    this.filteredCampaigns = this.campaigns.filter(campaign => {
      const matchesSearch = !this.searchQuery ||
        campaign.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        (campaign.description && campaign.description.toLowerCase().includes(this.searchQuery.toLowerCase()));
      const matchesStatus = this.statusFilter === 'all' || campaign.status === this.statusFilter;
      const matchesType = this.typeFilter === 'all' || campaign.type === this.typeFilter;
      return matchesSearch && matchesStatus && matchesType;
    });
  }

  openAddModal() {
    this.campaignForm = this.getEmptyForm();
    this.activeTab = 'general';
    this.showAddModal = true;
  }

  editCampaign(campaign: Campaign) {
    this.selectedCampaign = campaign;
    this.campaignForm = {
      name: campaign.name,
      description: campaign.description || '',
      type: campaign.type,
      status: campaign.status,
      startDate: campaign.startDate ? this.formatDateForInput(campaign.startDate) : '',
      endDate: campaign.endDate ? this.formatDateForInput(campaign.endDate) : '',
      discountPercentage: campaign.discountPercentage || null,
      maxSubscriptions: campaign.maxSubscriptions || null,
      targetSubscriptionId: campaign.targetSubscriptionId || null,
      accessRights: campaign.accessRights || this.adminService.getDefaultAccessRights()
    };
    this.activeTab = 'general';
    this.showViewModal = false;
    this.showEditModal = true;
  }

  viewCampaign(campaign: Campaign) {
    this.selectedCampaign = campaign;
    this.showViewModal = true;
  }

  toggleCampaignStatus(campaign: Campaign) {
    const newStatus = campaign.status === 'active' ? 'paused' : 'active';
    this.adminService.updateCampaign(campaign.id, { status: newStatus } as any).subscribe({
      next: () => {
        campaign.status = newStatus as any;
      },
      error: (err) => {
        console.error('Error updating campaign status:', err);
        alert('Error updating campaign status');
      }
    });
  }

  confirmDelete(campaign: Campaign) {
    this.campaignToDelete = campaign;
    this.showDeleteModal = true;
  }

  deleteCampaign() {
    if (this.campaignToDelete) {
      this.adminService.deleteCampaign(this.campaignToDelete.id).subscribe({
        next: () => {
          this.loadData();
          this.closeDeleteModal();
        },
        error: (err) => {
          console.error('Error deleting campaign:', err);
          alert(err.error?.message || 'Error deleting campaign');
        }
      });
    }
  }

  saveCampaign() {
    const data: any = {
      name: this.campaignForm.name,
      description: this.campaignForm.description || undefined,
      type: this.campaignForm.type,
      status: this.campaignForm.status,
      startDate: this.campaignForm.startDate || undefined,
      endDate: this.campaignForm.endDate || undefined,
      discountPercentage: this.campaignForm.discountPercentage || undefined,
      maxSubscriptions: this.campaignForm.maxSubscriptions || undefined,
      targetSubscriptionId: this.campaignForm.targetSubscriptionId || undefined,
      accessRights: this.campaignForm.accessRights
    };

    if (this.showEditModal && this.selectedCampaign) {
      this.adminService.updateCampaign(this.selectedCampaign.id, data).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error updating campaign:', err);
          alert(err.error?.message || 'Error updating campaign');
        }
      });
    } else {
      this.adminService.createCampaign(data).subscribe({
        next: () => {
          this.loadData();
          this.closeModals();
        },
        error: (err) => {
          console.error('Error creating campaign:', err);
          alert(err.error?.message || 'Error creating campaign');
        }
      });
    }
  }

  closeModals() {
    this.showAddModal = false;
    this.showEditModal = false;
    this.showViewModal = false;
    this.selectedCampaign = null;
    this.campaignForm = this.getEmptyForm();
  }

  closeDeleteModal() {
    this.showDeleteModal = false;
    this.campaignToDelete = null;
  }

  getAccessRight(key: string): boolean {
    return (this.campaignForm.accessRights as any)[key] ?? false;
  }

  setAccessRight(key: string, value: boolean) {
    (this.campaignForm.accessRights as any)[key] = value;
  }

  private getEmptyForm() {
    return {
      name: '',
      description: '',
      type: 'standard',
      status: 'draft',
      startDate: '',
      endDate: '',
      discountPercentage: null as number | null,
      maxSubscriptions: null as number | null,
      targetSubscriptionId: null as number | null,
      accessRights: this.adminService.getDefaultAccessRights()
    };
  }

  formatDate(date: Date): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' });
  }

  formatDateForInput(date: Date): string {
    const d = new Date(date);
    return d.toISOString().split('T')[0];
  }

  formatDateRange(campaign: Campaign): string {
    const start = campaign.startDate ? this.formatDate(campaign.startDate) : 'Start';
    const end = campaign.endDate ? this.formatDate(campaign.endDate) : 'Ongoing';
    return `${start} - ${end}`;
  }
}
