export interface User {
  id: string;
  email: string;
  name: string;
  companyId: string;
  role: 'admin' | 'manager' | 'employee';
}

export interface Company {
  id: string;
  name: string;
  type: 'location' | 'transport' | 'other';
  subscriptionId: string;
  maxVehicles: number;
  createdAt: Date;
}

export interface Subscription {
  id: string;
  name: string;
  type: 'parc' | 'parc_gps' | 'parc_gps_install';
  price: number;
  features: string[];
  gpsTracking: boolean;
  gpsInstallation: boolean;
}

export interface Vehicle {
  id: string;
  companyId: string;
  name: string;
  type: 'camion' | 'citadine' | 'suv' | 'utilitaire' | 'other';
  brand: string;
  model: string;
  plate: string;
  year: number;
  status: 'available' | 'in_use' | 'maintenance';
  hasGPS: boolean;
  assignedDriverId?: string;
  assignedSupervisorId?: string;
  documents: VehicleDocument[];
  mileage: number;
  rentalMileage?: number;
  registration_number?: string;
  currentSpeed?: number;
  currentLocation?: { lat: number; lng: number };
  isOnline?: boolean;
}

export interface VehicleDocument {
  id: string;
  vehicleId: string;
  type: 'registration' | 'insurance' | 'customs' | 'maintenance';
  name: string;
  expiryDate?: Date;
  fileUrl?: string;
}

export interface Employee {
  id: string;
  companyId: string;
  name: string;
  email: string;
  phone: string;
  role: 'driver' | 'accountant' | 'hr' | 'supervisor' | 'other';
  assignedVehicles: string[];
  status: 'active' | 'inactive';
  hireDate: Date;
}

export interface GPSLocation {
  vehicleId: string;
  latitude: number;
  longitude: number;
  speed: number;
  direction: number;
  timestamp: Date;
  address?: string;
}

export interface GPSAlert {
  id: string;
  vehicleId: string;
  type: 'speeding' | 'stopped' | 'geofence' | 'other';
  message: string;
  timestamp: Date;
  resolved: boolean;
}

export interface RentalContract {
  id: string;
  vehicleId: string;
  customerName: string;
  customerContact: string;
  startDate: Date;
  endDate: Date;
  startMileage: number;
  endMileage?: number;
  status: 'active' | 'completed' | 'cancelled';
  price: number;
}
