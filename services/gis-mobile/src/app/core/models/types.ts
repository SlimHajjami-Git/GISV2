// Reused from gis-frontend/src/models/types.ts — adapted for mobile

export interface User {
  id: string;
  email: string;
  name: string;
  companyId: string;
  role: 'admin' | 'manager' | 'employee';
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
  mileage: number;
  fuelTankCapacity?: number;
  currentSpeed?: number;
  currentLocation?: { lat: number; lng: number };
  isOnline?: boolean;
  ignitionOn?: boolean;
  lastAddress?: string;
  /** Horodatage de la dernière position connue (SignalR ou REST) — sert à
   *  dater la position dans le partage pour ne pas la présenter comme live. */
  lastRecordedAt?: string;
  gpsDeviceId?: string;
  gpsImei?: string;
  // Nested GPS device object as returned by the backend GetVehicles query.
  // `gpsDeviceId` above is flattened from this at load time (see
  // vehicles.page.ts loadVehicles) so template *ngIf checks stay simple.
  gpsDevice?: {
    id: number;
    deviceUid?: string;
    label?: string;
    status?: string;
    lastCommunication?: string;
  };
  stats?: {
    fuelLevel?: number;
    batteryLevel?: number;
    temperature?: number;
  };
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
  type: 'speeding' | 'stopped' | 'geofence' | 'maintenance' | 'other';
  message: string;
  timestamp: Date;
  resolved: boolean;
}

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
  coordinates?: GeofencePoint[];
  center?: GeofencePoint;
  radius?: number;
  alertOnEntry: boolean;
  alertOnExit: boolean;
  isActive: boolean;
}

export interface VehicleTrip {
  id: string;
  vehicleId: string;
  startTime: Date;
  endTime: Date;
  startLocation: GeofencePoint;
  endLocation: GeofencePoint;
  distance: number;
  maxSpeed: number;
  avgSpeed: number;
  fuelConsumed?: number;
  routePoints: TripPoint[];
}

export interface TripPoint {
  lat: number;
  lng: number;
  speed: number;
  timestamp: Date;
  heading?: number;
}

export interface DashboardStats {
  totalVehicles: number;
  activeVehicles: number;
  inMaintenanceVehicles: number;
  totalDistanceToday: number;
  activeAlerts: number;
  overdueMaintenances: number;
}

export interface Notification {
  id: number;
  type: string;
  title: string;
  message: string;
  priority: string;
  referenceType?: string;
  referenceId?: number;
  actionUrl?: string;
  metadata?: any;
  isRead: boolean;
  createdAt: string;
}
