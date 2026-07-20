import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { MonitoringComponent } from '../../components/monitoring.component';

@Component({
  selector: 'admin-monitoring',
  standalone: true,
  imports: [CommonModule, AdminLayoutComponent, MonitoringComponent],
  template: `
    <admin-layout pageTitle="Monitoring">
      <app-monitoring [embedded]="true"></app-monitoring>
    </admin-layout>
  `,
  styles: [`
    :host {
      display: block;
      height: 100vh;
      background: var(--adm-bg);
    }
    :host ::ng-deep .wialon-monitoring {
      height: calc(100vh - 64px);
    }
  `]
})
export class AdminMonitoringComponent {}
