import { Component, OnInit, OnDestroy, NgZone } from '@angular/core';
import { Router, ActivatedRoute } from '@angular/router';
import { Subscription, firstValueFrom, interval } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/services/auth.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import {
  PositionShareService, ShareChannel, SharedPosition, hasKnownPosition
} from '../../core/services/position-share.service';
import {
  NO_DEVICE_STYLE, NoDeviceStyle, STATE_TEXT_ON_COLOR, VEHICLE_STATES, VEHICLE_STATE_ORDER, VehicleMotionState,
  VehicleStateStyle, countByState, isFresh, motionState, stateStyle
} from '../../core/vehicle-state.util';
import { Vehicle } from '../../core/models/types';
import { AlertController, ToastController } from '@ionic/angular';

/** Ce que montre la pastille d'une ligne : un des quatre états, ou « sans boîtier » (pas un état). */
export type VehicleRowState = VehicleMotionState | 'no-device';

/** Recalcul périodique des états : un boîtier muet n'envoie plus rien qui déclencherait le passage au gris. */
export const STATE_REFRESH_MS = 30000;

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

      <!-- Filter chips : une puce par état, pastille à la couleur de l'état + libellé + compteur.
           La puce sélectionnée prend la couleur de l'application (pas celle d'un état). -->
      <div class="filter-chips" *ngIf="showFilter" role="group" aria-label="Filtrer par état">
        <ion-chip class="filter-chip" data-filter="all" [color]="activeFilter === 'all' ? 'primary' : 'medium'"
                  [attr.aria-pressed]="activeFilter === 'all'" (click)="setFilter('all')">
          <ion-label>Tous ({{ vehicles.length }})</ion-label>
        </ion-chip>
        <ion-chip *ngFor="let s of stateStyles" class="filter-chip" [attr.data-filter]="s.state"
                  [color]="activeFilter === s.state ? 'primary' : 'medium'"
                  [attr.aria-pressed]="activeFilter === s.state" (click)="setFilter(s.state)">
          <span class="chip-dot" [style.background]="s.color" aria-hidden="true"></span>
          <ion-label>{{ s.label }} ({{ counts[s.state] }})</ion-label>
        </ion-chip>
      </div>

      <!-- Vehicle list -->
      <ion-list>
        <ion-item *ngFor="let v of filteredVehicles; trackBy: trackById" detail (click)="selectVehicle(v)">
          <div slot="start" class="vehicle-status-dot" [ngClass]="stateOf(v)"
               [style.background]="styleOf(v).tint" [style.color]="styleOf(v).color">
            <ion-icon [name]="styleOf(v).icon" aria-hidden="true"></ion-icon>
          </div>
          <ion-label>
            <h2 class="vehicle-name">{{ v.name }}</h2>
            <p class="vehicle-info">
              <span class="vehicle-state-label">{{ styleOf(v).label }}</span>
              <span> &middot; {{ v.plate }}</span>
              <span *ngIf="v.brand"> &middot; {{ v.brand }} {{ v.model }}</span>
            </p>
            <p class="vehicle-location" *ngIf="v.lastAddress">
              <ion-icon name="location-outline" class="inline-icon"></ion-icon>
              {{ v.lastAddress }}
            </p>
          </ion-label>
          <!-- Pas de vitesse sous « Déconnecté » : celle d'une trame vieille de plusieurs jours
               contredisait la pastille grise (vitesse fantôme). -->
          <div slot="end" class="vehicle-speed" *ngIf="showsSpeed(v)">
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
            <div class="detail-status" [ngClass]="stateOf(selectedVehicle)"
                 [style.background]="styleOf(selectedVehicle).tint" [style.color]="styleOf(selectedVehicle).color">
              <ion-icon [name]="styleOf(selectedVehicle).icon" aria-hidden="true"></ion-icon>
            </div>
            <div class="detail-title">
              <h2>{{ selectedVehicle.name }}</h2>
              <p>{{ selectedVehicle.plate }} &middot; {{ selectedVehicle.brand }} {{ selectedVehicle.model }}</p>
              <span class="status-pill" [ngClass]="stateOf(selectedVehicle)"
                    [style.background]="pillBackground(selectedVehicle)" [style.color]="pillText(selectedVehicle)">{{ styleOf(selectedVehicle).label }}</span>
            </div>
            <ion-button fill="clear" (click)="selectedVehicle = null">
              <ion-icon name="close" slot="icon-only"></ion-icon>
            </ion-button>
          </div>

          <div class="detail-grid">
            <div class="detail-item">
              <ion-icon name="speedometer-outline" color="primary"></ion-icon>
              <span class="detail-value" *ngIf="showsSpeed(selectedVehicle); else noSpeed">{{ selectedVehicle.currentSpeed | number:'1.0-0' }} km/h</span>
              <ng-template #noSpeed><span class="detail-value no-speed">—</span></ng-template>
              <span class="detail-label">Vitesse</span>
            </div>
            <div class="detail-item">
              <!-- Ni flèche ni vert : c'était l'icône « En route », même sur un véhicule garé. -->
              <ion-icon name="trail-sign-outline" color="primary"></ion-icon>
              <span class="detail-value">{{ selectedVehicle.mileage | number:'1.0-0' }} km</span>
              <span class="detail-label">Kilométrage</span>
            </div>
            <div class="detail-item">
              <!-- Contact mis = couleur de l'application, pas le vert « En route » : au ralenti,
                   la fiche montrait une pastille orange à côté d'un éclair vert. -->
              <ion-icon name="flash-outline" [color]="selectedVehicle.ignitionOn ? 'primary' : 'medium'"></ion-icon>
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
          <app-position-share-bar
            [disabled]="!canSharePosition(selectedVehicle)"
            (share)="sharePosition(selectedVehicle, $event)"></app-position-share-bar>

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
    .filter-chips ion-chip { font-size: 12px; flex: none; }
    .chip-dot {
      width: 10px; height: 10px; border-radius: 50%; flex: none;
      margin-right: 6px; box-shadow: 0 0 0 1.5px #fff;
    }
    /* Couleurs d'état liées depuis vehicle-state.util : aucune n'est recodée ici. */
    .vehicle-status-dot {
      width: 32px; height: 32px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      margin-right: 4px; flex-shrink: 0;
    }
    .vehicle-status-dot ion-icon { font-size: 18px; }
    /* Sans boîtier : contour pointillé et non un disque teinté, pour ne se lire comme aucun état. */
    .vehicle-status-dot.no-device, .detail-status.no-device, .status-pill.no-device {
      box-sizing: border-box; border: 1.5px dashed currentColor;
    }
    .vehicle-name { font-weight: 600; font-size: 15px; }
    .vehicle-info { font-size: 13px; }
    .vehicle-state-label { font-weight: 600; color: var(--ion-text-color); }
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
      /* 85vh (au lieu de 70vh) : avec la rangée de partage, un chauffeur affecté et le
         bloc d'immobilisation, 70vh obligeait à défiler pour atteindre le partage sur
         un écran de 640 px de haut. */
      max-height: 85vh; overflow-y: auto;
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
    .detail-status ion-icon { font-size: 24px; }
    .detail-title { flex: 1; min-width: 0; }
    .detail-title h2 { margin: 0; font-size: 18px; font-weight: 700; }
    .detail-title p { margin: 2px 0 0; font-size: 13px; color: var(--ion-color-medium); }
    .status-pill {
      display: inline-block; margin-top: 6px;
      font-size: 11px; font-weight: 700; padding: 3px 10px; border-radius: 12px;
      white-space: nowrap;
    }
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
  activeFilter: 'all' | VehicleMotionState = 'all';
  /** Compteurs des puces de filtre, un par état (vehicle-state.util). */
  counts: Record<VehicleMotionState, number> = countByState([]);
  /** Puces « En route / Au ralenti / À l'arrêt / Déconnecté », dans l'ordre de la légende. */
  readonly stateStyles: VehicleStateStyle[] = VEHICLE_STATE_ORDER.map(s => VEHICLE_STATES[s]);
  /** Texte sur la couleur d'un état : lisible sur les quatre couleurs (contraste >= 4,5:1). */
  readonly textOnState = STATE_TEXT_ON_COLOR;

  canImmobilize = false;
  immoStates = new Map<string, any>();
  /** Les dernières positions REST sont arrivées : un véhicule encore sans position n'en a pas. */
  positionsLoaded = false;

  private subs: Subscription[] = [];
  private positionMap = new Map<number, PositionUpdate>();
  /**
   * Horodatage qui juge la fraîcheur de chaque véhicule : celui de sa dernière trame, ou
   * l'instant de réception d'une trame live qui n'en porte pas (« bénéfice du direct »).
   * Séparé de lastRecordedAt, qui date la position PARTAGÉE et ne doit pas être inventé.
   */
  private freshnessAt = new Map<string, string>();
  /** État calculé au dernier recomptage : pastilles, puces et filtre lisent le même instantané. */
  private states = new Map<string, VehicleMotionState>();
  /**
   * Adresse de la dernière position REST, AVEC le point qu'elle décrit. Les trames SignalR
   * n'ont pas d'adresse : dès que le véhicule a bougé, l'adresse gardée ne correspond plus
   * au lien partagé et ne doit plus figurer dans le message.
   */
  private restAddresses = new Map<string, { lat: number; lng: number; address: string }>();

  constructor(
    private api: ApiService,
    private authService: AuthService,
    private signalr: SignalRService,
    private router: Router,
    private route: ActivatedRoute,
    private alertCtrl: AlertController,
    private toastCtrl: ToastController,
    private positionShare: PositionShareService,
    private zone: NgZone
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
      this.signalr.positionBatch$.subscribe(batch => {
        let touched = false;
        for (const pos of batch) {
          this.positionMap.set(pos.vehicleId, pos);
          const v = this.vehicles.find(x => String(x.id) === String(pos.vehicleId));
          if (v) {
            v.currentSpeed = pos.speedKph;
            v.currentLocation = { lat: pos.latitude, lng: pos.longitude };
            v.ignitionOn = pos.ignitionOn;
            // Une trame poussée en direct peut être un rejeu d'historique
            // (trames AA23) : c'est son horodatage qui dit si le véhicule est
            // en ligne, pas le fait qu'elle vienne d'arriver. Sans horodatage,
            // on accorde le bénéfice du direct.
            v.isOnline = pos.recordedAt ? isFresh(pos.recordedAt) : true;
            v.lastRecordedAt = pos.recordedAt || v.lastRecordedAt;
            this.freshnessAt.set(String(v.id), pos.recordedAt || new Date().toISOString());
            touched = true;
          }
        }
        // updateCounts() does a full-fleet pass — run it once per batch instead
        // of once per device frame. Un filtre d'état actif est réappliqué : sinon
        // un véhicule passé de « Au ralenti » à « En route » resterait listé sous
        // la mauvaise puce, avec une pastille qui la contredit.
        if (touched) {
          this.updateCounts();
          if (this.activeFilter !== 'all') this.filterVehicles();
        }
      })
    );

    // Un boîtier qui se tait n'envoie plus rien : l'état mis en cache dans `states` n'était
    // recalculé qu'au lot SignalR suivant d'un AUTRE véhicule. Recalcul périodique pour qu'un
    // véhicule muet depuis plus de 30 min passe au gris « Déconnecté » (même cadence que le
    // repli de la carte). Désabonné avec les autres dans ngOnDestroy. L'horloge tourne HORS
    // de la zone Angular (une minuterie permanente dans la zone empêche l'application d'être
    // jamais « stable ») ; seul le recalcul y rentre, pour rafraîchir l'affichage.
    this.zone.runOutsideAngular(() => {
      this.subs.push(
        interval(STATE_REFRESH_MS).subscribe(() => this.zone.run(() => {
          this.updateCounts();
          if (this.activeFilter !== 'all') this.filterVehicles();
        }))
      );
    });
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  loadVehicles() {
    this.loading = true;
    // Les véhicules rechargés n'ont pas encore de position : ne pas griser le partage
    // avant que les dernières positions REST soient revenues.
    this.positionsLoaded = false;
    // Les véhicules rechargés repartent sans vitesse ni contact : garder leurs anciens
    // horodatages les peindrait « À l'arrêt » (frais, contact inconnu) le temps que les
    // positions reviennent. Sans horodatage, ils attendent en « Déconnecté ».
    this.freshnessAt.clear();
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
                // « Avoir une dernière position » ne veut pas dire « être en
                // ligne » : celle du 239 TU 6235 datait de 11 jours, et le
                // véhicule était pourtant compté parmi les connectés.
                v.isOnline = isFresh(p.lastPosition.recordedAt);
                v.lastRecordedAt = p.lastPosition.recordedAt || v.lastRecordedAt;
                if (p.lastPosition.recordedAt) this.freshnessAt.set(String(v.id), p.lastPosition.recordedAt);
                this.rememberAddress(v, p.lastPosition);
              }
            });
            this.positionsLoaded = true;
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

  /**
   * Recalcule l'état de chaque véhicule (motionState : fraîcheur d'abord, puis vitesse, puis
   * contact) et les compteurs des puces. L'ancienne version ne lisait que la vitesse : un
   * véhicule arrêté moteur tournant et un véhicule garé contact coupé tombaient tous deux dans
   * « À l'arrêt » en orange, alors que la carte les montrait orange et rouge.
   */
  updateCounts(nowMs: number = Date.now()) {
    const states = new Map<string, VehicleMotionState>();
    // Un véhicule sans boîtier n'a pas d'état : il n'entre que dans « Tous », comme au
    // tableau de bord, qui ne compte que la flotte équipée.
    for (const v of this.vehicles) {
      if (this.isEquipped(v)) states.set(String(v.id), this.computeState(v, nowMs));
    }
    this.states = states;
    this.counts = countByState(states.values());
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

    if (this.activeFilter !== 'all') {
      const wanted = this.activeFilter;
      result = result.filter(v => this.stateOf(v) === wanted);
    }

    this.filteredVehicles = result;
  }

  setFilter(filter: 'all' | VehicleMotionState) {
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

  /**
   * Boutons de partage actifs si la position est en mémoire, ou tant que les dernières
   * positions REST ne sont pas arrivées (ou ont échoué) : le repli REST au toucher peut
   * encore en trouver une. Grisés seulement quand on SAIT qu'il n'y en a aucune.
   */
  canSharePosition(v: Vehicle | null): boolean {
    if (!v) return false;
    if (v.currentLocation && hasKnownPosition(v.currentLocation.lat, v.currentLocation.lng)) return true;
    return !this.positionsLoaded;
  }

  /** Partage la dernière position connue (WhatsApp, Messenger, SMS ou feuille de
   *  partage). Même stratégie de repli que Localiser : position en mémoire, sinon
   *  un dernier fetch REST. Message et liens : PositionShareService. */
  async sharePosition(v: Vehicle, channel: ShareChannel) {
    const pos = await this.resolveSharedPosition(v);
    if (pos) await this.positionShare.share(channel, pos);
  }

  private async resolveSharedPosition(v: Vehicle): Promise<SharedPosition | null> {
    if (v.currentLocation && hasKnownPosition(v.currentLocation.lat, v.currentLocation.lng)) {
      return {
        label: v.plate || v.name,
        latitude: v.currentLocation.lat,
        longitude: v.currentLocation.lng,
        // Jamais v.lastAddress : rien ne la relie au point courant (les trames SignalR
        // déplacent currentLocation sans toucher à l'adresse).
        address: this.addressAt(v, v.currentLocation.lat, v.currentLocation.lng),
        recordedAt: v.lastRecordedAt
      };
    }
    let list: any[];
    try {
      const positions = await firstValueFrom(this.api.getLastPositions());
      list = Array.isArray(positions) ? positions : [];
    } catch {
      await this.showToast('Impossible de récupérer la position du véhicule.', 'danger', 2500);
      return null;
    }
    const found = list.find((p: any) => String(p.vehicleId) === String(v.id) && p.lastPosition);
    const lp = found?.lastPosition;
    if (!lp || !hasKnownPosition(lp.latitude, lp.longitude)) {
      await this.showToast(`Aucune position connue pour ${v.name}.`, 'warning', 3000);
      return null;
    }
    v.currentLocation = { lat: lp.latitude, lng: lp.longitude };
    v.lastRecordedAt = lp.recordedAt || v.lastRecordedAt;
    this.rememberAddress(v, lp);
    return {
      label: v.plate || v.name,
      latitude: lp.latitude,
      longitude: lp.longitude,
      // L'adresse de CETTE position seulement, pas une adresse gardée d'avant.
      address: this.addressAt(v, lp.latitude, lp.longitude),
      // La date de CETTE position, pas celle d'une trame précédente : sans elle le
      // message dit « date inconnue » plutôt que de dater faussement.
      recordedAt: lp.recordedAt
    };
  }

  /** Mémorise l'adresse d'une position REST avec ses coordonnées (ou l'oublie s'il n'y en a pas). */
  private rememberAddress(v: Vehicle, lp: { latitude: number; longitude: number; address?: string | null }) {
    const address = (lp.address || '').trim();
    if (address) this.restAddresses.set(String(v.id), { lat: lp.latitude, lng: lp.longitude, address });
    else this.restAddresses.delete(String(v.id));
  }

  /** L'adresse connue pour CE point (au 1e-6 près, la précision du lien), sinon aucune. */
  private addressAt(v: Vehicle, lat: number, lng: number): string | null {
    const known = this.restAddresses.get(String(v.id));
    const same = (a: number, b: number) => Number(a).toFixed(6) === Number(b).toFixed(6);
    return known && same(known.lat, lat) && same(known.lng, lng) ? known.address : null;
  }

  private async showToast(message: string, color: 'warning' | 'danger', duration: number) {
    const toast = await this.toastCtrl.create({ message, duration, color, position: 'bottom' });
    await toast.present();
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

  /**
   * État affiché d'un véhicule — même règle que la carte (motionState). Un véhicule équipé
   * qui n'a jamais transmis, ou muet depuis plus de 30 min, est « Déconnecté » ; un véhicule
   * sans boîtier n'a pas d'état (« Sans boîtier », pastille neutre).
   */
  stateOf(v: Vehicle): VehicleRowState {
    if (!this.isEquipped(v)) return 'no-device';
    return this.states.get(String(v.id)) ?? this.computeState(v, Date.now());
  }

  /** Couleur, libellé et icône de l'état (vehicle-state.util, jamais recodés ici). */
  styleOf(v: Vehicle): VehicleStateStyle | NoDeviceStyle {
    const s = this.stateOf(v);
    return s === 'no-device' ? NO_DEVICE_STYLE : stateStyle(s);
  }

  /** Fond de la pastille de la fiche : couleur pleine de l'état, transparent sans boîtier. */
  pillBackground(v: Vehicle): string {
    const s = this.styleOf(v);
    return s.state === 'no-device' ? 'transparent' : s.color;
  }

  /** Texte de la pastille : foncé sur la couleur d'un état, couleur de l'application sans boîtier. */
  pillText(v: Vehicle): string {
    return this.stateOf(v) === 'no-device' ? NO_DEVICE_STYLE.color : this.textOnState;
  }

  /**
   * La vitesse n'est affichée que si elle décrit le présent : sous « Déconnecté », c'est
   * celle de la dernière trame (38 km/h il y a 11 jours) et elle contredit la pastille.
   */
  showsSpeed(v: Vehicle): boolean {
    const s = this.stateOf(v);
    return s !== 'offline' && s !== 'no-device' && v.currentSpeed != null;
  }

  /**
   * Équipé = a un boîtier, ou a déjà transmis (boîtier posé après le chargement de la liste :
   * ses trames arrivent par SignalR avant le prochain rechargement).
   */
  private isEquipped(v: Vehicle): boolean {
    return !!v.gpsDeviceId || !!v.lastRecordedAt || this.freshnessAt.has(String(v.id));
  }

  private computeState(v: Vehicle, nowMs: number): VehicleMotionState {
    return motionState({
      speedKph: v.currentSpeed,
      ignitionOn: v.ignitionOn,
      recordedAt: this.freshnessAt.get(String(v.id)) ?? v.lastRecordedAt
    }, nowMs);
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
