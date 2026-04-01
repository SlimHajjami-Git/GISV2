import { Injectable } from '@angular/core';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

export interface PdfReportConfig {
  title: string;
  subtitle?: string;
  vehicleName?: string;
  dateRange?: string;
  statistics?: Record<string, string>;
  columns: { header: string; dataKey: string }[];
  data: any[];
  formatters?: Record<string, (value: any, row: any) => string>;
}

@Injectable({ providedIn: 'root' })
export class PdfExportService {

  private readonly primaryColor: [number, number, number] = [30, 58, 138];
  private readonly accentColor: [number, number, number] = [59, 130, 246];
  private readonly lightBg: [number, number, number] = [241, 245, 249];

  private sanitizeText(text: string): string {
    if (!text) return '';
    // Normalize to NFC (composed form) — keeps é, è, ê, à, ç etc. as single characters
    // Helvetica in jsPDF supports Latin-1 (U+00A0–U+00FF) which includes all French accents
    return text
      .normalize('NFC')
      .replace(/[^\x20-\x7E\xA0-\xFF]/g, (ch) => {
        const map: Record<string, string> = {
          '\u2019': "'", '\u2018': "'", '\u201C': '"', '\u201D': '"',
          '\u2013': '-', '\u2014': '-', '\u2026': '...',
          '\u0152': 'OE', '\u0153': 'oe',
        };
        return map[ch] || '';
      });
  }

  exportReport(config: PdfReportConfig): void {
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
    const pageWidth = doc.internal.pageSize.getWidth();
    let y = 15;

    // ── Header bar ──
    doc.setFillColor(...this.primaryColor);
    doc.rect(0, 0, pageWidth, 32, 'F');

    doc.setTextColor(255, 255, 255);
    doc.setFontSize(16);
    doc.setFont('helvetica', 'bold');
    doc.text(this.sanitizeText(config.title), 14, 14);

    doc.setFontSize(9);
    doc.setFont('helvetica', 'normal');
    const meta: string[] = [];
    if (config.vehicleName) meta.push(`V\u00e9hicule: ${this.sanitizeText(config.vehicleName)}`);
    if (config.dateRange) meta.push(`P\u00e9riode: ${this.sanitizeText(config.dateRange)}`);
    meta.push(`G\u00e9n\u00e9r\u00e9 le: ${new Date().toLocaleDateString('fr-FR')} \u00e0 ${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`);
    doc.text(meta.join('  |  '), 14, 22);

    if (config.subtitle) {
      doc.setFontSize(9);
      doc.text(this.sanitizeText(config.subtitle), pageWidth - 14, 14, { align: 'right' });
    }

    y = 38;

    // ── Statistics block ──
    if (config.statistics && Object.keys(config.statistics).length > 0) {
      const entries = Object.entries(config.statistics);
      const colCount = Math.min(entries.length, 4);
      const cardW = (pageWidth - 28) / colCount;
      const cardH = 20;

      entries.slice(0, 8).forEach(([label, value], i) => {
        const row = Math.floor(i / colCount);
        const col = i % colCount;
        const x = 14 + col * cardW;
        const cy = y + row * (cardH + 3);

        doc.setFillColor(...this.lightBg);
        doc.roundedRect(x, cy, cardW - 3, cardH, 2, 2, 'F');

        doc.setTextColor(100, 116, 139);
        doc.setFontSize(8);
        doc.setFont('helvetica', 'normal');
        doc.text(this.sanitizeText(label), x + 4, cy + 7);

        doc.setTextColor(15, 23, 42);
        doc.setFontSize(12);
        doc.setFont('helvetica', 'bold');
        doc.text(this.sanitizeText(String(value)), x + 4, cy + 15);
      });

      const totalRows = Math.ceil(entries.length / colCount);
      y += totalRows * (cardH + 3) + 4;
    }

    // ── Data table ──
    const headers = config.columns.map(c => this.sanitizeText(c.header));
    const body = config.data.map(row => {
      return config.columns.map(col => {
        const val = row[col.dataKey];
        if (config.formatters && config.formatters[col.dataKey]) {
          return this.sanitizeText(config.formatters[col.dataKey](val, row));
        }
        if (val === null || val === undefined) return '-';
        return this.sanitizeText(String(val));
      });
    });

    autoTable(doc, {
      head: [headers],
      body: body,
      startY: y,
      theme: 'grid',
      styles: {
        fontSize: 7,
        cellPadding: 2.5,
        lineColor: [226, 232, 240],
        lineWidth: 0.2,
        textColor: [30, 41, 59],
        font: 'helvetica',
        overflow: 'linebreak'
      },
      headStyles: {
        fillColor: this.primaryColor,
        textColor: [255, 255, 255],
        fontStyle: 'bold',
        fontSize: 7.5,
        cellPadding: 2.5
      },
      alternateRowStyles: {
        fillColor: [248, 250, 252]
      },
      columnStyles: this.getColumnStyles(config.columns),
      margin: { left: 10, right: 10 },
      tableWidth: 'auto',
      didDrawPage: (data: any) => {
        // Re-draw header on subsequent pages
        const pageNum = (doc as any).internal.getCurrentPageInfo().pageNumber;
        if (pageNum > 1) {
          doc.setFillColor(...this.primaryColor);
          doc.rect(0, 0, pageWidth, 12, 'F');
          doc.setTextColor(255, 255, 255);
          doc.setFontSize(9);
          doc.setFont('helvetica', 'bold');
          doc.text(this.sanitizeText(config.title), 10, 8);
        }
        // Footer on every page
        const pageCount = (doc as any).internal.getNumberOfPages();
        doc.setFontSize(7);
        doc.setTextColor(148, 163, 184);
        doc.setFont('helvetica', 'normal');
        const footerY = doc.internal.pageSize.getHeight() - 7;
        doc.text(`GIS Fleet Management - ${this.sanitizeText(config.title)}`, 10, footerY);
        doc.text(`Page ${pageNum} / ${pageCount}`, pageWidth - 10, footerY, { align: 'right' });
      }
    });

    // ── Save ──
    const filename = this.sanitizeFilename(config.title);
    const dateStr = new Date().toISOString().split('T')[0];
    doc.save(`${filename}_${dateStr}.pdf`);
  }

  private getColumnStyles(columns: { header: string; dataKey: string }[]): Record<number, any> {
    const styles: Record<number, any> = {};
    columns.forEach((col, i) => {
      if (['address', '_address', 'startAddress', 'location', 'description'].includes(col.dataKey)) {
        styles[i] = { cellWidth: 50 };
      }
    });
    return styles;
  }

  private sanitizeFilename(name: string): string {
    return name
      .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-zA-Z0-9_\- ]/g, '')
      .replace(/\s+/g, '_')
      .toLowerCase();
  }

  // ── Column definitions per report type ──

  getColumnsForReport(type: string, options?: any): { header: string; dataKey: string }[] {
    switch (type) {
      case 'trips':
        return [
          ...(options?.allVehicles ? [{ header: 'V\u00e9hicule', dataKey: 'vehicleName' }] : []),
          { header: 'Type', dataKey: '_type' },
          { header: 'D\u00e9but', dataKey: 'startTime' },
          { header: 'Fin', dataKey: 'endTime' },
          { header: 'Dur\u00e9e', dataKey: 'duration' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Vit. max', dataKey: 'maxSpeed' },
          { header: 'Lieu', dataKey: '_address' }
        ];
      case 'stops':
        return [
          ...(options?.allVehicles ? [{ header: 'Véhicule', dataKey: 'vehicleName' }] : []),
          { header: 'Type', dataKey: '_typeLabel' },
          { header: 'Début', dataKey: 'time' },
          { header: 'Fin', dataKey: 'endTime' },
          { header: 'Durée', dataKey: 'duration' },
          { header: 'Adresse', dataKey: 'address' }
        ];
      case 'mileage':
        return [
          { header: 'Date', dataKey: 'date' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Trajets', dataKey: 'tripCount' },
          { header: 'Temps conduite', dataKey: 'drivingTime' },
          { header: 'Vit. max', dataKey: 'maxSpeed' },
          { header: 'Odom\u00e8tre', dataKey: 'odometer' }
        ];
      case 'mileage-period':
        const cols = [{ header: 'Période', dataKey: 'period' }];
        if (options?.periodType === 'day') cols.push({ header: 'Jour', dataKey: 'dayOfWeek' });
        cols.push({ header: 'Distance', dataKey: 'distance' });
        if (options?.periodType === 'month') {
          cols.push({ header: 'Moy. jour', dataKey: 'avgDaily' });
          cols.push({ header: 'Jours actifs', dataKey: 'activeDays' });
        }
        cols.push({ header: 'Trajets', dataKey: 'tripCount' });
        cols.push({ header: 'Temps conduite', dataKey: 'drivingTime' });
        if (options?.periodType !== 'month') {
          cols.push({ header: 'Vit. max', dataKey: 'maxSpeed' });
        }
        return cols;
      case 'daily':
        return [
          { header: '#', dataKey: 'eventNumber' },
          { header: 'Horaire', dataKey: 'time' },
          { header: 'Événement', dataKey: 'typeLabel' },
          { header: 'Durée', dataKey: 'duration' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Vitesse', dataKey: 'speed' },
          { header: 'Lieu', dataKey: 'address' }
        ];
      case 'speed':
        return [
          { header: 'Véhicule', dataKey: 'vehicleName' },
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Vitesse', dataKey: 'speed' },
          { header: 'Adresse', dataKey: 'address' }
        ];
      case 'speed-infraction':
        return [
          { header: 'Véhicule', dataKey: 'vehicle' },
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Adresse', dataKey: 'address' },
          { header: 'Vitesse', dataKey: 'speed' },
          { header: 'Limite', dataKey: 'limit' },
          { header: 'Sévérité', dataKey: 'severityLabel' }
        ];
      case 'driving-behavior':
        return [
          { header: 'Véhicule', dataKey: 'vehicle' },
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Type d\'incident', dataKey: 'incidentType' },
          { header: 'Adresse', dataKey: 'address' },
          { header: 'Valeur', dataKey: 'value' },
          { header: 'Sévérité', dataKey: 'severityLabel' }
        ];
      case 'fuel':
        return [
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Niveau', dataKey: 'fuelPercent' },
          { header: 'Variation', dataKey: 'fuelChange' },
          { header: 'Type', dataKey: 'eventType' },
          { header: 'Position', dataKey: 'location' },
          { header: 'Odomètre', dataKey: 'odometer' }
        ];
      case 'fuel-estimation':
        return [
          { header: 'Véhicule', dataKey: 'vehicleName' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Consommation est.', dataKey: 'fuelEstimated' },
          { header: 'Coût est.', dataKey: 'costEstimated' },
          { header: 'Conso. moy.', dataKey: 'avgConsumption' }
        ];
      case 'costs':
        return [
          { header: 'Véhicule', dataKey: 'vehicleName' },
          { header: 'Date', dataKey: 'date' },
          { header: 'Référence', dataKey: 'reference' },
          { header: 'Description', dataKey: 'description' },
          { header: 'Fournisseur', dataKey: 'supplierName' },
          { header: 'Main d\'oeuvre', dataKey: 'laborCostFormatted' },
          { header: 'Pièces', dataKey: 'partsCostFormatted' },
          { header: 'Total', dataKey: 'totalCostFormatted' },
          { header: 'Statut', dataKey: 'status' }
        ];
      case 'maintenance':
        return [
          { header: 'Véhicule', dataKey: 'vehicleName' },
          { header: 'Date', dataKey: 'date' },
          { header: 'Type', dataKey: 'type' },
          { header: 'Description', dataKey: 'description' },
          { header: 'Statut', dataKey: 'status' },
          { header: 'Coût', dataKey: 'costFormatted' },
          { header: 'Kilométrage', dataKey: 'mileage' }
        ];
      default:
        return [
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Valeur', dataKey: 'value' },
          { header: 'Détails', dataKey: 'details' },
          { header: 'Kilométrage', dataKey: 'kilometrage' }
        ];
    }
  }

  getFormattersForReport(type: string): Record<string, (value: any, row: any) => string> {
    switch (type) {
      case 'trips':
        return {
          '_type': (_v: any, row: any) => row.isTrip ? `Trajet T${row.tripNumber}` : 'Arrêt',
          '_address': (_v: any, row: any) => row.isTrip
            ? `${row.startAddress || ''} → ${row.endAddress || ''}`
            : (row.address || '-')
        };
      case 'stops':
        return {
          '_typeLabel': (_v: any, row: any) => `${row.typeCode} - ${row.typeLabel}`
        };
      default:
        return {};
    }
  }
}
