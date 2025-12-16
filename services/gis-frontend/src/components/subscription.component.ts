import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Subscription, Company } from '../models/types';

@Component({
  selector: 'app-subscription',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="dashboard">
      <aside class="sidebar">
        <div class="sidebar-header">
          <div class="logo">
            <svg width="32" height="32" viewBox="0 0 40 40" fill="none">
              <rect width="40" height="40" rx="8" fill="#2563eb"/>
              <path d="M12 20L18 26L28 14" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            <span>FleetTrack</span>
          </div>
        </div>
        <nav class="sidebar-nav">
          <a class="nav-item" (click)="navigate('/dashboard')">
            <span class="nav-icon">📊</span>
            <span>Tableau de bord</span>
          </a>
          <a class="nav-item" (click)="navigate('/vehicles')">
            <span class="nav-icon">🚗</span>
            <span>Véhicules</span>
          </a>
          <a class="nav-item" (click)="navigate('/employees')">
            <span class="nav-icon">👥</span>
            <span>Employés</span>
          </a>
          <a class="nav-item" (click)="navigate('/gps')">
            <span class="nav-icon">📍</span>
            <span>Suivi GPS</span>
          </a>
          <a class="nav-item active" (click)="navigate('/subscription')">
            <span class="nav-icon">💳</span>
            <span>Abonnement</span>
          </a>
        </nav>
        <div class="sidebar-footer">
          <div class="user-menu">
            <div class="user-avatar">{{ company?.name?.charAt(0) || 'U' }}</div>
            <div class="user-info">
              <div class="user-name">{{ company?.name || 'Entreprise' }}</div>
              <div class="user-role">Administrateur</div>
            </div>
          </div>
          <button class="btn-logout" (click)="logout()">Déconnexion</button>
        </div>
      </aside>

      <main class="main-content">
        <header class="page-header">
          <div>
            <h1>Mon abonnement</h1>
            <p>Gérez votre formule et vos options</p>
          </div>
        </header>

        <div class="subscription-layout">
          <div class="current-plan-card">
            <div class="plan-badge">Plan actuel</div>
            <h2>{{ currentSubscription?.name }}</h2>
            <div class="plan-price">
              <span class="currency">€</span>
              <span class="amount">{{ currentSubscription?.price }}</span>
              <span class="period">/mois</span>
            </div>
            <ul class="features-list">
              <li *ngFor="let feature of currentSubscription?.features">
                <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                  <path d="M7 10L9 12L13 8" stroke="#10b981" stroke-width="2" stroke-linecap="round"/>
                  <circle cx="10" cy="10" r="8" stroke="#10b981" stroke-width="2"/>
                </svg>
                {{ feature }}
              </li>
            </ul>
            <button class="btn-outline-full">Changer de formule</button>
          </div>

          <div class="usage-card">
            <h3>Utilisation</h3>
            <div class="usage-item">
              <div class="usage-label">Véhicules enregistrés</div>
              <div class="usage-bar">
                <div class="usage-progress" [style.width.%]="getUsagePercent()"></div>
              </div>
              <div class="usage-text">{{ vehiclesCount }} / {{ company?.maxVehicles }} véhicules</div>
            </div>

            <div class="usage-stats">
              <div class="stat-item">
                <div class="stat-label">Type d'entreprise</div>
                <div class="stat-value">{{ getCompanyTypeLabel() }}</div>
              </div>
              <div class="stat-item">
                <div class="stat-label">Date de création</div>
                <div class="stat-value">{{ formatDate(company?.createdAt) }}</div>
              </div>
            </div>
          </div>
        </div>

        <div class="all-plans-section">
          <h2>Toutes les formules</h2>
          <p>Choisissez la formule adaptée à vos besoins</p>

          <div class="plans-grid">
            <div
              class="plan-card"
              *ngFor="let sub of subscriptions"
              [class.current]="sub.id === currentSubscription?.id"
            >
              <div class="plan-header">
                <h3>{{ sub.name }}</h3>
                <div class="plan-price">
                  <span class="currency">€</span>
                  <span class="amount">{{ sub.price }}</span>
                  <span class="period">/mois</span>
                </div>
              </div>
              <ul class="features-list">
                <li *ngFor="let feature of sub.features">
                  <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                    <path d="M7 10L9 12L13 8" stroke="#10b981" stroke-width="2" stroke-linecap="round"/>
                    <circle cx="10" cy="10" r="8" stroke="#10b981" stroke-width="2"/>
                  </svg>
                  {{ feature }}
                </li>
              </ul>
              <button
                class="btn-primary btn-full"
                *ngIf="sub.id !== currentSubscription?.id"
              >
                Passer à cette formule
              </button>
              <button
                class="btn-outline-full"
                *ngIf="sub.id === currentSubscription?.id"
              >
                Formule actuelle
              </button>
            </div>
          </div>
        </div>
      </main>
    </div>
  `,
  styles: [`
    .dashboard { display: flex; min-height: 100vh; background: #f9fafb; }
    .sidebar {
      width: 280px; background: #111827; color: white;
      display: flex; flex-direction: column; position: fixed; height: 100vh;
    }
    .sidebar-header { padding: 24px; border-bottom: 1px solid #374151; }
    .logo { display: flex; align-items: center; gap: 12px; font-size: 20px; font-weight: 700; }
    .sidebar-nav { flex: 1; padding: 24px 16px; }
    .nav-item {
      display: flex; align-items: center; gap: 12px; padding: 12px 16px;
      border-radius: 8px; color: #9ca3af; cursor: pointer; transition: all 0.2s;
      margin-bottom: 4px; text-decoration: none;
    }
    .nav-item:hover { background: #1f2937; color: white; }
    .nav-item.active { background: #2563eb; color: white; }
    .nav-icon { font-size: 20px; }
    .sidebar-footer { padding: 24px; border-top: 1px solid #374151; }
    .user-menu { display: flex; align-items: center; gap: 12px; margin-bottom: 16px; }
    .user-avatar {
      width: 40px; height: 40px; border-radius: 50%; background: #2563eb;
      display: flex; align-items: center; justify-content: center; font-weight: 700;
    }
    .user-info { flex: 1; }
    .user-name { font-weight: 600; font-size: 14px; }
    .user-role { font-size: 12px; color: #9ca3af; }
    .btn-logout {
      width: 100%; padding: 10px; background: #1f2937; color: white;
      border: none; border-radius: 8px; font-weight: 600; cursor: pointer;
    }

    .main-content { flex: 1; margin-left: 280px; padding: 32px; }
    .page-header { margin-bottom: 32px; }
    .page-header h1 { font-size: 32px; font-weight: 800; color: #111827; margin: 0 0 4px; }
    .page-header p { color: #6b7280; margin: 0; }

    .subscription-layout { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; margin-bottom: 48px; }

    .current-plan-card {
      background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%);
      color: white; border-radius: 16px; padding: 40px; position: relative;
    }
    .plan-badge {
      background: rgba(255, 255, 255, 0.2); padding: 6px 16px; border-radius: 20px;
      font-size: 12px; font-weight: 700; display: inline-block; margin-bottom: 16px;
    }
    .current-plan-card h2 { font-size: 28px; font-weight: 800; margin: 0 0 24px; }
    .plan-price {
      display: flex; align-items: flex-start; gap: 4px; margin-bottom: 32px;
    }
    .plan-price .currency { font-size: 20px; margin-top: 8px; }
    .plan-price .amount { font-size: 48px; font-weight: 800; line-height: 1; }
    .plan-price .period { font-size: 16px; margin-top: 24px; opacity: 0.8; }

    .features-list {
      list-style: none; padding: 0; margin: 0 0 32px;
    }
    .features-list li {
      display: flex; align-items: center; gap: 12px; padding: 10px 0;
      font-size: 16px;
    }
    .current-plan-card .features-list svg path,
    .current-plan-card .features-list svg circle {
      stroke: white;
    }

    .btn-outline-full {
      width: 100%; padding: 14px; background: rgba(255, 255, 255, 0.1);
      color: white; border: 2px solid white; border-radius: 8px;
      font-size: 16px; font-weight: 600; cursor: pointer; transition: all 0.2s;
    }
    .btn-outline-full:hover {
      background: white; color: #2563eb;
    }

    .usage-card {
      background: white; border-radius: 16px; padding: 40px;
      box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
    }
    .usage-card h3 { font-size: 20px; font-weight: 700; color: #111827; margin: 0 0 24px; }
    .usage-item { margin-bottom: 32px; }
    .usage-label { font-size: 14px; color: #6b7280; margin-bottom: 12px; font-weight: 600; }
    .usage-bar {
      height: 12px; background: #e5e7eb; border-radius: 6px; overflow: hidden;
      margin-bottom: 8px;
    }
    .usage-progress {
      height: 100%; background: linear-gradient(90deg, #2563eb 0%, #3b82f6 100%);
      border-radius: 6px; transition: width 0.3s;
    }
    .usage-text { font-size: 14px; color: #111827; font-weight: 600; }

    .usage-stats { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; }
    .stat-item { }
    .stat-label { font-size: 14px; color: #6b7280; margin-bottom: 6px; }
    .stat-value { font-size: 18px; font-weight: 700; color: #111827; }

    .all-plans-section { }
    .all-plans-section h2 { font-size: 28px; font-weight: 800; color: #111827; margin: 0 0 8px; }
    .all-plans-section > p { color: #6b7280; margin: 0 0 32px; }

    .plans-grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 24px; }
    .plan-card {
      background: white; border: 2px solid #e5e7eb; border-radius: 16px;
      padding: 32px; transition: all 0.3s;
    }
    .plan-card:hover {
      transform: translateY(-4px); box-shadow: 0 12px 24px rgba(0, 0, 0, 0.1);
      border-color: #2563eb;
    }
    .plan-card.current {
      border-color: #2563eb; background: #eff6ff;
    }
    .plan-header { text-align: center; padding-bottom: 24px; border-bottom: 1px solid #e5e7eb; margin-bottom: 24px; }
    .plan-header h3 { font-size: 20px; font-weight: 700; color: #111827; margin: 0 0 16px; }
    .plan-header .plan-price { justify-content: center; margin: 0; }
    .plan-header .currency { font-size: 20px; color: #6b7280; }
    .plan-header .amount { font-size: 40px; color: #2563eb; }
    .plan-header .period { font-size: 14px; color: #6b7280; }

    .btn-primary {
      width: 100%; padding: 14px; background: #2563eb; color: white;
      border: none; border-radius: 8px; font-size: 16px; font-weight: 600;
      cursor: pointer; transition: all 0.2s;
    }
    .btn-primary:hover { background: #1d4ed8; transform: translateY(-1px); }
    .btn-full { width: 100%; }

    @media (max-width: 968px) {
      .subscription-layout,
      .plans-grid {
        grid-template-columns: 1fr;
      }
    }
  `]
})
export class SubscriptionComponent implements OnInit {
  subscriptions: Subscription[] = [];
  currentSubscription: Subscription | null = null;
  company: Company | null = null;
  vehiclesCount: number = 0;

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.company = this.dataService.getCurrentCompany();
    if (this.company) {
      const vehicles = this.dataService.getVehiclesByCompany(this.company.id);
      this.vehiclesCount = vehicles.length;
    }

    this.dataService.getSubscriptions().subscribe(subs => {
      this.subscriptions = subs;
      if (this.company) {
        this.currentSubscription = subs.find(s => s.id === this.company!.subscriptionId) || null;
      }
    });
  }

  getUsagePercent(): number {
    if (!this.company || this.company.maxVehicles === 0) return 0;
    return (this.vehiclesCount / this.company.maxVehicles) * 100;
  }

  getCompanyTypeLabel(): string {
    const labels: any = {
      location: 'Location automobile',
      transport: 'Transport',
      other: 'Autre'
    };
    return this.company ? labels[this.company.type] || this.company.type : '';
  }

  formatDate(date: any): string {
    if (!date) return '';
    return new Date(date).toLocaleDateString('fr-FR');
  }

  navigate(path: string) {
    this.router.navigate([path]);
  }

  logout() {
    this.dataService.logout();
    this.router.navigate(['/']);
  }
}
