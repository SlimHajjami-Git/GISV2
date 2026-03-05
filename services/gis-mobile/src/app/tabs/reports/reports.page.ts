import { Component, OnInit } from '@angular/core';
import { ApiService } from '../../core/services/api.service';
import { Vehicle } from '../../core/models/types';
import { ActionSheetController, LoadingController } from '@ionic/angular';

@Component({
  selector: 'app-reports',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Rapports</ion-title>
      </ion-toolbar>
      <ion-toolbar>
        <ion-segment [(ngModel)]="activeTab" (ionChange)="onTabChange()">
          <ion-segment-button value="daily">
            <ion-label>Activité</ion-label>
          </ion-segment-button>
          <ion-segment-button value="mileage">
            <ion-label>Kilométrage</ion-label>
          </ion-segment-button>
          <ion-segment-button value="trips">
            <ion-label>Trajets</ion-label>
          </ion-segment-button>
          <ion-segment-button value="monthly">
            <ion-label>Mensuel</ion-label>
          </ion-segment-button>
        </ion-segment>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true">
      <!-- Date & vehicle filters -->
      <div class="filters-bar">
        <ion-item lines="none" class="filter-item" (click)="pickDate()">
          <ion-icon name="calendar-outline" slot="start" color="primary"></ion-icon>
          <ion-label>
            <p class="filter-label">Date</p>
            <h3>{{ displayDate }}</h3>
          </ion-label>
        </ion-item>
        <ion-item lines="none" class="filter-item" (click)="pickVehicle()" *ngIf="activeTab !== 'monthly'">
          <ion-icon name="car-sport-outline" slot="start" color="primary"></ion-icon>
          <ion-label>
            <p class="filter-label">Véhicule</p>
            <h3>{{ selectedVehicleName }}</h3>
          </ion-label>
        </ion-item>
      </div>

      <!-- Hidden date input -->
      <ion-datetime
        #datePicker
        [presentation]="activeTab === 'monthly' ? 'month-year' : 'date'"
        [(ngModel)]="selectedDate"
        (ionChange)="onDateChange()"
        [style.display]="showDatePicker ? 'block' : 'none'"
        class="date-picker"
        [max]="todayStr"
      ></ion-datetime>

      <ion-spinner *ngIf="loading" name="crescent" class="center-spinner"></ion-spinner>

      <!-- ═══════════ DAILY ACTIVITY ═══════════ -->
      <div *ngIf="activeTab === 'daily' && !loading">
        <div class="empty-state" *ngIf="dailyReports.length === 0">
          <ion-icon name="document-text-outline"></ion-icon>
          <p>Aucune activité pour cette date</p>
        </div>

        <ion-card *ngFor="let r of dailyReports" class="report-card">
          <ion-card-header>
            <ion-card-title class="vehicle-title">
              <ion-icon name="car-sport" color="primary"></ion-icon>
              {{ r.vehicleName }}
            </ion-card-title>
            <ion-card-subtitle>{{ r.plate }} &middot; {{ r.date | date:'dd/MM/yyyy' }}</ion-card-subtitle>
          </ion-card-header>
          <ion-card-content>
            <div class="kpi-grid">
              <div class="kpi-item">
                <span class="kpi-value">{{ r.totalDistanceKm | number:'1.1-1' }}</span>
                <span class="kpi-label">km parcourus</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ r.maxSpeedKph | number:'1.0-0' }}</span>
                <span class="kpi-label">km/h max</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ r.tripCount || r.driveCount || 0 }}</span>
                <span class="kpi-label">trajets</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ formatDuration(r.totalDriveDurationMinutes || r.driveDurationMinutes) }}</span>
                <span class="kpi-label">conduite</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ formatDuration(r.totalStopDurationMinutes || r.stopDurationMinutes) }}</span>
                <span class="kpi-label">arrêt</span>
              </div>
              <div class="kpi-item" *ngIf="r.totalIdleDurationMinutes || r.idleDurationMinutes">
                <span class="kpi-value">{{ formatDuration(r.totalIdleDurationMinutes || r.idleDurationMinutes) }}</span>
                <span class="kpi-label">ralenti</span>
              </div>
            </div>

            <!-- Timeline events -->
            <div class="timeline" *ngIf="r.events?.length || r.segments?.length">
              <div class="timeline-title">
                <ion-icon name="time-outline"></ion-icon>
                Chronologie ({{ (r.events || r.segments)?.length }} événements)
              </div>
              <div class="timeline-event" *ngFor="let e of (r.events || r.segments)?.slice(0, 8)">
                <div class="event-dot" [ngClass]="getEventClass(e)"></div>
                <div class="event-content">
                  <span class="event-time">{{ formatEventTime(e.startTime || e.timestamp) }}</span>
                  <span class="event-label">{{ getEventLabel(e) }}</span>
                </div>
              </div>
              <div class="timeline-more" *ngIf="(r.events || r.segments)?.length > 8">
                +{{ (r.events || r.segments).length - 8 }} autres événements
              </div>
            </div>
          </ion-card-content>
        </ion-card>
      </div>

      <!-- ═══════════ MILEAGE ═══════════ -->
      <div *ngIf="activeTab === 'mileage' && !loading">
        <div class="empty-state" *ngIf="mileageReports.length === 0">
          <ion-icon name="speedometer-outline"></ion-icon>
          <p>Aucun rapport kilométrique</p>
        </div>

        <ion-card *ngFor="let r of mileageReports" class="report-card">
          <ion-card-header>
            <ion-card-title class="vehicle-title">
              <ion-icon name="car-sport" color="primary"></ion-icon>
              {{ r.vehicleName }}
            </ion-card-title>
            <ion-card-subtitle>{{ r.plate }}</ion-card-subtitle>
          </ion-card-header>
          <ion-card-content>
            <div class="kpi-grid">
              <div class="kpi-item highlight">
                <span class="kpi-value">{{ r.totalDistanceKm | number:'1.1-1' }}</span>
                <span class="kpi-label">km total</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ r.averageDailyKm | number:'1.0-0' }}</span>
                <span class="kpi-label">km/jour moy.</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ r.daysWithActivity || r.activeDays || 0 }}</span>
                <span class="kpi-label">jours actifs</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ r.maxDailyKm | number:'1.0-0' }}</span>
                <span class="kpi-label">km max/jour</span>
              </div>
            </div>

            <!-- Daily breakdown chart (simplified bar chart) -->
            <div class="mini-chart" *ngIf="r.dailyBreakdown?.length">
              <div class="chart-title">Distance journalière</div>
              <div class="bar-chart">
                <div class="bar-item" *ngFor="let d of r.dailyBreakdown.slice(-14)"
                     [title]="(d.date | date:'dd/MM') + ': ' + (d.distanceKm | number:'1.1-1') + ' km'">
                  <div class="bar" [style.height.%]="getBarHeight(d.distanceKm, r.maxDailyKm)"></div>
                  <span class="bar-label">{{ d.date | date:'dd' }}</span>
                </div>
              </div>
            </div>
          </ion-card-content>
        </ion-card>
      </div>

      <!-- ═══════════ TRIPS ═══════════ -->
      <div *ngIf="activeTab === 'trips' && !loading">
        <!-- Summary card -->
        <ion-card class="summary-card" *ngIf="tripsSummary">
          <ion-card-content>
            <div class="kpi-grid">
              <div class="kpi-item highlight">
                <span class="kpi-value">{{ tripsSummary.totalTrips || trips.length }}</span>
                <span class="kpi-label">trajets</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ tripsSummary.totalDistanceKm | number:'1.1-1' }}</span>
                <span class="kpi-label">km total</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ tripsSummary.averageSpeedKph | number:'1.0-0' }}</span>
                <span class="kpi-label">km/h moy.</span>
              </div>
              <div class="kpi-item">
                <span class="kpi-value">{{ formatDuration(tripsSummary.totalDurationMinutes) }}</span>
                <span class="kpi-label">durée totale</span>
              </div>
            </div>
          </ion-card-content>
        </ion-card>

        <div class="empty-state" *ngIf="trips.length === 0">
          <ion-icon name="navigate-outline"></ion-icon>
          <p>Aucun trajet pour cette période</p>
        </div>

        <ion-list>
          <ion-item *ngFor="let t of trips; trackBy: trackTripById" detail>
            <div slot="start" class="trip-icon">
              <ion-icon name="navigate" color="primary"></ion-icon>
            </div>
            <ion-label>
              <h2 class="trip-vehicle">{{ t.vehicleName || t.vehicle?.name || 'Véhicule' }}</h2>
              <p class="trip-time">
                <ion-icon name="time-outline"></ion-icon>
                {{ t.startTime | date:'HH:mm' }} → {{ t.endTime | date:'HH:mm' }}
              </p>
              <p class="trip-details">
                {{ t.distanceKm | number:'1.1-1' }} km &middot;
                {{ formatDuration(t.durationMinutes) }} &middot;
                Max {{ t.maxSpeedKph | number:'1.0-0' }} km/h
              </p>
              <p class="trip-address" *ngIf="t.startAddress || t.endAddress">
                <ion-icon name="location-outline"></ion-icon>
                {{ t.startAddress || '...' }} → {{ t.endAddress || '...' }}
              </p>
            </ion-label>
            <ion-note slot="end" class="trip-distance">
              <span class="dist-value">{{ t.distanceKm | number:'1.0-0' }}</span>
              <span class="dist-unit">km</span>
            </ion-note>
          </ion-item>
        </ion-list>
      </div>

      <!-- ═══════════ MONTHLY FLEET ═══════════ -->
      <div *ngIf="activeTab === 'monthly' && !loading">
        <div class="empty-state" *ngIf="!monthlyReport">
          <ion-icon name="bar-chart-outline"></ion-icon>
          <p>Aucun rapport mensuel disponible</p>
        </div>

        <div *ngIf="monthlyReport">
          <!-- Fleet overview -->
          <ion-card class="report-card">
            <ion-card-header>
              <ion-card-title class="section-title">
                <ion-icon name="analytics" color="primary"></ion-icon>
                Vue d'ensemble - {{ monthlyReport.reportMonth }}/{{ monthlyReport.reportYear }}
              </ion-card-title>
            </ion-card-header>
            <ion-card-content>
              <div class="kpi-grid">
                <div class="kpi-item highlight">
                  <span class="kpi-value">{{ monthlyReport.fleet?.totalVehicles || monthlyReport.totalVehicles || 0 }}</span>
                  <span class="kpi-label">véhicules</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.fleet?.totalDistanceKm || monthlyReport.totalDistanceKm || 0 | number:'1.0-0' }}</span>
                  <span class="kpi-label">km total</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.fleet?.totalTrips || monthlyReport.totalTrips || 0 }}</span>
                  <span class="kpi-label">trajets</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.fleet?.averageDailyKm || monthlyReport.avgDailyKm || 0 | number:'1.0-0' }}</span>
                  <span class="kpi-label">km/jour moy.</span>
                </div>
              </div>
            </ion-card-content>
          </ion-card>

          <!-- Utilization -->
          <ion-card class="report-card" *ngIf="monthlyReport.utilization">
            <ion-card-header>
              <ion-card-title class="section-title">
                <ion-icon name="pie-chart" color="tertiary"></ion-icon>
                Utilisation
              </ion-card-title>
            </ion-card-header>
            <ion-card-content>
              <div class="kpi-grid">
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.utilization.averageUtilizationPercent | number:'1.0-0' }}%</span>
                  <span class="kpi-label">utilisation moy.</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ formatDuration(monthlyReport.utilization.totalDriveHours * 60) }}</span>
                  <span class="kpi-label">conduite</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ formatDuration(monthlyReport.utilization.totalIdleHours * 60) }}</span>
                  <span class="kpi-label">ralenti</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.utilization.averageTripsPerDay | number:'1.0-0' }}</span>
                  <span class="kpi-label">trajets/jour</span>
                </div>
              </div>
            </ion-card-content>
          </ion-card>

          <!-- Fuel analytics -->
          <ion-card class="report-card" *ngIf="monthlyReport.fuel">
            <ion-card-header>
              <ion-card-title class="section-title">
                <ion-icon name="water" color="warning"></ion-icon>
                Carburant
              </ion-card-title>
            </ion-card-header>
            <ion-card-content>
              <div class="kpi-grid">
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.fuel.totalFuelCost | number:'1.0-0' }}</span>
                  <span class="kpi-label">DT dépensés</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.fuel.totalLiters | number:'1.0-0' }}</span>
                  <span class="kpi-label">litres</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.fuel.averageConsumptionPer100Km | number:'1.1-1' }}</span>
                  <span class="kpi-label">L/100km moy.</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.fuel.costPerKm | number:'1.2-2' }}</span>
                  <span class="kpi-label">DT/km</span>
                </div>
              </div>
            </ion-card-content>
          </ion-card>

          <!-- Costs -->
          <ion-card class="report-card" *ngIf="monthlyReport.costs">
            <ion-card-header>
              <ion-card-title class="section-title">
                <ion-icon name="cash" color="success"></ion-icon>
                Coûts
              </ion-card-title>
            </ion-card-header>
            <ion-card-content>
              <div class="kpi-grid">
                <div class="kpi-item highlight">
                  <span class="kpi-value">{{ monthlyReport.costs.totalCost | number:'1.0-0' }}</span>
                  <span class="kpi-label">DT total</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.costs.fuelCost | number:'1.0-0' }}</span>
                  <span class="kpi-label">DT carburant</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.costs.maintenanceCost | number:'1.0-0' }}</span>
                  <span class="kpi-label">DT maintenance</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.costs.costPerKm | number:'1.2-2' }}</span>
                  <span class="kpi-label">DT/km</span>
                </div>
              </div>
            </ion-card-content>
          </ion-card>

          <!-- Alerts summary -->
          <ion-card class="report-card" *ngIf="monthlyReport.alerts">
            <ion-card-header>
              <ion-card-title class="section-title">
                <ion-icon name="warning" color="danger"></ion-icon>
                Alertes du mois
              </ion-card-title>
            </ion-card-header>
            <ion-card-content>
              <div class="kpi-grid">
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.alerts.totalAlerts }}</span>
                  <span class="kpi-label">total alertes</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.alerts.speedingAlerts || 0 }}</span>
                  <span class="kpi-label">excès vitesse</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.alerts.geofenceAlerts || 0 }}</span>
                  <span class="kpi-label">géofence</span>
                </div>
                <div class="kpi-item">
                  <span class="kpi-value">{{ monthlyReport.alerts.maintenanceAlerts || 0 }}</span>
                  <span class="kpi-label">maintenance</span>
                </div>
              </div>
            </ion-card-content>
          </ion-card>
        </div>
      </div>
    </ion-content>
  `,
  styles: [`
    .filters-bar {
      display: flex; gap: 4px; padding: 8px 12px; background: var(--ion-card-background, #fff);
      border-bottom: 1px solid rgba(0,0,0,0.06);
    }
    .filter-item {
      flex: 1; --background: var(--ion-color-light); --border-radius: 10px;
      --min-height: 48px; cursor: pointer; font-size: 13px;
    }
    .filter-item ion-icon { font-size: 18px; margin-right: 6px; }
    .filter-label { font-size: 10px; color: var(--ion-color-medium); margin: 0; }
    .filter-item h3 { font-size: 13px; font-weight: 600; margin: 0; }
    .date-picker { margin: 0 auto; }
    .center-spinner { display: block; margin: 40px auto; }
    .empty-state { text-align: center; padding: 60px 20px; color: var(--ion-color-medium); }
    .empty-state ion-icon { font-size: 56px; display: block; margin: 0 auto 16px; }
    .empty-state p { font-size: 15px; }
    .report-card { margin: 12px; border-radius: 16px; }
    .summary-card { margin: 12px; border-radius: 16px; }
    .vehicle-title, .section-title {
      display: flex; align-items: center; gap: 8px; font-size: 16px;
    }
    .vehicle-title ion-icon, .section-title ion-icon { font-size: 20px; }
    .kpi-grid {
      display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px;
    }
    .kpi-item {
      text-align: center; padding: 10px 4px;
      background: var(--ion-color-light); border-radius: 10px;
    }
    .kpi-item.highlight { background: rgba(26,86,219,0.08); }
    .kpi-value { display: block; font-size: 18px; font-weight: 700; color: var(--ion-text-color); }
    .kpi-label { display: block; font-size: 10px; color: var(--ion-color-medium); margin-top: 2px; }
    .mini-chart { margin-top: 16px; }
    .chart-title { font-size: 12px; font-weight: 600; margin-bottom: 8px; color: var(--ion-color-medium); }
    .bar-chart {
      display: flex; align-items: flex-end; gap: 3px; height: 80px;
      padding: 0 4px; border-bottom: 1px solid var(--ion-color-light-shade);
    }
    .bar-item { flex: 1; display: flex; flex-direction: column; align-items: center; height: 100%; justify-content: flex-end; }
    .bar {
      width: 100%; min-height: 2px; max-height: 100%;
      background: linear-gradient(to top, var(--ion-color-primary), var(--ion-color-primary-tint));
      border-radius: 3px 3px 0 0;
    }
    .bar-label { font-size: 8px; color: var(--ion-color-medium); margin-top: 2px; }
    .timeline { margin-top: 16px; }
    .timeline-title {
      display: flex; align-items: center; gap: 6px;
      font-size: 12px; font-weight: 600; color: var(--ion-color-medium); margin-bottom: 10px;
    }
    .timeline-event {
      display: flex; align-items: center; gap: 10px;
      padding: 6px 0; border-left: 2px solid var(--ion-color-light-shade);
      margin-left: 6px; padding-left: 14px; position: relative;
    }
    .event-dot {
      position: absolute; left: -5px;
      width: 8px; height: 8px; border-radius: 50%;
    }
    .event-dot.drive { background: #10b981; }
    .event-dot.stop { background: #f59e0b; }
    .event-dot.idle { background: #9ca3af; }
    .event-dot.default { background: #6b7280; }
    .event-content { display: flex; gap: 8px; font-size: 12px; }
    .event-time { font-weight: 600; color: var(--ion-text-color); white-space: nowrap; }
    .event-label { color: var(--ion-color-medium); }
    .timeline-more { font-size: 11px; color: var(--ion-color-medium); margin-left: 20px; margin-top: 4px; }
    .trip-icon {
      width: 36px; height: 36px; border-radius: 50%;
      background: rgba(26,86,219,0.1); display: flex; align-items: center; justify-content: center;
    }
    .trip-icon ion-icon { font-size: 18px; }
    .trip-vehicle { font-weight: 600; font-size: 14px; }
    .trip-time { font-size: 12px; display: flex; align-items: center; gap: 4px; }
    .trip-time ion-icon { font-size: 12px; }
    .trip-details { font-size: 12px; color: var(--ion-color-medium); }
    .trip-address { font-size: 11px; color: var(--ion-color-medium); display: flex; align-items: center; gap: 3px; }
    .trip-address ion-icon { font-size: 11px; }
    .trip-distance { text-align: center; }
    .dist-value { display: block; font-size: 16px; font-weight: 700; color: var(--ion-color-primary); }
    .dist-unit { font-size: 10px; color: var(--ion-color-medium); }
  `]
})
export class ReportsPage implements OnInit {
  activeTab = 'daily';
  loading = false;
  showDatePicker = false;
  selectedDate = new Date().toISOString();
  todayStr = new Date().toISOString();
  selectedVehicleId: number | null = null;
  selectedVehicleName = 'Tous';
  vehicles: Vehicle[] = [];

  // Report data
  dailyReports: any[] = [];
  mileageReports: any[] = [];
  trips: any[] = [];
  tripsSummary: any = null;
  monthlyReport: any = null;

  constructor(
    private api: ApiService,
    private actionSheetCtrl: ActionSheetController,
    private loadingCtrl: LoadingController
  ) {}

  ngOnInit() {
    this.loadVehicles();
    this.loadReport();
  }

  get displayDate(): string {
    const d = new Date(this.selectedDate);
    if (this.activeTab === 'monthly') {
      return d.toLocaleDateString('fr-FR', { month: 'long', year: 'numeric' });
    }
    return d.toLocaleDateString('fr-FR', { day: '2-digit', month: 'short', year: 'numeric' });
  }

  loadVehicles() {
    this.api.getVehicles().subscribe({
      next: (v) => this.vehicles = Array.isArray(v) ? v : []
    });
  }

  onTabChange() {
    this.loadReport();
  }

  onDateChange() {
    this.showDatePicker = false;
    this.loadReport();
  }

  pickDate() {
    this.showDatePicker = !this.showDatePicker;
  }

  async pickVehicle() {
    const buttons: any[] = [
      {
        text: 'Tous les véhicules',
        handler: () => {
          this.selectedVehicleId = null;
          this.selectedVehicleName = 'Tous';
          this.loadReport();
        }
      },
      ...this.vehicles.slice(0, 15).map(v => ({
        text: `${v.name} (${v.plate})`,
        handler: () => {
          this.selectedVehicleId = parseInt(v.id);
          this.selectedVehicleName = v.name;
          this.loadReport();
        }
      })),
      { text: 'Annuler', role: 'cancel' }
    ];

    const sheet = await this.actionSheetCtrl.create({
      header: 'Choisir un véhicule',
      buttons
    });
    await sheet.present();
  }

  async loadReport() {
    this.loading = true;
    const d = new Date(this.selectedDate);
    const dateStr = d.toISOString().split('T')[0];

    switch (this.activeTab) {
      case 'daily':
        this.loadDailyReport(dateStr);
        break;
      case 'mileage':
        this.loadMileageReport(dateStr);
        break;
      case 'trips':
        this.loadTripsReport(dateStr);
        break;
      case 'monthly':
        this.loadMonthlyReport(d.getFullYear(), d.getMonth() + 1);
        break;
    }
  }

  private loadDailyReport(date: string) {
    if (this.selectedVehicleId) {
      this.api.getDailyReport(this.selectedVehicleId, date).subscribe({
        next: (r) => { this.dailyReports = r ? [r] : []; this.loading = false; },
        error: () => { this.dailyReports = []; this.loading = false; }
      });
    } else {
      this.api.getDailyReports(date).subscribe({
        next: (r) => { this.dailyReports = Array.isArray(r) ? r : []; this.loading = false; },
        error: () => { this.dailyReports = []; this.loading = false; }
      });
    }
  }

  private loadMileageReport(date: string) {
    const d = new Date(date);
    const start = new Date(d.getFullYear(), d.getMonth(), 1).toISOString().split('T')[0];
    const end = date;

    if (this.selectedVehicleId) {
      this.api.getMileageReport(this.selectedVehicleId, start, end).subscribe({
        next: (r) => { this.mileageReports = r ? [r] : []; this.loading = false; },
        error: () => { this.mileageReports = []; this.loading = false; }
      });
    } else {
      this.api.getMileageReports(start, end).subscribe({
        next: (r) => { this.mileageReports = Array.isArray(r) ? r : []; this.loading = false; },
        error: () => { this.mileageReports = []; this.loading = false; }
      });
    }
  }

  private loadTripsReport(date: string) {
    const startDate = date;
    const endDate = date;

    this.api.getTrips(this.selectedVehicleId || undefined, startDate, endDate).subscribe({
      next: (t) => { this.trips = Array.isArray(t) ? t : []; this.loading = false; },
      error: () => { this.trips = []; this.loading = false; }
    });

    this.api.getTripsSummary(startDate, endDate).subscribe({
      next: (s) => this.tripsSummary = s,
      error: () => this.tripsSummary = null
    });
  }

  private loadMonthlyReport(year: number, month: number) {
    this.api.getMonthlyFleetReport(year, month).subscribe({
      next: (r) => { this.monthlyReport = r; this.loading = false; },
      error: () => { this.monthlyReport = null; this.loading = false; }
    });
  }

  formatDuration(minutes: number | undefined): string {
    if (!minutes) return '0m';
    if (minutes < 60) return `${Math.round(minutes)}m`;
    const h = Math.floor(minutes / 60);
    const m = Math.round(minutes % 60);
    return m > 0 ? `${h}h${m.toString().padStart(2, '0')}` : `${h}h`;
  }

  getBarHeight(value: number, max: number): number {
    if (!max || !value) return 2;
    return Math.max(2, (value / max) * 100);
  }

  getEventClass(e: any): string {
    const type = (e.type || e.eventType || '').toLowerCase();
    if (type.includes('drive') || type.includes('moving')) return 'drive';
    if (type.includes('stop')) return 'stop';
    if (type.includes('idle')) return 'idle';
    return 'default';
  }

  getEventLabel(e: any): string {
    const type = (e.type || e.eventType || '').toLowerCase();
    if (type.includes('drive') || type.includes('moving')) {
      const dist = e.distanceKm ? ` — ${e.distanceKm.toFixed(1)} km` : '';
      return `Conduite${dist}`;
    }
    if (type.includes('stop')) {
      const dur = e.durationMinutes ? ` — ${this.formatDuration(e.durationMinutes)}` : '';
      return `Arrêt${dur}`;
    }
    if (type.includes('idle')) return 'Ralenti';
    if (type.includes('ignition_on')) return 'Contact ON';
    if (type.includes('ignition_off')) return 'Contact OFF';
    return e.type || 'Événement';
  }

  formatEventTime(ts: string): string {
    if (!ts) return '';
    return new Date(ts).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  trackTripById(_: number, t: any) { return t.id; }
}
