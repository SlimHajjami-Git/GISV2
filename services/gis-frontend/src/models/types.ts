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
  type: string;
  brand: string;
  model: string;
  plate: string;
  year: number;
  color?: string;
  status: 'available' | 'in_use' | 'maintenance';
  hasGPS: boolean;
  assignedDriverId?: string;
  assignedDriverName?: string;
  assignedSupervisorId?: string;
  documents?: VehicleDocument[];
  mileage: number;
  rentalMileage?: number;
  registration_number?: string;
  currentSpeed?: number;
  currentLocation?: { lat: number; lng: number };
  isOnline?: boolean;
  
  // GPS Device info (embedded when hasGPS is true)
  gpsDeviceId?: string;
  gpsImei?: string;
  gpsSimNumber?: string;
  gpsSimOperator?: 'ooredoo' | 'orange_tunisie' | 'tunisie_telecom' | 'other';
  gpsModel?: string;
  gpsBrand?: string;
  gpsInstallationDate?: Date;
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

// GPS Device attached to a vehicle
export interface GPSDevice {
  id: string;
  vehicleId?: string;
  companyId: string;
  imei: string;
  simNumber: string;
  simOperator: 'orange' | 'inwi' | 'maroc_telecom' | 'ooredoo_tunisie' | 'orange_tunisie' | 'tunisie_telecom' | 'other';
  model: string;
  brand: string;
  firmwareVersion?: string;
  installationDate?: Date;
  status: 'active' | 'inactive' | 'maintenance' | 'unassigned';
  lastCommunication?: Date;
  batteryLevel?: number;
  signalStrength?: number;
}

// Vehicle Maintenance & Repairs
export interface MaintenanceRecord {
  id: string;
  vehicleId: string;
  companyId: string;
  type: 'scheduled' | 'repair' | 'inspection' | 'tire_change' | 'oil_change' | 'other';
  description: string;
  mileageAtService: number;
  date: Date;
  nextServiceDate?: Date;
  nextServiceMileage?: number;
  status: 'scheduled' | 'in_progress' | 'completed' | 'cancelled';
  
  // Cost details
  laborCost: number;
  partsCost: number;
  totalCost: number;
  
  // Provider info
  serviceProvider?: string;
  providerContact?: string;
  invoiceNumber?: string;
  invoiceUrl?: string;
  
  // Parts used
  parts?: MaintenancePart[];
  notes?: string;
}

export interface MaintenancePart {
  id: string;
  name: string;
  partNumber?: string;
  quantity: number;
  unitCost: number;
  totalCost: number;
}

// Vehicle cost tracking
export interface VehicleCost {
  id: string;
  vehicleId: string;
  companyId: string;
  type: 'fuel' | 'maintenance' | 'insurance' | 'tax' | 'toll' | 'parking' | 'fine' | 'other';
  description: string;
  amount: number;
  date: Date;
  mileage?: number;
  receiptNumber?: string;
  receiptUrl?: string;
  
  // Fuel specific
  fuelType?: 'diesel' | 'gasoline' | 'electric';
  liters?: number;
  pricePerLiter?: number;
  
  createdBy?: string;
}

// Geofence types
export interface GeofencePoint {
  lat: number;
  lng: number;
}

export interface Geofence {
  id: string;
  companyId: string;
  name: string;
  description?: string;
  type: 'polygon' | 'circle';
  color: string;
  
  // For polygon type
  coordinates?: GeofencePoint[];
  
  // For circle type
  center?: GeofencePoint;
  radius?: number; // in meters
  
  // Alert settings
  alertOnEntry: boolean;
  alertOnExit: boolean;
  alertSpeed?: number; // Speed limit inside geofence
  
  // Assignment
  assignedVehicleIds?: string[];
  
  // Status
  isActive: boolean;
  createdAt: Date;
  updatedAt?: Date;
}

export interface GeofenceEvent {
  id: string;
  geofenceId: string;
  vehicleId: string;
  vehicleName?: string;
  type: 'entry' | 'exit' | 'speed_violation';
  timestamp: Date;
  location: GeofencePoint;
  latitude?: number;
  longitude?: number;
  address?: string;
  speed?: number;
  durationInsideSeconds?: number;
  isNotified?: boolean;
}

export interface GeofenceGroup {
  id: number;
  name: string;
  description?: string;
  color: string;
  iconName?: string;
  companyId: number;
  geofenceCount?: number;
  createdAt?: Date;
  updatedAt?: Date;
}

// Driver Score & Driving Behavior
export interface DriverScore {
  id: string;
  employeeId: string;
  period: { from: Date; to: Date };
  overallScore: number; // 0-100
  
  // Behavior metrics
  harshBrakingCount: number;
  harshAccelerationCount: number;
  speedingCount: number;
  
  // Consumption data per vehicle
  vehicleConsumption: VehicleConsumptionData[];
  
  // Totals
  totalKmDriven: number;
  totalTrips: number;
  averageSpeed: number;
}

export interface VehicleConsumptionData {
  vehicleId: string;
  vehicleName: string;
  vehiclePlate: string;
  kmDriven: number;
  fuelConsumed: number; // liters
  avgConsumption: number; // L/100km - driver's consumption
  vehicleAvgConsumption: number; // L/100km - vehicle's standard consumption
  trips: number;
}

// Vehicle Trip for Replay
export interface VehicleTrip {
  id: string;
  vehicleId: string;
  driverId?: string;
  startTime: Date;
  endTime: Date;
  startLocation: GeofencePoint;
  endLocation: GeofencePoint;
  distance: number; // km
  maxSpeed: number;
  avgSpeed: number;
  fuelConsumed?: number;
  
  // Route points for replay
  routePoints: TripPoint[];
}

export interface TripPoint {
  lat: number;
  lng: number;
  speed: number;
  timestamp: Date;
  heading?: number;
}
