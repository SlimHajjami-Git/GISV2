import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable } from 'rxjs';
import {
  User,
  Company,
  Subscription,
  Vehicle,
  Employee,
  GPSLocation,
  GPSAlert,
  RentalContract,
} from '../models/types';

@Injectable({
  providedIn: 'root',
})
export class MockDataService {
  private currentUser$ = new BehaviorSubject<User | null>(null);
  private subscriptions$ = new BehaviorSubject<Subscription[]>([]);
  private companies$ = new BehaviorSubject<Company[]>([]);
  private vehicles$ = new BehaviorSubject<Vehicle[]>([]);
  private employees$ = new BehaviorSubject<Employee[]>([]);
  private gpsLocations$ = new BehaviorSubject<GPSLocation[]>([]);
  private gpsAlerts$ = new BehaviorSubject<GPSAlert[]>([]);
  private rentalContracts$ = new BehaviorSubject<RentalContract[]>([]);

  constructor() {
    this.initializeMockData();
  }

  private initializeMockData() {
    const subscriptions: Subscription[] = [
      {
        id: 'sub1',
        name: 'Parc Automobile',
        type: 'parc',
        price: 49.99,
        features: [
          'Gestion complète du parc',
          'Documents véhicules',
          'Gestion employés',
          'Rapports basiques',
        ],
        gpsTracking: false,
        gpsInstallation: false,
      },
      {
        id: 'sub2',
        name: 'Parc + Suivi GPS',
        type: 'parc_gps',
        price: 99.99,
        features: [
          'Toutes les fonctionnalités Parc',
          'Suivi GPS temps réel',
          'Historique des trajets',
          'Alertes configurables',
          'Rapports avancés',
        ],
        gpsTracking: true,
        gpsInstallation: false,
      },
      {
        id: 'sub3',
        name: 'Parc + GPS + Installation',
        type: 'parc_gps_install',
        price: 149.99,
        features: [
          'Toutes les fonctionnalités Parc + GPS',
          'Installation GPS physique',
          'Support technique prioritaire',
          'Formation incluse',
        ],
        gpsTracking: true,
        gpsInstallation: true,
      },
    ];

    const companies: Company[] = [
      {
        id: 'comp1',
        name: 'TransExpress SARL',
        type: 'transport',
        subscriptionId: 'sub2',
        maxVehicles: 20,
        createdAt: new Date('2024-01-15'),
      },
      {
        id: 'comp2',
        name: 'AutoLocation Plus',
        type: 'location',
        subscriptionId: 'sub3',
        maxVehicles: 50,
        createdAt: new Date('2024-02-20'),
      },
    ];

    const vehicles: Vehicle[] = [
      {
        id: 'veh1',
        companyId: 'comp1',
        name: 'Camion Transport 01',
        type: 'camion',
        brand: 'Mercedes',
        model: 'Actros',
        plate: 'AB-1234-CD',
        year: 2022,
        status: 'in_use',
        hasGPS: true,
        assignedDriverId: 'emp1',
        assignedSupervisorId: 'emp3',
        documents: [],
        mileage: 45000,
      },
      {
        id: 'veh2',
        companyId: 'comp1',
        name: 'Utilitaire 01',
        type: 'utilitaire',
        brand: 'Renault',
        model: 'Master',
        plate: 'EF-5678-GH',
        year: 2023,
        status: 'available',
        hasGPS: true,
        documents: [],
        mileage: 12000,
      },
      {
        id: 'veh3',
        companyId: 'comp2',
        name: 'SUV Location 01',
        type: 'suv',
        brand: 'Toyota',
        model: 'RAV4',
        plate: 'IJ-9012-KL',
        year: 2024,
        status: 'in_use',
        hasGPS: true,
        assignedDriverId: 'emp4',
        documents: [],
        mileage: 5000,
        rentalMileage: 200,
      },
      {
        id: 'veh4',
        companyId: 'comp2',
        name: 'Citadine Location 02',
        type: 'citadine',
        brand: 'Peugeot',
        model: '208',
        plate: 'MN-3456-OP',
        year: 2024,
        status: 'available',
        hasGPS: true,
        documents: [],
        mileage: 3000,
      },
    ];

    const employees: Employee[] = [
      {
        id: 'emp1',
        companyId: 'comp1',
        name: 'Amadou Diallo',
        email: 'amadou@transexpress.com',
        phone: '+221 77 123 4567',
        role: 'driver',
        assignedVehicles: ['veh1'],
        status: 'active',
        hireDate: new Date('2023-06-01'),
      },
      {
        id: 'emp2',
        companyId: 'comp1',
        name: 'Fatou Sow',
        email: 'fatou@transexpress.com',
        phone: '+221 77 234 5678',
        role: 'accountant',
        assignedVehicles: [],
        status: 'active',
        hireDate: new Date('2023-07-15'),
      },
      {
        id: 'emp3',
        companyId: 'comp1',
        name: 'Moussa Ba',
        email: 'moussa@transexpress.com',
        phone: '+221 77 345 6789',
        role: 'supervisor',
        assignedVehicles: ['veh1', 'veh2'],
        status: 'active',
        hireDate: new Date('2023-05-01'),
      },
      {
        id: 'emp4',
        companyId: 'comp2',
        name: 'Aïssatou Ndiaye',
        email: 'aissatou@autolocation.com',
        phone: '+221 77 456 7890',
        role: 'driver',
        assignedVehicles: ['veh3'],
        status: 'active',
        hireDate: new Date('2024-01-10'),
      },
    ];

    const gpsLocations: GPSLocation[] = [
      {
        vehicleId: 'veh1',
        latitude: 14.6937,
        longitude: -17.4441,
        speed: 45,
        direction: 180,
        timestamp: new Date(),
        address: 'Avenue Cheikh Anta Diop, Dakar',
      },
      {
        vehicleId: 'veh2',
        latitude: 14.7167,
        longitude: -17.4677,
        speed: 0,
        direction: 0,
        timestamp: new Date(),
        address: 'Plateau, Dakar',
      },
      {
        vehicleId: 'veh3',
        latitude: 14.7645,
        longitude: -17.3667,
        speed: 60,
        direction: 90,
        timestamp: new Date(),
        address: 'Route de Rufisque',
      },
      {
        vehicleId: 'veh4',
        latitude: 14.6928,
        longitude: -17.4467,
        speed: 0,
        direction: 0,
        timestamp: new Date(),
        address: 'Sacré-Coeur, Dakar',
      },
    ];

    const gpsAlerts: GPSAlert[] = [
      {
        id: 'alert1',
        vehicleId: 'veh3',
        type: 'speeding',
        message: 'Vitesse excessive détectée: 95 km/h',
        timestamp: new Date(Date.now() - 1000 * 60 * 30),
        resolved: false,
      },
      {
        id: 'alert2',
        vehicleId: 'veh2',
        type: 'stopped',
        message: 'Véhicule à l\'arrêt depuis plus de 2 heures',
        timestamp: new Date(Date.now() - 1000 * 60 * 120),
        resolved: false,
      },
    ];

    const rentalContracts: RentalContract[] = [
      {
        id: 'rent1',
        vehicleId: 'veh3',
        customerName: 'Ibrahima Fall',
        customerContact: '+221 77 567 8901',
        startDate: new Date('2024-12-01'),
        endDate: new Date('2024-12-10'),
        startMileage: 4800,
        status: 'active',
        price: 35000,
      },
    ];

    this.subscriptions$.next(subscriptions);
    this.companies$.next(companies);
    this.vehicles$.next(vehicles);
    this.employees$.next(employees);
    this.gpsLocations$.next(gpsLocations);
    this.gpsAlerts$.next(gpsAlerts);
    this.rentalContracts$.next(rentalContracts);
  }

  login(email: string, password: string): boolean {
    const user: User = {
      id: 'user1',
      email: email,
      name: 'Admin User',
      companyId: 'comp1',
      role: 'admin',
    };
    this.currentUser$.next(user);
    return true;
  }

  logout() {
    this.currentUser$.next(null);
  }

  getCurrentUser(): Observable<User | null> {
    return this.currentUser$.asObservable();
  }

  isAuthenticated(): boolean {
    return this.currentUser$.value !== null;
  }

  getSubscriptions(): Observable<Subscription[]> {
    return this.subscriptions$.asObservable();
  }

  getCompanies(): Observable<Company[]> {
    return this.companies$.asObservable();
  }

  getCurrentCompany(): Company | null {
    const user = this.currentUser$.value;
    if (!user) return null;
    return this.companies$.value.find((c) => c.id === user.companyId) || null;
  }

  getVehicles(): Observable<Vehicle[]> {
    return this.vehicles$.asObservable();
  }

  getVehiclesByCompany(companyId: string): Vehicle[] {
    return this.vehicles$.value.filter((v) => v.companyId === companyId);
  }

  getEmployees(): Observable<Employee[]> {
    return this.employees$.asObservable();
  }

  getEmployeesByCompany(companyId: string): Employee[] {
    return this.employees$.value.filter((e) => e.companyId === companyId);
  }

  getGPSLocations(): Observable<GPSLocation[]> {
    return this.gpsLocations$.asObservable();
  }

  getGPSAlerts(): Observable<GPSAlert[]> {
    return this.gpsAlerts$.asObservable();
  }

  getRentalContracts(): Observable<RentalContract[]> {
    return this.rentalContracts$.asObservable();
  }

  addVehicle(vehicle: Vehicle) {
    const vehicles = [...this.vehicles$.value, vehicle];
    this.vehicles$.next(vehicles);
  }

  updateVehicle(vehicle: Vehicle) {
    const vehicles = this.vehicles$.value.map((v) =>
      v.id === vehicle.id ? vehicle : v
    );
    this.vehicles$.next(vehicles);
  }

  deleteVehicle(id: string) {
    const vehicles = this.vehicles$.value.filter((v) => v.id !== id);
    this.vehicles$.next(vehicles);
  }

  addEmployee(employee: Employee) {
    const employees = [...this.employees$.value, employee];
    this.employees$.next(employees);
  }

  updateEmployee(employee: Employee) {
    const employees = this.employees$.value.map((e) =>
      e.id === employee.id ? employee : e
    );
    this.employees$.next(employees);
  }

  deleteEmployee(id: string) {
    const employees = this.employees$.value.filter((e) => e.id !== id);
    this.employees$.next(employees);
  }
}
