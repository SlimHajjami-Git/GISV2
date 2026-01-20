import { Component, OnInit, ViewChild, ElementRef, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService, MonthlyFleetReport, ChartData, MultiSeriesChartData, Kpi, FleetAlert } from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ButtonComponent, CardComponent } from './shared/ui';
import { Chart, ChartConfiguration, registerables } from 'chart.js';

Chart.register(...registerables);

@Component({
  selector: 'app-monthly-report',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent, ButtonComponent, CardComponent],
  templateUrl: './monthly-report.component.html',
  styleUrls: ['./monthly-report.component.css']
})
export class MonthlyReportComponent implements OnInit, AfterViewInit {
  Math = Math; // Expose Math to template
  
  @ViewChild('fleetCompositionChart') fleetCompositionChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('costDistributionChart') costDistributionChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('utilizationTrendChart') utilizationTrendChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('distanceTrendChart') distanceTrendChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('vehicleRankingChart') vehicleRankingChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('driverRankingChart') driverRankingChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('maintenanceCostChart') maintenanceCostChartRef?: ElementRef<HTMLCanvasElement>;
  @ViewChild('vehicleStatusChart') vehicleStatusChartRef?: ElementRef<HTMLCanvasElement>;

  report: MonthlyFleetReport | null = null;
  loading = false;
  error: string | null = null;

  // Filters
  selectedYear: number;
  selectedMonth: number;
  years: number[] = [];
  months = [
    { value: 1, label: 'Janvier' },
    { value: 2, label: 'Février' },
    { value: 3, label: 'Mars' },
    { value: 4, label: 'Avril' },
    { value: 5, label: 'Mai' },
    { value: 6, label: 'Juin' },
    { value: 7, label: 'Juillet' },
    { value: 8, label: 'Août' },
    { value: 9, label: 'Septembre' },
    { value: 10, label: 'Octobre' },
    { value: 11, label: 'Novembre' },
    { value: 12, label: 'Décembre' }
  ];

  // Active section for navigation
  activeSection = 'summary';
  sections = [
    { id: 'summary', label: 'Résumé', icon: '📊' },
    { id: 'fleet', label: 'Flotte', icon: '🚗' },
    { id: 'utilization', label: 'Utilisation', icon: '📈' },
    { id: 'fuel', label: 'Carburant', icon: '⛽' },
    { id: 'maintenance', label: 'Maintenance', icon: '🔧' },
    { id: 'drivers', label: 'Conducteurs', icon: '👤' },
    { id: 'costs', label: 'Coûts', icon: '💰' },
    { id: 'kpis', label: 'KPIs', icon: '🎯' },
    { id: 'alerts', label: 'Alertes', icon: '⚠️' }
  ];

  // Charts
  private charts: Map<string, Chart> = new Map();

  constructor(
    private router: Router,
    private apiService: ApiService
  ) {
    const now = new Date();
    const lastMonth = new Date(now.getFullYear(), now.getMonth() - 1, 1);
    this.selectedYear = lastMonth.getFullYear();
    this.selectedMonth = lastMonth.getMonth() + 1;
    
    // Generate year options (last 3 years)
    for (let i = 0; i < 3; i++) {
      this.years.push(now.getFullYear() - i);
    }
  }

  ngOnInit() {
    this.loadReport();
  }

  ngAfterViewInit() {
    // Charts will be created after data is loaded
  }

  loadReport() {
    this.loading = true;
    this.error = null;

    this.apiService.getMonthlyFleetReport(this.selectedYear, this.selectedMonth).subscribe({
      next: (data) => {
        this.report = data;
        this.loading = false;
        setTimeout(() => this.createCharts(), 100);
      },
      error: (err) => {
        console.error('Error loading monthly report:', err);
        this.error = 'Erreur lors du chargement du rapport';
        this.loading = false;
      }
    });
  }

  onPeriodChange() {
    this.loadReport();
  }

  setActiveSection(sectionId: string) {
    this.activeSection = sectionId;
    setTimeout(() => this.createCharts(), 100);
  }

  createCharts() {
    if (!this.report) return;

    this.destroyCharts();

    // Create charts based on active section
    switch (this.activeSection) {
      case 'summary':
      case 'fleet':
        this.createPieChart('fleetComposition', this.fleetCompositionChartRef, this.report.charts.fleetComposition);
        this.createPieChart('vehicleStatus', this.vehicleStatusChartRef, this.report.charts.vehicleStatusDistribution);
        break;
      case 'utilization':
        this.createLineChart('utilizationTrend', this.utilizationTrendChartRef, this.report.charts.efficiencyTrend);
        this.createLineChart('distanceTrend', this.distanceTrendChartRef, this.report.charts.dailyDistanceTrend);
        break;
      case 'costs':
        this.createPieChart('costDistribution', this.costDistributionChartRef, this.report.charts.costDistribution);
        break;
      case 'maintenance':
        this.createBarChart('maintenanceCost', this.maintenanceCostChartRef, this.report.charts.maintenanceCostByType);
        break;
      case 'drivers':
        this.createBarChart('driverRanking', this.driverRankingChartRef, this.report.charts.driverRanking);
        break;
    }
  }

  private destroyCharts() {
    this.charts.forEach(chart => chart.destroy());
    this.charts.clear();
  }

  private createPieChart(id: string, ref: ElementRef<HTMLCanvasElement> | undefined, data: ChartData) {
    if (!ref?.nativeElement || !data) return;

    const ctx = ref.nativeElement.getContext('2d');
    if (!ctx) return;

    const chart = new Chart(ctx, {
      type: 'pie',
      data: {
        labels: data.labels,
        datasets: [{
          data: data.values,
          backgroundColor: data.colors || ['#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6', '#EC4899'],
          borderWidth: 2,
          borderColor: '#ffffff'
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: {
            position: 'bottom',
            labels: { padding: 20, usePointStyle: true }
          },
          tooltip: {
            callbacks: {
              label: (context) => {
                const total = (context.dataset.data as number[]).reduce((a, b) => a + b, 0);
                const percentage = ((context.parsed / total) * 100).toFixed(1);
                return `${context.label}: ${context.parsed} (${percentage}%)`;
              }
            }
          }
        }
      }
    });

    this.charts.set(id, chart);
  }

  private createBarChart(id: string, ref: ElementRef<HTMLCanvasElement> | undefined, data: ChartData) {
    if (!ref?.nativeElement || !data) return;

    const ctx = ref.nativeElement.getContext('2d');
    if (!ctx) return;

    const chart = new Chart(ctx, {
      type: 'bar',
      data: {
        labels: data.labels,
        datasets: [{
          label: data.title,
          data: data.values,
          backgroundColor: 'rgba(59, 130, 246, 0.7)',
          borderColor: 'rgba(59, 130, 246, 1)',
          borderWidth: 1,
          borderRadius: 4
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        indexAxis: 'y',
        plugins: {
          legend: { display: false }
        },
        scales: {
          x: { beginAtZero: true },
          y: { grid: { display: false } }
        }
      }
    });

    this.charts.set(id, chart);
  }

  private createLineChart(id: string, ref: ElementRef<HTMLCanvasElement> | undefined, data: MultiSeriesChartData) {
    if (!ref?.nativeElement || !data) return;

    const ctx = ref.nativeElement.getContext('2d');
    if (!ctx) return;

    const colors = ['#3B82F6', '#10B981', '#F59E0B', '#EF4444', '#8B5CF6'];
    
    const chart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: data.labels,
        datasets: data.series.map((series, index) => ({
          label: series.name,
          data: series.data,
          borderColor: series.color || colors[index % colors.length],
          backgroundColor: (series.color || colors[index % colors.length]) + '20',
          fill: true,
          tension: 0.4,
          pointRadius: 3,
          pointHoverRadius: 6
        }))
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: {
            position: 'top',
            labels: { usePointStyle: true }
          }
        },
        scales: {
          y: {
            beginAtZero: true,
            title: { display: !!data.yAxisLabel, text: data.yAxisLabel || '' }
          },
          x: {
            title: { display: !!data.xAxisLabel, text: data.xAxisLabel || '' }
          }
        }
      }
    });

    this.charts.set(id, chart);
  }

  // Helper methods
  getKpiStatus(kpi: Kpi): string {
    return kpi.status === 'OnTarget' ? 'success' : kpi.status === 'Above' ? 'warning' : 'danger';
  }

  getAlertSeverityClass(severity: string): string {
    switch (severity.toLowerCase()) {
      case 'critical': return 'alert-critical';
      case 'warning': return 'alert-warning';
      default: return 'alert-info';
    }
  }

  getTrendIcon(trend: string): string {
    switch (trend) {
      case 'increase': return '📈';
      case 'decrease': return '📉';
      default: return '➡️';
    }
  }

  formatNumber(value: number, decimals = 0): string {
    return value.toLocaleString('fr-FR', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
  }

  formatCurrency(value: number): string {
    return value.toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' TND';
  }

  formatPercent(value: number): string {
    return value.toFixed(1) + '%';
  }

  // Export functionality
  exportToPdf() {
    window.print();
  }

  exportToExcel() {
    if (!this.report) return;
    
    // Create CSV content
    const csvContent = this.generateCsvContent();
    const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `rapport-mensuel-${this.report.monthName.replace(' ', '-')}.csv`;
    link.click();
  }

  private generateCsvContent(): string {
    if (!this.report) return '';

    let csv = 'Rapport Mensuel de Flotte\n';
    csv += `Période: ${this.report.reportPeriod}\n\n`;

    // Executive Summary
    csv += 'RÉSUMÉ EXÉCUTIF\n';
    csv += `Véhicules totaux,${this.report.executiveSummary.totalVehicles}\n`;
    csv += `Véhicules actifs,${this.report.executiveSummary.activeVehicles}\n`;
    csv += `Distance totale (km),${this.report.executiveSummary.totalDistanceKm}\n`;
    csv += `Carburant consommé (L),${this.report.executiveSummary.totalFuelConsumedLiters}\n`;
    csv += `Coût opérationnel total,${this.report.executiveSummary.totalOperationalCost}\n`;
    csv += `Taux d'utilisation,${this.report.executiveSummary.fleetUtilizationRate}%\n\n`;

    // Vehicle Utilization
    csv += 'UTILISATION PAR VÉHICULE\n';
    csv += 'Véhicule,Plaque,Taux utilisation,Distance (km),Trajets,Jours actifs\n';
    this.report.utilization.byVehicle.forEach(v => {
      csv += `${v.vehicleName},${v.plate || ''},${v.utilizationRate}%,${v.totalDistanceKm},${v.totalTrips},${v.operatingDays}\n`;
    });

    return csv;
  }

  goBack() {
    this.router.navigate(['/reports']);
  }
}
