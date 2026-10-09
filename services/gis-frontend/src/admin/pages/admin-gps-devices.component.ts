import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Subject, takeUntil } from 'rxjs';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { GpsDeviceDeleteModalComponent } from '../components/gps-device-delete-modal.component';
import { AdminGpsDevice, AdminService, Client, GpsDeviceBlocklistEntry } from '../services/admin.service';

/**
 * Écran admin « Boîtiers GPS » (09/10/2026) : toutes les fiches boîtier, y compris celles
 * sans véhicule qu'aucun écran ne montrait (28 sur TN ce jour-là), avec leur société, leur
 * véhicule et leur dernière trame ; suppression définitive par la fenêtre partagée ; et la
 * liste de blocage (IMEI supprimés dont l'ingestion ignore les trames), avec « Débloquer »
 * pour une réinstallation.
 */
@Component({
  selector: 'admin-gps-devices',
  standalone: true,
  imports: [CommonModule, FormsModule, AdminLayoutComponent, GpsDeviceDeleteModalComponent],
  template: `
    <admin-layout pageTitle="Boîtiers GPS">
      <div class="page">
        <div class="page-header">
          <div>
            <h2><span class="title-dash"></span>Boîtiers GPS</h2>
            <p class="subtitle">Toutes les fiches boîtier, avec ou sans véhicule. La suppression emporte le véhicule rattaché et toutes ses données ; l'IMEI passe en liste de blocage.</p>
          </div>
          <div class="header-actions">
            <select [(ngModel)]="selectedCompanyId" (ngModelChange)="load()">
              <option [ngValue]="null">Toutes les sociétés</option>
              <option *ngFor="let c of companies; trackBy: suiviSociete" [ngValue]="c.id">{{ c.name }}</option>
            </select>
            <label class="chk"><input type="checkbox" [(ngModel)]="unassignedOnly" (ngModelChange)="load()"> Sans véhicule seulement</label>
            <input type="text" class="filter" placeholder="IMEI, MAT, plaque, véhicule — Entrée pour filtrer" [(ngModel)]="search" (keyup.enter)="load()">
            <button type="button" class="btn-refresh" (click)="load()">Actualiser</button>
          </div>
        </div>

        <section class="card">
          <div class="loading-state" *ngIf="loading"><span>Chargement des boîtiers…</span></div>
          <p class="error" *ngIf="loadError" role="alert">{{ loadError }}</p>

          <table class="devices" *ngIf="!loading && devices.length">
            <thead>
              <tr>
                <th>IMEI</th><th>MAT</th><th>Société</th><th>Véhicule</th><th>Boîtier</th><th>Dernière trame</th><th>Créée le</th><th></th>
              </tr>
            </thead>
            <tbody>
              <tr *ngFor="let d of devices; trackBy: suiviBoitier" [class.orphan]="!d.vehicleId">
                <td class="mono">{{ d.imei }}</td>
                <td class="mono">{{ d.mat || '—' }}</td>
                <td>{{ d.companyName || ('#' + d.companyId) }}</td>
                <td>
                  <ng-container *ngIf="d.vehicleId; else sansVehicule">
                    <b>{{ d.plate || d.vehicleName }}</b><span *ngIf="d.plate && d.vehicleName" class="sub"> {{ d.vehicleName }}</span>
                  </ng-container>
                  <ng-template #sansVehicule><span class="sub">— aucun</span></ng-template>
                </td>
                <td class="sub">{{ d.brand || '' }} {{ d.model || '' }}<span *ngIf="d.protocolType"> · {{ d.protocolType }}</span></td>
                <td>{{ d.lastCommunication ? (d.lastCommunication | date:'dd/MM/yyyy HH:mm') : 'jamais' }}</td>
                <td class="sub">{{ d.createdAt | date:'dd/MM/yyyy' }}</td>
                <td class="actions">
                  <button type="button" class="btn-delete" (click)="ouvrirSuppression(d)" title="Supprimer le boîtier et toutes ses données">Supprimer</button>
                </td>
              </tr>
            </tbody>
          </table>
          <p class="empty" *ngIf="!loading && !devices.length && !loadError">Aucun boîtier ne correspond.</p>
          <p class="count" *ngIf="!loading && devices.length">
            {{ devices.length }} boîtier(s)<span *ngIf="devices.length >= 3000"> — liste tronquée à 3 000, affinez le filtre</span>
          </p>
        </section>

        <section class="card">
          <div class="card-head">
            <h3>Liste de blocage</h3>
            <p class="subtitle">IMEI supprimés : leurs trames sont ignorées et leur fiche n'est plus recréée. « Débloquer » pour réinstaller le boîtier.</p>
          </div>
          <table class="devices" *ngIf="blocklist.length">
            <thead><tr><th>IMEI</th><th>MAT</th><th>Bloqué le</th><th>Ancienne fiche</th><th>Motif</th><th></th></tr></thead>
            <tbody>
              <tr *ngFor="let b of blocklist; trackBy: suiviBlocage">
                <td class="mono">{{ b.deviceUid }}</td>
                <td class="mono">{{ b.mat || '—' }}</td>
                <td>{{ b.blockedAt | date:'dd/MM/yyyy HH:mm' }}</td>
                <td class="sub">#{{ b.deviceIdBefore || '?' }}<span *ngIf="b.companyIdBefore"> · société #{{ b.companyIdBefore }}</span></td>
                <td class="sub">{{ b.reason || '' }}</td>
                <td class="actions"><button type="button" class="btn-unblock" (click)="debloquer(b)">Débloquer</button></td>
              </tr>
            </tbody>
          </table>
          <p class="empty" *ngIf="!blocklist.length">Aucun IMEI bloqué.</p>
          <p class="error" *ngIf="blocklistError" role="alert">{{ blocklistError }}</p>
        </section>

        <gps-device-delete-modal [deviceId]="deviceToDeleteId" (closed)="fermerSuppression($event)"></gps-device-delete-modal>
      </div>
    </admin-layout>
  `,
  styles: [`
    .page { display: flex; flex-direction: column; gap: 16px; }
    .page-header { display: flex; justify-content: space-between; gap: 16px; flex-wrap: wrap; align-items: flex-start; }
    .page-header h2 { margin: 0; font-size: 20px; display: flex; align-items: center; gap: 10px; }
    .title-dash { width: 18px; height: 3px; background: var(--adm-accent, #2563eb); border-radius: 2px; display: inline-block; }
    .subtitle { margin: 4px 0 0; color: var(--adm-sub, #64748b); font-size: 13px; }
    .header-actions { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; }
    .header-actions select, .header-actions .filter { padding: 8px 10px; border: 1px solid var(--adm-border, #e5e7eb); border-radius: 10px; font-size: 13px; background: #fff; }
    .chk { font-size: 13px; display: flex; gap: 6px; align-items: center; }
    .btn-refresh { padding: 8px 14px; border-radius: 10px; border: 1px solid var(--adm-border, #e5e7eb); background: #fff; cursor: pointer; font-size: 13px; }
    .card { background: var(--adm-card, #fff); border: 1px solid var(--adm-border, #e5e7eb); border-radius: 14px; padding: 16px 18px; }
    .card-head h3 { margin: 0 0 4px; font-size: 15px; }
    .devices { width: 100%; border-collapse: collapse; font-size: 13px; }
    .devices th { text-align: left; font-size: 11.5px; text-transform: uppercase; letter-spacing: .04em; color: var(--adm-sub, #64748b); padding: 8px 8px; border-bottom: 1px solid var(--adm-border, #e5e7eb); }
    .devices td { padding: 8px 8px; border-bottom: 1px solid var(--adm-border, #e5e7eb); vertical-align: middle; }
    .devices tr.orphan td { background: #fffbeb; }
    .mono { font-family: ui-monospace, Menlo, monospace; font-size: 12.5px; }
    .sub { color: var(--adm-sub, #64748b); font-size: 12px; }
    .actions { text-align: right; white-space: nowrap; }
    .btn-delete { padding: 6px 12px; border-radius: 8px; border: 1px solid #fecaca; background: #fef2f2; color: #991b1b; cursor: pointer; font-size: 12.5px; font-weight: 600; }
    .btn-delete:hover { background: #fee2e2; }
    .btn-unblock { padding: 6px 12px; border-radius: 8px; border: 1px solid var(--adm-border, #e5e7eb); background: #fff; cursor: pointer; font-size: 12.5px; }
    .loading-state, .empty, .count { color: var(--adm-sub, #64748b); font-size: 13px; margin: 8px 0 0; }
    .error { color: var(--adm-red-ink, #b91c1c); font-size: 13px; margin: 8px 0 0; }
  `]
})
export class AdminGpsDevicesComponent implements OnInit, OnDestroy {
  companies: Client[] = [];
  devices: AdminGpsDevice[] = [];
  blocklist: GpsDeviceBlocklistEntry[] = [];
  selectedCompanyId: number | null = null;
  unassignedOnly = false;
  search = '';
  loading = false;
  loadError = '';
  blocklistError = '';
  deviceToDeleteId: number | null = null;

  private readonly destroy$ = new Subject<void>();

  constructor(private readonly adminService: AdminService, private readonly cdr: ChangeDetectorRef) {}

  suiviSociete = (_: number, c: Client) => c.id;
  suiviBoitier = (_: number, d: AdminGpsDevice) => d.deviceId;
  suiviBlocage = (_: number, b: GpsDeviceBlocklistEntry) => b.deviceUid;

  ngOnInit(): void {
    this.adminService.getClients().pipe(takeUntil(this.destroy$)).subscribe({
      next: (clients) => { this.companies = clients; this.cdr.markForCheck(); },
      error: () => { this.companies = []; },
    });
    this.load();
    this.loadBlocklist();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  load(): void {
    this.loading = true;
    this.loadError = '';
    this.adminService.getAdminGpsDevices({ companyId: this.selectedCompanyId, unassignedOnly: this.unassignedOnly, search: this.search })
      .pipe(takeUntil(this.destroy$)).subscribe({
        next: (devices) => { this.devices = devices; this.loading = false; this.cdr.markForCheck(); },
        error: (err) => {
          this.devices = [];
          this.loading = false;
          this.loadError = err?.error?.message || 'Impossible de charger les boîtiers.';
          this.cdr.markForCheck();
        },
      });
  }

  loadBlocklist(): void {
    this.blocklistError = '';
    this.adminService.getGpsDeviceBlocklist().pipe(takeUntil(this.destroy$)).subscribe({
      next: (rows) => { this.blocklist = rows; this.cdr.markForCheck(); },
      error: (err) => { this.blocklistError = err?.error?.message || 'Impossible de charger la liste de blocage.'; this.cdr.markForCheck(); },
    });
  }

  ouvrirSuppression(d: AdminGpsDevice): void {
    this.deviceToDeleteId = d.deviceId;
  }

  fermerSuppression(e: { deleted: boolean }): void {
    this.deviceToDeleteId = null;
    if (e.deleted) { this.load(); this.loadBlocklist(); }
    this.cdr.markForCheck();
  }

  debloquer(b: GpsDeviceBlocklistEntry): void {
    const ok = confirm(`Débloquer le boîtier IMEI ${b.deviceUid} ?\n\nS'il émet encore, l'ingestion recréera sa fiche (société Belive, non affectée) à sa prochaine trame.`);
    if (!ok) return;
    this.adminService.unblockGpsDevice(b.deviceUid).pipe(takeUntil(this.destroy$)).subscribe({
      next: () => this.loadBlocklist(),
      error: (err) => { alert(err?.error?.message || 'Le déblocage a échoué.'); this.cdr.markForCheck(); },
    });
  }
}
