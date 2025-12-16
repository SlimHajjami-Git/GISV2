import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Subscription } from '../models/types';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="landing-page">
      <header class="header">
        <div class="container">
          <div class="logo">
            <svg width="40" height="40" viewBox="0 0 40 40" fill="none">
              <rect width="40" height="40" rx="8" fill="#2563eb"/>
              <path d="M12 20L18 26L28 14" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
            <span>FleetTrack Pro</span>
          </div>
          <nav>
            <a href="#features">Fonctionnalités</a>
            <a href="#pricing">Tarifs</a>
            <a href="#contact">Contact</a>
            <button class="btn-secondary" (click)="goToLogin()">Connexion</button>
            <button class="btn-primary" (click)="goToRegister()">Commencer</button>
          </nav>
        </div>
      </header>

      <section class="hero">
        <div class="container">
          <div class="hero-content">
            <h1>Gérez votre parc automobile en toute simplicité</h1>
            <p>Solution complète de gestion de flotte avec suivi GPS en temps réel. Optimisez vos opérations et réduisez vos coûts.</p>
            <div class="hero-buttons">
              <button class="btn-primary btn-large" (click)="goToRegister()">Essai gratuit 14 jours</button>
              <button class="btn-outline btn-large" (click)="scrollTo('pricing')">Voir les tarifs</button>
            </div>
            <div class="hero-stats">
              <div class="stat">
                <div class="stat-number">500+</div>
                <div class="stat-label">Véhicules suivis</div>
              </div>
              <div class="stat">
                <div class="stat-number">50+</div>
                <div class="stat-label">Entreprises clientes</div>
              </div>
              <div class="stat">
                <div class="stat-number">99.9%</div>
                <div class="stat-label">Disponibilité</div>
              </div>
            </div>
          </div>
          <div class="hero-image">
            <div class="dashboard-preview">
              <div class="preview-header">
                <div class="preview-dots">
                  <span></span><span></span><span></span>
                </div>
              </div>
              <div class="preview-content">
                <div class="preview-card">
                  <div class="card-icon blue">🚗</div>
                  <div class="card-text">
                    <div class="card-label">Véhicules actifs</div>
                    <div class="card-value">24</div>
                  </div>
                </div>
                <div class="preview-card">
                  <div class="card-icon green">📍</div>
                  <div class="card-text">
                    <div class="card-label">Suivi GPS</div>
                    <div class="card-value">Temps réel</div>
                  </div>
                </div>
                <div class="preview-map"></div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section id="features" class="features">
        <div class="container">
          <div class="section-header">
            <h2>Fonctionnalités complètes</h2>
            <p>Tout ce dont vous avez besoin pour gérer efficacement votre parc automobile</p>
          </div>
          <div class="features-grid">
            <div class="feature-card">
              <div class="feature-icon">🚗</div>
              <h3>Gestion de parc</h3>
              <p>Centralisez toutes les informations de vos véhicules: documents, entretiens, affectations.</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon">📍</div>
              <h3>Suivi GPS en temps réel</h3>
              <p>Localisez tous vos véhicules sur une carte interactive avec historique des trajets.</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon">👥</div>
              <h3>Gestion d'équipe</h3>
              <p>Gérez vos chauffeurs, superviseurs et affectations de véhicules facilement.</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon">🔔</div>
              <h3>Alertes intelligentes</h3>
              <p>Recevez des notifications pour les excès de vitesse, arrêts prolongés et plus.</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon">📊</div>
              <h3>Rapports détaillés</h3>
              <p>Analyses et statistiques pour optimiser l'utilisation de votre flotte.</p>
            </div>
            <div class="feature-card">
              <div class="feature-icon">🔧</div>
              <h3>Maintenance préventive</h3>
              <p>Planifiez et suivez l'entretien de vos véhicules automatiquement.</p>
            </div>
          </div>
        </div>
      </section>

      <section id="pricing" class="pricing">
        <div class="container">
          <div class="section-header">
            <h2>Choisissez votre formule</h2>
            <p>Des offres adaptées à tous les besoins</p>
          </div>
          <div class="pricing-grid">
            <div class="pricing-card" *ngFor="let sub of subscriptions">
              <div class="pricing-header">
                <h3>{{ sub.name }}</h3>
                <div class="price">
                  <span class="currency">€</span>
                  <span class="amount">{{ sub.price }}</span>
                  <span class="period">/mois</span>
                </div>
              </div>
              <ul class="features-list">
                <li *ngFor="let feature of sub.features">
                  <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                    <path d="M7 10L9 12L13 8" stroke="#10b981" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                    <circle cx="10" cy="10" r="8" stroke="#10b981" stroke-width="2"/>
                  </svg>
                  {{ feature }}
                </li>
              </ul>
              <button class="btn-primary btn-full" (click)="selectPlan(sub.id)">
                Choisir cette offre
              </button>
            </div>
          </div>
        </div>
      </section>

      <section class="cta">
        <div class="container">
          <div class="cta-content">
            <h2>Prêt à optimiser votre flotte?</h2>
            <p>Rejoignez des centaines d'entreprises qui nous font confiance</p>
            <button class="btn-primary btn-large" (click)="goToRegister()">Commencer maintenant</button>
          </div>
        </div>
      </section>

      <footer class="footer">
        <div class="container">
          <div class="footer-content">
            <div class="footer-section">
              <div class="logo">
                <svg width="32" height="32" viewBox="0 0 40 40" fill="none">
                  <rect width="40" height="40" rx="8" fill="#2563eb"/>
                  <path d="M12 20L18 26L28 14" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>
                <span>FleetTrack Pro</span>
              </div>
              <p>Solution professionnelle de gestion de parc automobile</p>
            </div>
            <div class="footer-section">
              <h4>Produit</h4>
              <a href="#features">Fonctionnalités</a>
              <a href="#pricing">Tarifs</a>
              <a href="#">Documentation</a>
            </div>
            <div class="footer-section">
              <h4>Entreprise</h4>
              <a href="#">À propos</a>
              <a href="#contact">Contact</a>
              <a href="#">Mentions légales</a>
            </div>
            <div class="footer-section">
              <h4>Support</h4>
              <a href="#">Centre d'aide</a>
              <a href="#">Support technique</a>
              <a href="#">Status</a>
            </div>
          </div>
          <div class="footer-bottom">
            <p>&copy; 2024 FleetTrack Pro. Tous droits réservés.</p>
          </div>
        </div>
      </footer>
    </div>
  `,
  styles: [`
    .landing-page {
      min-height: 100vh;
      background: #ffffff;
    }

    .container {
      max-width: 1200px;
      margin: 0 auto;
      padding: 0 24px;
    }

    .header {
      position: fixed;
      top: 0;
      left: 0;
      right: 0;
      background: rgba(255, 255, 255, 0.95);
      backdrop-filter: blur(10px);
      border-bottom: 1px solid #e5e7eb;
      z-index: 1000;
    }

    .header .container {
      display: flex;
      justify-content: space-between;
      align-items: center;
      height: 72px;
    }

    .logo {
      display: flex;
      align-items: center;
      gap: 12px;
      font-size: 20px;
      font-weight: 700;
      color: #111827;
    }

    nav {
      display: flex;
      align-items: center;
      gap: 32px;
    }

    nav a {
      color: #6b7280;
      text-decoration: none;
      font-weight: 500;
      transition: color 0.2s;
    }

    nav a:hover {
      color: #2563eb;
    }

    .btn-primary, .btn-secondary, .btn-outline {
      padding: 10px 20px;
      border-radius: 8px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.2s;
      border: none;
      font-size: 14px;
    }

    .btn-primary {
      background: #2563eb;
      color: white;
    }

    .btn-primary:hover {
      background: #1d4ed8;
      transform: translateY(-1px);
      box-shadow: 0 4px 12px rgba(37, 99, 235, 0.3);
    }

    .btn-secondary {
      background: #f3f4f6;
      color: #374151;
    }

    .btn-secondary:hover {
      background: #e5e7eb;
    }

    .btn-outline {
      background: white;
      color: #2563eb;
      border: 2px solid #2563eb;
    }

    .btn-outline:hover {
      background: #eff6ff;
    }

    .btn-large {
      padding: 16px 32px;
      font-size: 16px;
    }

    .btn-full {
      width: 100%;
    }

    .hero {
      padding: 120px 0 80px;
      background: linear-gradient(135deg, #f8fafc 0%, #e0f2fe 100%);
    }

    .hero .container {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 64px;
      align-items: center;
    }

    .hero-content h1 {
      font-size: 56px;
      font-weight: 800;
      color: #111827;
      line-height: 1.2;
      margin: 0 0 24px;
    }

    .hero-content p {
      font-size: 20px;
      color: #6b7280;
      line-height: 1.6;
      margin: 0 0 32px;
    }

    .hero-buttons {
      display: flex;
      gap: 16px;
      margin-bottom: 48px;
    }

    .hero-stats {
      display: flex;
      gap: 48px;
    }

    .stat {
      text-align: center;
    }

    .stat-number {
      font-size: 32px;
      font-weight: 800;
      color: #2563eb;
    }

    .stat-label {
      font-size: 14px;
      color: #6b7280;
      margin-top: 4px;
    }

    .dashboard-preview {
      background: white;
      border-radius: 16px;
      box-shadow: 0 20px 60px rgba(0, 0, 0, 0.1);
      overflow: hidden;
    }

    .preview-header {
      background: #f3f4f6;
      padding: 16px;
      border-bottom: 1px solid #e5e7eb;
    }

    .preview-dots {
      display: flex;
      gap: 8px;
    }

    .preview-dots span {
      width: 12px;
      height: 12px;
      border-radius: 50%;
      background: #d1d5db;
    }

    .preview-content {
      padding: 24px;
    }

    .preview-card {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 16px;
      background: #f9fafb;
      border-radius: 12px;
      margin-bottom: 16px;
    }

    .card-icon {
      width: 48px;
      height: 48px;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 24px;
    }

    .card-icon.blue {
      background: #dbeafe;
    }

    .card-icon.green {
      background: #d1fae5;
    }

    .card-text {
      flex: 1;
    }

    .card-label {
      font-size: 14px;
      color: #6b7280;
    }

    .card-value {
      font-size: 24px;
      font-weight: 700;
      color: #111827;
    }

    .preview-map {
      height: 200px;
      background: linear-gradient(135deg, #dbeafe 0%, #bfdbfe 100%);
      border-radius: 12px;
      position: relative;
      overflow: hidden;
    }

    .features, .pricing {
      padding: 80px 0;
    }

    .section-header {
      text-align: center;
      margin-bottom: 64px;
    }

    .section-header h2 {
      font-size: 40px;
      font-weight: 800;
      color: #111827;
      margin: 0 0 16px;
    }

    .section-header p {
      font-size: 18px;
      color: #6b7280;
    }

    .features-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 32px;
    }

    .feature-card {
      padding: 32px;
      background: white;
      border: 1px solid #e5e7eb;
      border-radius: 16px;
      transition: all 0.3s;
    }

    .feature-card:hover {
      transform: translateY(-4px);
      box-shadow: 0 12px 24px rgba(0, 0, 0, 0.08);
      border-color: #2563eb;
    }

    .feature-icon {
      width: 64px;
      height: 64px;
      background: #eff6ff;
      border-radius: 12px;
      display: flex;
      align-items: center;
      justify-content: center;
      font-size: 32px;
      margin-bottom: 24px;
    }

    .feature-card h3 {
      font-size: 20px;
      font-weight: 700;
      color: #111827;
      margin: 0 0 12px;
    }

    .feature-card p {
      font-size: 16px;
      color: #6b7280;
      line-height: 1.6;
      margin: 0;
    }

    .pricing {
      background: #f9fafb;
    }

    .pricing-grid {
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 32px;
    }

    .pricing-card {
      background: white;
      border: 2px solid #e5e7eb;
      border-radius: 16px;
      padding: 40px;
      transition: all 0.3s;
    }

    .pricing-card:hover {
      transform: translateY(-8px);
      box-shadow: 0 20px 40px rgba(0, 0, 0, 0.12);
      border-color: #2563eb;
    }

    .pricing-header {
      text-align: center;
      padding-bottom: 32px;
      border-bottom: 1px solid #e5e7eb;
      margin-bottom: 32px;
    }

    .pricing-header h3 {
      font-size: 24px;
      font-weight: 700;
      color: #111827;
      margin: 0 0 16px;
    }

    .price {
      display: flex;
      align-items: flex-start;
      justify-content: center;
      gap: 4px;
    }

    .currency {
      font-size: 24px;
      color: #6b7280;
      margin-top: 8px;
    }

    .amount {
      font-size: 48px;
      font-weight: 800;
      color: #2563eb;
      line-height: 1;
    }

    .period {
      font-size: 16px;
      color: #6b7280;
      margin-top: 24px;
    }

    .features-list {
      list-style: none;
      padding: 0;
      margin: 0 0 32px;
    }

    .features-list li {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 0;
      font-size: 16px;
      color: #374151;
    }

    .cta {
      padding: 80px 0;
      background: linear-gradient(135deg, #2563eb 0%, #1d4ed8 100%);
      color: white;
    }

    .cta-content {
      text-align: center;
    }

    .cta-content h2 {
      font-size: 40px;
      font-weight: 800;
      margin: 0 0 16px;
    }

    .cta-content p {
      font-size: 20px;
      margin: 0 0 32px;
      opacity: 0.9;
    }

    .cta-content .btn-primary {
      background: white;
      color: #2563eb;
    }

    .cta-content .btn-primary:hover {
      background: #f3f4f6;
    }

    .footer {
      background: #111827;
      color: white;
      padding: 64px 0 32px;
    }

    .footer-content {
      display: grid;
      grid-template-columns: 2fr 1fr 1fr 1fr;
      gap: 48px;
      margin-bottom: 48px;
    }

    .footer-section h4 {
      font-size: 16px;
      font-weight: 700;
      margin: 0 0 16px;
    }

    .footer-section a {
      display: block;
      color: #9ca3af;
      text-decoration: none;
      margin-bottom: 12px;
      transition: color 0.2s;
    }

    .footer-section a:hover {
      color: white;
    }

    .footer-section p {
      color: #9ca3af;
      margin: 16px 0 0;
    }

    .footer-bottom {
      padding-top: 32px;
      border-top: 1px solid #374151;
      text-align: center;
      color: #9ca3af;
    }

    @media (max-width: 968px) {
      .hero .container {
        grid-template-columns: 1fr;
      }

      .hero-content h1 {
        font-size: 40px;
      }

      .features-grid,
      .pricing-grid {
        grid-template-columns: 1fr;
      }

      .footer-content {
        grid-template-columns: 1fr 1fr;
      }

      nav {
        gap: 16px;
      }
    }
  `]
})
export class LandingComponent {
  subscriptions: Subscription[] = [];

  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {
    this.dataService.getSubscriptions().subscribe(subs => {
      this.subscriptions = subs;
    });
  }

  goToLogin() {
    this.router.navigate(['/login']);
  }

  goToRegister() {
    this.router.navigate(['/register']);
  }

  selectPlan(planId: string) {
    this.router.navigate(['/register'], { queryParams: { plan: planId } });
  }

  scrollTo(id: string) {
    const element = document.getElementById(id);
    if (element) {
      element.scrollIntoView({ behavior: 'smooth' });
    }
  }
}
