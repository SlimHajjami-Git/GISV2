export interface VehicleGroup {
  id: string;
  company_id: string;
  name: string;
  description?: string;
  color: string;
  icon: string;
  created_at: string;
  vehicles?: Vehicle[];
}

export interface VehicleGroupMember {
  id: string;
  group_id: string;
  vehicle_id: string;
  created_at: string;
}

export interface Geofence {
  id: string;
  company_id: string;
  name: string;
  description?: string;
  type: 'circle' | 'polygon' | 'route';
  geometry: GeoJSON;
  color: string;
  is_active: boolean;
  created_at: string;
}

export interface GeoJSON {
  type: string;
  coordinates: number[] | number[][] | number[][][];
  radius?: number;
}

export interface GeofenceEvent {
  id: string;
  vehicle_id: string;
  geofence_id: string;
  event_type: 'enter' | 'exit';
  timestamp: string;
  created_at: string;
  vehicle?: any;
  geofence?: Geofence;
}

export interface VehicleSensor {
  id: string;
  vehicle_id: string;
  sensor_type: 'fuel' | 'temperature' | 'engine' | 'door' | 'ignition' | 'battery' | 'pressure';
  name: string;
  unit?: string;
  min_value?: number;
  max_value?: number;
  created_at: string;
}

export interface SensorData {
  id: string;
  sensor_id: string;
  value: number;
  timestamp: string;
  created_at: string;
  sensor?: VehicleSensor;
}

export interface Trip {
  id: string;
  vehicle_id: string;
  start_location?: LocationData;
  end_location?: LocationData;
  start_time: string;
  end_time?: string;
  distance: number;
  duration: number;
  max_speed: number;
  avg_speed: number;
  fuel_consumed: number;
  idle_time: number;
  created_at: string;
  vehicle?: any;
  stops?: Stop[];
}

export interface LocationData {
  lat: number;
  lng: number;
  address?: string;
}

export interface Stop {
  id: string;
  trip_id: string;
  vehicle_id: string;
  location?: LocationData;
  start_time: string;
  end_time?: string;
  duration: number;
  created_at: string;
}

export interface ReportTemplate {
  id: string;
  company_id: string;
  name: string;
  type: 'trips' | 'fuel' | 'speed' | 'stops' | 'geofence' | 'custom';
  configuration: ReportConfiguration;
  is_default: boolean;
  created_at: string;
}

export interface ReportConfiguration {
  columns: string[];
  groupBy?: string;
  filters?: Record<string, any>;
  charts?: ChartConfiguration[];
}

export interface ChartConfiguration {
  type: 'line' | 'bar' | 'pie' | 'doughnut';
  dataKey: string;
  label: string;
}

export interface GeneratedReport {
  id: string;
  company_id: string;
  template_id?: string;
  name: string;
  period_start: string;
  period_end: string;
  data: any;
  status: 'generating' | 'completed' | 'failed';
  created_at: string;
  template?: ReportTemplate;
}

export interface Vehicle {
  id: string;
  company_id: string;
  type_id: string;
  registration_number: string;
  brand?: string;
  model?: string;
  year?: number;
  vin?: string;
  color?: string;
  mileage: number;
  fuel_type?: 'gasoline' | 'diesel' | 'electric' | 'hybrid';
  status: 'available' | 'in_use' | 'maintenance' | 'rented';
  gps_device_id?: string;
  created_at: string;
  updated_at: string;
  type?: any;
  currentLocation?: LocationData;
  currentSpeed?: number;
  isOnline?: boolean;
}
