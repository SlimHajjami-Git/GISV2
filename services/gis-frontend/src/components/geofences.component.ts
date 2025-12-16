import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { CardComponent } from './shared/ui';

@Component({
  selector: 'app-geofences',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent, CardComponent],
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
      background: #f1f5f9;
      display: flex;
      flex-direction: column;
      min-height: calc(100vh - 42px);
    }

    .geofences-content {
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
