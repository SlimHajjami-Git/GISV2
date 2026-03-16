import { Injectable } from '@angular/core';

export interface ReportFilterState {
  selectedTemplateId: string;
  selectedVehicleId: string;
  selectedDriverId: string;
  selectedDepartmentId: string;
  selectedVehicleIds: string[];
  filterByVehicle: boolean;
  filterByDriver: boolean;
  filterByDepartment: boolean;
  selectedStandardPeriod: string;
  selectedCostPeriod: string;
  customStartDate: string;
  customEndDate: string;
  dailyReportDate: string;
  speedLimit: number;
  selectedMileagePeriodType: string;
  mileagePeriodDate: string;
  mileagePeriodStartDate: string;
  mileagePeriodEndDate: string;
  mileagePeriodMonth: number;
  mileagePeriodYear: number;
  selectedMonthlyYear: number;
  selectedMonthlyMonth: number;
  reportGenerated: boolean;
  activeTab: string;
  expandedSections: { [key: string]: boolean };
  drivingBehaviorFilters: { [key: string]: boolean };
  aiFleetPeriod: string;
}

const STORAGE_KEY = 'gisv2_report_filters';

@Injectable({ providedIn: 'root' })
export class ReportStateService {
  private state: ReportFilterState | null = null;

  save(state: ReportFilterState): void {
    this.state = { ...state };
    try {
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(this.state));
    } catch (_) { /* quota exceeded – in-memory fallback is fine */ }
  }

  restore(): ReportFilterState | null {
    if (this.state) return this.state;
    try {
      const raw = sessionStorage.getItem(STORAGE_KEY);
      if (raw) {
        this.state = JSON.parse(raw);
        return this.state;
      }
    } catch (_) { /* corrupted data */ }
    return null;
  }

  clear(): void {
    this.state = null;
    try { sessionStorage.removeItem(STORAGE_KEY); } catch (_) {}
  }

  hasState(): boolean {
    return !!this.state || !!sessionStorage.getItem(STORAGE_KEY);
  }
}
