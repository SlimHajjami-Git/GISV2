import { Component, OnInit, OnDestroy, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Subject } from 'rxjs';
import { takeUntil } from 'rxjs/operators';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, Estimate, EstimateInput, Client } from '../services/admin.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'admin-estimates',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Devis">
      <div class="devis-page">

        <!-- ══ En-tête : stats + bouton ══ -->
        <div class="page-head">
          <div class="stats-row">
            <div class="stat-card sc-indigo">
              <span class="sc-num">{{ estimates.length }}</span>
              <span class="sc-lbl">Devis</span>
            </div>
            <div class="stat-card sc-amber">
              <span class="sc-num">{{ countByStatus('draft') + countByStatus('sent') }}</span>
              <span class="sc-lbl">En attente</span>
            </div>
            <div class="stat-card sc-green">
              <span class="sc-num">{{ fmtMoney(totalAccepted()) }}</span>
              <span class="sc-lbl">Montant accepté</span>
            </div>
          </div>
          <button class="btn-primary" (click)="openCreate()">+ Nouveau devis</button>
        </div>

        <!-- ══ Liste ══ -->
        <div class="card">
          <div class="empty" *ngIf="!loading && estimates.length === 0">Aucun devis — créez le premier.</div>
          <div class="empty" *ngIf="loading">Chargement…</div>
          <table class="tbl" *ngIf="estimates.length > 0">
            <thead>
              <tr><th>Numéro</th><th>Client</th><th>Date</th><th>Validité</th><th class="ta-r">Total TTC</th><th>Statut</th><th class="ta-r">Actions</th></tr>
            </thead>
            <tbody>
              <tr *ngFor="let e of estimates">
                <td class="mono strong">{{ e.number }}</td>
                <td>
                  <div class="cell-client">
                    <span class="c-name">{{ e.clientName }}</span>
                    <span class="c-sub" *ngIf="e.companyName">{{ e.companyName }}</span>
                  </div>
                </td>
                <td>{{ e.issueDate | date:'dd/MM/yyyy' }}</td>
                <td>
                  <span [class.expired]="isExpired(e)">{{ e.validUntil ? (e.validUntil | date:'dd/MM/yyyy') : '—' }}</span>
                </td>
                <td class="ta-r strong num">{{ fmtMoney(e.total) }}</td>
                <td>
                  <select class="status-select" [class]="'st-' + e.status" [ngModel]="e.status" (ngModelChange)="changeStatus(e, $event)">
                    <option value="draft">Brouillon</option>
                    <option value="sent">Envoyé</option>
                    <option value="accepted">Accepté</option>
                    <option value="rejected">Refusé</option>
                  </select>
                </td>
                <td class="ta-r">
                  <button class="link-btn" (click)="openEdit(e)">Modifier</button>
                  <button class="link-btn" (click)="generatePdf(e)">PDF</button>
                  <button class="link-btn danger" (click)="remove(e)">Supprimer</button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- ══ Éditeur ══ -->
        <div class="modal-overlay" *ngIf="showEditor" (mousedown)="closeEditor()">
          <div class="modal" (mousedown)="$event.stopPropagation()">
            <div class="modal-head">
              <h2>{{ editingId ? 'Modifier le devis' : 'Nouveau devis' }}</h2>
              <button class="close-btn" (click)="closeEditor()">&#10005;</button>
            </div>

            <div class="modal-body">
              <!-- Client -->
              <h4 class="grp-title">Client</h4>
              <div class="grid-2">
                <label class="fld">
                  <span>Société existante (optionnel)</span>
                  <select [(ngModel)]="form.companyId" (change)="prefillFromCompany()">
                    <option [ngValue]="null">— Prospect / saisie libre —</option>
                    <option *ngFor="let c of companies" [ngValue]="c.id">{{ c.name }}</option>
                  </select>
                </label>
                <label class="fld"><span>Nom du client *</span><input [(ngModel)]="form.clientName" placeholder="Société ou personne" /></label>
                <label class="fld"><span>E-mail</span><input [(ngModel)]="form.clientEmail" type="email" placeholder="contact@client.tn" /></label>
                <label class="fld"><span>Téléphone</span><input [(ngModel)]="form.clientPhone" placeholder="+216 …" /></label>
                <label class="fld span-2"><span>Adresse</span><input [(ngModel)]="form.clientAddress" placeholder="Adresse de facturation" /></label>
              </div>

              <!-- Lignes -->
              <h4 class="grp-title">Lignes du devis</h4>
              <div class="lines">
                <div class="line-head"><span>Désignation</span><span>Qté</span><span>P.U. HT</span><span>Total HT</span><span></span></div>
                <div class="line" *ngFor="let it of form.items; let i = index">
                  <input class="li-desc" [(ngModel)]="it.description" placeholder="Ex: Abonnement GPS mensuel / véhicule" />
                  <input class="li-num" type="number" min="0" step="1" [(ngModel)]="it.quantity" />
                  <input class="li-num" type="number" min="0" step="0.001" [(ngModel)]="it.unitPrice" />
                  <span class="li-total num">{{ fmtMoney(it.quantity * it.unitPrice) }}</span>
                  <button class="li-del" (click)="removeLine(i)" title="Supprimer la ligne">&#10005;</button>
                </div>
                <button class="btn-outline sm" (click)="addLine()">+ Ajouter une ligne</button>
              </div>

              <!-- Conditions + totaux -->
              <div class="bottom-grid">
                <div class="conds">
                  <h4 class="grp-title">Conditions</h4>
                  <label class="fld"><span>Valable jusqu'au</span><input type="date" [(ngModel)]="form.validUntil" /></label>
                  <label class="fld"><span>Remise (%)</span><input type="number" min="0" max="100" [(ngModel)]="form.discountPercent" /></label>
                  <label class="fld"><span>TVA (%)</span><input type="number" min="0" max="100" [(ngModel)]="form.taxPercent" /></label>
                  <label class="fld"><span>Notes / conditions</span><textarea rows="3" [(ngModel)]="form.notes" placeholder="Modalités de paiement, délais, matériel inclus…"></textarea></label>
                </div>
                <div class="totals">
                  <div class="t-line"><span>Sous-total HT</span><b class="num">{{ fmtMoney(subtotal()) }}</b></div>
                  <div class="t-line" *ngIf="form.discountPercent > 0"><span>Remise ({{ form.discountPercent }} %)</span><b class="num">-{{ fmtMoney(discountAmount()) }}</b></div>
                  <div class="t-line"><span>TVA ({{ form.taxPercent }} %)</span><b class="num">{{ fmtMoney(taxAmount()) }}</b></div>
                  <div class="t-line t-total"><span>Total TTC</span><b class="num">{{ fmtMoney(totalTtc()) }}</b></div>
                </div>
              </div>

              <div class="err" *ngIf="editorError">{{ editorError }}</div>
            </div>

            <div class="modal-foot">
              <button class="btn-outline" (click)="closeEditor()">Annuler</button>
              <button class="btn-outline" (click)="save(true)" [disabled]="saving">Enregistrer + PDF</button>
              <button class="btn-primary" (click)="save(false)" [disabled]="saving">{{ saving ? 'Enregistrement…' : 'Enregistrer' }}</button>
            </div>
          </div>
        </div>

      </div>
    </admin-layout>
  `,
  styles: [`
    .devis-page { display: flex; flex-direction: column; gap: 20px; }
    .page-head { display: flex; align-items: flex-end; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    .stats-row { display: flex; gap: 14px; flex-wrap: wrap; }
    .stat-card { background: var(--adm-card); border: 1px solid var(--adm-border); border-left-width: 3px; border-radius: 14px; box-shadow: var(--adm-shadow); padding: 14px 20px; display: flex; flex-direction: column; min-width: 140px; }
    .sc-indigo { border-left-color: var(--adm-indigo); }
    .sc-amber { border-left-color: var(--adm-amber); }
    .sc-green { border-left-color: var(--adm-green); }
    .sc-num { font-size: 22px; font-weight: 800; color: var(--adm-ink); font-variant-numeric: tabular-nums; }
    .sc-lbl { font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--adm-sub); }

    .btn-primary { background: var(--adm-indigo); color: #fff; border: none; border-radius: 10px; padding: 10px 18px; font-size: 13.5px; font-weight: 600; cursor: pointer; transition: all .2s; white-space: nowrap; }
    .btn-primary:hover:not(:disabled) { background: var(--adm-indigo-ink); }
    .btn-primary:disabled { opacity: .6; }
    .btn-outline { background: #fff; color: var(--adm-ink); border: 1px solid var(--adm-border); border-radius: 10px; padding: 10px 16px; font-size: 13.5px; font-weight: 600; cursor: pointer; transition: all .2s; }
    .btn-outline:hover:not(:disabled) { border-color: var(--adm-indigo); color: var(--adm-indigo); }
    .btn-outline.sm { padding: 7px 12px; font-size: 12.5px; }

    .card { background: var(--adm-card); border: 1px solid var(--adm-border); border-radius: 16px; box-shadow: var(--adm-shadow); overflow: hidden; }
    .empty { padding: 40px; text-align: center; color: var(--adm-sub); font-size: 13px; }
    .tbl { width: 100%; border-collapse: collapse; }
    .tbl thead th { text-align: left; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .06em; color: var(--adm-sub); background: #f8fafc; padding: 11px 14px; border-bottom: 1px solid var(--adm-border); }
    .tbl td { padding: 12px 14px; font-size: 13px; color: var(--adm-ink); border-bottom: 1px solid #eef2f7; vertical-align: middle; }
    .tbl tbody tr:hover { background: #f8fafc; }
    .ta-r { text-align: right; }
    .mono { font-family: 'Courier New', monospace; }
    .strong { font-weight: 700; }
    .num { font-variant-numeric: tabular-nums; }
    .cell-client { display: flex; flex-direction: column; }
    .c-name { font-weight: 600; }
    .c-sub { font-size: 11.5px; color: var(--adm-sub); }
    .expired { color: var(--adm-red-ink); font-weight: 600; }
    .link-btn { background: none; border: none; color: var(--adm-indigo); font-size: 12.5px; font-weight: 600; cursor: pointer; padding: 4px 7px; }
    .link-btn:hover { text-decoration: underline; }
    .link-btn.danger { color: var(--adm-red); }

    .status-select { border: 1px solid var(--adm-border); border-radius: 999px; padding: 5px 10px; font-size: 12px; font-weight: 700; cursor: pointer; outline: none; }
    .st-draft { background: rgba(100,116,139,.10); color: var(--adm-slate-ink); }
    .st-sent { background: rgba(8,145,178,.10); color: var(--adm-cyan-ink); }
    .st-accepted { background: rgba(5,150,105,.10); color: var(--adm-green-ink); }
    .st-rejected { background: rgba(220,38,38,.10); color: var(--adm-red-ink); }

    /* Éditeur */
    .modal-overlay { position: fixed; inset: 0; background: rgba(15,23,42,.55); backdrop-filter: blur(4px); display: flex; align-items: center; justify-content: center; z-index: 1000; padding: 20px; }
    .modal { background: #fff; border-radius: 18px; width: 860px; max-width: 96vw; max-height: 92vh; display: flex; flex-direction: column; overflow: hidden; box-shadow: 0 24px 60px -24px rgba(2,6,23,.45); }
    .modal-head { display: flex; align-items: center; justify-content: space-between; padding: 18px 24px; border-bottom: 1px solid var(--adm-border); }
    .modal-head h2 { margin: 0; font-size: 17px; font-weight: 800; color: var(--adm-ink); }
    .close-btn { width: 32px; height: 32px; border: none; background: #f1f5f9; border-radius: 9px; color: var(--adm-sub); cursor: pointer; font-size: 13px; }
    .close-btn:hover { background: #e2e8f0; color: var(--adm-ink); }
    .modal-body { padding: 20px 24px; overflow-y: auto; }
    .modal-foot { display: flex; justify-content: flex-end; gap: 10px; padding: 16px 24px; border-top: 1px solid var(--adm-border); }

    .grp-title { margin: 0 0 12px; font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em; color: var(--adm-sub); display: flex; align-items: center; gap: 8px; }
    .grp-title::before { content: ''; width: 3px; height: 12px; border-radius: 2px; background: var(--adm-indigo); }
    .grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 12px 16px; margin-bottom: 20px; }
    .span-2 { grid-column: span 2; }
    .fld { display: flex; flex-direction: column; gap: 6px; font-size: 12.5px; font-weight: 600; color: var(--adm-slate-ink); }
    .fld input, .fld select, .fld textarea { padding: 9px 12px; border: 1px solid var(--adm-border); border-radius: 10px; font-size: 13.5px; color: var(--adm-ink); outline: none; font-family: inherit; background: #fff; }
    .fld input:focus, .fld select:focus, .fld textarea:focus { border-color: var(--adm-indigo); box-shadow: 0 0 0 3px rgba(79,70,229,.12); }

    .lines { margin-bottom: 20px; }
    .line-head { display: grid; grid-template-columns: 1fr 80px 120px 110px 32px; gap: 8px; font-size: 11px; font-weight: 700; text-transform: uppercase; letter-spacing: .05em; color: var(--adm-sub); padding: 0 2px 6px; }
    .line { display: grid; grid-template-columns: 1fr 80px 120px 110px 32px; gap: 8px; align-items: center; margin-bottom: 8px; }
    .line input { padding: 8px 10px; border: 1px solid var(--adm-border); border-radius: 9px; font-size: 13px; outline: none; }
    .line input:focus { border-color: var(--adm-indigo); box-shadow: 0 0 0 3px rgba(79,70,229,.12); }
    .li-total { text-align: right; font-size: 13px; font-weight: 700; color: var(--adm-ink); }
    .li-del { width: 28px; height: 28px; border: none; background: rgba(220,38,38,.08); color: var(--adm-red); border-radius: 8px; cursor: pointer; font-size: 11px; }
    .li-del:hover { background: rgba(220,38,38,.16); }

    .bottom-grid { display: grid; grid-template-columns: 1fr 280px; gap: 24px; align-items: start; }
    .conds { display: flex; flex-direction: column; gap: 10px; }
    .totals { background: #f8fafc; border: 1px solid var(--adm-border); border-radius: 12px; padding: 16px; display: flex; flex-direction: column; gap: 8px; }
    .t-line { display: flex; justify-content: space-between; font-size: 13px; color: var(--adm-sub); }
    .t-line b { color: var(--adm-ink); }
    .t-total { padding-top: 10px; border-top: 1px solid var(--adm-border); font-size: 15px; }
    .t-total b { color: var(--adm-indigo-ink); font-size: 17px; }

    .err { margin-top: 14px; padding: 11px 14px; background: rgba(220,38,38,.08); border: 1px solid rgba(220,38,38,.25); border-radius: 10px; color: var(--adm-red-ink); font-size: 13px; }

    @media (max-width: 760px) {
      .grid-2, .bottom-grid { grid-template-columns: 1fr; }
      .span-2 { grid-column: span 1; }
    }
  `]
})
export class AdminEstimatesComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  estimates: Estimate[] = [];
  companies: Client[] = [];
  loading = true;

  showEditor = false;
  editingId: number | null = null;
  saving = false;
  editorError = '';
  form = this.emptyForm();

  currency = (environment as any).defaultCurrency || 'DT';

  constructor(private router: Router, private admin: AdminService, private cdr: ChangeDetectorRef) {}

  ngOnInit() {
    if (!this.admin.isAuthenticated()) { this.router.navigate(['/admin/login']); return; }
    this.load();
    this.admin.getClients().pipe(takeUntil(this.destroy$)).subscribe({
      next: (c) => { this.companies = c || []; this.cdr.detectChanges(); },
      error: () => {}
    });
  }

  load() {
    this.loading = true;
    this.admin.getEstimates().pipe(takeUntil(this.destroy$)).subscribe({
      next: (list) => { this.estimates = list || []; this.loading = false; this.cdr.detectChanges(); },
      error: () => { this.loading = false; this.cdr.detectChanges(); }
    });
  }

  // ── Helpers liste ──
  countByStatus(s: string): number { return this.estimates.filter(e => e.status === s).length; }
  totalAccepted(): number { return this.estimates.filter(e => e.status === 'accepted').reduce((sum, e) => sum + e.total, 0); }
  isExpired(e: Estimate): boolean {
    return !!e.validUntil && new Date(e.validUntil).getTime() < Date.now() && e.status !== 'accepted';
  }
  fmtMoney(v: number): string {
    return `${(v || 0).toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${this.currency}`;
  }

  changeStatus(e: Estimate, status: string) {
    this.admin.updateEstimateStatus(e.id, status).subscribe({
      next: () => { e.status = status as any; this.cdr.detectChanges(); },
      error: (err) => { alert(err?.error?.message || 'Changement de statut impossible.'); this.load(); }
    });
  }

  remove(e: Estimate) {
    if (!confirm(`Supprimer le devis ${e.number} (${e.clientName}) ?`)) return;
    this.admin.deleteEstimate(e.id).subscribe({
      next: () => this.load(),
      error: (err) => alert(err?.error?.message || 'Suppression impossible.')
    });
  }

  // ── Éditeur ──
  private emptyForm() {
    return {
      companyId: null as number | null,
      clientName: '',
      clientEmail: '',
      clientPhone: '',
      clientAddress: '',
      validUntil: '',
      discountPercent: 0,
      taxPercent: 19,
      notes: '',
      items: [{ description: '', quantity: 1, unitPrice: 0 }]
    };
  }

  openCreate() {
    this.form = this.emptyForm();
    const in30 = new Date(Date.now() + 30 * 86400000);
    this.form.validUntil = in30.toISOString().slice(0, 10);
    this.editingId = null;
    this.editorError = '';
    this.showEditor = true;
  }

  openEdit(e: Estimate) {
    this.editingId = e.id;
    this.editorError = '';
    this.form = {
      companyId: e.companyId ?? null,
      clientName: e.clientName,
      clientEmail: e.clientEmail || '',
      clientPhone: e.clientPhone || '',
      clientAddress: e.clientAddress || '',
      validUntil: e.validUntil ? e.validUntil.slice(0, 10) : '',
      discountPercent: e.discountPercent,
      taxPercent: e.taxPercent,
      notes: e.notes || '',
      items: e.items.map(i => ({ description: i.description, quantity: i.quantity, unitPrice: i.unitPrice }))
    };
    if (this.form.items.length === 0) this.form.items.push({ description: '', quantity: 1, unitPrice: 0 });
    this.showEditor = true;
  }

  closeEditor() { this.showEditor = false; }

  prefillFromCompany() {
    const c: any = this.companies.find(x => x.id === this.form.companyId);
    if (!c) return;
    this.form.clientName = c.name || this.form.clientName;
    if (c.email) this.form.clientEmail = c.email;
    if (c.phone) this.form.clientPhone = c.phone;
    if (c.address) this.form.clientAddress = c.address;
  }

  addLine() { this.form.items.push({ description: '', quantity: 1, unitPrice: 0 }); }
  removeLine(i: number) {
    this.form.items.splice(i, 1);
    if (this.form.items.length === 0) this.addLine();
  }

  subtotal(): number { return this.form.items.reduce((s, i) => s + (i.quantity || 0) * (i.unitPrice || 0), 0); }
  discountAmount(): number { return this.subtotal() * (this.form.discountPercent || 0) / 100; }
  taxAmount(): number { return (this.subtotal() - this.discountAmount()) * (this.form.taxPercent || 0) / 100; }
  totalTtc(): number { return this.subtotal() - this.discountAmount() + this.taxAmount(); }

  save(withPdf: boolean) {
    this.editorError = '';
    const payload: EstimateInput = {
      companyId: this.form.companyId,
      clientName: this.form.clientName,
      clientEmail: this.form.clientEmail || null,
      clientPhone: this.form.clientPhone || null,
      clientAddress: this.form.clientAddress || null,
      validUntil: this.form.validUntil ? new Date(this.form.validUntil + 'T12:00:00').toISOString() : null,
      discountPercent: this.form.discountPercent || 0,
      taxPercent: this.form.taxPercent ?? 19,
      notes: this.form.notes || null,
      items: this.form.items
        .filter(i => (i.description || '').trim())
        .map(i => ({ description: i.description.trim(), quantity: i.quantity || 1, unitPrice: i.unitPrice || 0 }))
    };

    this.saving = true;
    const obs = this.editingId
      ? this.admin.updateEstimate(this.editingId, payload)
      : this.admin.createEstimate(payload);

    obs.subscribe({
      next: (saved) => {
        this.saving = false;
        this.showEditor = false;
        this.load();
        if (withPdf) this.generatePdf(saved);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.editorError = err?.error?.message || 'Enregistrement impossible.';
        this.cdr.detectChanges();
      }
    });
  }

  // ── PDF ──
  generatePdf(e: Estimate) {
    const brand = (environment as any).brandName || 'Calypso';
    const fmtD = (d?: string | null) => d ? new Date(d).toLocaleDateString('fr-FR') : '—';
    const money = (v: number) => `${(v || 0).toLocaleString('fr-FR', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${this.currency}`;

    const doc = new jsPDF();
    const pw = doc.internal.pageSize.getWidth();

    // Bandeau carbone (charte)
    doc.setFillColor(13, 20, 37);
    doc.rect(0, 0, pw, 34, 'F');
    doc.setTextColor(255, 255, 255);
    doc.setFontSize(20); doc.setFont('helvetica', 'bold');
    doc.text(brand, 14, 15);
    doc.setFontSize(10); doc.setFont('helvetica', 'normal'); doc.setTextColor(165, 180, 252);
    doc.text('Gestion de flotte GPS', 14, 22);
    doc.setFontSize(16); doc.setFont('helvetica', 'bold'); doc.setTextColor(255, 255, 255);
    doc.text('DEVIS', pw - 14, 15, { align: 'right' });
    doc.setFontSize(10); doc.setFont('helvetica', 'normal');
    doc.text(e.number, pw - 14, 22, { align: 'right' });

    // Client + dates
    doc.setTextColor(15, 23, 42);
    let y = 46;
    doc.setFontSize(11); doc.setFont('helvetica', 'bold');
    doc.text('Client', 14, y);
    doc.setFont('helvetica', 'normal'); doc.setFontSize(10);
    const clientLines = [e.clientName, e.clientEmail, e.clientPhone, e.clientAddress].filter(Boolean) as string[];
    clientLines.forEach((l, i) => doc.text(l, 14, y + 6 + i * 5));

    doc.setFontSize(10);
    doc.text(`Date d'émission : ${fmtD(e.issueDate)}`, pw - 14, y, { align: 'right' });
    doc.text(`Valable jusqu'au : ${fmtD(e.validUntil)}`, pw - 14, y + 6, { align: 'right' });
    y = Math.max(y + 6 + clientLines.length * 5, y + 14) + 8;

    // Lignes
    autoTable(doc, {
      startY: y,
      head: [['Désignation', 'Qté', 'P.U. HT', 'Total HT']],
      body: e.items.map(i => [i.description, `${i.quantity}`, money(i.unitPrice), money(i.total ?? i.quantity * i.unitPrice)]),
      theme: 'striped',
      headStyles: { fillColor: [13, 20, 37] },
      styles: { fontSize: 10 },
      columnStyles: { 1: { halign: 'right', cellWidth: 18 }, 2: { halign: 'right', cellWidth: 34 }, 3: { halign: 'right', cellWidth: 34 } }
    });
    y = (doc as any).lastAutoTable.finalY + 8;

    // Totaux
    const totals: [string, string][] = [['Sous-total HT', money(e.subtotal)]];
    if (e.discountPercent > 0) totals.push([`Remise (${e.discountPercent} %)`, `-${money(e.discountAmount)}`]);
    totals.push([`TVA (${e.taxPercent} %)`, money(e.taxAmount)]);
    totals.push(['Total TTC', money(e.total)]);
    autoTable(doc, {
      startY: y,
      body: totals,
      theme: 'plain',
      styles: { fontSize: 10, halign: 'right' },
      columnStyles: { 1: { fontStyle: 'bold' } },
      margin: { left: pw - 100 },
      tableWidth: 86,
      didParseCell: (d: any) => {
        if (d.row.index === totals.length - 1) { d.cell.styles.fontSize = 12; d.cell.styles.textColor = [67, 56, 202]; }
      }
    });
    y = (doc as any).lastAutoTable.finalY + 10;

    if (e.notes) {
      doc.setFontSize(11); doc.setFont('helvetica', 'bold');
      doc.text('Notes & conditions', 14, y); y += 6;
      doc.setFontSize(9.5); doc.setFont('helvetica', 'normal'); doc.setTextColor(71, 85, 105);
      const wrapped = doc.splitTextToSize(e.notes, pw - 28);
      doc.text(wrapped, 14, y);
      doc.setTextColor(15, 23, 42);
    }

    doc.setFontSize(8); doc.setTextColor(130);
    doc.text(`${brand} — devis ${e.number} généré le ${new Date().toLocaleDateString('fr-FR')}`, 14, doc.internal.pageSize.getHeight() - 8);

    doc.save(`${e.number}_${e.clientName.replace(/\s+/g, '_')}.pdf`);
  }

  ngOnDestroy() {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
