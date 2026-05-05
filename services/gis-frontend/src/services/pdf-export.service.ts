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

export interface PdfGroup {
  /** Main label shown as section header (e.g. vehicle name) */
  groupLabel: string;
  /** Optional secondary info (e.g. plate, km) shown right of the label */
  groupSubtitle?: string;
  /** Rows for this group */
  rows: any[];
  /** Optional right-aligned subtotal line (e.g. "3 réparations · 1 250,00 DT") */
  subtotal?: string;
}

export interface GroupedPdfReportConfig {
  title: string;
  subtitle?: string;
  dateRange?: string;
  /** Top statistic cards (global totals) */
  statistics?: Record<string, string>;
  columns: { header: string; dataKey: string }[];
  groups: PdfGroup[];
  formatters?: Record<string, (value: any, row: any) => string>;
  /** Optional grand-total line shown after the last group */
  grandTotal?: string;
}

@Injectable({ providedIn: 'root' })
export class PdfExportService {

  private readonly primaryColor: [number, number, number] = [30, 58, 138];
  private readonly accentColor: [number, number, number] = [59, 130, 246];
  private readonly lightBg: [number, number, number] = [241, 245, 249];

  /**
   * Calypso 7 — branding PDF : on charge le logo Calypso une seule fois au
   * boot du service (data URL base64) puis on l'embarque dans chaque PDF
   * via doc.addImage(). Le footer est rebrandé « Calypso · Belive » au
   * lieu de l'ancien « GIS Fleet Management ».
   */
  private logoDataUrl: string | null = null;
  private logoLoading: Promise<string | null> | null = null;

  constructor() {
    // Préchargement non bloquant : le premier export attendra cette promise,
    // les suivants utilisent la valeur cachée.
    this.preloadLogo();
  }

  private preloadLogo(): Promise<string | null> {
    if (this.logoDataUrl) return Promise.resolve(this.logoDataUrl);
    if (this.logoLoading) return this.logoLoading;
    this.logoLoading = fetch('assets/logo/calypso-logo.png')
      .then(r => r.ok ? r.blob() : null)
      .then(blob => {
        if (!blob) return null;
        return new Promise<string>((resolve) => {
          const reader = new FileReader();
          reader.onloadend = () => {
            const url = (reader.result as string) || '';
            this.logoDataUrl = url;
            resolve(url);
          };
          reader.readAsDataURL(blob);
        });
      })
      .catch(() => null);
    return this.logoLoading;
  }

  /** Logo Calypso à insérer dans le header du PDF. Retourne le data URL ou null si l'asset n'est pas dispo. */
  private getLogo(): string | null {
    return this.logoDataUrl;
  }

  /** Texte du footer rebrandé Calypso (remplace l'ancien « GIS Fleet Management »). */
  private readonly footerBrand = 'Calypso · Belive';

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
      })
      .replace(/^\s+/, '')   // trim leading spaces left by stripped emojis
      .replace(/\s{2,}/g, ' '); // collapse multiple spaces into one
  }

  async exportReport(config: PdfReportConfig): Promise<void> {
    // Calypso 7 — on attend le logo (preloaded au boot du service).
    await this.preloadLogo();
    return this.exportReportSync(config);
  }

  private exportReportSync(config: PdfReportConfig): void {
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
    const pageWidth = doc.internal.pageSize.getWidth();
    let y = 15;

    // ── Header bar ──
    doc.setFillColor(...this.primaryColor);
    doc.rect(0, 0, pageWidth, 32, 'F');

    doc.setTextColor(255, 255, 255);
    doc.setFontSize(16);
    doc.setFont('helvetica', 'bold');
    // Logo Calypso a gauche dans un cartouche blanc, titre decale a droite.
    const logo = this.getLogo();
    const titleLeft = logo ? 38 : 14;
    if (logo) {
      doc.setFillColor(255, 255, 255);
      doc.roundedRect(8, 4, 26, 24, 2, 2, 'F');
      try { doc.addImage(logo, 'PNG', 10, 5, 22, 22); } catch {}
    }
    doc.setTextColor(255, 255, 255);
    doc.text(this.sanitizeText(config.title), titleLeft, 14);

    doc.setFontSize(9);
    doc.setFont('helvetica', 'normal');
    const meta: string[] = [];
    if (config.vehicleName) meta.push(`V\u00e9hicule: ${this.sanitizeText(config.vehicleName)}`);
    if (config.dateRange) meta.push(`P\u00e9riode: ${this.sanitizeText(config.dateRange)}`);
    meta.push(`G\u00e9n\u00e9r\u00e9 le: ${new Date().toLocaleDateString('fr-FR')} \u00e0 ${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`);
    doc.text(meta.join('  |  '), titleLeft, 22);

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
      tableWidth: pageWidth - 20,
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
        doc.text(`${this.footerBrand} · ${this.sanitizeText(config.title)}`, 10, footerY);
        doc.text(`Page ${pageNum} / ${pageCount}`, pageWidth - 10, footerY, { align: 'right' });
      }
    });

    // ── Save ──
    const filename = this.sanitizeFilename(config.title);
    const dateStr = new Date().toISOString().split('T')[0];
    doc.save(`${filename}_${dateStr}.pdf`);
  }

  /**
   * Export a PDF where rows are organized in sections (one table per group),
   * each with its own subtotal line, followed by a grand total.
   * Reuses the same header bar, footer, column sizing and sanitizer as exportReport.
   */
  async exportGroupedReport(config: GroupedPdfReportConfig): Promise<void> {
    await this.preloadLogo();
    return this.exportGroupedReportSync(config);
  }

  private exportGroupedReportSync(config: GroupedPdfReportConfig): void {
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
    const pageWidth = doc.internal.pageSize.getWidth();
    const pageHeight = doc.internal.pageSize.getHeight();
    let y = 15;

    // ── Header bar ──
    doc.setFillColor(...this.primaryColor);
    doc.rect(0, 0, pageWidth, 32, 'F');

    // Logo Calypso a gauche dans un cartouche blanc.
    const logo = this.getLogo();
    const titleLeft = logo ? 38 : 14;
    if (logo) {
      doc.setFillColor(255, 255, 255);
      doc.roundedRect(8, 4, 26, 24, 2, 2, 'F');
      try { doc.addImage(logo, 'PNG', 10, 5, 22, 22); } catch {}
    }

    doc.setTextColor(255, 255, 255);
    doc.setFontSize(16);
    doc.setFont('helvetica', 'bold');
    doc.text(this.sanitizeText(config.title), titleLeft, 14);

    doc.setFontSize(9);
    doc.setFont('helvetica', 'normal');
    const meta: string[] = [];
    if (config.dateRange) meta.push(`P\u00e9riode: ${this.sanitizeText(config.dateRange)}`);
    meta.push(`G\u00e9n\u00e9r\u00e9 le: ${new Date().toLocaleDateString('fr-FR')} \u00e0 ${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`);
    doc.text(meta.join('  |  '), titleLeft, 22);

    if (config.subtitle) {
      doc.setFontSize(9);
      doc.text(this.sanitizeText(config.subtitle), pageWidth - 14, 14, { align: 'right' });
    }

    y = 38;

    // ── Statistics block (global totals) ──
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
      y += totalRows * (cardH + 3) + 6;
    }

    const headers = config.columns.map(c => this.sanitizeText(c.header));

    // Draws header bar + footer on each new page (used by didDrawPage)
    const drawPageChrome = () => {
      const pageNum = (doc as any).internal.getCurrentPageInfo().pageNumber;
      if (pageNum > 1) {
        doc.setFillColor(...this.primaryColor);
        doc.rect(0, 0, pageWidth, 12, 'F');
        doc.setTextColor(255, 255, 255);
        doc.setFontSize(9);
        doc.setFont('helvetica', 'bold');
        doc.text(this.sanitizeText(config.title), 10, 8);
      }
      const pageCount = (doc as any).internal.getNumberOfPages();
      doc.setFontSize(7);
      doc.setTextColor(148, 163, 184);
      doc.setFont('helvetica', 'normal');
      const footerY = pageHeight - 7;
      doc.text(`GIS Fleet Management - ${this.sanitizeText(config.title)}`, 10, footerY);
      doc.text(`Page ${pageNum} / ${pageCount}`, pageWidth - 10, footerY, { align: 'right' });
    };

    // ── Render each group ──
    for (let g = 0; g < config.groups.length; g++) {
      const group = config.groups[g];

      // If the section header + a couple of rows wouldn't fit on current page, start a new page
      if (y > pageHeight - 50) {
        doc.addPage();
        y = 18;
      }

      // Section header bar
      doc.setFillColor(...this.lightBg);
      doc.roundedRect(10, y, pageWidth - 20, 10, 1.5, 1.5, 'F');
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(11);
      doc.setTextColor(...this.primaryColor);
      doc.text(this.sanitizeText(group.groupLabel), 14, y + 7);
      if (group.groupSubtitle) {
        doc.setFont('helvetica', 'normal');
        doc.setFontSize(9);
        doc.setTextColor(100, 116, 139);
        doc.text(this.sanitizeText(group.groupSubtitle), pageWidth - 14, y + 7, { align: 'right' });
      }
      y += 12;

      // Section table
      const body = group.rows.map(row => {
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
        tableWidth: pageWidth - 20,
        didDrawPage: drawPageChrome
      });

      y = (doc as any).lastAutoTable.finalY + 2;

      // Subtotal line
      if (group.subtotal) {
        if (y > pageHeight - 18) {
          doc.addPage();
          y = 18;
        }
        doc.setFillColor(241, 245, 249);
        doc.rect(10, y, pageWidth - 20, 7, 'F');
        doc.setFont('helvetica', 'bold');
        doc.setFontSize(8.5);
        doc.setTextColor(30, 41, 59);
        doc.text(this.sanitizeText(group.subtotal), pageWidth - 14, y + 5, { align: 'right' });
        y += 10;
      } else {
        y += 4;
      }
    }

    // ── Grand total ──
    if (config.grandTotal) {
      if (y > pageHeight - 20) {
        doc.addPage();
        y = 18;
      }
      doc.setFillColor(...this.primaryColor);
      doc.rect(10, y, pageWidth - 20, 10, 'F');
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(11);
      doc.setTextColor(255, 255, 255);
      doc.text(this.sanitizeText(config.grandTotal), pageWidth - 14, y + 7, { align: 'right' });
      y += 12;
    }

    // Ensure footer is drawn on the last page (autoTable only triggers didDrawPage
    // when it actually renders a table on that page — the grand total alone wouldn't).
    drawPageChrome();

    // ── Save ──
    const filename = this.sanitizeFilename(config.title);
    const dateStr = new Date().toISOString().split('T')[0];
    doc.save(`${filename}_${dateStr}.pdf`);
  }

  private getColumnStyles(columns: { header: string; dataKey: string }[]): Record<number, any> {
    const styles: Record<number, any> = {};
    const tableWidth = 190; // A4 (210mm) - 10mm margins each side

    // Assign proportional weight to each column based on content type
    const widthWeights: Record<string, number> = {
      // Wide columns — addresses, descriptions, multi-value text
      'address': 3, '_address': 3.5, 'startAddress': 3, 'location': 2.5,
      'description': 3, 'incidentType': 2.5,
      // Medium columns — names, dates, formatted values
      'vehicleName': 2, 'vehicle': 2, 'supplierName': 2,
      'startTime': 1.8, 'endTime': 1.8, 'time': 1.8, 'date': 1.5,
      'period': 1.8, 'dayOfWeek': 1.2,
      'duration': 1.2, 'drivingTime': 1.5,
      'reference': 1.5, 'typeLabel': 1.5, '_typeLabel': 1.5,
      'severityLabel': 1.3, 'status': 1.2, 'eventType': 1.3,
      // Narrow columns — numbers, short values
      'distance': 1.2, 'speed': 1, 'maxSpeed': 1, 'limit': 1,
      'fuelPercent': 1, 'fuelChange': 1, 'odometer': 1.2, 'mileage': 1.2,
      'tripCount': 0.8, 'eventNumber': 0.6, 'score': 0.8,
      'avgDaily': 1.2, 'activeDays': 1,
      'laborCostFormatted': 1.2, 'partsCostFormatted': 1.2,
      'totalCostFormatted': 1.2, 'costFormatted': 1.2,
      'fuelEstimated': 1.2, 'costEstimated': 1.2, 'avgConsumption': 1.2,
      'consumption': 1, 'plate': 1.2, 'fuel': 1, 'cost': 1,
      '_type': 1, 'type': 1, 'value': 1, 'trips': 0.8, 'stops': 0.8,
    };

    const defaultWeight = 1.2;
    const weights = columns.map(col => widthWeights[col.dataKey] || defaultWeight);
    const totalWeight = weights.reduce((sum, w) => sum + w, 0);

    columns.forEach((_col, i) => {
      styles[i] = { cellWidth: (weights[i] / totalWeight) * tableWidth };
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
        // Calypso 7 (correction client) : la colonne \u00ab Type \u00bb n a aucune
        // valeur informative quand les lignes Arret sont deja filtrees a
        // la source (PDF n affiche que les vrais trajets). On la retire.
        return [
          ...(options?.allVehicles ? [{ header: 'V\u00e9hicule', dataKey: 'vehicleName' }] : []),
          { header: '#', dataKey: '_tripNumber' },
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
          // Numéro de trajet seul (T1, T2…). Les pseudos-lignes « Arret »
          // qui polluaient le PDF sont filtrees a la source dans
          // reports.component.ts.
          '_tripNumber': (_v: any, row: any) => `T${row.tripNumber}`,
          '_address': (_v: any, row: any) => `${row.startAddress || ''} → ${row.endAddress || ''}`
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
