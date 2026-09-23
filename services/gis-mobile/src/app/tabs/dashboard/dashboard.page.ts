import { Component, OnInit, OnDestroy, NgZone } from '@angular/core';
import { Subscription, interval } from 'rxjs';
import { ApiService } from '../../core/services/api.service';
import { SignalRService, PositionUpdate } from '../../core/services/signalr.service';
import {
  VehicleMotionState, VehicleStateStyle, countByState, motionState, stateStyle
} from '../../core/vehicle-state.util';

/** Ce qu'il faut d'une position pour classer un véhicule (motionState). */
type StateInput = { speedKph?: number | null; ignitionOn?: boolean | null; recordedAt?: string | null };
import { AuthService } from '../../core/services/auth.service';
import { Router } from '@angular/router';

@Component({
  selector: 'app-dashboard',
  standalone: false,
  template: `
    <ion-header>
      <ion-toolbar color="primary">
        <ion-title>Tableau de bord</ion-title>
        <ion-buttons slot="end">
          <ion-button (click)="goToProfile()">
            <ion-icon name="person-circle-outline" slot="icon-only"></ion-icon>
          </ion-button>
        </ion-buttons>
      </ion-toolbar>
    </ion-header>

    <ion-content [fullscreen]="true">
      <ion-refresher slot="fixed" (ionRefresh)="onRefresh($event)">
        <ion-refresher-content pullingText="Tirer pour rafraîchir"></ion-refresher-content>
      </ion-refresher>

      <!-- Welcome card -->
      <div class="welcome-card">
        <h2>Bonjour, {{ userName }} 👋</h2>
        <p>{{ companyName }}</p>
        <div class="connection-badge" [class.connected]="isConnected" [class.disconnected]="!isConnected">
          <ion-icon [name]="isConnected ? 'wifi' : 'wifi-outline'"></ion-icon>
          {{ isConnected ? 'Temps réel actif' : 'Hors ligne' }}
        </div>
      </div>

      <!-- Stats Grid -->
      <div class="stats-grid">
        <div class="stat-card" (click)="goToTab('vehicles')">
          <div class="stat-icon" style="background: rgba(59,130,246,0.15);">
            <ion-icon name="car-sport" style="color: #3b82f6;"></ion-icon>
          </div>
          <div class="stat-value">{{ totalVehicles }}</div>
          <div class="stat-label">Véhicules</div>
        </div>

        <!-- Les tuiles qui ne sont PAS des états de véhicule évitent le vert, l'orange et le
             gris (réservés à En route / Au ralenti / Déconnecté) : la distance passe en indigo,
             la maintenance (autrefois orange, lue « au ralenti ») en violet. Seules les alertes
             gardent le rouge d'usage, porté par leur triangle et leur libellé. -->
        <div class="stat-card">
          <div class="stat-icon" style="background: rgba(99,102,241,0.15);">
            <!-- Le cadran de compteur est parti avec le mot : il disait « relevé de compteur »
                 sur une somme de la journée. Même pictogramme que l'onglet Distance. -->
            <ion-icon name="analytics-outline" style="color: #6366f1;"></ion-icon>
          </div>
          <div class="stat-value">{{ distanceToday | number:'1.0-0' }} <span class="stat-unit">km</span></div>
          <!-- « Distance aujourd'hui » à côté d'un pictogramme de compteur se lisait comme un
               relevé de compteur. La tuile dit la grandeur (distance parcourue), sa période
               (le jour) et d'où elle sort (les trajets détectés, comme l'onglet Trajets). -->
          <div class="stat-label">Distance parcourue</div>
          <div class="stat-sub">aujourd'hui &middot; trajets détectés</div>
        </div>

        <div class="stat-card">
          <div class="stat-icon" style="background: rgba(139,92,246,0.15);">
            <ion-icon name="build" style="color: #8b5cf6;"></ion-icon>
          </div>
          <div class="stat-value">{{ inMaintenance }}</div>
          <div class="stat-label">En maintenance</div>
        </div>

        <div class="stat-card" (click)="goToTab('alerts')">
          <div class="stat-icon" style="background: rgba(239,68,68,0.15);">
            <ion-icon name="warning" style="color: #ef4444;"></ion-icon>
          </div>
          <div class="stat-value">{{ alertCount }}</div>
          <div class="stat-label">Alertes</div>
        </div>
      </div>

      <!-- État de la flotte : les quatre états, aux couleurs et libellés de la carte -->
      <ion-card class="info-card fleet-card" (click)="goToTab('monitoring')">
        <ion-card-content>
          <app-fleet-state-summary [counts]="stateCounts"></app-fleet-state-summary>
        </ion-card-content>
      </ion-card>

      <!-- Recent vehicles activity -->
      <div class="section-header">
        <h3>Activité récente</h3>
        <ion-button fill="clear" size="small" (click)="goToTab('monitoring')">
          Voir tout
          <ion-icon name="chevron-forward" slot="end"></ion-icon>
        </ion-button>
      </div>

      <ion-list *ngIf="recentPositions.length > 0">
        <ion-item *ngFor="let pos of recentPositions; trackBy: trackByVehicleId" detail (click)="goToVehicle(pos.vehicleId)">
          <div class="vehicle-avatar" slot="start" [ngClass]="stateOf(pos)"
               [style.background]="styleOf(pos).tint" [style.color]="styleOf(pos).color">
            <ion-icon [name]="styleOf(pos).icon" aria-hidden="true"></ion-icon>
          </div>
          <ion-label>
            <h2>{{ pos.vehicleName }}</h2>
            <!-- Pas de vitesse sous « Déconnecté » : celle de la dernière trame contredirait la pastille. -->
            <p><span class="state-label">{{ styleOf(pos).label }}</span> &middot; {{ pos.plate }}<span class="recent-speed" *ngIf="stateOf(pos) !== 'offline'"> &middot; {{ pos.speedKph | number:'1.0-0' }} km/h</span></p>
          </ion-label>
          <ion-note slot="end" color="medium">
            <!-- Contact mis en couleur de l'application : le vert est réservé à « En route ». -->
            <ion-icon [name]="pos.ignitionOn ? 'flash' : 'flash-off'" [color]="pos.ignitionOn ? 'primary' : 'medium'"></ion-icon>
          </ion-note>
        </ion-item>
      </ion-list>

      <div class="empty-state" *ngIf="recentPositions.length === 0 && !loading">
        <ion-icon name="analytics-outline"></ion-icon>
        <p>Aucune activité récente</p>
      </div>

      <ion-spinner *ngIf="loading" name="crescent" class="center-spinner"></ion-spinner>
    </ion-content>
  `,
  styles: [`
    .welcome-card {
      background: linear-gradient(135deg, var(--ion-color-primary), var(--ion-color-primary-shade));
      color: #fff;
      padding: 24px 20px 20px;
      margin: 0;
    }
    .welcome-card h2 { margin: 0 0 4px; font-size: 20px; font-weight: 700; }
    .welcome-card p { margin: 0; opacity: 0.8; font-size: 14px; }
    .connection-badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      margin-top: 12px;
      padding: 4px 12px;
      border-radius: 20px;
      font-size: 12px;
      font-weight: 500;
    }
    /* Connexion de l'APPLICATION, pas un état de véhicule : ni le vert « En route » ni le
       rouge « À l'arrêt » (juste au-dessus de « État de la flotte »), mêmes teintes neutres
       que le badge de la carte. Texte blanc lisible sur les deux (>= 4,5:1). */
    .connection-badge.connected { background: rgba(255,255,255,0.2); }
    .connection-badge.disconnected { background: #4b5563; }
    .stats-grid {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 12px;
      padding: 16px;
    }
    .stat-card {
      background: var(--ion-card-background, #fff);
      border-radius: 16px;
      padding: 16px;
      box-shadow: 0 2px 8px rgba(0,0,0,0.06);
      cursor: pointer;
    }
    .stat-icon {
      width: 40px; height: 40px;
      border-radius: 12px;
      display: flex; align-items: center; justify-content: center;
      margin-bottom: 8px;
    }
    .stat-icon ion-icon { font-size: 20px; }
    .stat-value { font-size: 24px; font-weight: 700; color: var(--ion-text-color); }
    .stat-label { font-size: 12px; color: var(--ion-color-medium); margin-top: 2px; }
    .stat-sub { font-size: 10px; line-height: 1.3; color: var(--ion-color-medium); opacity: 0.8; }
    .stat-unit { font-size: 13px; font-weight: 600; color: var(--ion-color-medium); }
    .info-card { margin: 0 16px; border-radius: 16px; }
    .fleet-card { cursor: pointer; }
    .state-label { font-weight: 600; color: var(--ion-text-color); }
    .section-header {
      display: flex; justify-content: space-between; align-items: center;
      padding: 16px 16px 4px;
    }
    .section-header h3 { margin: 0; font-size: 16px; font-weight: 600; }
    .vehicle-avatar {
      width: 36px; height: 36px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .vehicle-avatar ion-icon { font-size: 18px; }
    .empty-state {
      text-align: center; padding: 40px 20px; color: var(--ion-color-medium);
    }
    .empty-state ion-icon { font-size: 48px; display: block; margin: 0 auto 12px; }
    .center-spinner { display: block; margin: 40px auto; }
  `]
})
export class DashboardPage implements OnInit, OnDestroy {
  userName = '';
  companyName = '';
  isConnected = false;
  loading = true;

  totalVehicles = 0;
  inMaintenance = 0;
  alertCount = 0;
  distanceToday = 0;

  /**
   * Compteurs par état sur TOUTE la flotte équipée d'un boîtier. Avant, « En mouvement »
   * affichait le nombre de véhicules EN LIGNE renvoyé par l'API (un véhicule garé mais
   * connecté y comptait), puis était recalculé sur les 10 lignes de « Activité récente »
   * seulement ; « À l'arrêt » mélangeait ralenti, contact coupé et déconnectés.
   */
  stateCounts: Record<VehicleMotionState, number> = countByState([]);

  recentPositions: PositionUpdate[] = [];
  /** Dernière position connue de chaque véhicule équipé (REST puis SignalR). */
  private fleet = new Map<number, StateInput>();
  private subs: Subscription[] = [];

  constructor(
    private api: ApiService,
    private signalr: SignalRService,
    private authService: AuthService,
    private router: Router,
    private zone: NgZone
  ) {}

  ngOnInit() {
    const user = this.authService.getCurrentUserSync();
    if (user) {
      this.userName = user.name?.split(' ')[0] || 'Utilisateur';
      this.companyName = user.companyName || '';
    }

    this.subs.push(
      this.signalr.connectionState$.subscribe(state => {
        this.isConnected = state === 'Connected';
      })
    );

    this.subs.push(
      this.signalr.positionBatch$.subscribe(batch => {
        for (const pos of batch) {
          this.fleet.set(pos.vehicleId, pos);
          const idx = this.recentPositions.findIndex(p => p.vehicleId === pos.vehicleId);
          if (idx >= 0) {
            this.recentPositions[idx] = pos;
          } else {
            this.recentPositions.unshift(pos);
            if (this.recentPositions.length > 10) this.recentPositions.pop();
          }
        }
        // Recompute counts once per batch, not once per device frame.
        // motionState teste la fraîcheur AVANT la vitesse : sans cela, un boîtier
        // muet dont la dernière trame portait 8 km/h comptait « en mouvement »
        // pour toujours et gonflait ce compteur.
        this.recount();
      })
    );

    this.subs.push(
      this.signalr.notification$.subscribe(() => {
        this.alertCount++;
      })
    );

    // Un boîtier qui se tait n'envoie plus de lot qui déclencherait le recomptage : sans
    // horloge, il restait compté « En route » tant qu'aucun autre véhicule ne transmettait.
    // Horloge hors de la zone Angular (sinon l'application n'est jamais « stable ») ; seul
    // le recomptage y rentre, pour rafraîchir l'affichage.
    this.zone.runOutsideAngular(() => {
      this.subs.push(interval(30000).subscribe(() => this.zone.run(() => this.recount())));
    });

    this.loadData();
    // SignalR is already started by AppComponent on the authenticated user —
    // no need to reconnect here (removes redundant startup work).
  }

  ngOnDestroy() {
    this.subs.forEach(s => s.unsubscribe());
  }

  loadData() {
    this.loading = true;

    this.api.getDashboardStats().subscribe({
      next: (data: any) => {
        this.totalVehicles = data.vehicles?.total || 0;
        this.inMaintenance = data.maintenance?.upcoming || 0;
        this.alertCount = data.alerts?.unresolved || 0;
        this.distanceToday = data.trips?.distanceToday || 0;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });

    this.api.getLastPositions().subscribe({
      next: (positions) => {
        const list = Array.isArray(positions) ? positions : [];
        // Toute la flotte équipée compte, y compris un boîtier qui n'a jamais
        // transmis (sans position = « Déconnecté »).
        this.fleet.clear();
        for (const p of list) {
          if (p?.vehicleId == null) continue;
          const lp = p.lastPosition;
          this.fleet.set(p.vehicleId, lp
            ? { speedKph: lp.speedKph, ignitionOn: lp.ignitionOn, recordedAt: lp.recordedAt }
            : { recordedAt: null });
        }
        this.recount();
        this.recentPositions = list
          .filter((p: any) => p.lastPosition)
          .slice(0, 10)
          .map((p: any) => ({
            deviceId: p.deviceId || 0,
            deviceUid: p.deviceUid || '',
            vehicleId: p.vehicleId,
            vehicleName: p.vehicleName || 'Véhicule',
            plate: p.plate || '',
            latitude: p.lastPosition.latitude,
            longitude: p.lastPosition.longitude,
            speedKph: p.lastPosition.speedKph || 0,
            courseDeg: p.lastPosition.courseDeg || 0,
            ignitionOn: p.lastPosition.ignitionOn ?? false,
            // La fraîcheur d'abord : une dernière position vieille de plusieurs
            // jours ne rend pas le véhicule « en mouvement », quelle que soit la
            // vitesse qu'elle portait au moment où elle a été prise.
            isMoving: motionState({
              speedKph: p.lastPosition.speedKph,
              ignitionOn: p.lastPosition.ignitionOn,
              recordedAt: p.lastPosition.recordedAt
            }) === 'moving',
            recordedAt: p.lastPosition.recordedAt || '',
            timestamp: p.lastPosition.recordedAt || ''
          }));
      }
    });
  }

  async onRefresh(event: any) {
    this.loadData();
    setTimeout(() => event.target.complete(), 1500);
  }

  /** Recompte les quatre états sur la flotte connue (même règle que la carte). */
  recount(nowMs: number = Date.now()) {
    this.stateCounts = countByState(Array.from(this.fleet.values(), p => motionState(p, nowMs)));
  }

  stateOf(pos: PositionUpdate): VehicleMotionState {
    return motionState(pos);
  }

  /** Couleur, libellé et icône de l'état (vehicle-state.util, jamais recodés ici). */
  styleOf(pos: PositionUpdate): VehicleStateStyle {
    return stateStyle(this.stateOf(pos));
  }

  goToProfile() {
    this.router.navigate(['/tabs/profile']);
  }

  goToTab(tab: string) {
    this.router.navigate([`/tabs/${tab}`]);
  }

  goToVehicle(vehicleId: number) {
    this.router.navigate(['/tabs/vehicles'], { queryParams: { id: vehicleId } });
  }

  trackByVehicleId(_: number, item: PositionUpdate) {
    return item.vehicleId;
  }
}
