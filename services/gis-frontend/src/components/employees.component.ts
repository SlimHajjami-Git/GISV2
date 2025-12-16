import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Employee, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';

@Component({
  selector: 'app-employees',
  standalone: true,
  imports: [CommonModule, AppLayoutComponent],
  templateUrl: './employees.component.html',
  styleUrls: ['./employees.component.css']
})
export class EmployeesComponent implements OnInit {
  employees: Employee[] = [];
  company: Company | null = null;

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
      this.employees = this.dataService.getEmployeesByCompany(this.company.id);
    }
  }

  getRoleLabel(role: string): string {
    const labels: any = {
      driver: 'Chauffeur',
      accountant: 'Comptable',
      hr: 'Ressources Humaines',
      supervisor: 'Superviseur',
      other: 'Autre'
    };
    return labels[role] || role;
  }

  formatDate(date: Date): string {
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
