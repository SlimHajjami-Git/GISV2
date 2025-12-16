import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { CompanyService } from '../../core/services/company.service';
import { MonitoringService } from '../../core/services/monitoring.service';
import { VehicleService } from '../../core/services/vehicle.service';
import { ReportTemplate, GeneratedReport, Trip, Vehicle } from '../../core/models/monitoring.types';

@Component({
  selector: 'app-reports',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './reports.component.html',
  styleUrls: ['./reports.component.css']
})
export class ReportsComponent implements OnInit {
  templates = signal<ReportTemplate[]>([]);
  reports = signal<GeneratedReport[]>([]);
  vehicles = signal<Vehicle[]>([]);
  loading = signal(true);
  generating = signal(false);

  showGenerateModal = signal(false);
  selectedTemplate = signal<ReportTemplate | null>(null);
  selectedReportData = signal<any | null>(null);

  reportForm = {
    template_id: '',
    name: '',
    period_start: '',
    period_end: '',
    vehicle_ids: [] as string[]
  };

  constructor(
    private companyService: CompanyService,
    private monitoringService: MonitoringService,
    private vehicleService: VehicleService
  ) {}

  async ngOnInit() {
    await this.loadData();
  }

  async loadData() {
    const company = this.companyService.currentCompany();
    if (!company) return;

    try {
      this.loading.set(true);
      const [templates, reports, vehicles] = await Promise.all([
        this.monitoringService.getReportTemplates(company.id),
        this.monitoringService.getGeneratedReports(company.id),
        this.vehicleService.getVehicles(company.id)
      ]);

      this.templates.set(templates);
      this.reports.set(reports);
      this.vehicles.set(vehicles);
    } catch (error) {
      console.error('Error loading reports data:', error);
    } finally {
      this.loading.set(false);
    }
  }

  openGenerateModal(template: ReportTemplate) {
    this.selectedTemplate.set(template);
    this.showGenerateModal.set(true);
    this.reportForm.template_id = template.id;
    this.reportForm.name = `${template.name} - ${new Date().toLocaleDateString('fr-FR')}`;

    const today = new Date();
    const lastWeek = new Date(today);
    lastWeek.setDate(today.getDate() - 7);

    this.reportForm.period_end = today.toISOString().split('T')[0];
    this.reportForm.period_start = lastWeek.toISOString().split('T')[0];
  }

  closeGenerateModal() {
    this.showGenerateModal.set(false);
    this.selectedTemplate.set(null);
    this.resetForm();
  }

  resetForm() {
    this.reportForm = {
      template_id: '',
      name: '',
      period_start: '',
      period_end: '',
      vehicle_ids: []
    };
  }

  async generateReport() {
    const company = this.companyService.currentCompany();
    if (!company || !this.reportForm.template_id) return;

    try {
      this.generating.set(true);

      await this.monitoringService.generateReport(
        company.id,
        this.reportForm.template_id,
        this.reportForm.name,
        this.reportForm.period_start + 'T00:00:00Z',
        this.reportForm.period_end + 'T23:59:59Z'
      );

      await this.loadData();
      this.closeGenerateModal();
    } catch (error: any) {
      console.error('Error generating report:', error);
      alert('Erreur lors de la génération du rapport: ' + error.message);
    } finally {
      this.generating.set(false);
    }
  }

  async viewReport(report: GeneratedReport) {
    try {
      const fullReport = await this.monitoringService.getGeneratedReport(report.id);
      this.selectedReportData.set(fullReport);
    } catch (error) {
      console.error('Error viewing report:', error);
    }
  }

  closeReportView() {
    this.selectedReportData.set(null);
  }

  getReportTypeLabel(type: string): string {
    const labels: Record<string, string> = {
      trips: 'Trajets',
      fuel: 'Carburant',
      speed: 'Vitesse',
      stops: 'Arrêts',
      geofence: 'Géofencing',
      custom: 'Personnalisé'
    };
    return labels[type] || type;
  }

  getReportTypeIcon(type: string): string {
    const icons: Record<string, string> = {
      trips: '🛣️',
      fuel: '⛽',
      speed: '🏎️',
      stops: '🅿️',
      geofence: '📍',
      custom: '📊'
    };
    return icons[type] || '📄';
  }

  getStatusBadgeClass(status: string): string {
    const classes: Record<string, string> = {
      generating: 'badge-warning',
      completed: 'badge-success',
      failed: 'badge-error'
    };
    return classes[status] || 'badge-default';
  }

  getStatusLabel(status: string): string {
    const labels: Record<string, string> = {
      generating: 'En cours',
      completed: 'Terminé',
      failed: 'Échec'
    };
    return labels[status] || status;
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('fr-FR', {
      year: 'numeric',
      month: 'long',
      day: 'numeric'
    });
  }

  formatDateTime(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleString('fr-FR');
  }

  exportReport(report: GeneratedReport, format: 'pdf' | 'excel' | 'csv') {
    console.log(`Exporting report ${report.id} as ${format}`);
    alert(`Fonctionnalité d'export ${format.toUpperCase()} à venir`);
  }
}
