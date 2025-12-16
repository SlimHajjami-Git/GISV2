import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="notifications-page">
        <div class="notifications-content">
      <div class="coming-soon">
        <svg width="120" height="120" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
          <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
          <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
        </svg>
        <h1>Notifications</h1>
        <p>Module en cours de développement</p>
        <p class="description">Configurez des alertes pour être notifié en temps réel des événements importants de votre flotte.</p>
      </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .notifications-page {
      flex: 1;
      background: var(--bg-page);
    }

    .notifications-content {
      display: flex;
      align-items: center;
      justify-content: center;
      min-height: 400px;
      padding: 40px;
    }

    .coming-soon {
      text-align: center;
      max-width: 600px;
    }

    .coming-soon svg {
      color: var(--primary);
      opacity: 0.7;
      margin-bottom: 32px;
    }

    .coming-soon h1 {
      font-size: 28px;
      font-weight: 700;
      color: var(--text-primary);
      margin: 0 0 16px 0;
    }

    .coming-soon p {
      font-size: 16px;
      color: var(--text-secondary);
      margin: 0 0 12px 0;
      line-height: 1.6;
    }

    .coming-soon .description {
      font-size: 14px;
      color: var(--text-muted);
      margin-top: 24px;
    }
  `]
})
export class NotificationsComponent implements OnInit {
  constructor(
    private router: Router,
    private dataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.dataService.isAuthenticated()) {
      this.router.navigate(['/login']);
    }
  }
}
