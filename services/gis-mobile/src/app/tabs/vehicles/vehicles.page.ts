import { Component, OnInit, OnDestroy } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { Subscription } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import { Vehicle } from '../../core/models/types';
import { AlertController, ToastController } from '@ionic/angular';

@Component({
  selector: 'app-vehicles',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Véhicules</ion-title>
        <ion-buttons slot="end">
          <ion-button (click)="toggleFilter()">
            <ion-icon name="filter-outline" slot="icon-only"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
      <ion-toolbar *ngIf="showSearch">
        <ion-searchbar
          placeholder="Rechercher un véhicule..."
          [(ngModel)]="searchTerm"
          (ionInput)="filterVehicles()"
          [debounce]="300"
        ></ion-searchbar>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true">
      <ion-refresher slot="fixed" (ionRefresh)="onRefresh($event)">
        <ion-refresher-content pullingText="Tirer pour rafraîchir"></ion-refresher-content>
      </ion-refresher>

      <!-- Filter chips -->
      <div class="filter-chips" *ngIf="showFilter">
        <ion-chip [color]="activeFilter === 'all' ? 'primary' : 'medium'" (click)="setFilter('all')">
          <ion-label>Tous ({{ vehicles.length }})</ion-label>
        </ion-chip>
        <ion-chip [color]="activeFilter === 'moving' ? 'success' : 'medium'" (click)="setFilter('moving')">
          <ion-icon name="navigate"></ion-icon>
          <ion-label>En mouvement ({{ movingCount }})</ion-label>
        </ion-chip>
        <ion-chip [color]="activeFilter === 'stopped' ? 'warning' : 'medium'" (click)="setFilter('stopped')">
          <ion-icon name="stop-circle"></ion-icon>
          <ion-label>À l'arrêt ({{ stoppedCount }})</ion-label>
        </ion-chip>
        <ion-chip [color]="activeFilter === 'offline' ? 'danger' : 'medium'" (click)="setFilter('offline')">
          <ion-icon name="cloud-offline"></ion-icon>
          <ion-label>Hors ligne ({{ offlineCount }})</ion-label>
        </ion-chip>
      </div>

      <!-- Vehicle list -->
      <ion-list>
        <ion-item *ngFor="let v of filteredVehicles; trackBy: trackById" detail (click)="selectVehicle(v)">
          <div slot="start" class="vehicle-status-dot" [ngClass]="getStatusClass(v)"></div>
          <ion-label>
            <h2 class="vehicle-name">{{ v.name }}</h2>
            <p class="vehicle-info">
              <span>{{ v.plate }}</span>
              <span *ngIf="v.brand"> &middot; {{ v.brand }} {{ v.model }}</span>
            </p>
            <p class="vehicle-location" *ngIf="v.lastAddress">
              <ion-icon name="location-outline" class="inline-icon"></ion-icon>
              {{ v.lastAddress }}
            </p>
          </ion-label>
          <div slot="end" class="vehicle-speed" *ngIf="v.currentSpeed !== undefined && v.currentSpeed !== null">
            <span class="speed-value">{{ v.currentSpeed | number:'1.0-0' }}</span>
            <span class="speed-unit">km/h</span>
          </div>
        </ion-item>
      </ion-list>

      <div class="empty-state" *ngIf="filteredVehicles.length === 0 && !loading">
        <ion-icon name="car-sport-outline"></ion-icon>
        <p>Aucun véhicule trouvé</p>
      </div>

      <ion-spinner *ngIf="loading" name="crescent" class="center-spinner"></ion-spinner>

      <!-- Vehicle detail modal (bottom sheet style) -->
      <div class="detail-sheet" *ngIf="selectedVehicle" (click)="closeDetail($event)">
        <div class="detail-card" (click)="$event.stopPropagation()">
          <div class="sheet-handle"></div>

          <div class="detail-header">
            <div class="detail-status" [ngClass]="getStatusClass(selectedVehicle)">
              <ion-icon [name]="getStatusIcon(selectedVehicle)"></ion-icon>
            </div>
            <div class="detail-title">
              <h2>{{ selectedVehicle.name }}</h2>
              <p>{{ selectedVehicle.plate }} &middot; {{ selectedVehicle.brand }} {{ selectedVehicle.model }}</p>
            </div>
            <ion-button fill="clear" (click)="selectedVehicle = null">
              <ion-icon name="close" slot="icon-only"></ion-icon>
            </ion-button>
          </div>

          <div class="detail-grid">
            <div class="detail-item">
              <ion-icon name="speedometer-outline" color="primary"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.currentSpeed || 0 }} km/h</span>
              <span class="detail-label">Vitesse</span>
            </div>
            <div class="detail-item">
              <ion-icon name="navigate-outline" color="success"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.mileage | number:'1.0-0' }} km</span>
              <span class="detail-label">Kilométrage</span>
            </div>
            <div class="detail-item">
              <ion-icon name="flash-outline" [color]="selectedVehicle.ignitionOn ? 'success' : 'medium'"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.ignitionOn ? 'ON' : 'OFF' }}</span>
              <span class="detail-label">Contact</span>
            </div>
            <div class="detail-item">
              <ion-icon name="car-outline" color="tertiary"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.type }}</span>
              <span class="detail-label">Type</span>
            </div>
          </div>

          <div class="detail-driver" *ngIf="selectedVehicle.assignedDriverName">
            <ion-icon name="person-outline" color="primary"></ion-icon>
            <span>{{ selectedVehicle.assignedDriverName }}</span>
          </div>

          <div class="detail-actions">
            <ion-button expand="block" fill="outline" shape="round" (click)="locateOnMap(selectedVehicle)">
              <ion-icon name="map-outline" slot="start"></ion-icon>
              Localiser
            </ion-button>
            <ion-button expand="block" fill="solid" color="primary" shape="round" (click)="openPlayback(selectedVehicle)">
              <ion-icon name="play-circle-outline" slot="start"></ion-icon>
              Playback
            </ion-button>
          </div>

          <!-- Immobilization -->
          <div class="immo-section" *ngIf="canImmobilize && selectedVehicle.gpsDeviceId">
            <div class="immo-status" *ngIf="immoStates.get(selectedVehicle.gpsDeviceId!)">
              <ion-icon name="lock-closed" color="danger" *ngIf="immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationActive"></ion-icon>
              <ion-icon name="lock-open" color="success" *ngIf="!immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationActive"></ion-icon>
              <span *ngIf="immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationActive">
                Véhicule immobilisé
                <span class="immo-by" *ngIf="immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationByName">
                  par {{ immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationByName }}
                </span>
              </span>
              <span *ngIf="!immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationActive">Véhicule libre</span>
            </div>
            <div class="immo-actions">
              <ion-button expand="block" color="danger" shape="round"
                          *ngIf="!immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationActive"
                          (click)="onStopVehicle(selectedVehicle)">
                <ion-icon name="hand-left" slot="start"></ion-icon>
                Arrêter le véhicule
              </ion-button>
              <ion-button expand="block" color="success" shape="round"
                          *ngIf="immoStates.get(selectedVehicle.gpsDeviceId!)?.immobilizationActive"
                          (click)="onGoVehicle(selectedVehicle)">
                <ion-icon name="play" slot="start"></ion-icon>
                Libérer le véhicule
              </ion-button>
            </div>
          </div>
        </div>
      </div>
    </ion-content>
  `,
  styles: [`
    .filter-chips {
      display: flex; gap: 6px; padding: 8px 12px;
      overflow-x: auto; white-space: nowrap;
    }
    .filter-chips ion-chip { font-size: 12px; }
    .vehicle-status-dot {
      width: 12px; height: 12px; border-radius: 50%;
      margin-right: 4px; flex-shrink: 0;
    }
    .vehicle-status-dot.moving { background: #10b981; }
    .vehicle-status-dot.stopped { background: #f59e0b; }
    .vehicle-status-dot.offline { background: #ef4444; }
    .vehicle-status-dot.available { background: #6b7280; }
    .vehicle-name { font-weight: 600; font-size: 15px; }
    .vehicle-info { font-size: 13px; }
    .vehicle-location { font-size: 12px; color: var(--ion-color-medium); display: flex; align-items: center; gap: 2px; }
    .inline-icon { font-size: 12px; }
    .vehicle-speed { text-align: center; }
    .speed-value { display: block; font-size: 18px; font-weight: 700; color: var(--ion-color-primary); }
    .speed-unit { font-size: 10px; color: var(--ion-color-medium); }
    .empty-state { text-align: center; padding: 40px 20px; color: var(--ion-color-medium); }
    .empty-state ion-icon { font-size: 48px; display: block; margin: 0 auto 12px; }
    .center-spinner { display: block; margin: 40px auto; }
    .detail-sheet {
      position: fixed; top: 0; left: 0; right: 0; bottom: 0;
      z-index: 9999; background: rgba(0,0,0,0.4);
      display: flex; align-items: flex-end;
    }
    .detail-card {
      background: var(--ion-card-background, #fff);
      border-radius: 20px 20px 0 0;
      padding: 12px 20px 32px;
      width: 100%;
      max-height: 70vh; overflow-y: auto;
    }
    .sheet-handle {
      width: 40px; height: 4px; background: var(--ion-color-light-shade);
      border-radius: 2px; margin: 0 auto 16px;
    }
    .detail-header { display: flex; align-items: center; gap: 12px; margin-bottom: 20px; }
    .detail-status {
      width: 48px; height: 48px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .detail-status.moving { background: rgba(16,185,129,0.15); color: #10b981; }
    .detail-status.stopped { background: rgba(245,158,11,0.15); color: #f59e0b; }
    .detail-status.offline { background: rgba(239,68,68,0.15); color: #ef4444; }
    .detail-status.available { background: rgba(107,114,128,0.15); color: #6b7280; }
    .detail-status ion-icon { font-size: 24px; }
    .detail-title { flex: 1; }
    .detail-title h2 { margin: 0; font-size: 18px; font-weight: 700; }
    .detail-title p { margin: 2px 0 0; font-size: 13px; color: var(--ion-color-medium); }
    .detail-grid {
      display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 16px;
    }
    .detail-item {
      background: var(--ion-color-light); border-radius: 12px;
      padding: 12px; text-align: center;
    }
    .detail-item ion-icon { font-size: 20px; display: block; margin: 0 auto 4px; }
    .detail-value { display: block; font-weight: 600; font-size: 14px; }
    .detail-label { display: block; font-size: 11px; color: var(--ion-color-medium); }
    .detail-driver {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 14px; background: var(--ion-color-light);
      border-radius: 10px; margin-bottom: 16px; font-size: 14px;
    }
    .detail-actions { display: flex; gap: 8px; }
    .detail-actions ion-button { flex: 1; }
    .immo-section { margin-top: 16px; border-top: 1px solid var(--ion-color-light-shade); padding-top: 16px; }
    .immo-status {
      display: flex; align-items: center; gap: 8px;
      padding: 10px 14px; background: var(--ion-color-light);
      border-radius: 10px; margin-bottom: 12px; font-size: 14px; font-weight: 500;
    }
    .immo-by { font-weight: 400; font-size: 12px; color: var(--ion-color-medium); }
    .immo-actions ion-button { font-weight: 700; }
  `]
})
export class VehiclesPage implements OnInit, OnDestroy {
  vehicles: Vehicle[] = [];
  filteredVehicles: Vehicle[] = [];
  selectedVehicle: Vehicle | null = null;
  loading = true;
  searchTerm = '';
  showSearch = true;
  showFilter = false;
  activeFilter = 'all';
  movingCount = 0;
  stoppedCount = 0;
  offlineCount = 0;

  canImmobilize = false;
  immoStates = new Map<string, any>();

  private subs: Subscription[] = [];
  private positionMap = new Map<number, PositionUpdate>();

  constructor(
    private api: ApiService,
    private authService: AuthService,
    private signalr: SignalRService,
    private router: Router,
    private route: ActivatedRoute,
    private alertCtrl: AlertController,
    private toastCtrl: ToastController
  ) {}

  ngOnInit() {
    // Only company admins may stop/release a vehicle. Gate the UI on the
    // backend truth (Role.IsCompanyAdmin), not on "any logged-in user".
    //
    // Subscribe reactively to currentUser$ so that when an admin is promoted
    // AFTER login, the next token refresh (which repopulates `isCompanyAdmin`
    // in the BehaviorSubject) will immediately reveal the stop-vehicle
    // controls — no forced logout needed. Conversely, a demoted user loses
    // the controls as soon as their token rotates.
    //
    // This is strictly a UX gate. The real enforcement lives in the backend
    // (GpsController.ExecuteImmobilizationAsync → 403 for non-admins). Never
    // trust this boolean alone.
    this.subs.push(
      this.authService.getCurrentUser().subscribe(user => {
        this.canImmobilize = !!user?.isCompanyAdmin;
      })
    );

    this.loadVehicles();

    // Update vehicles with real-time positions.
    // NOTE: The API serialises vehicle.id as a JSON number, but the
    // mobile TypeScript model types it as `string`. So `v.id` is
    // actually a number at runtime despite the annotation. Compare
    // both sides as strings to be safe against either shape.
    this.subs.push(
      this.signalr.positionUpdate$.subscribe(pos => {
        this.positionMap.set(pos.vehicleId, pos);
        const v = this.vehicles.find(x => String(x.id) === String(pos.vehicleId));
        if (v) {
          v.currentSpeed = pos.speedKph;
          v.currentLocation = { lat: pos.latitude, lng: pos.longitude };
          v.ignitionOn = pos.ignitionOn;
          v.isOnline = true;
          this.updateCounts();
        }
      })
    );
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  loadVehicles() {
    this.loading = true;
    this.api.getVehicles().subscribe({
      next: (vehicles) => {
        // Backend returns the GPS device as a nested object:
        //   vehicle.gpsDevice = { id, deviceUid, ... }
        // but the mobile Vehicle model exposes only a flat `gpsDeviceId`
        // field (used by the immobilization section's *ngIf). Without this
        // flattening `gpsDeviceId` is always undefined, the "Arrêter/Libérer"
        // section never renders, and the whole mobile-initiated stop flow
        // is silently invisible to the user.
        const raw: any[] = Array.isArray(vehicles) ? vehicles : [];
        this.vehicles = raw.map((v: any) => ({
          ...v,
          gpsDeviceId: v.gpsDevice?.id != null ? String(v.gpsDevice.id) : undefined,
        }));
        this.updateCounts();
        this.filterVehicles();
        this.loading = false;
        // Merge in last known positions from REST so the "Localiser" button
        // works even before any SignalR event has been received for a given
        // vehicle. Without this, vehicles that haven't transmitted since
        // the tab was opened have currentLocation=undefined, and tapping
        // Localiser silently navigates to /monitoring with no target.
        this.api.getLastPositions().subscribe({
          next: (positions) => {
            const list = Array.isArray(positions) ? positions : [];
            list.forEach((p: any) => {
              if (!p?.lastPosition) return;
              // API returns vehicleId as number; our Vehicle.id is
              // typed as string but actually arrives as number too —
              // compare via String() on both sides.
              const v = this.vehicles.find(x => String(x.id) === String(p.vehicleId));
              if (v) {
                v.currentLocation = {
                  lat: p.lastPosition.latitude,
                  lng: p.lastPosition.longitude
                };
                v.currentSpeed = p.lastPosition.speedKph || 0;
                v.ignitionOn = p.lastPosition.ignitionOn ?? v.ignitionOn;
                v.isOnline = true;
              }
            });
            this.updateCounts();
            this.filterVehicles();
          },
          error: () => { /* non-fatal: live SignalR will still populate positions */ }
        });
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  updateCounts() {
    this.movingCount = this.vehicles.filter(v => v.isOnline && (v.currentSpeed || 0) > 3).length;
    this.stoppedCount = this.vehicles.filter(v => v.isOnline && (v.currentSpeed || 0) <= 3).length;
    this.offlineCount = this.vehicles.filter(v => !v.isOnline).length;
  }

  filterVehicles() {
    let result = [...this.vehicles];

    if (this.searchTerm) {
      const term = this.searchTerm.toLowerCase();
      result = result.filter(v =>
        v.name?.toLowerCase().includes(term) ||
        v.plate?.toLowerCase().includes(term) ||
        v.brand?.toLowerCase().includes(term)
      );
    }

    switch (this.activeFilter) {
      case 'moving':
        result = result.filter(v => v.isOnline && (v.currentSpeed || 0) > 3);
        break;
      case 'stopped':
        result = result.filter(v => v.isOnline && (v.currentSpeed || 0) <= 3);
        break;
      case 'offline':
        result = result.filter(v => !v.isOnline);
        break;
    }

    this.filteredVehicles = result;
  }

  setFilter(filter: string) {
    this.activeFilter = filter;
    this.filterVehicles();
  }

  toggleFilter() {
    this.showFilter = !this.showFilter;
  }

  selectVehicle(v: Vehicle) {
    this.selectedVehicle = v;
    if (this.canImmobilize && v.gpsDeviceId) {
      this.loadImmoState(v.gpsDeviceId);
    }
  }

  private loadImmoState(deviceId: string) {
    this.api.getImmobilizationState(parseInt(deviceId)).subscribe({
      next: (state) => this.immoStates.set(deviceId, state),
      error: () => {}
    });
  }

  closeDetail(event: Event) {
    this.selectedVehicle = null;
  }

  async locateOnMap(v: Vehicle) {
    this.selectedVehicle = null;
    // If we already know the vehicle's location, navigate directly.
    if (v.currentLocation) {
      this.router.navigate(['/tabs/monitoring'], {
        queryParams: { lat: v.currentLocation.lat, lng: v.currentLocation.lng, vehicleId: v.id }
      });
      return;
    }
    // Otherwise try one last fetch before giving up — REST may have a
    // more recent position than what we hold in memory.
    this.api.getLastPositions().subscribe({
      next: async (positions) => {
        const list = Array.isArray(positions) ? positions : [];
        // Both sides stringified — v.id is a JSON number at runtime
        // despite the TS `string` annotation.
        const found = list.find((p: any) => String(p.vehicleId) === String(v.id) && p.lastPosition);
        if (found) {
          const lp = found.lastPosition;
          v.currentLocation = { lat: lp.latitude, lng: lp.longitude };
          this.router.navigate(['/tabs/monitoring'], {
            queryParams: { lat: lp.latitude, lng: lp.longitude, vehicleId: v.id }
          });
        } else {
          const toast = await this.toastCtrl.create({
            message: `Aucune position connue pour ${v.name}. Le véhicule n'a jamais transmis sa position.`,
            duration: 3000,
            color: 'warning',
            position: 'bottom'
          });
          await toast.present();
        }
      },
      error: async () => {
        const toast = await this.toastCtrl.create({
          message: 'Impossible de récupérer la position du véhicule.',
          duration: 2500,
          color: 'danger'
        });
        await toast.present();
      }
    });
  }

  openPlayback(v: Vehicle) {
    this.selectedVehicle = null;
    this.router.navigate(['/tabs/playback'], {
      queryParams: { vehicleId: v.id, vehicleName: v.name }
    });
  }

  async onRefresh(event: any) {
    this.loadVehicles();
    setTimeout(() => event.target.complete(), 1500);
  }

  getStatusClass(v: Vehicle): string {
    if (v.isOnline && (v.currentSpeed || 0) > 3) return 'moving';
    if (v.isOnline) return 'stopped';
    if (v.status === 'maintenance') return 'offline';
    return 'available';
  }

  getStatusIcon(v: Vehicle): string {
    if (v.isOnline && (v.currentSpeed || 0) > 3) return 'navigate';
    if (v.isOnline) return 'stop-circle';
    return 'cloud-offline';
  }

  trackById(_: number, v: Vehicle) {
    return v.id;
  }

  // ── Immobilization flow ──

  async onStopVehicle(v: Vehicle) {
    if (!v.gpsDeviceId) return;
    const deviceId = parseInt(v.gpsDeviceId);

    const passwordAlert = await this.alertCtrl.create({
      header: 'Confirmation requise',
      message: `Pour arrêter "${v.name}", entrez votre mot de passe`,
      backdropDismiss: false,
      inputs: [{ name: 'password', type: 'password', placeholder: 'Mot de passe' }],
      buttons: [
        { text: 'Annuler', role: 'cancel' },
        { text: 'Envoyer', handler: (data) => {
          // SECURITY: mobile must ALWAYS go through the direct (password-verified)
          // dispatch path. Returning true with an empty password used to fall
          // through to the backend "request" mode, which fan-outs an approval
          // notification to admins. When the caller is themselves an admin
          // (common on mobile) the notification bounces right back via SignalR,
          // they tap ACCEPTER, and the vehicle stops without ever having typed
          // a password — bug reported on v1.0.6. Here we refuse to close the
          // alert on empty input.
          const pwd = (data?.password || '').trim();
          if (!pwd) {
            this.toastCtrl.create({
              message: 'Mot de passe requis', duration: 1500, color: 'warning', position: 'top'
            }).then(t => t.present());
            return false;
          }
          this.verifyAndSendRequest(v, deviceId, pwd, 'stop');
          return true;
        }}
      ]
    });
    await passwordAlert.present();
  }

  /**
   * Single-call flow: the backend verifies the password AND dispatches the
   * command atomically (direct mode). A 401 means wrong password; 200 means
   * the command was pushed to the device.
   */
  private verifyAndSendRequest(v: Vehicle, deviceId: number, password: string, type: 'stop' | 'go') {
    const call = type === 'stop'
      ? this.api.stopVehicle(deviceId, password)
      : this.api.goVehicle(deviceId, password);

    call.subscribe({
      next: async (res: any) => {
        const isDirect = res?.mode === 'direct';
        const defaultMsg = type === 'stop'
          ? (isDirect ? 'Commande d\'arrêt envoyée' : 'Demande envoyée à l\'administrateur')
          : (isDirect ? 'Commande de libération envoyée' : 'Demande envoyée à l\'administrateur');
        const toast = await this.toastCtrl.create({
          message: res?.message || defaultMsg, duration: 3000, color: 'success', position: 'top'
        });
        await toast.present();
      },
      error: async (err) => {
        const is401 = err?.status === 401;
        const msg = is401
          ? 'Mot de passe incorrect'
          : (err?.error?.message || 'Erreur lors de l\'envoi');
        const toast = await this.toastCtrl.create({
          message: msg, duration: 2500, color: 'danger', position: 'top'
        });
        await toast.present();
      }
    });
  }

  async onGoVehicle(v: Vehicle) {
    if (!v.gpsDeviceId) return;
    const deviceId = parseInt(v.gpsDeviceId);

    const passwordAlert = await this.alertCtrl.create({
      header: 'Libérer le véhicule',
      message: `Pour libérer "${v.name}", entrez votre mot de passe`,
      backdropDismiss: false,
      inputs: [{ name: 'password', type: 'password', placeholder: 'Mot de passe' }],
      buttons: [
        { text: 'Annuler', role: 'cancel' },
        { text: 'Envoyer', handler: (data) => {
          // SECURITY: same reasoning as onStopVehicle — empty password used to
          // fall through to the backend "request" path, which fires an admin
          // approval notification that an admin caller can auto-accept,
          // effectively bypassing password verification. Refuse to close
          // the alert on empty input.
          const pwd = (data?.password || '').trim();
          if (!pwd) {
            this.toastCtrl.create({
              message: 'Mot de passe requis', duration: 1500, color: 'warning', position: 'top'
            }).then(t => t.present());
            return false;
          }
          this.verifyAndSendRequest(v, deviceId, pwd, 'go');
          return true;
        }}
      ]
    });
    await passwordAlert.present();
  }
}
