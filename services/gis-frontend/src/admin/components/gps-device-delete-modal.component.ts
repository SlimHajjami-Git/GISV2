import { ChangeDetectorRef, Component, EventEmitter, HostListener, Input, OnChanges, OnDestroy, Output, SimpleChanges } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import {
  AdminService, GpsDeviceDeletionImpact, GpsDeviceDeletionResult, GpsDeviceDeletionRow
} from '../services/admin.service';
import { confirmationMatches } from '../pages/vehicle-gps-save.helpers';

/**
 * Fenêtre « Supprimer le boîtier » (demande de Slim du 09/10/2026) : inventaire chiffré de
 * tout ce qui part — le véhicule rattaché compris —, ce qui est conservé (dossiers de
 * sinistre), ce qui est refusé (historique d'un autre véhicule vivant), puis l'IMEI à
 * retaper avant une suppression définitive. Partagée par Admin › Véhicules et Admin ›
 * Boîtiers GPS ; s'ouvre dès que `deviceId` est renseigné.
 */
@Component({
  selector: 'gps-device-delete-modal',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="modal-overlay" *ngIf="open" (mousedown)="fermer()">
      <div class="modal" (mousedown)="$event.stopPropagation()">
        <div class="modal-header">
          <h2>Supprimer le boîtier</h2>
          <button type="button" class="close-btn" (click)="fermer()" [disabled]="deleting" aria-label="Fermer">×</button>
        </div>

        <div class="modal-body">
          <p class="loading" *ngIf="!impact && !impactFailed">Inventaire des données du boîtier…</p>
          <p class="error" *ngIf="impactFailed" role="alert">Impossible d'établir l'inventaire : la suppression est désactivée. Réessayez.</p>

          <ng-container *ngIf="impact">
            <div class="identity">
              <span>IMEI <code>{{ impact.imei }}</code></span>
              <span>MAT <b>{{ impact.mat || '—' }}</b></span>
              <span>{{ impact.companyName || ('société #' + impact.companyId) }}</span>
              <span>Dernière trame : {{ impact.lastCommunication ? (impact.lastCommunication | date:'dd/MM/yyyy HH:mm') : 'jamais' }}</span>
            </div>
            <p class="vehicle" *ngIf="impact.vehicle">
              Véhicule rattaché : <b>{{ impact.vehicle.plate || impact.vehicle.name }}</b>
              <span *ngIf="impact.vehicle.plate"> ({{ impact.vehicle.name }})</span>
              <span *ngIf="impact.vehicle.companyName"> — {{ impact.vehicle.companyName }}</span>
            </p>
            <p class="vehicle none" *ngIf="!impact.vehicle">Aucun véhicule rattaché.</p>

            <p class="refusal" *ngIf="impact.refusal" role="alert">{{ impact.refusal }}</p>

            <ng-container *ngIf="!impact.refusal && !result">
              <p class="warning">
                Irréversible. Le boîtier, tout son historique et, s'il est rattaché, le véhicule et
                toutes ses données sont supprimés. Les dossiers de sinistre sont conservés, détachés.
                Si le boîtier émet encore, ses trames seront ignorées (liste de blocage) jusqu'à déblocage.
              </p>
              <ul class="warnings" *ngIf="impact.warnings.length">
                <li *ngFor="let w of impact.warnings">{{ w }}</li>
              </ul>
              <table class="rows" *ngIf="rows.length">
                <tbody>
                  <tr *ngFor="let r of rows; trackBy: suiviLigne">
                    <td>{{ r.label }}</td>
                    <td class="num">{{ r.capped ? 'plus de ' : '' }}{{ r.rows | number }}</td>
                  </tr>
                </tbody>
              </table>
              <p class="total">{{ impact.capped ? 'Plus de ' : '' }}{{ impact.totalRows | number }} ligne(s) au total.</p>

              <label class="confirm">
                Pour confirmer, tapez <code>{{ impact.confirmationToken }}</code>
                <input type="text" [(ngModel)]="confirmation" [placeholder]="impact.confirmationToken"
                       autocomplete="off" [disabled]="deleting" (keyup.enter)="confirmer()">
              </label>
              <p class="error" *ngIf="error" role="alert">{{ error }}</p>
              <p class="deleting-note" *ngIf="deleting" role="status">
                Suppression en cours — ne fermez pas cet onglet, elle peut prendre plusieurs minutes.
              </p>
            </ng-container>

            <div class="result" *ngIf="result">
              <p class="success">{{ result.message }} ({{ result.durationMs | number }} ms)</p>
              <table class="rows">
                <tbody>
                  <tr *ngFor="let r of result.deleted; trackBy: suiviLigne">
                    <td>{{ r.label }}</td>
                    <td class="num">{{ r.rows | number }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </ng-container>
        </div>

        <div class="modal-footer">
          <button type="button" class="btn-secondary" (click)="fermer()" [disabled]="deleting">{{ result ? 'Fermer' : 'Annuler' }}</button>
          <button type="button" class="btn-danger" *ngIf="!result" (click)="confirmer()" [disabled]="!canDelete">
            {{ deleting ? 'Suppression…' : 'Supprimer définitivement' }}
          </button>
        </div>
      </div>
    </div>
  `,
  styles: [`
    .modal-overlay { position: fixed; inset: 0; background: rgba(15, 23, 42, .55); display: flex; align-items: center; justify-content: center; z-index: 1000; padding: 16px; }
    .modal { background: var(--adm-card, #fff); border-radius: 14px; width: 100%; max-width: 560px; max-height: 92vh; display: flex; flex-direction: column; box-shadow: 0 20px 60px rgba(0,0,0,.25); }
    .modal-header { display: flex; align-items: center; justify-content: space-between; padding: 16px 22px; border-bottom: 1px solid var(--adm-border, #e5e7eb); }
    .modal-header h2 { margin: 0; font-size: 17px; color: var(--adm-red-ink, #b91c1c); }
    .close-btn { background: none; border: none; font-size: 22px; line-height: 1; cursor: pointer; color: var(--adm-sub, #64748b); }
    .modal-body { padding: 16px 22px; overflow: auto; font-size: 13px; color: var(--adm-ink, #0f172a); }
    .identity { display: flex; flex-wrap: wrap; gap: 6px 16px; font-size: 12.5px; color: var(--adm-sub, #64748b); margin-bottom: 8px; }
    .identity code, .confirm code { font-family: ui-monospace, Menlo, monospace; background: var(--adm-bg, #f1f5f9); padding: 1px 6px; border-radius: 6px; }
    .vehicle { margin: 0 0 10px; }
    .vehicle.none { color: var(--adm-sub, #64748b); }
    .refusal { background: #fef2f2; border: 1px solid #fecaca; color: #991b1b; padding: 10px 12px; border-radius: 10px; margin: 0 0 8px; }
    .warning { margin: 0 0 8px; color: var(--adm-sub, #64748b); }
    .warnings { margin: 0 0 10px; padding-left: 18px; color: #92400e; }
    .warnings li + li { margin-top: 3px; }
    .rows { width: 100%; border-collapse: collapse; margin: 0 0 6px; }
    .rows td { padding: 4px 6px; border-bottom: 1px solid var(--adm-border, #e5e7eb); }
    .rows .num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
    .total { margin: 0 0 12px; font-weight: 600; }
    .confirm { display: flex; flex-direction: column; gap: 6px; margin-top: 8px; }
    .confirm input { padding: 8px 10px; border: 1px solid var(--adm-border, #e5e7eb); border-radius: 8px; font-size: 14px; font-family: ui-monospace, Menlo, monospace; }
    .loading, .error { margin: 0 0 8px; }
    .error { color: var(--adm-red-ink, #b91c1c); }
    .success { color: #166534; font-weight: 600; margin: 0 0 8px; }
    .deleting-note { margin: 8px 0 0; color: #92400e; font-weight: 600; }
    .close-btn:disabled, .btn-secondary:disabled { opacity: .5; cursor: not-allowed; }
    .modal-footer { display: flex; justify-content: flex-end; gap: 10px; padding: 14px 22px; border-top: 1px solid var(--adm-border, #e5e7eb); }
    .btn-secondary { padding: 9px 16px; background: var(--adm-bg, #f1f5f9); border: 1px solid var(--adm-border, #e5e7eb); border-radius: 10px; cursor: pointer; font-size: 13px; }
    .btn-danger { padding: 9px 18px; background: var(--adm-red, #dc2626); border: none; border-radius: 10px; color: #fff; font-size: 13px; font-weight: 600; cursor: pointer; }
    .btn-danger:disabled { opacity: .5; cursor: not-allowed; }
  `]
})
export class GpsDeviceDeleteModalComponent implements OnChanges, OnDestroy {
  /** Fiche à supprimer ; null = fenêtre fermée. */
  @Input() deviceId: number | null = null;
  @Output() closed = new EventEmitter<{ deleted: boolean }>();

  impact: GpsDeviceDeletionImpact | null = null;
  /** Lignes non nulles de l'inventaire, calculées une fois (pas de getter qui alloue dans le gabarit). */
  rows: GpsDeviceDeletionRow[] = [];
  impactFailed = false;
  confirmation = '';
  deleting = false;
  result: GpsDeviceDeletionResult | null = null;
  error = '';

  private readonly destroy$ = new Subject<void>();

  constructor(private readonly adminService: AdminService, private readonly cdr: ChangeDetectorRef) {}

  get open(): boolean { return this.deviceId !== null && this.deviceId !== undefined; }

  get canDelete(): boolean {
    return !!this.impact && !this.impact.refusal && !this.deleting && !this.result
      && confirmationMatches(this.confirmation, this.impact.confirmationToken);
  }

  suiviLigne = (_: number, r: GpsDeviceDeletionRow) => r.table;

  /** Quitter la page pendant la suppression : le navigateur demande confirmation. */
  @HostListener('window:beforeunload', ['$event'])
  avantDechargement(event: BeforeUnloadEvent): void {
    if (!this.deleting) return;
    event.preventDefault();
    event.returnValue = '';
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (!changes['deviceId']) return;
    this.reset();
    if (this.open) this.chargerInventaire(this.deviceId!);
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private reset(): void {
    this.impact = null;
    this.rows = [];
    this.impactFailed = false;
    this.confirmation = '';
    this.deleting = false;
    this.result = null;
    this.error = '';
  }

  private chargerInventaire(id: number): void {
    this.adminService.getGpsDeviceDeletionImpact(id).pipe(takeUntil(this.destroy$)).subscribe({
      // Réponse d'une fiche déjà refermée : ne jamais l'afficher devant une autre.
      next: (impact) => {
        if (impact.deviceId !== this.deviceId) return;
        this.impact = impact;
        this.rows = impact.rows.filter(r => r.rows > 0);
        this.cdr.markForCheck();
      },
      error: () => {
        if (id !== this.deviceId) return;
        this.impactFailed = true;
        this.cdr.markForCheck();
      },
    });
  }

  confirmer(): void {
    if (!this.canDelete || this.deviceId === null) return;
    const id = this.deviceId;
    this.deleting = true;
    this.error = '';
    this.adminService.deleteGpsDeviceWithData(id, this.confirmation).pipe(takeUntil(this.destroy$)).subscribe({
      next: (result) => {
        if (id !== this.deviceId) return;
        this.result = result;
        this.deleting = false;
        this.cdr.markForCheck();
      },
      error: (err) => {
        if (id !== this.deviceId) return;
        this.deleting = false;
        // Le serveur explique son refus (confirmation fausse, inventaire changé, autre véhicule…).
        this.error = err?.error?.message || 'La suppression a échoué. Rien n’a été confirmé : vérifiez l’état actuel avant de réessayer.';
        this.cdr.markForCheck();
      },
    });
  }

  fermer(): void {
    if (this.deleting) return;
    this.closed.emit({ deleted: !!this.result });
  }
}
