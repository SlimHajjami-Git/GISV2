/**
 * Contrat de l'espace chauffeur — miroir de services/GisAPI/Controllers/DriverAppController.cs
 * (ce fichier .NET fait foi). Le serveur sérialise en camelCase.
 */

export type TourStatus = 'planned' | 'in_progress' | 'completed' | 'cancelled' | string;
export type WaypointType = 'origin' | 'stop' | 'destination' | string;
export type WaypointStatus = 'pending' | 'completed' | 'skipped' | string;
/** Cadence du suivi par téléphone : « eco » quand le boîtier du véhicule est vivant, « full » sinon. */
export type TrackingMode = 'eco' | 'full';

/** GET /api/driver-app/me */
export interface DriverMe {
  driverId: number;
  firstName: string;
  lastName: string;
  company: { id: number; name: string } | null;
  assignedVehicle: { id: number; name: string; plate: string; hasGps: boolean } | null;
}

/** GET /api/driver-app/tours?scope=active|history (une tuile de la liste) */
export interface DriverTourSummary {
  id: number;
  name: string;
  status: TourStatus;
  scheduledStartTime: string;
  scheduledEndTime: string | null;
  actualStartTime: string | null;
  actualEndTime: string | null;
  sentAt: string | null;
  openedAt: string | null;
  vehicleName: string | null;
  vehiclePlate: string | null;
  vehicleHasGps: boolean;
  waypointCount: number;
  completedCount: number;
  nextWaypointName: string | null;
  origin: string | null;
  destination: string | null;
  estimatedDistanceKm: number | null;
  estimatedDurationMinutes: number | null;
}

export interface DriverWaypoint {
  id: number;
  sequenceOrder: number;
  name: string | null;
  address: string | null;
  latitude: number;
  longitude: number;
  type: WaypointType;
  estimatedArrivalTime: string | null;
  plannedPauseMinutes: number | null;
  isCompleted: boolean;
  waypointStatus: WaypointStatus;
  actualArrivalTime: string | null;
  arrivalSource: string | null;
  driverArrivedAt: string | null;
  driverDepartedAt: string | null;
}

/** GET /api/driver-app/tours/{id} */
export interface DriverTourDetail {
  id: number;
  name: string;
  description: string | null;
  status: TourStatus;
  scheduledStartTime: string;
  scheduledEndTime: string | null;
  actualStartTime: string | null;
  actualEndTime: string | null;
  sentAt: string | null;
  openedAt: string | null;
  notes: string | null;
  vehicleName: string | null;
  vehiclePlate: string | null;
  vehicleHasGps: boolean;
  estimatedDistanceKm: number | null;
  estimatedDurationMinutes: number | null;
  /** Polyline encodée (précision 6, format Valhalla) ; absente sur les vieilles tournées. */
  estimatedRoutePolyline: string | null;
  tracking: boolean;
  waypoints: DriverWaypoint[];
}

/** Corps de POST …/waypoints/{wid}/depart et /arrive (DriverEventRequest). */
export interface DriverEventRequest {
  clientTime?: string;
  latitude?: number;
  longitude?: number;
  accuracyM?: number;
  /** Arrivée à destination : oui, marquer « non visitées » les étapes restantes. */
  confirmSkipPending?: boolean;
}

/** Réponse de depart / arrive. */
export interface DriverEventResponse {
  tourStatus: TourStatus;
  actualStartTime?: string | null;
  tracking: boolean;
  mode: TrackingMode;
  waypoint: DriverWaypoint;
  warning?: string | null;
}

/** Codes d'erreur du serveur (409 / 403). */
export const DRIVER_ERR_PENDING_STOPS = 'PENDING_STOPS';
export const DRIVER_ERR_TOO_FAR = 'TOO_FAR';
export const DRIVER_ERR_NO_PROFILE = 'NO_DRIVER_PROFILE';

export interface PendingStopsConflict {
  code: typeof DRIVER_ERR_PENDING_STOPS;
  pending: { id: number; name: string }[];
  message: string;
}

export interface TooFarConflict {
  code: typeof DRIVER_ERR_TOO_FAR;
  distanceM: number;
  message: string;
}

/** Un point du téléphone (PhonePoint). */
export interface PhonePoint {
  recordedAt: string;
  latitude: number;
  longitude: number;
  accuracyM?: number | null;
  speedKph?: number | null;
  heading?: number | null;
  isMocked: boolean;
}

/** POST /api/driver-app/positions */
export interface PhonePositionsRequest {
  points: PhonePoint[];
  sentAt: string;
  batteryLevel?: number | null;
}

export interface PhonePositionsResponse {
  tracking: boolean;
  mode: TrackingMode;
  activeTourId: number | null;
  accepted: number;
}
