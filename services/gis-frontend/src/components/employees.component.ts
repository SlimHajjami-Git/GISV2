import { Component, OnInit, NgZone, ChangeDetectorRef, ApplicationRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterModule } from '@angular/router';
import { ApiService } from '../services/api.service';
import { MockDataService } from '../services/mock-data.service';
import { Employee, Company, Vehicle, DriverScore } from '../models/types';
import { AppLayoutComponent } from './shared/app-layout.component';
import { EmployeePopupComponent } from './shared/employee-popup.component';
import { DriverScoreDetailComponent } from './shared/driver-score-detail.component';

@Component({
  selector: 'app-employees',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, AppLayoutComponent, EmployeePopupComponent, DriverScoreDetailComponent],
  templateUrl: './employees.component.html',
  styleUrls: ['./employees.component.css']
})
export class EmployeesComponent implements OnInit {
  drivers: any[] = [];
  allDrivers: any[] = [];
  allVehicles: Vehicle[] = [];
  isPopupOpen = false;
  selectedDriver: any = null;

  // Driver Score Detail
  isDetailOpen = false;
  detailEmployee: Employee | null = null;
  detailDriverScore: DriverScore | null = null;
  detailAssignedVehicle: Vehicle | null = null;

  // Filters
  searchQuery = '';
  filterStatus = '';

  constructor(
    private router: Router,
    private apiService: ApiService,
    private mockDataService: MockDataService,
    private ngZone: NgZone,
    private cdr: ChangeDetectorRef,
    private appRef: ApplicationRef
  ) {}

  ngOnInit() {
    if (!this.apiService.isAuthenticated()) {
      this.router.navigate(['/login']);
      return;
    }
    this.loadData();
  }

  loadData() {
    this.apiService.getDrivers().subscribe({
      next: (drivers) => {
        this.ngZone.run(() => {
          this.allDrivers = drivers;
          this.drivers = [...this.allDrivers];
          this.cdr.detectChanges();
        });
      },
      error: (err) => console.error('Error loading drivers:', err)
    });

    this.apiService.getVehicles().subscribe({
      next: (vehicles) => {
        this.ngZone.run(() => {
          this.allVehicles = vehicles;
          this.cdr.detectChanges();
        });
      },
      error: (err) => console.error('Error loading vehicles:', err)
    });

  }

  filterEmployees() {
    this.drivers = this.allDrivers.filter(d => {
      const fullName = (d.firstName || '') + ' ' + (d.lastName || '');
      const matchesSearch = !this.searchQuery ||
        fullName.toLowerCase().includes(this.searchQuery.toLowerCase()) ||
        (d.email || '').toLowerCase().includes(this.searchQuery.toLowerCase());
      const matchesStatus = !this.filterStatus || d.status === this.filterStatus;
      return matchesSearch && matchesStatus;
    });
  }

  formatDate(date: any): string {
    if (!date) return 'N/A';
    return new Date(date).toLocaleDateString('fr-FR');
  }

  openAddPopup() {
    this.selectedDriver = null;
    this.isPopupOpen = true;
  }

  openEditPopup(driver: any) {
    this.selectedDriver = driver;
    this.isPopupOpen = true;
  }

  closePopup() {
    this.isPopupOpen = false;
    this.selectedDriver = null;
  }

  errorMessage = '';

  saveDriver(driverData: any) {
    this.errorMessage = '';
    if (this.selectedDriver?.id) {
      this.apiService.updateDriver(this.selectedDriver.id, driverData).subscribe({
        next: () => {
          this.closePopup();
          this.loadData();
        },
        error: (err: any) => {
          this.ngZone.run(() => {
            this.errorMessage = err.error?.message || err.error?.title || 'Erreur lors de la mise à jour';
            this.cdr.detectChanges();
          });
        }
      });
    } else {
      this.apiService.createDriver(driverData).subscribe({
        next: () => {
          this.closePopup();
          this.loadData();
        },
        error: (err: any) => {
          this.ngZone.run(() => {
            this.errorMessage = err.error?.message || err.error?.title || 'Erreur lors de la création';
            this.cdr.detectChanges();
          });
        }
      });
    }
  }

  deleteDriver(driver: any) {
    if (confirm(`Supprimer le chauffeur ${driver.firstName} ${driver.lastName} ?`)) {
      this.apiService.deleteDriver(driver.id).subscribe({
        next: () => this.loadData(),
        error: (err: any) => console.error('Error deleting driver:', err)
      });
    }
  }

  // Driver Score Detail Methods
  openDriverDetail(driver: any) {
    this.detailEmployee = {
      id: driver.id, firstName: driver.firstName, lastName: driver.lastName,
      name: driver.firstName + ' ' + driver.lastName, email: driver.email,
      phone: driver.phone, status: driver.status, employeeRole: 'driver'
    } as any;
    this.detailDriverScore = this.mockDataService.getDriverScoreByEmployee(String(driver.id)) || null;
    if (driver.assignedVehicleId) {
      this.detailAssignedVehicle = this.allVehicles.find(v => v.id === String(driver.assignedVehicleId)) || null;
    } else {
      this.detailAssignedVehicle = null;
    }
    this.isDetailOpen = true;
  }

  closeDriverDetail() {
    this.isDetailOpen = false;
    this.detailEmployee = null;
    this.detailDriverScore = null;
    this.detailAssignedVehicle = null;
  }

  onDateFilterChanged(range: { from: string; to: string }) {
    console.log('Date filter changed:', range);
  }

  // Permit deadline methods
  getDaysUntilPermitExpiry(driver: any): number {
    if (!driver.permitExpiry) return 999;
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const expiry = new Date(driver.permitExpiry);
    expiry.setHours(0, 0, 0, 0);
    return Math.ceil((expiry.getTime() - today.getTime()) / (1000 * 60 * 60 * 24));
  }

  getPermitStatus(driver: any): string {
    const days = this.getDaysUntilPermitExpiry(driver);
    if (days < 0) return 'expired';
    if (days <= 15) return 'critical';
    if (days <= 30) return 'warning';
    return 'ok';
  }

  getPermitAlertText(driver: any): string {
    const days = this.getDaysUntilPermitExpiry(driver);
    if (days < 0) return 'Permis expiré';
    if (days === 0) return "Permis expire aujourd'hui";
    if (days <= 30) return `Permis expire dans ${days}j`;
    return `Permis valide (${days}j)`;
  }

  getActiveCount(): number {
    return this.allDrivers.filter(d => d.status === 'active').length;
  }

  getInactiveCount(): number {
    return this.allDrivers.filter(d => d.status !== 'active').length;
  }

  getExpiringPermitsCount(): number {
    return this.allDrivers.filter(d => {
      const days = this.getDaysUntilPermitExpiry(d);
      return days <= 30;
    }).length;
  }
}
