import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import {
  AdminService, Client, DeviceCommandHistoryRow, DeviceCommandSendResult, DeviceCommandTarget
} from '../services/admin.service';

/**
 * Écran admin « Commandes boîtiers » : envoi d'une commande AJ+ (configuration NEMS)
 * à un, plusieurs ou tous les boîtiers d'une société, via le socket tenu par l'ingest.
 *
 * Ce que l'écran garantit côté client n'est qu'un confort : le serveur refuse de
 * toute façon tout STOP, tout texte hors AJ+, et tout boîtier d'une autre société.
 * Les boîtiers NEMS ne répondent pas aux commandes TCP (ils les exécutent en
 * silence) : « poussée » veut dire écrite sur le socket, pas acquittée.
 */
@Component({
  selector: 'admin-device-commands',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent],
  template: `
    <admin-layout pageTitle="Commandes boîtiers">
      <div class="cmd-page">
        <div class="page-header">
          <div>
            <h2><span class="title-dash"></span>Commandes boîtiers</h2>
            <p class="subtitle">Envoyer une commande AJ+ via le socket à un, plusieurs ou tous les boîtiers d'une société. Chaque envoi est tracé.</p>
          </div>
          <div class="header-actions">
            <select [(ngModel)]="selectedCompanyId" (ngModelChange)="onCompanyChange()">
              <option [ngValue]="null">— Choisir une société —</option>
              <option *ngFor="let c of companies" [ngValue]="c.id">{{ c.name }}</option>
            </select>
            <button class="btn-refresh" (click)="loadTargets(); loadHistory()" [disabled]="!selectedCompanyId">Actualiser</button>
          </div>
        </div>

        <div class="empty-state" *ngIf="!selectedCompanyId">
          <p>Choisis une société pour voir ses boîtiers.</p>
        </div>

        <ng-container *ngIf="selectedCompanyId">
          <!-- ── 1. Cibles ─────────────────────────────────────────── -->
          <section class="card">
            <div class="card-head">
              <h3>1. Boîtiers ciblés</h3>
              <div class="card-tools">
                <label class="chk"><input type="checkbox" [(ngModel)]="allFleet" (ngModelChange)="onAllFleetChange()"> <b>Tout le parc</b> ({{ selectableTargets.length }} NEMS)</label>
                <input type="text" class="filter" placeholder="Filtrer plaque / marque / modèle / IMEI / MAT" [(ngModel)]="filterText" [disabled]="allFleet">
                <label class="chk"><input type="checkbox" [(ngModel)]="onlyOnline" [disabled]="allFleet"> En ligne seulement</label>
                <button class="btn-link" (click)="selectVisible()" [disabled]="allFleet">Cocher les visibles</button>
                <button class="btn-link" (click)="clearSelection()" [disabled]="allFleet">Tout décocher</button>
              </div>
            </div>

            <div class="loading-state" *ngIf="loadingTargets"><div class="spinner"></div><span>Chargement des boîtiers…</span></div>

            <div class="stats-bar" *ngIf="!loadingTargets && targets.length">
              <div class="stat-card"><span class="stat-value">{{ targets.length }}</span><span class="stat-label">Boîtiers</span></div>
              <div class="stat-card"><span class="stat-value">{{ selectableTargets.length }}</span><span class="stat-label">Compatibles AJ+</span></div>
              <div class="stat-card"><span class="stat-value">{{ onlineCount }}</span><span class="stat-label">En ligne (&lt; 10 min)</span></div>
              <div class="stat-card accent"><span class="stat-value">{{ allFleet ? selectableTargets.length : selected.size }}</span><span class="stat-label">Sélectionnés</span></div>
            </div>

            <div class="table-container" *ngIf="!loadingTargets && targets.length" [class.dimmed]="allFleet">
              <table>
                <thead>
                  <tr><th></th><th>Plaque</th><th>Véhicule</th><th>Marque</th><th>Modèle</th><th>IMEI</th><th>MAT</th><th>Boîtier</th><th>Firmware</th><th>Dernière trame</th><th>État</th></tr>
                </thead>
                <tbody>
                  <tr *ngFor="let t of filteredTargets" [class.disabled]="!t.isNems" (click)="toggle(t)">
                    <td><input type="checkbox" [checked]="allFleet ? t.isNems : selected.has(t.deviceId)" [disabled]="!t.isNems || allFleet" (click)="$event.stopPropagation()" (change)="toggle(t)"></td>
                    <td class="mono">{{ t.plate || '—' }}</td>
                    <td>{{ t.vehicleName || '—' }}</td>
                    <td>{{ t.vehicleBrand || '—' }}</td>
                    <td>{{ t.vehicleModel || '—' }}</td>
                    <td class="mono">{{ t.imei }}</td>
                    <td class="mono">{{ t.mat || '—' }}</td>
                    <td>{{ t.brand || '' }} {{ t.model || '' }} <span class="tag" *ngIf="!t.isNems" title="Pas de protocole AJ+">{{ t.protocolType || 'non NEMS' }}</span></td>
                    <td class="mono">{{ t.firmwareVersion || '—' }}</td>
                    <td>{{ t.lastCommunication ? (t.lastCommunication | date:'dd/MM HH:mm:ss') : '—' }}</td>
                    <td><span class="status-badge" [ngClass]="t.onlineRecently ? 'online' : 'offline'">{{ t.onlineRecently ? 'en ligne' : 'hors ligne' }}</span></td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div class="empty-state small" *ngIf="!loadingTargets && !targets.length"><p>Aucun boîtier pour cette société.</p></div>
          </section>

          <!-- ── 2. Commande ───────────────────────────────────────── -->
          <section class="card">
            <div class="card-head"><h3>2. Commande</h3></div>
            <div class="cmd-row">
              <select [(ngModel)]="preset" (ngModelChange)="applyPreset()">
                <option value="">Saisie libre</option>
                <option *ngFor="let p of presets" [value]="p.text">{{ p.label }}</option>
              </select>
              <input type="text" class="cmd-input mono" [(ngModel)]="commandText" (ngModelChange)="resetConfirm()" placeholder="AJ+CONFN=…" maxlength="99" spellcheck="false">
              <span class="len" [class.over]="commandText.trim().length > 99">{{ commandText.trim().length }}/99</span>
            </div>
            <p class="hint warn" *ngIf="clientWarning">{{ clientWarning }}</p>
            <p class="hint">Une seule ligne, ASCII, préfixe <b>AJ+</b>. Le retour à la ligne final est ajouté automatiquement. La coupure moteur (STOP) est refusée : elle ne passe que par l'immobilisation approuvée.</p>
          </section>

          <!-- ── 3. Confirmation & envoi ───────────────────────────── -->
          <section class="card confirm" *ngIf="targetCount > 0 && commandText.trim()">
            <div class="card-head"><h3>3. Envoi</h3></div>
            <p>
              Envoyer <b class="mono">{{ commandText.trim() }}</b> à <b>{{ targetCount }}</b> boîtier{{ targetCount > 1 ? 's' : '' }}
              de <b>{{ selectedCompanyName }}</b><span *ngIf="allFleet"> (tout le parc)</span>.
              Pour confirmer, tape <b>{{ confirmPhrase }}</b>.
            </p>
            <div class="confirm-row">
              <input type="text" class="confirm-input" [(ngModel)]="confirmInput" [placeholder]="confirmPhrase" [disabled]="sending">
              <button class="btn-primary" (click)="send()" [disabled]="sending || confirmInput !== confirmPhrase || !!clientWarning">
                {{ sending ? 'Envoi…' : 'Envoyer via le socket' }}
              </button>
            </div>
            <p class="hint error" *ngIf="error">{{ error }}</p>
          </section>

          <!-- ── Résultat ───────────────────────────────────────────── -->
          <section class="card" *ngIf="result">
            <div class="card-head"><h3>Résultat</h3></div>
            <div class="stats-bar">
              <div class="stat-card"><span class="stat-value">{{ result.targeted }}</span><span class="stat-label">Ciblés</span></div>
              <div class="stat-card ok"><span class="stat-value">{{ result.pushedLive }}</span><span class="stat-label">Poussés (socket)</span></div>
              <div class="stat-card warn"><span class="stat-value">{{ result.offline }}</span><span class="stat-label">Hors ligne (à la prochaine trame)</span></div>
              <div class="stat-card bad"><span class="stat-value">{{ result.failed }}</span><span class="stat-label">Échec push (en attente)</span></div>
              <div class="stat-card"><span class="stat-value">{{ result.skippedNonNems }}</span><span class="stat-label">Ignorés (non NEMS)</span></div>
              <div class="stat-card bad" *ngIf="result.blocked"><span class="stat-value">{{ result.blocked }}</span><span class="stat-label">Bloqués</span></div>
            </div>
            <div class="table-container">
              <table>
                <thead><tr><th>Plaque</th><th>Véhicule</th><th>IMEI</th><th>Sort</th><th>Détail</th></tr></thead>
                <tbody>
                  <tr *ngFor="let d of result.details">
                    <td class="mono">{{ d.plate || '—' }}</td><td>{{ d.vehicleName || '—' }}</td><td class="mono">{{ d.imei }}</td>
                    <td><span class="status-badge" [ngClass]="d.outcome">{{ outcomeLabel(d.outcome) }}</span></td>
                    <td class="muted">{{ d.detail || '' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>

          <!-- ── Historique ─────────────────────────────────────────── -->
          <section class="card">
            <div class="card-head">
              <h3>Historique des envois admin</h3>
              <div class="card-tools">
                <select [(ngModel)]="historyLimit" (ngModelChange)="loadHistory()">
                  <option [ngValue]="50">50</option><option [ngValue]="200">200</option><option [ngValue]="500">500</option>
                </select>
              </div>
            </div>
            <div class="loading-state" *ngIf="loadingHistory"><div class="spinner"></div><span>Chargement…</span></div>
            <div class="table-container" *ngIf="!loadingHistory && history.length">
              <table>
                <thead><tr><th>Date</th><th>Plaque</th><th>IMEI</th><th>Commande</th><th>Statut</th><th>Envoyée</th><th>Tentatives</th><th>Par</th></tr></thead>
                <tbody>
                  <tr *ngFor="let h of history">
                    <td>{{ h.createdAt | date:'dd/MM/yyyy HH:mm:ss' }}</td>
                    <td class="mono">{{ h.plate || '—' }}</td>
                    <td class="mono">{{ h.imei || h.deviceId }}</td>
                    <td class="mono">{{ h.commandText.trim() }}</td>
                    <td><span class="status-badge" [ngClass]="h.status">{{ h.status }}</span></td>
                    <td>{{ h.sentAt ? (h.sentAt | date:'dd/MM HH:mm:ss') : '—' }}</td>
                    <td>{{ h.attempts }}</td>
                    <td class="mono">#{{ h.userId }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div class="empty-state small" *ngIf="!loadingHistory && !history.length"><p>Aucune commande admin envoyée à cette société.</p></div>
          </section>
        </ng-container>
      </div>
    </admin-layout>
  `,
  styles: [`
    .cmd-page { padding: 24px; display: flex; flex-direction: column; gap: 18px; }
    .page-header { display: flex; justify-content: space-between; align-items: flex-start; gap: 16px; flex-wrap: wrap; }
    .page-header h2 { margin: 0; display: flex; align-items: center; gap: 8px; font-size: 12px; font-weight: 700; text-transform: uppercase; letter-spacing: .08em; color: var(--adm-sub); }
    .title-dash { display: inline-block; width: 12px; height: 3px; border-radius: 2px; background: var(--adm-indigo); }
    .subtitle { margin: 6px 0 0; font-size: 13px; color: var(--adm-sub); }
    .header-actions, .card-tools { display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
    select, .filter, .cmd-input, .confirm-input { padding: 9px 12px; border: 1px solid var(--adm-border); border-radius: 10px; font-size: 13px; background: #fff; outline: none; }
    .filter { min-width: 220px; }
    .card { background: #fff; border: 1px solid var(--adm-border); border-radius: 14px; padding: 16px 18px; }
    .card-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .card-head h3 { margin: 0; font-size: 14px; font-weight: 700; }
    .chk { display: inline-flex; align-items: center; gap: 6px; font-size: 13px; cursor: pointer; }
    .btn-link { background: none; border: none; color: var(--adm-indigo); cursor: pointer; font-size: 13px; }
    .btn-link:disabled { color: var(--adm-sub); cursor: default; }
    .btn-refresh, .btn-primary { padding: 9px 14px; border-radius: 10px; border: 1px solid var(--adm-border); background: #fff; cursor: pointer; font-size: 13px; }
    .btn-primary { background: var(--adm-indigo); color: #fff; border-color: var(--adm-indigo); font-weight: 600; }
    .btn-primary:disabled, .btn-refresh:disabled { opacity: .5; cursor: default; }
    .stats-bar { display: flex; gap: 10px; flex-wrap: wrap; margin-bottom: 12px; }
    .stat-card { padding: 10px 14px; border: 1px solid var(--adm-border); border-radius: 12px; display: flex; flex-direction: column; min-width: 110px; }
    .stat-card.accent { border-color: var(--adm-indigo); }
    .stat-card.ok { border-color: #16a34a; } .stat-card.warn { border-color: #d97706; } .stat-card.bad { border-color: var(--adm-red); }
    .stat-value { font-size: 20px; font-weight: 700; } .stat-label { font-size: 11px; color: var(--adm-sub); }
    .table-container { overflow-x: auto; }
    .table-container.dimmed { opacity: .55; }
    table { width: 100%; border-collapse: collapse; font-size: 13px; }
    th { text-align: left; font-size: 11px; text-transform: uppercase; letter-spacing: .06em; color: var(--adm-sub); padding: 8px 10px; border-bottom: 1px solid var(--adm-border); }
    td { padding: 8px 10px; border-bottom: 1px solid var(--adm-border); }
    tbody tr { cursor: pointer; } tbody tr:hover { background: rgba(79,70,229,.04); }
    tbody tr.disabled { cursor: default; color: var(--adm-sub); }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; font-size: 12px; }
    .muted { color: var(--adm-sub); }
    .tag { display: inline-block; margin-left: 6px; padding: 2px 6px; border-radius: 6px; background: rgba(148,163,184,.2); font-size: 11px; }
    .status-badge { display: inline-block; padding: 3px 8px; border-radius: 999px; font-size: 11px; font-weight: 600; background: rgba(148,163,184,.2); }
    .status-badge.online, .status-badge.pushed, .status-badge.sent, .status-badge.acknowledged { background: rgba(22,163,74,.15); color: #15803d; }
    .status-badge.offline, .status-badge.pending { background: rgba(217,119,6,.15); color: #b45309; }
    .status-badge.failed, .status-badge.blocked, .status-badge.expired { background: rgba(220,38,38,.12); color: #b91c1c; }
    .status-badge.skipped_non_nems { background: rgba(148,163,184,.25); color: #475569; }
    .cmd-row { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    .cmd-input { flex: 1; min-width: 320px; }
    .len { font-size: 12px; color: var(--adm-sub); } .len.over { color: var(--adm-red); font-weight: 700; }
    .hint { margin: 8px 0 0; font-size: 12px; color: var(--adm-sub); }
    .hint.warn { color: #b45309; font-weight: 600; } .hint.error { color: var(--adm-red); font-weight: 600; }
    .card.confirm { border-color: rgba(79,70,229,.35); background: rgba(79,70,229,.03); }
    .confirm-row { display: flex; gap: 10px; align-items: center; }
    .confirm-input { max-width: 220px; letter-spacing: .1em; text-transform: uppercase; }
    .loading-state { display: flex; align-items: center; gap: 10px; color: var(--adm-sub); padding: 12px 0; }
    .spinner { width: 16px; height: 16px; border: 2px solid var(--adm-border); border-top-color: var(--adm-indigo); border-radius: 50%; animation: spin .8s linear infinite; }
    @keyframes spin { to { transform: rotate(360deg); } }
    .empty-state { padding: 24px; text-align: center; color: var(--adm-sub); } .empty-state.small { padding: 12px; }
  `]
})
export class AdminDeviceCommandsComponent implements OnInit, OnDestroy {
  companies: Client[] = [];
  selectedCompanyId: number | null = null;

  targets: DeviceCommandTarget[] = [];
  loadingTargets = false;
  selected = new Set<number>();
  filterText = '';
  onlyOnline = false;
  allFleet = false;

  presets = [
    { label: 'AJ+GO — lever une immobilisation', text: 'AJ+GO#9999' },
    { label: 'AJ+CONFN — limite de vitesse (exemple : 377 = 60 km/h)', text: 'AJ+CONFN=101,3,2,377,0,0,#9999' },
  ];
  preset = '';
  commandText = '';
  readonly confirmPhrase = 'ENVOYER';
  confirmInput = '';
  sending = false;
  error: string | null = null;
  result: DeviceCommandSendResult | null = null;

  history: DeviceCommandHistoryRow[] = [];
  historyLimit = 50;
  loadingHistory = false;

  private destroy$ = new Subject<void>();

  constructor(private adminService: AdminService, private cdr: ChangeDetectorRef) {}

  ngOnInit(): void {
    this.adminService.getClients().pipe(takeUntil(this.destroy$)).subscribe({
      next: (clients) => { this.companies = clients; this.cdr.detectChanges(); },
      error: () => { this.companies = []; this.cdr.detectChanges(); }
    });
  }

  ngOnDestroy(): void { this.destroy$.next(); this.destroy$.complete(); }

  get selectedCompanyName(): string {
    return this.companies.find(c => c.id === this.selectedCompanyId)?.name ?? '';
  }
  get selectableTargets(): DeviceCommandTarget[] { return this.targets.filter(t => t.isNems); }
  get onlineCount(): number { return this.targets.filter(t => t.onlineRecently).length; }
  get filteredTargets(): DeviceCommandTarget[] {
    const q = this.filterText.trim().toLowerCase();
    return this.targets.filter(t =>
      (!this.onlyOnline || t.onlineRecently) &&
      (!q || [t.plate, t.imei, t.mat, t.vehicleName, t.vehicleBrand, t.vehicleModel, t.label].some(v => (v || '').toLowerCase().includes(q))));
  }
  get targetCount(): number { return this.allFleet ? this.selectableTargets.length : this.selected.size; }

  /** Miroir non contractuel du garde-fou serveur, pour éviter un aller-retour inutile. */
  get clientWarning(): string | null {
    const t = this.commandText.trim();
    if (!t) return null;
    if (/stop/i.test(t)) return 'Refusé : la coupure moteur (STOP) ne passe pas par cet écran.';
    if (!/^aj\+/i.test(t)) return 'Refusé : seules les commandes AJ+ (NEMS) sont acceptées.';
    if (t.length > 99) return 'Refusé : 99 caractères maximum.';
    if (/[^\x20-\x7E]/.test(t)) return 'Refusé : ASCII imprimable uniquement.';
    return null;
  }

  onCompanyChange(): void {
    this.selected.clear(); this.targets = []; this.history = []; this.result = null; this.error = null; this.resetConfirm();
    if (this.selectedCompanyId) { this.loadTargets(); this.loadHistory(); }
    this.cdr.detectChanges();
  }

  loadTargets(): void {
    if (!this.selectedCompanyId) return;
    this.loadingTargets = true; this.cdr.detectChanges();
    this.adminService.getDeviceCommandTargets(this.selectedCompanyId).pipe(takeUntil(this.destroy$)).subscribe({
      next: (rows) => {
        this.targets = rows;
        // Un boîtier qui a disparu entre deux rafraîchissements ne reste pas coché.
        const ids = new Set(rows.filter(r => r.isNems).map(r => r.deviceId));
        this.selected.forEach(id => { if (!ids.has(id)) this.selected.delete(id); });
        this.loadingTargets = false; this.cdr.detectChanges();
      },
      error: (err) => { this.loadingTargets = false; this.error = this.describe(err); this.cdr.detectChanges(); }
    });
  }

  loadHistory(): void {
    if (!this.selectedCompanyId) return;
    this.loadingHistory = true; this.cdr.detectChanges();
    this.adminService.getDeviceCommandHistory(this.selectedCompanyId, this.historyLimit).pipe(takeUntil(this.destroy$)).subscribe({
      next: (rows) => { this.history = rows; this.loadingHistory = false; this.cdr.detectChanges(); },
      error: () => { this.history = []; this.loadingHistory = false; this.cdr.detectChanges(); }
    });
  }

  toggle(t: DeviceCommandTarget): void {
    if (!t.isNems || this.allFleet) return;
    if (this.selected.has(t.deviceId)) this.selected.delete(t.deviceId); else this.selected.add(t.deviceId);
    this.resetConfirm(); this.cdr.detectChanges();
  }
  selectVisible(): void { this.filteredTargets.filter(t => t.isNems).forEach(t => this.selected.add(t.deviceId)); this.resetConfirm(); this.cdr.detectChanges(); }
  clearSelection(): void { this.selected.clear(); this.resetConfirm(); this.cdr.detectChanges(); }
  onAllFleetChange(): void { this.resetConfirm(); this.cdr.detectChanges(); }
  applyPreset(): void { if (this.preset) this.commandText = this.preset; this.resetConfirm(); this.cdr.detectChanges(); }
  resetConfirm(): void { this.confirmInput = ''; this.error = null; }

  send(): void {
    if (!this.selectedCompanyId || this.sending || this.confirmInput !== this.confirmPhrase || this.clientWarning) return;
    this.sending = true; this.error = null; this.result = null; this.cdr.detectChanges();
    this.adminService.sendDeviceCommand({
      companyId: this.selectedCompanyId,
      commandText: this.commandText.trim(),
      deviceIds: this.allFleet ? undefined : Array.from(this.selected),
      allFleet: this.allFleet
    }).pipe(takeUntil(this.destroy$)).subscribe({
      next: (r) => { this.result = r; this.sending = false; this.confirmInput = ''; this.loadHistory(); this.cdr.detectChanges(); },
      error: (err) => { this.sending = false; this.error = this.describe(err); this.cdr.detectChanges(); }
    });
  }

  outcomeLabel(o: string): string {
    switch (o) {
      case 'pushed': return 'poussée';
      case 'offline': return 'hors ligne';
      case 'failed': return 'échec push';
      case 'skipped_non_nems': return 'ignoré (non NEMS)';
      case 'blocked': return 'bloqué';
      default: return o;
    }
  }

  private describe(err: any): string {
    const body = err?.error;
    return body?.error || body?.message || err?.message || 'Erreur inattendue.';
  }
}
