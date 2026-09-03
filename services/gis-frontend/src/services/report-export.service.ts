import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../environments/environment';
import { ToastService } from './toast.service';

/**
 * Export des rapports en CSV et en Excel.
 *
 * Recette client : « dans tous les rapports il faut avoir un export Excel, PDF
 * et csv ». Le PDF existait déjà (PdfExportService) ; ce service ajoute les deux
 * autres formats en réutilisant EXACTEMENT la même description de rapport
 * (titre, colonnes, lignes, formateurs) pour qu'un rapport n'ait jamais à
 * décrire ses colonnes deux fois — d'où la forme identique à PdfReportConfig.
 *
 * Le CSV est produit dans le navigateur (aucune dépendance ajoutée) ; le XLSX
 * est mis en forme par l'API (ClosedXML) à partir des lignes envoyées.
 */

export interface ReportExportColumn {
  header: string;
  dataKey: string;
}

/** Même forme que PdfReportConfig : un rapport décrit ses colonnes une seule fois. */
export interface ReportExportConfig {
  title: string;
  subtitle?: string;
  vehicleName?: string;
  dateRange?: string;
  statistics?: Record<string, string>;
  columns: ReportExportColumn[];
  data: any[];
  formatters?: Record<string, (value: any, row: any) => string>;
}

@Injectable({ providedIn: 'root' })
export class ReportExportService {

  /** Séparateur attendu par Excel en configuration française. */
  private readonly separator = ';';

  constructor(
    private http: HttpClient,
    private toast: ToastService
  ) {}

  // ───────────────────────────────── CSV ─────────────────────────────────

  /**
   * Génère et télécharge le CSV du rapport, entièrement côté navigateur.
   *
   * Deux détails non négociables sous Excel français : le séparateur est le
   * point-virgule, et le fichier commence par un BOM UTF-8 — sans lui Excel
   * lit le fichier en ANSI et tous les accents deviennent illisibles.
   */
  exportCsv(config: ReportExportConfig): void {
    const sep = this.separator;
    const lines: string[] = [];

    // Préambule : les mêmes métadonnées que l'en-tête du PDF, pour qu'un
    // fichier isolé reste identifiable (quel rapport, quel véhicule, quand).
    lines.push(this.csvText(config.title));
    if (config.subtitle) lines.push(this.csvText(config.subtitle));
    if (config.vehicleName) lines.push(`${this.csvText('Véhicule')}${sep}${this.csvText(config.vehicleName)}`);
    if (config.dateRange) lines.push(`${this.csvText('Période')}${sep}${this.csvText(config.dateRange)}`);
    if (config.statistics) {
      for (const [label, value] of Object.entries(config.statistics)) {
        lines.push(`${this.csvText(label)}${sep}${this.csvText(String(value ?? ''))}`);
      }
    }
    lines.push('');

    const columns = config.columns || [];
    lines.push(columns.map(c => this.csvText(c.header)).join(sep));

    for (const row of config.data || []) {
      lines.push(columns.map(c => this.csvValue(this.cellValue(config, c.dataKey, row))).join(sep));
    }

    const csv = '\uFEFF' + lines.join('\r\n');
    this.downloadBlob(
      new Blob([csv], { type: 'text/csv;charset=utf-8;' }),
      this.buildFilename(config.title, 'csv')
    );
  }

  // ──────────────────────────────── Excel ────────────────────────────────

  /**
   * Demande à l'API de mettre en forme les lignes déjà affichées et télécharge
   * le .xlsx obtenu. Le serveur ne relit aucune donnée : il ne fait que
   * transformer en classeur ce que le client lui envoie.
   */
  exportXlsx(config: ReportExportConfig): void {
    const columns = (config.columns || []).map(c => ({ header: c.header, dataKey: c.dataKey }));
    const rows = (config.data || []).map(row => {
      const out: Record<string, string | number> = {};
      for (const col of columns) out[col.dataKey] = this.cellValue(config, col.dataKey, row);
      return out;
    });

    this.http.post(`${environment.apiUrl}/reports/export/xlsx`,
      { title: config.title, columns, rows },
      { responseType: 'blob' }
    ).subscribe({
      next: (blob: Blob) => this.downloadBlob(blob, this.buildFilename(config.title, 'xlsx')),
      error: (err: unknown) => {
        console.error('Export Excel échoué', err);
        this.toast.error('Export Excel', "Le fichier Excel n'a pas pu être généré.");
      }
    });
  }

  // ─────────────────────────────── Valeurs ───────────────────────────────

  /**
   * Valeur d'une cellule, dans le même ordre de priorité que le PDF : le
   * formateur du rapport s'il existe, sinon la donnée brute. Les nombres
   * restent des nombres pour qu'Excel puisse les additionner.
   */
  private cellValue(config: ReportExportConfig, dataKey: string, row: any): string | number {
    const formatter = config.formatters?.[dataKey];
    if (formatter) {
      try {
        const formatted = formatter(row?.[dataKey], row);
        return formatted === null || formatted === undefined ? '' : String(formatted);
      } catch {
        return '';
      }
    }

    const value = row?.[dataKey];
    if (value === null || value === undefined) return '';
    if (typeof value === 'number') return Number.isFinite(value) ? value : '';
    if (typeof value === 'boolean') return value ? 'Oui' : 'Non';
    if (value instanceof Date) return this.formatDateTime(value);
    if (typeof value === 'object') return '';

    const text = String(value);
    // Beaucoup de lignes de rapport portent déjà un nombre sous forme de texte
    // (résultat d'un .toFixed()). On le rend numérique pour Excel, sauf quand
    // un zéro de tête indique une référence (« 00123 » n'est pas un nombre).
    if (/^-?(0|[1-9]\d*)(\.\d+)?$/.test(text)) {
      const parsed = Number(text);
      if (Number.isFinite(parsed)) return parsed;
    }
    return text;
  }

  /** Nombre au format français : virgule décimale, aucun séparateur de milliers
   *  (Excel refuse de reconnaître un nombre qui en contient dans un CSV). */
  private csvNumber(value: number): string {
    if (!Number.isFinite(value)) return '';
    const rounded = Number.isInteger(value) ? value : Number(value.toFixed(6));
    return String(rounded).replace('.', ',');
  }

  private csvValue(value: string | number): string {
    return typeof value === 'number' ? this.csvNumber(value) : this.csvText(value);
  }

  /** Échappement CSV : guillemets doublés dès qu'une cellule contient un
   *  guillemet, un point-virgule ou un retour à la ligne. */
  private csvText(value: string): string {
    const text = (value ?? '').toString();
    return /["\r\n;]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
  }

  private formatDateTime(date: Date): string {
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${pad(date.getDate())}/${pad(date.getMonth() + 1)}/${date.getFullYear()} ` +
           `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  }

  // ──────────────────────────── Téléchargement ────────────────────────────

  /** `<titre-slugifie>-AAAA-MM-JJ.<ext>` */
  private buildFilename(title: string, extension: string): string {
    const slug = (title || 'rapport')
      .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-zA-Z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .toLowerCase() || 'rapport';
    const now = new Date();
    const pad = (n: number) => String(n).padStart(2, '0');
    return `${slug}-${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}.${extension}`;
  }

  private downloadBlob(blob: Blob, filename: string): void {
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    // Révocation différée : Safari annule le téléchargement si l'URL objet
    // disparaît avant que le navigateur ait fini de lire le blob.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
}
