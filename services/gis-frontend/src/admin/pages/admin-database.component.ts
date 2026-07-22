import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService } from '../services/admin.service';

@Component({
  selector: 'admin-database',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Base de données">
      <div class="db-page">

        <!-- ══ Stockage ══ -->
        <div class="stats-row" *ngIf="storage">
          <div class="stat-card sc-indigo">
            <span class="sc-num">{{ humanSize(storage.dbSizeBytes) }}</span>
            <span class="sc-lbl">Taille de la base</span>
          </div>
          <div class="stat-card sc-cyan">
            <span class="sc-num">{{ humanSize(storage.backupsBytes) }}</span>
            <span class="sc-lbl">Sauvegardes stockées</span>
          </div>
          <div class="stat-card" [class.sc-green]="diskUsedPct() < 75" [class.sc-amber]="diskUsedPct() >= 75 && diskUsedPct() < 90" [class.sc-red]="diskUsedPct() >= 90">
            <span class="sc-num">{{ humanSize(storage.diskFreeBytes) }}</span>
            <span class="sc-lbl">Espace disque restant</span>
            <div class="sc-bar" *ngIf="storage.diskTotalBytes > 0">
              <div class="sc-bar-fill" [style.width.%]="diskUsedPct()"
                   [class.bf-amber]="diskUsedPct() >= 75 && diskUsedPct() < 90" [class.bf-red]="diskUsedPct() >= 90"></div>
            </div>
            <span class="sc-sub" *ngIf="storage.diskTotalBytes > 0">{{ diskUsedPct() }} % utilisés sur {{ humanSize(storage.diskTotalBytes) }}</span>
          </div>
        </div>

        <!-- ══ Sauvegardes ══ -->
        <section class="card">
          <div class="card-head">
            <div>
              <h2 class="sec-title">Sauvegardes</h2>
              <p class="sec-sub">Copie complète et restaurable de la base (format pg_dump). Une sauvegarde automatique est réalisée chaque nuit.</p>
            </div>
            <button class="btn-primary" (click)="createBackup()" [disabled]="creating">
              {{ creating ? 'Sauvegarde en cours…' : '+ Créer une sauvegarde' }}
            </button>
          </div>

          <div class="empty" *ngIf="!loadingBackups && backups.length === 0">Aucune sauvegarde pour l'instant.</div>
          <div class="empty" *ngIf="loadingBackups">Chargement…</div>

          <table class="tbl" *ngIf="backups.length > 0">
            <thead><tr><th>Nom</th><th>Type</th><th>Taille</th><th>Créée le</th><th class="ta-r">Actions</th></tr></thead>
            <tbody>
              <tr *ngFor="let b of backups">
                <td class="mono">{{ b.name }}</td>
                <td><span class="chip" [class.chip-auto]="isAuto(b)">{{ backupType(b) }}</span></td>
                <td>{{ humanSize(b.sizeBytes) }}</td>
                <td>{{ b.createdAt | date:'dd/MM/yyyy HH:mm' }}</td>
                <td class="ta-r">
                  <button class="link-btn" (click)="download(b)">Télécharger</button>
                  <button class="link-btn danger" (click)="deleteBackup(b)">Supprimer</button>
                </td>
              </tr>
            </tbody>
          </table>
        </section>

        <!-- ══ Purge de l'historique ══ -->
        <section class="card danger-card">
          <div class="card-head">
            <div>
              <h2 class="sec-title">Purger l'historique ancien</h2>
              <p class="sec-sub">Supprime définitivement les données d'historique plus anciennes que la limite choisie. Les données opérationnelles (véhicules, sociétés, abonnements, utilisateurs, dépenses) ne sont <b>jamais</b> touchées.</p>
            </div>
          </div>

          <div class="purge-controls">
            <label class="fld">
              <span>Supprimer les données de plus de</span>
              <select [(ngModel)]="months" (change)="resetPreview()">
                <option [ngValue]="3">3 mois</option>
                <option [ngValue]="6">6 mois</option>
                <option [ngValue]="12">12 mois</option>
                <option [ngValue]="24">24 mois</option>
              </select>
            </label>
          </div>

          <div class="tables-grid">
            <label class="table-check" *ngFor="let t of purgeableTables" [class.checked]="selected[t.table]">
              <input type="checkbox" [(ngModel)]="selected[t.table]" (change)="resetPreview()" />
              <span>{{ t.label }}</span>
            </label>
          </div>

          <button class="btn-outline" (click)="preview()" [disabled]="previewing || selectedCount() === 0">
            {{ previewing ? 'Calcul…' : 'Prévisualiser ce qui sera supprimé' }}
          </button>

          <!-- Résultat de la prévisualisation -->
          <div class="preview-box" *ngIf="previewRows">
            <div class="preview-line" *ngFor="let r of previewRows">
              <span class="pl-label">{{ r.label }}</span>
              <span class="pl-count"><b>{{ r.rowsToDelete | number }}</b> lignes à supprimer <span class="pl-total">sur {{ r.totalRows | number }}</span></span>
            </div>
            <div class="preview-total" *ngIf="totalToDelete() > 0">
              Total : <b>{{ totalToDelete() | number }}</b> lignes seront définitivement supprimées.
            </div>
            <div class="preview-empty" *ngIf="totalToDelete() === 0">Rien à supprimer sur cette période — aucune donnée plus ancienne que {{ months }} mois.</div>
          </div>

          <!-- Confirmation forte (n'apparaît qu'après prévisualisation avec des lignes) -->
          <div class="confirm-box" *ngIf="previewRows && totalToDelete() > 0">
            <p class="warn">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
              Action <b>irréversible</b>. Une sauvegarde de sécurité sera créée automatiquement avant la suppression.
              Pour confirmer, tapez <b>{{ confirmPhrase }}</b> ci-dessous.
            </p>
            <div class="confirm-row">
              <input type="text" [(ngModel)]="confirmInput" [placeholder]="confirmPhrase" class="confirm-input" />
              <button class="btn-danger" (click)="runPurge()" [disabled]="purging || confirmInput !== confirmPhrase">
                {{ purging ? 'Suppression…' : 'Purger définitivement' }}
              </button>
            </div>
          </div>

          <div class="purge-result" *ngIf="purgeResult">
            <b>Purge terminée.</b> Sauvegarde de sécurité : <span class="mono">{{ purgeResult.safetyBackup }}</span>.
            <span *ngFor="let k of resultKeys()"> {{ k }} : {{ purgeResult.deleted[k] | number }} supprimées.</span>
          </div>
        </section>

      </div>
    </admin-layout>
  `,
  styles: [`
    .db-page { display: flex; flex-direction: column; gap: 22px; max-width: 1000px; margin: 0 auto; }

    /* Cartes de stockage */
    .stats-row { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 14px; }
    .stat-card { background: var(--adm-card); border: 1px solid var(--adm-border); border-left-width: 3px; border-radius: 14px; box-shadow: var(--adm-shadow); padding: 16px 18px; display: flex; flex-direction: column; gap: 3px; }
    .sc-indigo { border-left-color: var(--adm-indigo); }
    .sc-cyan { border-left-color: var(--adm-cyan); }
    .sc-green { border-left-color: var(--adm-green); }
    .sc-amber { border-left-color: var(--adm-amber); }
    .sc-red { border-left-color: var(--adm-red); }
    .sc-num { font-size: 24px; font-weight: 800; color: var(--adm-ink); font-variant-numeric: tabular-nums; letter-spacing: -.01em; }
    .sc-lbl { font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--adm-sub); }
    .sc-bar { height: 6px; background: var(--adm-track); border-radius: 999px; margin-top: 8px; overflow: hidden; }
    .sc-bar-fill { height: 100%; background: var(--adm-green); border-radius: 999px; transition: width .4s ease; }
    .sc-bar-fill.bf-amber { background: var(--adm-amber); }
    .sc-bar-fill.bf-red { background: var(--adm-red); }
    .sc-sub { font-size: 11.5px; color: var(--adm-sub); margin-top: 4px; }
    .card { background: var(--adm-card); border: 1px solid var(--adm-border); border-radius: 16px; box-shadow: var(--adm-shadow); padding: 24px; }
    .danger-card { border-color: rgba(220,38,38,.25); }
    .card-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; margin-bottom: 18px; }
    .sec-title { margin: 0 0 4px; font-size: 16px; font-weight: 800; color: var(--adm-ink); letter-spacing: -.01em; }
    .sec-sub { margin: 0; font-size: 13px; color: var(--adm-sub); max-width: 640px; line-height: 1.5; }

    .btn-primary { background: var(--adm-indigo); color: #fff; border: none; border-radius: 10px; padding: 10px 18px; font-size: 13.5px; font-weight: 600; cursor: pointer; transition: all .2s; white-space: nowrap; }
    .btn-primary:hover:not(:disabled) { background: var(--adm-indigo-ink); }
    .btn-primary:disabled { opacity: .6; cursor: wait; }
    .btn-outline { background: #fff; color: var(--adm-ink); border: 1px solid var(--adm-border); border-radius: 10px; padding: 10px 16px; font-size: 13.5px; font-weight: 600; cursor: pointer; transition: all .2s; }
    .btn-outline:hover:not(:disabled) { border-color: var(--adm-indigo); color: var(--adm-indigo); }
    .btn-outline:disabled { opacity: .5; cursor: not-allowed; }
    .btn-danger { background: var(--adm-red); color: #fff; border: none; border-radius: 10px; padding: 10px 18px; font-size: 13.5px; font-weight: 700; cursor: pointer; transition: all .2s; white-space: nowrap; }
    .btn-danger:hover:not(:disabled) { background: var(--adm-red-ink); }
    .btn-danger:disabled { opacity: .5; cursor: not-allowed; }

    .tbl { width: 100%; border-collapse: collapse; }
    .tbl thead th { text-align: left; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--adm-sub); background: #f8fafc; padding: 10px 12px; border-bottom: 1px solid var(--adm-border); }
    .tbl td { padding: 11px 12px; font-size: 13px; color: var(--adm-ink); border-bottom: 1px solid #eef2f7; }
    .tbl tbody tr:hover { background: #f8fafc; }
    .ta-r { text-align: right; }
    .mono { font-family: 'Courier New', monospace; font-size: 12px; }
    .chip { display: inline-block; padding: 3px 10px; border-radius: 999px; font-size: 11px; font-weight: 700; background: rgba(79,70,229,.10); color: var(--adm-indigo-ink); }
    .chip-auto { background: rgba(5,150,105,.10); color: var(--adm-green-ink); }
    .link-btn { background: none; border: none; color: var(--adm-indigo); font-size: 13px; font-weight: 600; cursor: pointer; padding: 4px 8px; }
    .link-btn:hover { text-decoration: underline; }
    .link-btn.danger { color: var(--adm-red); }
    .empty { padding: 24px; text-align: center; color: var(--adm-sub); font-size: 13px; }

    .purge-controls { margin-bottom: 14px; }
    .fld { display: flex; align-items: center; gap: 10px; font-size: 13.5px; color: var(--adm-ink); font-weight: 600; }
    .fld select { padding: 9px 12px; border: 1px solid var(--adm-border); border-radius: 10px; font-size: 13.5px; color: var(--adm-ink); background: #fff; outline: none; }
    .fld select:focus { border-color: var(--adm-indigo); box-shadow: 0 0 0 3px rgba(79,70,229,.12); }

    .tables-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 10px; margin-bottom: 16px; }
    .table-check { display: flex; align-items: center; gap: 10px; padding: 11px 14px; border: 1px solid var(--adm-border); border-radius: 10px; cursor: pointer; font-size: 13px; color: var(--adm-ink); transition: all .15s; }
    .table-check.checked { border-color: var(--adm-indigo); background: rgba(79,70,229,.05); }
    .table-check input { accent-color: var(--adm-indigo); width: 16px; height: 16px; }

    .preview-box { margin-top: 16px; padding: 16px; background: #f8fafc; border: 1px solid var(--adm-border); border-radius: 12px; }
    .preview-line { display: flex; justify-content: space-between; padding: 6px 0; font-size: 13px; }
    .pl-label { color: var(--adm-sub); }
    .pl-total { color: var(--adm-sub); font-weight: 400; }
    .preview-total { margin-top: 10px; padding-top: 10px; border-top: 1px solid var(--adm-border); font-size: 14px; color: var(--adm-red-ink); }
    .preview-empty { font-size: 13px; color: var(--adm-green-ink); }

    .confirm-box { margin-top: 16px; padding: 16px; background: rgba(220,38,38,.05); border: 1px solid rgba(220,38,38,.2); border-radius: 12px; }
    .warn { display: flex; align-items: flex-start; gap: 10px; margin: 0 0 12px; font-size: 13px; color: #7f1d1d; line-height: 1.5; }
    .warn svg { color: var(--adm-red); flex-shrink: 0; margin-top: 2px; }
    .confirm-row { display: flex; gap: 10px; }
    .confirm-input { flex: 1; max-width: 260px; padding: 10px 14px; border: 1px solid var(--adm-border); border-radius: 10px; font-size: 14px; letter-spacing: .1em; text-transform: uppercase; outline: none; }
    .confirm-input:focus { border-color: var(--adm-red); box-shadow: 0 0 0 3px rgba(220,38,38,.12); }

    .purge-result { margin-top: 16px; padding: 14px 16px; background: rgba(5,150,105,.08); border: 1px solid rgba(5,150,105,.25); border-radius: 12px; font-size: 13px; color: var(--adm-green-ink); }
  `]
})
export class AdminDatabaseComponent implements OnInit {
  backups: any[] = [];
  loadingBackups = true;
  creating = false;
  storage: any = null;

  purgeableTables: { table: string; label: string }[] = [];
  confirmPhrase = 'SUPPRIMER';
  selected: Record<string, boolean> = {};
  months = 12;

  previewing = false;
  previewRows: any[] | null = null;

  purging = false;
  confirmInput = '';
  purgeResult: any = null;

  constructor(private router: Router, private admin: AdminService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    if (!this.admin.isAuthenticated()) { this.router.navigate(['/admin/login']); return; }
    this.loadBackups();
    this.admin.getPurgeableTables().subscribe({
      next: (r) => { this.purgeableTables = r.tables || []; this.confirmPhrase = r.confirmPhrase || 'SUPPRIMER'; this.cdr.detectChanges(); },
      error: () => {}
    });
  }

  loadBackups() {
    this.loadingBackups = true;
    this.admin.listBackups().subscribe({
      next: (b) => { this.backups = b || []; this.loadingBackups = false; this.cdr.detectChanges(); },
      error: () => { this.loadingBackups = false; this.cdr.detectChanges(); }
    });
    // Le stockage évolue avec chaque backup/purge : rafraîchi en même temps.
    this.admin.getDatabaseStorage().subscribe({
      next: (s) => { this.storage = s; this.cdr.detectChanges(); },
      error: () => {}
    });
  }

  diskUsedPct(): number {
    if (!this.storage?.diskTotalBytes) return 0;
    const used = this.storage.diskTotalBytes - this.storage.diskFreeBytes;
    return Math.round(used / this.storage.diskTotalBytes * 100);
  }

  createBackup() {
    this.creating = true;
    this.admin.createBackup().subscribe({
      next: () => { this.creating = false; this.loadBackups(); },
      error: (e) => { this.creating = false; alert(e?.error?.message || 'La sauvegarde a échoué.'); this.cdr.detectChanges(); }
    });
  }

  download(b: any) {
    this.admin.downloadBackup(b.name).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url; a.download = b.name; a.click();
        URL.revokeObjectURL(url);
      },
      error: () => alert('Téléchargement impossible.')
    });
  }

  deleteBackup(b: any) {
    if (!confirm(`Supprimer la sauvegarde ${b.name} ?`)) return;
    this.admin.deleteBackup(b.name).subscribe({ next: () => this.loadBackups(), error: () => alert('Suppression impossible.') });
  }

  selectedTableKeys(): string[] { return this.purgeableTables.filter(t => this.selected[t.table]).map(t => t.table); }
  selectedCount(): number { return this.selectedTableKeys().length; }
  resetPreview() { this.previewRows = null; this.confirmInput = ''; this.purgeResult = null; }

  preview() {
    this.previewing = true; this.purgeResult = null;
    this.admin.previewPurge(this.months, this.selectedTableKeys()).subscribe({
      next: (r) => { this.previewRows = r.rows || []; this.previewing = false; this.cdr.detectChanges(); },
      error: (e) => { this.previewing = false; alert(e?.error?.message || 'Prévisualisation impossible.'); this.cdr.detectChanges(); }
    });
  }

  totalToDelete(): number { return (this.previewRows || []).reduce((s, r) => s + (r.rowsToDelete || 0), 0); }

  runPurge() {
    if (this.confirmInput !== this.confirmPhrase) return;
    this.purging = true;
    this.admin.runPurge(this.months, this.selectedTableKeys(), this.confirmInput).subscribe({
      next: (r) => {
        this.purging = false; this.purgeResult = r; this.previewRows = null; this.confirmInput = '';
        this.loadBackups(); // le backup de sécurité apparaît dans la liste
        this.cdr.detectChanges();
      },
      error: (e) => { this.purging = false; alert(e?.error?.message || 'La purge a échoué.'); this.cdr.detectChanges(); }
    });
  }

  resultKeys(): string[] { return this.purgeResult?.deleted ? Object.keys(this.purgeResult.deleted) : []; }
  isAuto(b: any): boolean { return b.name?.startsWith('auto_') || b.name?.startsWith('weekly_'); }
  backupType(b: any): string {
    if (b.name?.startsWith('weekly_')) return 'Auto (hebdo)';
    if (b.name?.startsWith('auto_')) return 'Auto (nuit)';
    if (b.name?.startsWith('presuppr')) return 'Pré-purge';
    return 'Manuelle';
  }
  humanSize(bytes: number): string {
    if (!bytes) return '0 o';
    const u = ['o', 'Ko', 'Mo', 'Go']; let i = 0; let n = bytes;
    while (n >= 1024 && i < u.length - 1) { n /= 1024; i++; }
    return `${n.toFixed(1)} ${u[i]}`;
  }
}
