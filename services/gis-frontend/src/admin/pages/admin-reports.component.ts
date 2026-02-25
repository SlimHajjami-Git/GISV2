import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { ReportsComponent } from '../../components/reports.component';

@Component({
  selector: 'admin-reports',
  standalone: true,
  imports: [CommonModule, AdminLayoutComponent, ReportsComponent],
  template: `
    <admin-layout pageTitle="Rapports">
      <app-reports [embedded]="true"></app-reports>
    </admin-layout>
  `,
  styles: [`
    :host {
      display: block;
      height: 100vh;
    }
    :host ::ng-deep .reports-page {
      height: calc(100vh - 64px);
    }
  `]
})
export class AdminReportsComponent {}
