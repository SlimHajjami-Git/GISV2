import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MockDataService } from '../services/mock-data.service';
import { Employee, Company } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { EmployeePopupComponent } from './shared/employee-popup.component';

@Component({
  selector: 'app-employees',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, EmployeePopupComponent],
  templateUrl: './employees.component.html',
  styleUrls: ['./employees.component.css']
})
export class EmployeesComponent implements OnInit {
  employees: Employee[] = [];
  allEmployees: Employee[] = [];
  company: Company | null = null;
  isPopupOpen = false;
  selectedEmployee: Employee | null = null;

  // Filters
  searchQuery = '';
  filterStatus = '';
  filterRole = '';

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
      this.allEmployees = this.dataService.getEmployeesByCompany(this.company.id);
      this.employees = [...this.allEmployees];
    }
  }

  filterEmployees() {
    this.employees = this.allEmployees.filter(e => {
      const matchesSearch = !this.searchQuery || 
        e.name.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        e.email.toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesStatus = !this.filterStatus || e.status === this.filterStatus;
      const matchesRole = !this.filterRole || e.role === this.filterRole;
      return matchesSearch && matchesStatus && matchesRole;
    });
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

  openAddPopup() {
    this.selectedEmployee = null;
    this.isPopupOpen = true;
  }

  openEditPopup(employee: Employee) {
    this.selectedEmployee = employee;
    this.isPopupOpen = true;
  }

  closePopup() {
    this.isPopupOpen = false;
    this.selectedEmployee = null;
  }

  saveEmployee(employeeData: Partial<Employee>) {
    console.log('Saving employee:', employeeData);
    this.closePopup();
  }
}
