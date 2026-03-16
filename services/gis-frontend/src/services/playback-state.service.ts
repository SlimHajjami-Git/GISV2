import { Injectable } from '@angular/core';

export interface PlaybackTimelineEntry {
  type: 'trip' | 'stop';
  startIndex: number;
  endIndex: number;
  startTime: Date;
  endTime: Date;
  distance: number;     // meters (trips only)
  durationMs: number;
}

export interface PlaybackSavedState {
  vehicleId: string;
  vehiclePlate: string;
  fromDate: string;
  toDate: string;
  positions: any[];
  index: number;
  progress: number;
  speed: number;
  isLoaded: boolean;
  timeline: PlaybackTimelineEntry[];
  matchedRouteCoords: { lat: number; lng: number }[];
  segmentBoundaries: number[];
  dataGaps: number[];
  stopMarkerCount: number;
}

@Injectable({ providedIn: 'root' })
export class PlaybackStateService {
  private state: PlaybackSavedState | null = null;

  save(state: PlaybackSavedState): void {
    this.state = state;
  }

  restore(): PlaybackSavedState | null {
    return this.state;
  }

  clear(): void {
    this.state = null;
  }

  hasState(): boolean {
    return !!this.state;
  }
}
