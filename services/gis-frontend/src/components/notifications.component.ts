import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { CardComponent } from './shared/ui';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent, CardComponent],
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
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .notifications-content {
      display: flex;
      align-items: center;
      justify-content: center;
      flex: 1;
      padding: 40px;
      background: white;
      margin: 1px;
    }

    .coming-soon {
      text-align: center;
      max-width: 400px;
    }

    .coming-soon svg {
      color: #3b82f6;
      opacity: 0.5;
      margin-bottom: 16px;
      width: 48px;
      height: 48px;
    }

    .coming-soon h1 {
      font-size: 16px;
      font-weight: 600;
      color: #1e293b;
      margin: 0 0 8px 0;
    }

    .coming-soon p {
      font-size: 12px;
      color: #64748b;
      margin: 0 0 8px 0;
      line-height: 1.5;
    }

    .coming-soon .description {
      font-size: 11px;
      color: #94a3b8;
      margin-top: 12px;
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
