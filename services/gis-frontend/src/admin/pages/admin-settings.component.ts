import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService } from '../services/admin.service';

@Component({
  selector: 'admin-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Settings">
      <div class="settings-page">
        <div class="settings-nav">
          <button *ngFor="let tab of tabs" class="nav-tab" [class.active]="activeTab === tab.id" (click)="activeTab = tab.id">
            <span class="tab-icon" [innerHTML]="tab.icon"></span>
            {{ tab.label }}
          </button>
        </div>

        <div class="settings-content">
          <div class="settings-section" *ngIf="activeTab === 'general'">
            <div class="section-header">
              <h2>General Settings</h2>
              <p>Configure general platform settings</p>
            </div>

            <div class="settings-group">
              <h3>Platform Information</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Platform Name</label>
                  <input type="text" [(ngModel)]="settings.platformName" />
                </div>
                <div class="form-group">
                  <label>Support Email</label>
                  <input type="email" [(ngModel)]="settings.supportEmail" />
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Default Language</label>
                  <select [(ngModel)]="settings.defaultLanguage">
                    <option value="en">English</option>
                    <option value="fr">French</option>
                    <option value="ar">Arabic</option>
                  </select>
                </div>
                <div class="form-group">
                  <label>Default Timezone</label>
                  <select [(ngModel)]="settings.defaultTimezone">
                    <option value="Africa/Tunis">Africa/Tunis (GMT+1)</option>
                    <option value="Africa/Casablanca">Africa/Casablanca (GMT+1)</option>
                    <option value="Europe/Paris">Europe/Paris (GMT+1)</option>
                  </select>
                </div>
              </div>
            </div>

            <div class="settings-group">
              <h3>Session Settings</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Session Timeout (minutes)</label>
                  <input type="number" [(ngModel)]="settings.sessionTimeout" min="5" max="480" />
                </div>
                <div class="form-group">
                  <label>Max Login Attempts</label>
                  <input type="number" [(ngModel)]="settings.maxLoginAttempts" min="3" max="10" />
                </div>
              </div>
            </div>
          </div>

          <div class="settings-section" *ngIf="activeTab === 'notifications'">
            <div class="section-header">
              <h2>Notification Settings</h2>
              <p>Configure system notifications and alerts</p>
            </div>

            <div class="settings-group">
              <h3>Email Notifications</h3>
              <div class="toggle-group">
                <label class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">New Client Registration</span>
                    <span class="toggle-desc">Receive email when a new client registers</span>
                  </div>
                  <input type="checkbox" [(ngModel)]="settings.notifyNewClient" />
                  <span class="toggle-switch"></span>
                </label>
                <label class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">Service Downtime</span>
                    <span class="toggle-desc">Alert when a service goes down</span>
                  </div>
                  <input type="checkbox" [(ngModel)]="settings.notifyServiceDown" />
                  <span class="toggle-switch"></span>
                </label>
                <label class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">Payment Received</span>
                    <span class="toggle-desc">Notify when payment is received</span>
                  </div>
                  <input type="checkbox" [(ngModel)]="settings.notifyPayment" />
                  <span class="toggle-switch"></span>
                </label>
                <label class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">Subscription Expiry</span>
                    <span class="toggle-desc">Alert before client subscription expires</span>
                  </div>
                  <input type="checkbox" [(ngModel)]="settings.notifySubscriptionExpiry" />
                  <span class="toggle-switch"></span>
                </label>
              </div>
            </div>

            <div class="settings-group">
              <h3>Alert Recipients</h3>
              <div class="form-group">
                <label>Admin Email Recipients</label>
                <textarea [(ngModel)]="settings.alertEmails" rows="3" placeholder="admin@Calypso.tn&#10;support@Calypso.tn"></textarea>
                <span class="form-hint">One email per line</span>
              </div>
            </div>
          </div>

          <div class="settings-section" *ngIf="activeTab === 'security'">
            <div class="section-header">
              <h2>Security Settings</h2>
              <p>Configure security and authentication options</p>
            </div>

            <div class="settings-group">
              <h3>Password Policy</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Minimum Password Length</label>
                  <input type="number" [(ngModel)]="settings.minPasswordLength" min="8" max="24" />
                </div>
                <div class="form-group">
                  <label>Password Expiry (days)</label>
                  <input type="number" [(ngModel)]="settings.passwordExpiry" min="0" max="365" />
                  <span class="form-hint">0 = Never expires</span>
                </div>
              </div>
              <div class="toggle-group">
                <label class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">Require Special Characters</span>
                    <span class="toggle-desc">Password must include !@#$% etc.</span>
                  </div>
                  <input type="checkbox" [(ngModel)]="settings.requireSpecialChars" />
                  <span class="toggle-switch"></span>
                </label>
                <label class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">Two-Factor Authentication</span>
                    <span class="toggle-desc">Require 2FA for admin accounts</span>
                  </div>
                  <input type="checkbox" [(ngModel)]="settings.require2FA" />
                  <span class="toggle-switch"></span>
                </label>
              </div>
            </div>

            <div class="settings-group">
              <h3>IP Restrictions</h3>
              <div class="form-group">
                <label>Allowed IP Addresses (Admin Panel)</label>
                <textarea [(ngModel)]="settings.allowedIPs" rows="3" placeholder="192.168.1.0/24&#10;10.0.0.0/8"></textarea>
                <span class="form-hint">Leave empty to allow all IPs. CIDR notation supported.</span>
              </div>
            </div>
          </div>

          <div class="settings-section" *ngIf="activeTab === 'billing'">
            <div class="section-header">
              <h2>Billing Settings</h2>
              <p>Configure billing and payment options</p>
            </div>

            <div class="settings-group">
              <h3>Invoice Settings</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Default Tax Rate (%)</label>
                  <input type="number" [(ngModel)]="settings.taxRate" min="0" max="100" step="0.1" />
                </div>
                <div class="form-group">
                  <label>Currency</label>
                  <select [(ngModel)]="settings.currency">
                    <option value="TND">TND - Tunisian Dinar</option>
                    <option value="EUR">EUR - Euro</option>
                    <option value="USD">USD - US Dollar</option>
                  </select>
                </div>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Invoice Prefix</label>
                  <input type="text" [(ngModel)]="settings.invoicePrefix" />
                </div>
                <div class="form-group">
                  <label>Payment Terms (days)</label>
                  <input type="number" [(ngModel)]="settings.paymentTerms" min="0" max="90" />
                </div>
              </div>
            </div>

            <div class="settings-group">
              <h3>Company Information</h3>
              <div class="form-group">
                <label>Company Name</label>
                <input type="text" [(ngModel)]="settings.companyName" />
              </div>
              <div class="form-group">
                <label>Company Address</label>
                <textarea [(ngModel)]="settings.companyAddress" rows="3"></textarea>
              </div>
              <div class="form-row">
                <div class="form-group">
                  <label>Tax ID / VAT Number</label>
                  <input type="text" [(ngModel)]="settings.taxId" />
                </div>
                <div class="form-group">
                  <label>Phone</label>
                  <input type="text" [(ngModel)]="settings.companyPhone" />
                </div>
              </div>
            </div>
          </div>

          <div class="settings-section" *ngIf="activeTab === 'api'">
            <div class="section-header">
              <h2>API Settings</h2>
              <p>Configure API access and rate limits</p>
            </div>

            <div class="settings-group">
              <h3>Rate Limiting</h3>
              <div class="form-row">
                <div class="form-group">
                  <label>Requests per Minute</label>
                  <input type="number" [(ngModel)]="settings.rateLimit" min="10" max="1000" />
                </div>
                <div class="form-group">
                  <label>Burst Limit</label>
                  <input type="number" [(ngModel)]="settings.burstLimit" min="10" max="500" />
                </div>
              </div>
            </div>

            <div class="settings-group">
              <h3>API Keys</h3>
              <div class="api-key-display">
                <label>Admin API Key</label>
                <div class="key-row">
                  <input type="text" [value]="settings.apiKey" readonly />
                  <button class="copy-btn" (click)="copyApiKey()">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <rect x="9" y="9" width="13" height="13" rx="2" ry="2"/>
                      <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>
                    </svg>
                  </button>
                  <button class="regenerate-btn" (click)="regenerateApiKey()">Regenerate</button>
                </div>
              </div>
              <div class="toggle-group">
                <label class="toggle-row">
                  <div class="toggle-info">
                    <span class="toggle-label">Enable API Access for Clients</span>
                    <span class="toggle-desc">Allow clients to use the REST API</span>
                  </div>
                  <input type="checkbox" [(ngModel)]="settings.enableClientApi" />
                  <span class="toggle-switch"></span>
                </label>
              </div>
            </div>
          </div>
        </div>

        <div class="settings-footer">
          <button class="btn-secondary" (click)="resetSettings()">Reset to Default</button>
          <button class="btn-primary" (click)="saveSettings()">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
              <polyline points="17,21 17,13 7,13 7,21"/>
              <polyline points="7,3 7,8 15,8"/>
            </svg>
            Save Settings
          </button>
        </div>
      </div>
    </admin-layout>
  `,
  styles: [`
    .settings-page {
      display: flex;
      flex-direction: column;
      gap: 24px;
    }

    .settings-nav {
      display: flex;
      gap: 8px;
      padding: 8px;
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 14px;
      flex-wrap: wrap;
    }

    .nav-tab {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 20px;
      background: transparent;
      border: none;
      border-radius: 10px;
      color: #8b98a5;
      font-size: 14px;
      font-weight: 500;
      cursor: pointer;
      transition: all 0.2s;
    }

    .nav-tab:hover {
      background: rgba(255, 255, 255, 0.05);
      color: #e7e9ea;
    }

    .nav-tab.active {
      background: rgba(0, 212, 170, 0.15);
      color: #00d4aa;
    }

    .tab-icon {
      width: 18px;
      height: 18px;
      display: flex;
      align-items: center;
      justify-content: center;
    }

    .settings-content {
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.8) 0%, rgba(20, 24, 36, 0.9) 100%);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 16px;
      overflow: hidden;
    }

    .settings-section {
      padding: 28px;
    }

    .section-header {
      margin-bottom: 28px;
      padding-bottom: 20px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.08);
    }

    .section-header h2 {
      margin: 0 0 6px 0;
      font-size: 20px;
      font-weight: 600;
      color: #e7e9ea;
    }

    .section-header p {
      margin: 0;
      font-size: 14px;
      color: #8b98a5;
    }

    .settings-group {
      margin-bottom: 28px;
    }

    .settings-group:last-child {
      margin-bottom: 0;
    }

    .settings-group h3 {
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

    .form-row:last-child {
      margin-bottom: 0;
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
      transition: border-color 0.2s;
    }

    .form-group input:focus, .form-group select:focus, .form-group textarea:focus {
      border-color: #00d4aa;
    }

    .form-hint {
      font-size: 12px;
      color: #6b7280;
    }

    .toggle-group {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }

    .toggle-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 14px 16px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 10px;
      cursor: pointer;
      transition: all 0.2s;
    }

    .toggle-row:hover {
      background: rgba(255, 255, 255, 0.05);
    }

    .toggle-row input { display: none; }

    .toggle-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .toggle-label {
      font-size: 14px;
      font-weight: 500;
      color: #e7e9ea;
    }

    .toggle-desc {
      font-size: 12px;
      color: #6b7280;
    }

    .toggle-switch {
      width: 44px;
      height: 24px;
      background: rgba(255, 255, 255, 0.1);
      border-radius: 12px;
      position: relative;
      transition: all 0.3s;
      flex-shrink: 0;
    }

    .toggle-switch::after {
      content: '';
      position: absolute;
      width: 18px;
      height: 18px;
      background: #8b98a5;
      border-radius: 50%;
      top: 3px;
      left: 3px;
      transition: all 0.3s;
    }

    .toggle-row input:checked ~ .toggle-switch {
      background: #00d4aa;
    }

    .toggle-row input:checked ~ .toggle-switch::after {
      left: 23px;
      background: #fff;
    }

    .api-key-display {
      margin-bottom: 16px;
    }

    .api-key-display label {
      display: block;
      font-size: 13px;
      font-weight: 500;
      color: #e7e9ea;
      margin-bottom: 8px;
    }

    .key-row {
      display: flex;
      gap: 8px;
    }

    .key-row input {
      flex: 1;
      padding: 12px 14px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #8b98a5;
      font-size: 13px;
      font-family: monospace;
    }

    .copy-btn, .regenerate-btn {
      padding: 12px 16px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 10px;
      color: #8b98a5;
      font-size: 13px;
      cursor: pointer;
      transition: all 0.2s;
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .copy-btn:hover, .regenerate-btn:hover {
      background: rgba(255, 255, 255, 0.1);
      color: #e7e9ea;
    }

    .settings-footer {
      display: flex;
      justify-content: flex-end;
      gap: 12px;
      padding: 20px 28px;
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.6) 0%, rgba(20, 24, 36, 0.7) 100%);
      border: 1px solid rgba(255, 255, 255, 0.06);
      border-radius: 14px;
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
      transition: all 0.2s;
    }

    .btn-secondary:hover {
      background: rgba(255, 255, 255, 0.1);
    }

    .btn-primary {
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

    .btn-primary:hover {
      box-shadow: 0 4px 16px rgba(0, 212, 170, 0.3);
    }

    @media (max-width: 768px) {
      .form-row {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class AdminSettingsComponent implements OnInit {
  activeTab = 'general';

  tabs = [
    { id: 'general', label: 'General', icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>' },
    { id: 'notifications', label: 'Notifications', icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/></svg>' },
    { id: 'security', label: 'Security', icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/></svg>' },
    { id: 'billing', label: 'Billing', icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="1" y="4" width="22" height="16" rx="2" ry="2"/><line x1="1" y1="10" x2="23" y2="10"/></svg>' },
    { id: 'api', label: 'API', icon: '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="16,18 22,12 16,6"/><polyline points="8,6 2,12 8,18"/></svg>' },
  ];

  settings = {
    platformName: 'CalypsoGIS',
    supportEmail: 'support@Calypso.tn',
    defaultLanguage: 'fr',
    defaultTimezone: 'Africa/Tunis',
    sessionTimeout: 60,
    maxLoginAttempts: 5,

    notifyNewClient: true,
    notifyServiceDown: true,
    notifyPayment: true,
    notifySubscriptionExpiry: true,
    alertEmails: 'admin@Calypso.tn\nsupport@Calypso.tn',

    minPasswordLength: 8,
    passwordExpiry: 90,
    requireSpecialChars: true,
    require2FA: false,
    allowedIPs: '',

    taxRate: 19,
    currency: 'TND',
    invoicePrefix: 'INV-',
    paymentTerms: 30,
    companyName: 'Calypso Technology',
    companyAddress: '123 Tech Avenue\nTunis, Tunisia 1000',
    taxId: 'TN12345678',
    companyPhone: '+216 71 123 456',

    rateLimit: 100,
    burstLimit: 50,
    apiKey: 'blv_admin_sk_live_xxxxxxxxxxxxxxxxxxxxx',
    enableClientApi: true,
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
  }

  saveSettings() {
    alert('Settings saved successfully');
  }

  resetSettings() {
    if (confirm('Are you sure you want to reset all settings to default?')) {
      alert('Settings reset to default');
    }
  }

  copyApiKey() {
    navigator.clipboard.writeText(this.settings.apiKey);
    alert('API key copied to clipboard');
  }

  regenerateApiKey() {
    if (confirm('Are you sure? This will invalidate the current API key.')) {
      this.settings.apiKey = 'blv_admin_sk_live_' + Math.random().toString(36).substring(2, 15);
      alert('New API key generated');
    }
  }
}
