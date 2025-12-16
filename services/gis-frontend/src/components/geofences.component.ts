import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-geofences',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="geofences-page">
        <div class="geofences-content">
      <div class="coming-soon">
        <svg width="120" height="120" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1">
          <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/>
          <circle cx="12" cy="10" r="3"/>
        </svg>
        <h1>Geofences</h1>
        <p>Module en cours de développement</p>
        <p class="description">Créez des zones géographiques virtuelles pour recevoir des alertes lorsque vos véhicules entrent ou sortent de ces zones.</p>
      </div>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .geofences-page {
      flex: 1;
      background: var(--bg-page);
    }

    .geofences-content {
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
export class GeofencesComponent implements OnInit {
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
