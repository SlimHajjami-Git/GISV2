import { Injectable } from '@angular/core';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { AccidentReportDto } from './api.service';

/**
 * Calypso 6 (P9) — generates the structured accident report PDF (Blob)
 * that gets attached to an `accident_events` row right after the admin
 * clicks "Confirmer" in the decision modal.
 *
 * The document is intentionally simple and human-readable — it is meant
 * to be opened by the admin or forwarded to an insurance expert. The
 * damages section is left blank when the report is generated at confirm
 * time (the admin fills it on the /rapport-accident page afterwards).
 *
 * Reuses the same Helvetica-only fonts as `PdfExportService` — Helvetica
 * supports Latin-1 which covers French accents without needing an
 * embedded font file.
 */
@Injectable({ providedIn: 'root' })
export class AccidentPdfService {
  /**
   * Build the accident report PDF as a Blob, ready to be uploaded.
   * Never throws — falls back to a minimal "context unavailable" page
   * if any data is missing so the auto-attach never blocks the modal.
   */
  generate(report: AccidentReportDto): Blob {
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
    const pageWidth = doc.internal.pageSize.getWidth();
    const margin = 14;
    let cursorY = 18;

    // Header band ---------------------------------------------------------
    doc.setFillColor(220, 38, 38); // red-600 — reflects accident severity
    doc.rect(0, 0, pageWidth, 22, 'F');
    doc.setTextColor(255, 255, 255);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(16);
    doc.text("Rapport d'accident", margin, 14);
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    doc.text(`Référence: ${report.referenceCode ?? `ACC-${report.id}`}`, pageWidth - margin, 14, { align: 'right' });
    cursorY = 30;

    // Identification section ---------------------------------------------
    doc.setTextColor(15, 23, 42);
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.text("Identification du véhicule", margin, cursorY);
    cursorY += 5;

    const idRows: [string, string][] = [
      ['Véhicule', report.vehicleLabel ?? '—'],
      ['Identifiant interne', report.vehicleId != null ? `#${report.vehicleId}` : '—'],
      ['Boîtier GPS (IMEI)', report.deviceUid || '—'],
    ];
    autoTable(doc, {
      startY: cursorY,
      head: [],
      body: idRows,
      theme: 'plain',
      styles: { font: 'helvetica', fontSize: 10, cellPadding: 1.5 },
      columnStyles: { 0: { fontStyle: 'bold', cellWidth: 55, textColor: [71, 85, 105] }, 1: { textColor: [15, 23, 42] } },
      margin: { left: margin, right: margin },
    });
    cursorY = (doc as any).lastAutoTable.finalY + 6;

    // Incident context ---------------------------------------------------
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.text("Contexte de l'accident", margin, cursorY);
    cursorY += 5;

    const incidentDate = report.incidentAt ? new Date(report.incidentAt) : null;
    const dateLabel = incidentDate
      ? `${incidentDate.toLocaleDateString('fr-FR')} à ${incidentDate.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`
      : '—';
    const locationParts = [report.locationCommune, report.locationGovernorate, report.locationRoadType]
      .filter(Boolean)
      .join(', ');

    const ctxRows: [string, string][] = [
      ['Date / heure', dateLabel],
      ['Latitude', report.latitude?.toFixed(6) ?? '—'],
      ['Longitude', report.longitude?.toFixed(6) ?? '—'],
      ['Localisation', locationParts || '—'],
      ['Score de confiance', `${report.confidence ?? 0}/100`],
    ];
    autoTable(doc, {
      startY: cursorY,
      body: ctxRows,
      theme: 'plain',
      styles: { font: 'helvetica', fontSize: 10, cellPadding: 1.5 },
      columnStyles: { 0: { fontStyle: 'bold', cellWidth: 55, textColor: [71, 85, 105] } },
      margin: { left: margin, right: margin },
    });
    cursorY = (doc as any).lastAutoTable.finalY + 6;

    // Synthesis ----------------------------------------------------------
    if (report.synthesisText) {
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(12);
      doc.text('Synthèse', margin, cursorY);
      cursorY += 5;
      doc.setFont('helvetica', 'normal');
      doc.setFontSize(10);
      const lines = doc.splitTextToSize(report.synthesisText, pageWidth - 2 * margin);
      doc.text(lines, margin, cursorY);
      cursorY += lines.length * 5 + 4;
    }

    // Story timeline -----------------------------------------------------
    if (report.story && report.story.length > 0) {
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(12);
      doc.text('Chronologie', margin, cursorY);
      cursorY += 5;
      autoTable(doc, {
        startY: cursorY,
        head: [['Heure', 'Événement']],
        body: report.story.map(s => [s.time, `${s.title}\n${s.body}`]),
        theme: 'striped',
        styles: { font: 'helvetica', fontSize: 9, cellPadding: 2 },
        headStyles: { fillColor: [220, 38, 38], textColor: [255, 255, 255] },
        columnStyles: { 0: { cellWidth: 28, fontStyle: 'bold' } },
        margin: { left: margin, right: margin },
      });
      cursorY = (doc as any).lastAutoTable.finalY + 6;
    }

    // Indicators ---------------------------------------------------------
    if (report.indicators && report.indicators.length > 0) {
      doc.setFont('helvetica', 'bold');
      doc.setFontSize(12);
      doc.text('Indicateurs techniques', margin, cursorY);
      cursorY += 5;
      autoTable(doc, {
        startY: cursorY,
        head: [['Indicateur', 'Valeur']],
        body: report.indicators.map(i => [i.label, i.hint ? `${i.value}\n${i.hint}` : i.value]),
        theme: 'grid',
        styles: { font: 'helvetica', fontSize: 9, cellPadding: 2 },
        headStyles: { fillColor: [71, 85, 105], textColor: [255, 255, 255] },
        margin: { left: margin, right: margin },
      });
      cursorY = (doc as any).lastAutoTable.finalY + 6;
    }

    // Damages placeholder ------------------------------------------------
    // The PDF is generated at confirm time, BEFORE the admin fills the
    // damages form. We render an empty section the admin can print and
    // hand to an expert if they need a paper trail.
    if (cursorY > 240) { doc.addPage(); cursorY = 20; }
    doc.setFont('helvetica', 'bold');
    doc.setFontSize(12);
    doc.text("Évaluation des dégâts (à compléter)", margin, cursorY);
    cursorY += 5;
    doc.setFont('helvetica', 'normal');
    doc.setFontSize(10);
    const blanks = [
      'Description :',
      '',
      '',
      '',
      'Sévérité (légère / modérée / grave / totale) :',
      '',
      'Coût estimé de la réparation :',
      '',
      "N° de sinistre (assurance) :",
      '',
      'Notes internes :',
      '',
      '',
    ];
    blanks.forEach(line => {
      doc.text(line, margin, cursorY);
      cursorY += 6;
    });

    // Footer
    doc.setFontSize(8);
    doc.setTextColor(148, 163, 184);
    doc.text(
      `Généré automatiquement le ${new Date().toLocaleDateString('fr-FR')} à ${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })} — Calypso GIS`,
      pageWidth / 2,
      doc.internal.pageSize.getHeight() - 8,
      { align: 'center' }
    );

    return doc.output('blob');
  }
}
