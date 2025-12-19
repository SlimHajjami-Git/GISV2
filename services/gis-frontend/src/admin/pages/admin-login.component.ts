import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminService } from '../services/admin.service';

@Component({
  selector: 'admin-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="login-container">
      <div class="login-background">
        <div class="bg-gradient"></div>
        <div class="bg-pattern"></div>
      </div>

      <div class="login-card">
        <div class="login-header">
          <div class="logo">
            <span class="logo-icon">B</span>
            <div class="logo-text">
              <span class="brand">BeliveGIS</span>
              <span class="subtitle">Admin Portal</span>
            </div>
          </div>
          <p class="welcome-text">Welcome back! Please sign in to continue.</p>
        </div>

        <form (ngSubmit)="login()" class="login-form">
          <div class="form-group">
            <label for="email">Email Address</label>
            <div class="input-wrapper">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/>
                <polyline points="22,6 12,13 2,6"/>
              </svg>
              <input
                type="email"
                id="email"
                [(ngModel)]="email"
                name="email"
                placeholder="admin@belive.tn"
                required
              />
            </div>
          </div>

          <div class="form-group">
            <label for="password">Password</label>
            <div class="input-wrapper">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/>
                <path d="M7 11V7a5 5 0 0 1 10 0v4"/>
              </svg>
              <input
                [type]="showPassword ? 'text' : 'password'"
                id="password"
                [(ngModel)]="password"
                name="password"
                placeholder="Enter your password"
                required
              />
              <button type="button" class="toggle-password" (click)="showPassword = !showPassword">
                <svg *ngIf="!showPassword" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/>
                  <circle cx="12" cy="12" r="3"/>
                </svg>
                <svg *ngIf="showPassword" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/>
                  <line x1="1" y1="1" x2="23" y2="23"/>
                </svg>
              </button>
            </div>
          </div>

          <div class="form-options">
            <label class="remember-me">
              <input type="checkbox" [(ngModel)]="rememberMe" name="rememberMe" />
              <span class="checkmark"></span>
              Remember me
            </label>
            <a href="#" class="forgot-link">Forgot password?</a>
          </div>

          <div class="error-message" *ngIf="error">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"/>
              <line x1="12" y1="8" x2="12" y2="12"/>
              <line x1="12" y1="16" x2="12.01" y2="16"/>
            </svg>
            {{ error }}
          </div>

          <button type="submit" class="login-btn" [disabled]="loading">
            <span *ngIf="!loading">Sign In</span>
            <span *ngIf="loading" class="loading-spinner"></span>
          </button>
        </form>

        <div class="login-footer">
          <div class="security-badge">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
            </svg>
            <span>Secured Access - Admin Only</span>
          </div>
          <p class="copyright">2024 BeliveGIS. All rights reserved.</p>
        </div>
      </div>

      <div class="login-info">
        <h2>Admin Control Center</h2>
        <ul class="features-list">
          <li>
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
              <circle cx="9" cy="7" r="4"/>
              <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
              <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
            </svg>
            <div>
              <h3>Client Management</h3>
              <p>Manage all clients, subscriptions, and permissions</p>
            </div>
          </li>
          <li>
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 12h-4l-3 9L9 3l-3 9H2"/>
            </svg>
            <div>
              <h3>System Monitoring</h3>
              <p>Real-time health status of all services</p>
            </div>
          </li>
          <li>
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <rect x="3" y="3" width="7" height="7"/>
              <rect x="14" y="3" width="7" height="7"/>
              <rect x="14" y="14" width="7" height="7"/>
              <rect x="3" y="14" width="7" height="7"/>
            </svg>
            <div>
              <h3>Analytics Dashboard</h3>
              <p>Comprehensive usage and performance metrics</p>
            </div>
          </li>
        </ul>
      </div>
    </div>
  `,
  styles: [`
    .login-container {
      min-height: 100vh;
      display: flex;
      position: relative;
      background: #0f1419;
    }

    .login-background {
      position: absolute;
      inset: 0;
      overflow: hidden;
    }

    .bg-gradient {
      position: absolute;
      top: -50%;
      right: -20%;
      width: 80%;
      height: 150%;
      background: radial-gradient(ellipse at center, rgba(0, 212, 170, 0.15) 0%, transparent 70%);
      animation: pulse 8s ease-in-out infinite;
    }

    .bg-pattern {
      position: absolute;
      inset: 0;
      background-image:
        radial-gradient(rgba(255, 255, 255, 0.03) 1px, transparent 1px);
      background-size: 40px 40px;
    }

    @keyframes pulse {
      0%, 100% { transform: scale(1); opacity: 0.5; }
      50% { transform: scale(1.1); opacity: 0.8; }
    }

    .login-card {
      width: 480px;
      padding: 48px;
      display: flex;
      flex-direction: column;
      justify-content: center;
      background: linear-gradient(135deg, rgba(26, 31, 46, 0.95) 0%, rgba(20, 24, 36, 0.98) 100%);
      border-right: 1px solid rgba(255, 255, 255, 0.08);
      position: relative;
      z-index: 1;
    }

    .login-header {
      margin-bottom: 32px;
    }

    .logo {
      display: flex;
      align-items: center;
      gap: 14px;
      margin-bottom: 24px;
    }

    .logo-icon {
      width: 52px;
      height: 52px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border-radius: 14px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-weight: 700;
      font-size: 24px;
      color: #fff;
      box-shadow: 0 8px 24px rgba(0, 212, 170, 0.3);
    }

    .logo-text {
      display: flex;
      flex-direction: column;
    }

    .brand {
      font-size: 24px;
      font-weight: 700;
      color: #fff;
    }

    .subtitle {
      font-size: 14px;
      color: #00d4aa;
      font-weight: 500;
    }

    .welcome-text {
      color: #8b98a5;
      font-size: 15px;
      margin: 0;
    }

    .login-form {
      display: flex;
      flex-direction: column;
      gap: 20px;
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

    .input-wrapper {
      display: flex;
      align-items: center;
      gap: 12px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      border-radius: 12px;
      padding: 14px 16px;
      transition: all 0.2s;
    }

    .input-wrapper:focus-within {
      border-color: #00d4aa;
      background: rgba(0, 212, 170, 0.05);
      box-shadow: 0 0 0 3px rgba(0, 212, 170, 0.1);
    }

    .input-wrapper svg {
      color: #8b98a5;
      flex-shrink: 0;
    }

    .input-wrapper input {
      flex: 1;
      border: none;
      background: transparent;
      color: #e7e9ea;
      font-size: 15px;
      outline: none;
    }

    .input-wrapper input::placeholder {
      color: #6b7280;
    }

    .toggle-password {
      background: none;
      border: none;
      padding: 0;
      cursor: pointer;
      color: #8b98a5;
      display: flex;
      align-items: center;
      transition: color 0.2s;
    }

    .toggle-password:hover {
      color: #e7e9ea;
    }

    .form-options {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .remember-me {
      display: flex;
      align-items: center;
      gap: 10px;
      font-size: 14px;
      color: #8b98a5;
      cursor: pointer;
      position: relative;
      padding-left: 28px;
    }

    .remember-me input {
      position: absolute;
      opacity: 0;
      cursor: pointer;
    }

    .checkmark {
      position: absolute;
      left: 0;
      width: 18px;
      height: 18px;
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.2);
      border-radius: 4px;
      transition: all 0.2s;
    }

    .remember-me input:checked ~ .checkmark {
      background: #00d4aa;
      border-color: #00d4aa;
    }

    .checkmark::after {
      content: '';
      position: absolute;
      display: none;
      left: 6px;
      top: 2px;
      width: 4px;
      height: 9px;
      border: solid #fff;
      border-width: 0 2px 2px 0;
      transform: rotate(45deg);
    }

    .remember-me input:checked ~ .checkmark::after {
      display: block;
    }

    .forgot-link {
      font-size: 14px;
      color: #00d4aa;
      text-decoration: none;
      transition: color 0.2s;
    }

    .forgot-link:hover {
      color: #00e6b8;
    }

    .error-message {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 12px 16px;
      background: rgba(239, 68, 68, 0.1);
      border: 1px solid rgba(239, 68, 68, 0.3);
      border-radius: 10px;
      color: #ef4444;
      font-size: 14px;
    }

    .login-btn {
      width: 100%;
      padding: 16px;
      background: linear-gradient(135deg, #00d4aa 0%, #00a388 100%);
      border: none;
      border-radius: 12px;
      color: #fff;
      font-size: 16px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.3s;
      display: flex;
      align-items: center;
      justify-content: center;
      margin-top: 8px;
    }

    .login-btn:hover:not(:disabled) {
      transform: translateY(-2px);
      box-shadow: 0 8px 24px rgba(0, 212, 170, 0.35);
    }

    .login-btn:disabled {
      opacity: 0.7;
      cursor: not-allowed;
    }

    .loading-spinner {
      width: 20px;
      height: 20px;
      border: 2px solid rgba(255, 255, 255, 0.3);
      border-top-color: #fff;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    .login-footer {
      margin-top: 32px;
      text-align: center;
    }

    .security-badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 8px 16px;
      background: rgba(0, 212, 170, 0.1);
      border-radius: 20px;
      color: #00d4aa;
      font-size: 13px;
      margin-bottom: 16px;
    }

    .copyright {
      font-size: 12px;
      color: #6b7280;
      margin: 0;
    }

    .login-info {
      flex: 1;
      display: flex;
      flex-direction: column;
      justify-content: center;
      padding: 48px 64px;
      position: relative;
      z-index: 1;
    }

    .login-info h2 {
      font-size: 32px;
      font-weight: 700;
      color: #e7e9ea;
      margin: 0 0 40px 0;
    }

    .features-list {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 32px;
    }

    .features-list li {
      display: flex;
      align-items: flex-start;
      gap: 20px;
    }

    .features-list li svg {
      flex-shrink: 0;
      color: #00d4aa;
      padding: 12px;
      background: rgba(0, 212, 170, 0.1);
      border-radius: 12px;
    }

    .features-list li h3 {
      font-size: 18px;
      font-weight: 600;
      color: #e7e9ea;
      margin: 0 0 6px 0;
    }

    .features-list li p {
      font-size: 14px;
      color: #8b98a5;
      margin: 0;
      line-height: 1.5;
    }

    @media (max-width: 1024px) {
      .login-info {
        display: none;
      }
      .login-card {
        width: 100%;
        border-right: none;
      }
    }

    @media (max-width: 480px) {
      .login-card {
        padding: 32px 24px;
      }
    }
  `]
})
export class AdminLoginComponent {
  email = '';
  password = '';
  showPassword = false;
  rememberMe = false;
  loading = false;
  error = '';

  constructor(
    private router: Router,
    private adminService: AdminService
  ) {
    if (this.adminService.isAuthenticated()) {
      this.router.navigate(['/admin/dashboard']);
    }
  }

  login() {
    if (!this.email || !this.password) {
      this.error = 'Please enter your email and password';
      return;
    }

    this.loading = true;
    this.error = '';

    this.adminService.login(this.email, this.password).subscribe({
      next: () => {
        this.router.navigate(['/admin/dashboard']);
      },
      error: (err) => {
        this.error = 'Invalid email or password';
        this.loading = false;
      }
    });
  }
}
