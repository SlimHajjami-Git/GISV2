import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../services/api.service';
import { MockDataService } from '../services/mock-data.service';
import { Employee, Company, Vehicle, DriverScore } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { EmployeePopupComponent } from './shared/employee-popup.component';
import { DriverScoreDetailComponent } from './shared/driver-score-detail.component';

@Component({
  selector: 'app-employees',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, EmployeePopupComponent, DriverScoreDetailComponent],
  templateUrl: './employees.component.html',
  styleUrls: ['./employees.component.css']
})
export class EmployeesComponent implements OnInit {
  employees: Employee[] = [];
  allEmployees: Employee[] = [];
  allVehicles: Vehicle[] = [];
  company: Company | null = null;
  isPopupOpen = false;
  selectedEmployee: Employee | null = null;

  // Driver Score Detail
  isDetailOpen = false;
  detailEmployee: Employee | null = null;
  detailDriverScore: DriverScore | null = null;
  detailAssignedVehicle: Vehicle | null = null;

  // Filters
  searchQuery = '';
  filterStatus = '';
  filterRole = '';

  constructor(
    private router: Router,
    private apiService: ApiService,
    private mockDataService: MockDataService
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }

    this.loadEmployees();
  }

  loadEmployees() {
    this.apiService.getEmployees().subscribe({
      next: (employees) => {
        this.allEmployees = employees;
        this.employees = [...this.allEmployees];
      },
      error: (err) => console.error('Error loading employees:', err)
    });

    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.allVehicles = vehicles;
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });
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
    this.apiService.logout();
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

  // Driver Score Detail Methods
  openDriverDetail(employee: Employee) {
    console.log('openDriverDetail called with:', employee);
    this.detailEmployee = employee;
    
    // Get driver score from mock data
    this.detailDriverScore = this.mockDataService.getDriverScoreByEmployee(employee.id) || null;
    console.log('Driver score found:', this.detailDriverScore);
    
    // Get assigned vehicle
    if (employee.assignedVehicles && employee.assignedVehicles.length > 0) {
      this.detailAssignedVehicle = this.allVehicles.find(v => v.id === employee.assignedVehicles[0]) || null;
    } else {
      this.detailAssignedVehicle = null;
    }
    console.log('Assigned vehicle:', this.detailAssignedVehicle);
    
    this.isDetailOpen = true;
    console.log('isDetailOpen set to:', this.isDetailOpen);
  }

  closeDriverDetail() {
    this.isDetailOpen = false;
    this.detailEmployee = null;
    this.detailDriverScore = null;
    this.detailAssignedVehicle = null;
  }

  onDateFilterChanged(range: { from: string; to: string }) {
    // In a real app, this would fetch new data for the selected date range
    console.log('Date filter changed:', range);
  }
}
