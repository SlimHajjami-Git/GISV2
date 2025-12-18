import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, asapScheduler } from 'rxjs';
import { observeOn } from 'rxjs/operators';
import {
  User,
  Company,
  Subscription,
  Vehicle,
  Employee,
  GPSLocation,
  GPSAlert,
  RentalContract,
  GPSDevice,
  MaintenanceRecord,
  VehicleCost,
  Geofence,
  GeofenceEvent,
  DriverScore,
  VehicleTrip,
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
  private gpsDevices$ = new BehaviorSubject<GPSDevice[]>([]);
  private maintenanceRecords$ = new BehaviorSubject<MaintenanceRecord[]>([]);
  private vehicleCosts$ = new BehaviorSubject<VehicleCost[]>([]);
  private geofences$ = new BehaviorSubject<Geofence[]>([]);
  private geofenceEvents$ = new BehaviorSubject<GeofenceEvent[]>([]);
  private driverScores$ = new BehaviorSubject<DriverScore[]>([]);
  private vehicleTrips$ = new BehaviorSubject<VehicleTrip[]>([]);

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
        name: 'TransTunisie SARL',
        type: 'transport',
        subscriptionId: 'sub2',
        maxVehicles: 20,
        createdAt: new Date('2024-01-15'),
      },
      {
        id: 'comp2',
        name: 'Location Plus Tunisie',
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
        // GPS info
        gpsDeviceId: 'gps1',
        gpsImei: '358762109054321',
        gpsSimNumber: '+216 50 123 456',
        gpsSimOperator: 'maroc_telecom',
        gpsBrand: 'Concox',
        gpsModel: 'GT06N',
        gpsInstallationDate: new Date('2024-06-15'),
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
        // GPS info
        gpsDeviceId: 'gps2',
        gpsImei: '358762109054322',
        gpsSimNumber: '+216 51 234 567',
        gpsSimOperator: 'orange',
        gpsBrand: 'Coban',
        gpsModel: 'TK103B',
        gpsInstallationDate: new Date('2024-07-20'),
      },
      {
        id: 'veh3',
        companyId: 'comp1',
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
        // GPS info
        gpsDeviceId: 'gps3',
        gpsImei: '358762109054323',
        gpsSimNumber: '+216 52 345 678',
        gpsSimOperator: 'inwi',
        gpsBrand: 'Concox',
        gpsModel: 'GT06E',
        gpsInstallationDate: new Date('2024-08-10'),
      },
      {
        id: 'veh4',
        companyId: 'comp1',
        name: 'Citadine Location 02',
        type: 'citadine',
        brand: 'Peugeot',
        model: '208',
        plate: 'MN-3456-OP',
        year: 2024,
        status: 'available',
        hasGPS: false,
        documents: [],
        mileage: 3000,
      },
    ];

    const employees: Employee[] = [
      {
        id: 'emp1',
        companyId: 'comp1',
        name: 'Mohamed Ben Ali',
        email: 'mohamed@transtunisie.com',
        phone: '+216 50 123 456',
        role: 'driver',
        assignedVehicles: ['veh1'],
        status: 'active',
        hireDate: new Date('2023-06-01'),
      },
      {
        id: 'emp2',
        companyId: 'comp1',
        name: 'Fatma Trabelsi',
        email: 'fatma@transtunisie.com',
        phone: '+216 51 234 567',
        role: 'accountant',
        assignedVehicles: [],
        status: 'active',
        hireDate: new Date('2023-07-15'),
      },
      {
        id: 'emp3',
        companyId: 'comp1',
        name: 'Ahmed Gharbi',
        email: 'ahmed@transtunisie.com',
        phone: '+216 52 345 678',
        role: 'supervisor',
        assignedVehicles: ['veh1', 'veh2'],
        status: 'active',
        hireDate: new Date('2023-05-01'),
      },
      {
        id: 'emp4',
        companyId: 'comp2',
        name: 'Sarra Bouazizi',
        email: 'sarra@locationtunisie.com',
        phone: '+216 53 456 789',
        role: 'driver',
        assignedVehicles: ['veh3'],
        status: 'active',
        hireDate: new Date('2024-01-10'),
      },
    ];

    const gpsLocations: GPSLocation[] = [
      {
        vehicleId: 'veh1',
        latitude: 36.8065,
        longitude: 10.1815,
        speed: 45,
        direction: 180,
        timestamp: new Date(),
        address: 'Avenue Habib Bourguiba, Tunis',
      },
      {
        vehicleId: 'veh2',
        latitude: 36.8008,
        longitude: 10.1800,
        speed: 0,
        direction: 0,
        timestamp: new Date(),
        address: 'La Marsa, Tunis',
      },
      {
        vehicleId: 'veh3',
        latitude: 36.8500,
        longitude: 10.3200,
        speed: 60,
        direction: 90,
        timestamp: new Date(),
        address: 'Route de La Goulette',
      },
      {
        vehicleId: 'veh4',
        latitude: 36.7900,
        longitude: 10.1650,
        speed: 0,
        direction: 0,
        timestamp: new Date(),
        address: 'Lac de Tunis',
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

    const gpsDevices: GPSDevice[] = [
      {
        id: 'gps1',
        vehicleId: 'veh1',
        companyId: 'comp1',
        imei: '358762109054321',
        simNumber: '+216 50 123 456',
        simOperator: 'ooredoo_tunisie',
        model: 'GT06N',
        brand: 'Concox',
        firmwareVersion: 'v2.1.5',
        installationDate: new Date('2024-06-15'),
        status: 'active',
        lastCommunication: new Date(Date.now() - 1000 * 60 * 5),
        batteryLevel: 95,
        signalStrength: 85,
      },
      {
        id: 'gps2',
        vehicleId: 'veh2',
        companyId: 'comp1',
        imei: '358762109054322',
        simNumber: '+216 51 234 567',
        simOperator: 'orange_tunisie',
        model: 'TK103B',
        brand: 'Coban',
        firmwareVersion: 'v1.8.2',
        installationDate: new Date('2024-07-20'),
        status: 'active',
        lastCommunication: new Date(Date.now() - 1000 * 60 * 2),
        batteryLevel: 78,
        signalStrength: 92,
      },
      {
        id: 'gps3',
        vehicleId: 'veh3',
        companyId: 'comp1',
        imei: '358762109054323',
        simNumber: '+216 52 345 678',
        simOperator: 'tunisie_telecom',
        model: 'GT06E',
        brand: 'Concox',
        firmwareVersion: 'v2.2.0',
        installationDate: new Date('2024-08-10'),
        status: 'active',
        lastCommunication: new Date(Date.now() - 1000 * 60 * 8),
        batteryLevel: 100,
        signalStrength: 75,
      },
      {
        id: 'gps4',
        companyId: 'comp1',
        imei: '358762109054324',
        simNumber: '+216 53 456 789',
        simOperator: 'ooredoo_tunisie',
        model: 'VT200',
        brand: 'Queclink',
        firmwareVersion: 'v3.0.1',
        status: 'unassigned',
        batteryLevel: 100,
        signalStrength: 0,
      },
      {
        id: 'gps5',
        companyId: 'comp1',
        imei: '358762109054325',
        simNumber: '+216 54 567 890',
        simOperator: 'orange_tunisie',
        model: 'GT06N',
        brand: 'Concox',
        status: 'maintenance',
        batteryLevel: 0,
        signalStrength: 0,
      },
    ];

    const maintenanceRecords: MaintenanceRecord[] = [
      {
        id: 'maint1',
        vehicleId: 'veh1',
        companyId: 'comp1',
        type: 'oil_change',
        description: 'Vidange huile moteur + filtre',
        mileageAtService: 45000,
        date: new Date('2024-11-15'),
        nextServiceDate: new Date('2025-02-15'),
        nextServiceMileage: 55000,
        status: 'completed',
        laborCost: 150,
        partsCost: 350,
        totalCost: 500,
        serviceProvider: 'Garage Central Tunis',
        providerContact: '+216 71 234 567',
        invoiceNumber: 'INV-2024-0156',
        parts: [
          { id: 'p1', name: 'Huile moteur 5W30', quantity: 5, unitCost: 50, totalCost: 250 },
          { id: 'p2', name: 'Filtre à huile', quantity: 1, unitCost: 100, totalCost: 100 },
        ],
      },
      {
        id: 'maint2',
        vehicleId: 'veh2',
        companyId: 'comp1',
        type: 'repair',
        description: 'Remplacement plaquettes de frein avant',
        mileageAtService: 62000,
        date: new Date('2024-12-01'),
        status: 'completed',
        laborCost: 200,
        partsCost: 450,
        totalCost: 650,
        serviceProvider: 'Auto Service Tunisie',
        providerContact: '+216 71 345 678',
        invoiceNumber: 'INV-2024-0892',
        parts: [
          { id: 'p3', name: 'Plaquettes de frein ATE', quantity: 2, unitCost: 200, totalCost: 400 },
          { id: 'p4', name: 'Capteur usure', quantity: 1, unitCost: 50, totalCost: 50 },
        ],
      },
      {
        id: 'maint3',
        vehicleId: 'veh1',
        companyId: 'comp1',
        type: 'tire_change',
        description: 'Changement pneus hiver',
        mileageAtService: 47500,
        date: new Date('2024-12-10'),
        status: 'completed',
        laborCost: 100,
        partsCost: 2400,
        totalCost: 2500,
        serviceProvider: 'Euromaster',
        invoiceNumber: 'INV-EM-5678',
        parts: [
          { id: 'p5', name: 'Pneu Michelin 205/55R16', quantity: 4, unitCost: 600, totalCost: 2400 },
        ],
      },
      {
        id: 'maint4',
        vehicleId: 'veh3',
        companyId: 'comp1',
        type: 'scheduled',
        description: 'Révision des 30 000 km',
        mileageAtService: 30000,
        date: new Date('2024-12-20'),
        status: 'scheduled',
        laborCost: 300,
        partsCost: 0,
        totalCost: 300,
        serviceProvider: 'Garage Central',
      },
      {
        id: 'maint5',
        vehicleId: 'veh2',
        companyId: 'comp1',
        type: 'inspection',
        description: 'Contrôle technique annuel',
        mileageAtService: 63000,
        date: new Date('2024-12-15'),
        status: 'in_progress',
        laborCost: 400,
        partsCost: 0,
        totalCost: 400,
        serviceProvider: 'Centre de Contrôle Technique',
      },
    ];

    const vehicleCosts: VehicleCost[] = [
      { id: 'cost1', vehicleId: 'veh1', companyId: 'comp1', type: 'fuel', description: 'Plein essence', amount: 850, date: new Date('2024-12-14'), mileage: 47600, fuelType: 'gasoline', liters: 60 },
      { id: 'cost2', vehicleId: 'veh1', companyId: 'comp1', type: 'fuel', description: 'Plein essence', amount: 780, date: new Date('2024-12-08'), mileage: 47200, fuelType: 'gasoline', liters: 55 },
      { id: 'cost3', vehicleId: 'veh2', companyId: 'comp1', type: 'fuel', description: 'Plein diesel', amount: 920, date: new Date('2024-12-12'), mileage: 63100, fuelType: 'diesel', liters: 70 },
      { id: 'cost4', vehicleId: 'veh1', companyId: 'comp1', type: 'toll', description: 'Autoroute Casa-Rabat', amount: 70, date: new Date('2024-12-10'), receiptNumber: 'TL-20241210' },
      { id: 'cost5', vehicleId: 'veh2', companyId: 'comp1', type: 'parking', description: 'Parking aéroport', amount: 120, date: new Date('2024-12-11'), receiptNumber: 'PK-8547' },
      { id: 'cost6', vehicleId: 'veh1', companyId: 'comp1', type: 'insurance', description: 'Assurance annuelle', amount: 4500, date: new Date('2024-01-15'), receiptNumber: 'ASS-2024-001' },
      { id: 'cost7', vehicleId: 'veh3', companyId: 'comp1', type: 'tax', description: 'Vignette annuelle', amount: 1200, date: new Date('2024-01-20'), receiptNumber: 'VIG-2024' },
      { id: 'cost8', vehicleId: 'veh2', companyId: 'comp1', type: 'maintenance', description: 'Révision 60000km', amount: 1500, date: new Date('2024-11-20'), mileage: 60000, receiptNumber: 'FAC-GC-2024-089' },
    ];

    // Geofences - Zones en Tunisie (Tunis region)
    const geofences: Geofence[] = [
      {
        id: 'geo1',
        companyId: 'comp1',
        name: 'Siège Social',
        description: 'Zone du siège social de l\'entreprise',
        type: 'circle',
        color: '#22c55e',
        center: { lat: 36.8065, lng: 10.1815 },
        radius: 500,
        alertOnEntry: true,
        alertOnExit: true,
        isActive: true,
        assignedVehicleIds: ['veh1', 'veh2', 'veh3', 'veh4'],
        createdAt: new Date('2024-01-01'),
      },
      {
        id: 'geo2',
        companyId: 'comp1',
        name: 'Entrepôt Principal',
        description: 'Zone de stockage et logistique',
        type: 'polygon',
        color: '#3b82f6',
        coordinates: [
          { lat: 36.8300, lng: 10.1600 },
          { lat: 36.8300, lng: 10.1800 },
          { lat: 36.8200, lng: 10.1800 },
          { lat: 36.8200, lng: 10.1600 },
        ],
        alertOnEntry: true,
        alertOnExit: true,
        alertSpeed: 30,
        isActive: true,
        assignedVehicleIds: ['veh1', 'veh2'],
        createdAt: new Date('2024-02-15'),
      },
      {
        id: 'geo3',
        companyId: 'comp1',
        name: 'Zone Industrielle',
        description: 'Zone industrielle Ben Arous',
        type: 'circle',
        color: '#f97316',
        center: { lat: 36.7500, lng: 10.2200 },
        radius: 1000,
        alertOnEntry: false,
        alertOnExit: true,
        isActive: true,
        createdAt: new Date('2024-03-10'),
      },
      {
        id: 'geo4',
        companyId: 'comp1',
        name: 'Aéroport Tunis-Carthage',
        description: 'Zone aéroportuaire',
        type: 'circle',
        color: '#8b5cf6',
        center: { lat: 36.8510, lng: 10.2272 },
        radius: 2000,
        alertOnEntry: true,
        alertOnExit: true,
        alertSpeed: 50,
        isActive: true,
        assignedVehicleIds: ['veh3'],
        createdAt: new Date('2024-04-01'),
      },
      {
        id: 'geo5',
        companyId: 'comp1',
        name: 'Port de La Goulette',
        description: 'Zone portuaire',
        type: 'polygon',
        color: '#06b6d4',
        coordinates: [
          { lat: 36.8200, lng: 10.3000 },
          { lat: 36.8200, lng: 10.3200 },
          { lat: 36.8100, lng: 10.3200 },
          { lat: 36.8100, lng: 10.3000 },
        ],
        alertOnEntry: true,
        alertOnExit: false,
        isActive: false,
        createdAt: new Date('2024-05-20'),
      },
    ];

    const geofenceEvents: GeofenceEvent[] = [
      { id: 'evt1', geofenceId: 'geo1', vehicleId: 'veh1', type: 'entry', timestamp: new Date('2024-12-16T08:30:00'), location: { lat: 36.8065, lng: 10.1815 } },
      { id: 'evt2', geofenceId: 'geo1', vehicleId: 'veh1', type: 'exit', timestamp: new Date('2024-12-16T17:45:00'), location: { lat: 36.8070, lng: 10.1820 } },
      { id: 'evt3', geofenceId: 'geo2', vehicleId: 'veh2', type: 'entry', timestamp: new Date('2024-12-16T09:15:00'), location: { lat: 36.8250, lng: 10.1700 } },
      { id: 'evt4', geofenceId: 'geo3', vehicleId: 'veh1', type: 'speed_violation', timestamp: new Date('2024-12-15T14:20:00'), location: { lat: 36.7500, lng: 10.2200 }, speed: 65 },
    ];

    // Driver Scores - Mock data for driving behavior analysis
    const driverScores: DriverScore[] = [
      {
        id: 'ds1',
        employeeId: 'emp1',
        period: { from: new Date('2024-12-01'), to: new Date('2024-12-18') },
        overallScore: 78,
        harshBrakingCount: 12,
        harshAccelerationCount: 8,
        speedingCount: 5,
        vehicleConsumption: [
          {
            vehicleId: 'veh1',
            vehicleName: 'Camion Transport 01',
            vehiclePlate: 'AB-1234-CD',
            kmDriven: 2450,
            fuelConsumed: 318.5,
            avgConsumption: 13.0,
            vehicleAvgConsumption: 12.5,
            trips: 45
          },
          {
            vehicleId: 'veh2',
            vehicleName: 'Utilitaire 01',
            vehiclePlate: 'EF-5678-GH',
            kmDriven: 580,
            fuelConsumed: 46.4,
            avgConsumption: 8.0,
            vehicleAvgConsumption: 7.5,
            trips: 12
          }
        ],
        totalKmDriven: 3030,
        totalTrips: 57,
        averageSpeed: 52
      },
      {
        id: 'ds2',
        employeeId: 'emp4',
        period: { from: new Date('2024-12-01'), to: new Date('2024-12-18') },
        overallScore: 92,
        harshBrakingCount: 3,
        harshAccelerationCount: 2,
        speedingCount: 1,
        vehicleConsumption: [
          {
            vehicleId: 'veh3',
            vehicleName: 'SUV Location 01',
            vehiclePlate: 'IJ-9012-KL',
            kmDriven: 1850,
            fuelConsumed: 148,
            avgConsumption: 8.0,
            vehicleAvgConsumption: 8.2,
            trips: 38
          }
        ],
        totalKmDriven: 1850,
        totalTrips: 38,
        averageSpeed: 48
      },
      {
        id: 'ds3',
        employeeId: 'emp3',
        period: { from: new Date('2024-12-01'), to: new Date('2024-12-18') },
        overallScore: 65,
        harshBrakingCount: 18,
        harshAccelerationCount: 15,
        speedingCount: 9,
        vehicleConsumption: [
          {
            vehicleId: 'veh1',
            vehicleName: 'Camion Transport 01',
            vehiclePlate: 'AB-1234-CD',
            kmDriven: 890,
            fuelConsumed: 124.6,
            avgConsumption: 14.0,
            vehicleAvgConsumption: 12.5,
            trips: 15
          }
        ],
        totalKmDriven: 890,
        totalTrips: 15,
        averageSpeed: 58
      }
    ];

    // Vehicle Trips for Replay feature
    const vehicleTrips: VehicleTrip[] = [
      {
        id: 'trip1',
        vehicleId: 'veh1',
        driverId: 'emp1',
        startTime: new Date('2024-12-17T08:00:00'),
        endTime: new Date('2024-12-17T10:30:00'),
        startLocation: { lat: 36.8065, lng: 10.1815 },
        endLocation: { lat: 36.7500, lng: 10.2200 },
        distance: 45.2,
        maxSpeed: 85,
        avgSpeed: 42,
        fuelConsumed: 5.8,
        routePoints: [
          { lat: 36.8065, lng: 10.1815, speed: 0, timestamp: new Date('2024-12-17T08:00:00'), heading: 180 },
          { lat: 36.8050, lng: 10.1820, speed: 25, timestamp: new Date('2024-12-17T08:05:00'), heading: 175 },
          { lat: 36.8000, lng: 10.1850, speed: 45, timestamp: new Date('2024-12-17T08:15:00'), heading: 160 },
          { lat: 36.7900, lng: 10.1900, speed: 60, timestamp: new Date('2024-12-17T08:30:00'), heading: 145 },
          { lat: 36.7800, lng: 10.2000, speed: 55, timestamp: new Date('2024-12-17T08:45:00'), heading: 130 },
          { lat: 36.7700, lng: 10.2100, speed: 70, timestamp: new Date('2024-12-17T09:00:00'), heading: 120 },
          { lat: 36.7600, lng: 10.2150, speed: 85, timestamp: new Date('2024-12-17T09:15:00'), heading: 110 },
          { lat: 36.7550, lng: 10.2180, speed: 50, timestamp: new Date('2024-12-17T09:30:00'), heading: 100 },
          { lat: 36.7520, lng: 10.2190, speed: 30, timestamp: new Date('2024-12-17T10:00:00'), heading: 95 },
          { lat: 36.7500, lng: 10.2200, speed: 0, timestamp: new Date('2024-12-17T10:30:00'), heading: 90 }
        ]
      },
      {
        id: 'trip2',
        vehicleId: 'veh1',
        driverId: 'emp1',
        startTime: new Date('2024-12-17T14:00:00'),
        endTime: new Date('2024-12-17T16:15:00'),
        startLocation: { lat: 36.7500, lng: 10.2200 },
        endLocation: { lat: 36.8065, lng: 10.1815 },
        distance: 42.8,
        maxSpeed: 75,
        avgSpeed: 38,
        fuelConsumed: 5.2,
        routePoints: [
          { lat: 36.7500, lng: 10.2200, speed: 0, timestamp: new Date('2024-12-17T14:00:00'), heading: 0 },
          { lat: 36.7550, lng: 10.2150, speed: 30, timestamp: new Date('2024-12-17T14:10:00'), heading: 320 },
          { lat: 36.7650, lng: 10.2100, speed: 55, timestamp: new Date('2024-12-17T14:25:00'), heading: 310 },
          { lat: 36.7750, lng: 10.2000, speed: 65, timestamp: new Date('2024-12-17T14:45:00'), heading: 300 },
          { lat: 36.7850, lng: 10.1900, speed: 75, timestamp: new Date('2024-12-17T15:00:00'), heading: 340 },
          { lat: 36.7950, lng: 10.1850, speed: 50, timestamp: new Date('2024-12-17T15:20:00'), heading: 355 },
          { lat: 36.8020, lng: 10.1830, speed: 35, timestamp: new Date('2024-12-17T15:45:00'), heading: 5 },
          { lat: 36.8065, lng: 10.1815, speed: 0, timestamp: new Date('2024-12-17T16:15:00'), heading: 0 }
        ]
      },
      {
        id: 'trip3',
        vehicleId: 'veh3',
        driverId: 'emp4',
        startTime: new Date('2024-12-17T09:00:00'),
        endTime: new Date('2024-12-17T11:00:00'),
        startLocation: { lat: 36.8500, lng: 10.3200 },
        endLocation: { lat: 36.8800, lng: 10.2500 },
        distance: 28.5,
        maxSpeed: 70,
        avgSpeed: 35,
        fuelConsumed: 2.3,
        routePoints: [
          { lat: 36.8500, lng: 10.3200, speed: 0, timestamp: new Date('2024-12-17T09:00:00'), heading: 270 },
          { lat: 36.8520, lng: 10.3100, speed: 40, timestamp: new Date('2024-12-17T09:15:00'), heading: 280 },
          { lat: 36.8580, lng: 10.2900, speed: 55, timestamp: new Date('2024-12-17T09:35:00'), heading: 290 },
          { lat: 36.8650, lng: 10.2700, speed: 70, timestamp: new Date('2024-12-17T09:50:00'), heading: 300 },
          { lat: 36.8720, lng: 10.2600, speed: 45, timestamp: new Date('2024-12-17T10:15:00'), heading: 315 },
          { lat: 36.8780, lng: 10.2530, speed: 25, timestamp: new Date('2024-12-17T10:40:00'), heading: 330 },
          { lat: 36.8800, lng: 10.2500, speed: 0, timestamp: new Date('2024-12-17T11:00:00'), heading: 0 }
        ]
      }
    ];

    this.subscriptions$.next(subscriptions);
    this.companies$.next(companies);
    this.vehicles$.next(vehicles);
    this.employees$.next(employees);
    this.gpsLocations$.next(gpsLocations);
    this.gpsAlerts$.next(gpsAlerts);
    this.rentalContracts$.next(rentalContracts);
    this.gpsDevices$.next(gpsDevices);
    this.maintenanceRecords$.next(maintenanceRecords);
    this.vehicleCosts$.next(vehicleCosts);
    this.geofences$.next(geofences);
    this.geofenceEvents$.next(geofenceEvents);
    this.driverScores$.next(driverScores);
    this.vehicleTrips$.next(vehicleTrips);
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
    // Check both mock user and real auth token from AuthService
    return this.currentUser$.value !== null || !!localStorage.getItem('auth_token');
  }

  getSubscriptions(): Observable<Subscription[]> {
    return this.subscriptions$.asObservable();
  }

  getCompanies(): Observable<Company[]> {
    return this.companies$.asObservable();
  }

  getCurrentCompany(): Company | null {
    // Try mock user first
    const user = this.currentUser$.value;
    if (user) {
      return this.companies$.value.find((c) => c.id === user.companyId) || null;
    }
    
    // Fallback: check real auth from localStorage
    const authUser = localStorage.getItem('auth_user');
    if (authUser) {
      try {
        const parsed = JSON.parse(authUser);
        // Return a mock company with the user's company info
        return {
          id: parsed.companyId?.toString() || '1',
          name: parsed.companyName || 'My Company',
          type: 'transport',
          subscriptionId: '1'
        } as Company;
      } catch (e) {
        console.error('Error parsing auth_user:', e);
      }
    }
    return null;
  }

  getVehicles(): Observable<Vehicle[]> {
    // Use asapScheduler to ensure async emission for proper Angular change detection
    return this.vehicles$.asObservable().pipe(observeOn(asapScheduler));
  }

  getVehiclesByCompany(companyId: string): Vehicle[] {
    return this.vehicles$.value.filter((v) => v.companyId === companyId);
  }

  getEmployees(): Observable<Employee[]> {
    return this.employees$.asObservable().pipe(observeOn(asapScheduler));
  }

  getEmployeesByCompany(companyId: string): Employee[] {
    return this.employees$.value.filter((e) => e.companyId === companyId);
  }

  getGPSLocations(): Observable<GPSLocation[]> {
    return this.gpsLocations$.asObservable().pipe(observeOn(asapScheduler));
  }

  getGPSAlerts(): Observable<GPSAlert[]> {
    return this.gpsAlerts$.asObservable().pipe(observeOn(asapScheduler));
  }

  getAlertsByCompany(companyId: string): GPSAlert[] {
    const companyVehicles = this.getVehiclesByCompany(companyId);
    const vehicleIds = companyVehicles.map(v => v.id);
    return this.gpsAlerts$.value.filter(a => vehicleIds.includes(a.vehicleId));
  }

  resolveAlert(alertId: string) {
    const alerts = this.gpsAlerts$.value.map(a =>
      a.id === alertId ? { ...a, resolved: true } : a
    );
    this.gpsAlerts$.next(alerts);
  }

  getRentalContracts(): Observable<RentalContract[]> {
    return this.rentalContracts$.asObservable().pipe(observeOn(asapScheduler));
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

  // GPS Devices
  getGPSDevices(): Observable<GPSDevice[]> {
    return this.gpsDevices$.asObservable().pipe(observeOn(asapScheduler));
  }

  getGPSDevicesByCompany(companyId: string): GPSDevice[] {
    return this.gpsDevices$.value.filter((d) => d.companyId === companyId);
  }

  getGPSDeviceByVehicle(vehicleId: string): GPSDevice | undefined {
    return this.gpsDevices$.value.find((d) => d.vehicleId === vehicleId);
  }

  addGPSDevice(device: GPSDevice) {
    const devices = [...this.gpsDevices$.value, device];
    this.gpsDevices$.next(devices);
  }

  updateGPSDevice(device: GPSDevice) {
    const devices = this.gpsDevices$.value.map((d) =>
      d.id === device.id ? device : d
    );
    this.gpsDevices$.next(devices);
  }

  deleteGPSDevice(id: string) {
    const devices = this.gpsDevices$.value.filter((d) => d.id !== id);
    this.gpsDevices$.next(devices);
  }

  // Maintenance Records
  getMaintenanceRecords(): Observable<MaintenanceRecord[]> {
    return this.maintenanceRecords$.asObservable().pipe(observeOn(asapScheduler));
  }

  getMaintenanceByCompany(companyId: string): MaintenanceRecord[] {
    return this.maintenanceRecords$.value.filter((m) => m.companyId === companyId);
  }

  getMaintenanceByVehicle(vehicleId: string): MaintenanceRecord[] {
    return this.maintenanceRecords$.value.filter((m) => m.vehicleId === vehicleId);
  }

  addMaintenanceRecord(record: MaintenanceRecord) {
    const records = [...this.maintenanceRecords$.value, record];
    this.maintenanceRecords$.next(records);
  }

  updateMaintenanceRecord(record: MaintenanceRecord) {
    const records = this.maintenanceRecords$.value.map((m) =>
      m.id === record.id ? record : m
    );
    this.maintenanceRecords$.next(records);
  }

  deleteMaintenanceRecord(id: string) {
    const records = this.maintenanceRecords$.value.filter((m) => m.id !== id);
    this.maintenanceRecords$.next(records);
  }

  // Vehicle Costs
  getVehicleCosts(): Observable<VehicleCost[]> {
    return this.vehicleCosts$.asObservable().pipe(observeOn(asapScheduler));
  }

  getVehicleCostsByCompany(companyId: string): VehicleCost[] {
    return this.vehicleCosts$.value.filter((c) => c.companyId === companyId);
  }

  getVehicleCostsByVehicle(vehicleId: string): VehicleCost[] {
    return this.vehicleCosts$.value.filter((c) => c.vehicleId === vehicleId);
  }

  addVehicleCost(cost: VehicleCost) {
    const newCost = { ...cost, id: 'cost' + Date.now() };
    const costs = [...this.vehicleCosts$.value, newCost];
    this.vehicleCosts$.next(costs);
  }

  updateVehicleCost(id: string, updates: Partial<VehicleCost>) {
    const costs = this.vehicleCosts$.value.map((c) =>
      c.id === id ? { ...c, ...updates } : c
    );
    this.vehicleCosts$.next(costs);
  }

  deleteVehicleCost(id: string) {
    const costs = this.vehicleCosts$.value.filter((c) => c.id !== id);
    this.vehicleCosts$.next(costs);
  }

  // Geofences
  getGeofences(): Observable<Geofence[]> {
    return this.geofences$.asObservable().pipe(observeOn(asapScheduler));
  }

  getGeofencesByCompany(companyId: string): Geofence[] {
    return this.geofences$.value.filter((g) => g.companyId === companyId);
  }

  getGeofenceById(id: string): Geofence | undefined {
    return this.geofences$.value.find((g) => g.id === id);
  }

  addGeofence(geofence: Geofence) {
    const newGeofence = { ...geofence, id: 'geo' + Date.now() };
    const geofences = [...this.geofences$.value, newGeofence];
    this.geofences$.next(geofences);
    return newGeofence;
  }

  updateGeofence(id: string, updates: Partial<Geofence>) {
    const geofences = this.geofences$.value.map((g) =>
      g.id === id ? { ...g, ...updates, updatedAt: new Date() } : g
    );
    this.geofences$.next(geofences);
  }

  deleteGeofence(id: string) {
    const geofences = this.geofences$.value.filter((g) => g.id !== id);
    this.geofences$.next(geofences);
  }

  // Geofence Events
  getGeofenceEvents(): Observable<GeofenceEvent[]> {
    return this.geofenceEvents$.asObservable().pipe(observeOn(asapScheduler));
  }

  getGeofenceEventsByCompany(companyId: string): GeofenceEvent[] {
    const companyGeofenceIds = this.geofences$.value
      .filter((g) => g.companyId === companyId)
      .map((g) => g.id);
    return this.geofenceEvents$.value.filter((e) =>
      companyGeofenceIds.includes(e.geofenceId)
    );
  }

  getGeofenceEventsByGeofence(geofenceId: string): GeofenceEvent[] {
    return this.geofenceEvents$.value.filter((e) => e.geofenceId === geofenceId);
  }

  // Driver Scores
  getDriverScores(): Observable<DriverScore[]> {
    return this.driverScores$.asObservable().pipe(observeOn(asapScheduler));
  }

  getDriverScoreByEmployee(employeeId: string): DriverScore | undefined {
    return this.driverScores$.value.find((ds) => ds.employeeId === employeeId);
  }

  getDriverScoresByDateRange(from: Date, to: Date): DriverScore[] {
    return this.driverScores$.value.filter((ds) => 
      ds.period.from >= from && ds.period.to <= to
    );
  }

  // Vehicle Trips
  getVehicleTrips(): Observable<VehicleTrip[]> {
    return this.vehicleTrips$.asObservable().pipe(observeOn(asapScheduler));
  }

  getVehicleTripsByVehicle(vehicleId: string): VehicleTrip[] {
    return this.vehicleTrips$.value.filter((t) => t.vehicleId === vehicleId);
  }

  getVehicleTripsByDriver(driverId: string): VehicleTrip[] {
    return this.vehicleTrips$.value.filter((t) => t.driverId === driverId);
  }

  getVehicleTripsByDateRange(vehicleId: string, from: Date, to: Date): VehicleTrip[] {
    return this.vehicleTrips$.value.filter((t) => 
      t.vehicleId === vehicleId && 
      t.startTime >= from && 
      t.endTime <= to
    );
  }

  getVehicleTrip(tripId: string): VehicleTrip | undefined {
    return this.vehicleTrips$.value.find((t) => t.id === tripId);
  }
}
