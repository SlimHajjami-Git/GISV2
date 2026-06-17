import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import * as L from 'leaflet';
import { ApiService } from '../services/api.service';
import { AppLayoutComponent } from './shared/app-layout.component';

interface TrackPoint { t: number; lat: number; lng: number; speed: number; }

interface VehicleTrack {
  id: number;
  name: string;
  plate: string;
  color: string;
  points: TrackPoint[];
  idx: number;
  curSpeed: number;
  marker: L.Marker | null;
  trail: L.Polyline | null;
}

/**
 * Playback multi-véhicules : rejoue simultanément le trajet de plusieurs véhicules
 * sur un axe de temps commun. Chaque véhicule a sa couleur (marqueur + tracé). Vue
 * dédiée (n'affecte pas le playback mono-véhicule existant du monitoring).
 */
@Component({
  selector: 'app-multi-playback',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="mp-page">
        <div class="mp-header">
          <h1>🎬 Playback multi-véhicules</h1>
          <p class="subtitle">Rejouez plusieurs véhicules en même temps sur une ligne de temps commune</p>
        </div>

        <!-- Setup -->
        <div class="mp-setup">
          <div class="mp-veh">
            <label class="mp-label">Véhicules ({{ selectedIds.size }} sélectionné(s))</label>
            <input class="mp-search" type="text" placeholder="Rechercher…" [(ngModel)]="search">
            <div class="mp-veh-list">
              @for (v of filteredVehicles(); track v.id) {
                <label class="mp-veh-item">
                  <input type="checkbox" [checked]="selectedIds.has(+v.id)" (change)="toggle(+v.id)">
                  <span>{{ v.name }} <span class="mp-plate" *ngIf="v.plate">{{ v.plate }}</span></span>
                </label>
              }
            </div>
          </div>

          <div class="mp-params">
            <div class="mp-field">
              <label class="mp-label">De</label>
              <input type="datetime-local" lang="fr-FR" [(ngModel)]="fromStr" class="mp-date">
            </div>
            <div class="mp-field">
              <label class="mp-label">À</label>
              <input type="datetime-local" lang="fr-FR" [(ngModel)]="toStr" class="mp-date">
            </div>
            <button class="mp-btn-load" (click)="load()" [disabled]="loading || selectedIds.size === 0">
              {{ loading ? 'Chargement…' : '▶ Lancer le playback' }}
            </button>
            @if (loadError) { <span class="mp-error">{{ loadError }}</span> }
          </div>
        </div>

        <!-- Player -->
        @if (loaded) {
          <div class="mp-controls">
            <button class="mp-play" (click)="togglePlay()">{{ playing ? '⏸' : '▶' }}</button>
            <input type="range" class="mp-scrub" min="0" max="1000" [value]="progress * 1000" (input)="onSeek($event)">
            <span class="mp-time">{{ currentTimeLabel }}</span>
            <div class="mp-speeds">
              @for (s of speeds; track s) {
                <button class="mp-speed" [class.active]="speed === s" (click)="setSpeed(s)">{{ s }}×</button>
              }
            </div>
          </div>

          <div class="mp-legend">
            @for (t of tracks; track t.id) {
              <span class="mp-chip" [style.borderColor]="t.color">
                <i class="mp-dot" [style.background]="t.color"></i>
                {{ t.name }} <b>{{ t.curSpeed | number:'1.0-0' }}</b> km/h
              </span>
            }
          </div>

          <div id="mp-map" class="mp-map"></div>
        }
      </div>
    </app-layout>
  `,
  styles: [`
    .mp-page { padding: 20px; max-width: 1400px; margin: 0 auto; }
    .mp-header h1 { font-size: 24px; font-weight: 700; color: #1e293b; margin: 0 0 4px; }
    .subtitle { color: #64748b; margin: 0 0 16px; }
    .mp-setup { display: flex; gap: 20px; flex-wrap: wrap; background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 16px; margin-bottom: 16px; }
    .mp-veh { flex: 1 1 320px; min-width: 280px; }
    .mp-params { flex: 1 1 280px; display: flex; flex-direction: column; gap: 12px; justify-content: flex-start; }
    .mp-label { display: block; font-size: 12px; font-weight: 600; color: #64748b; text-transform: uppercase; margin-bottom: 6px; }
    .mp-search { width: 100%; padding: 8px 12px; border: 1px solid #e2e8f0; border-radius: 8px; margin-bottom: 8px; box-sizing: border-box; }
    .mp-veh-list { max-height: 220px; overflow-y: auto; border: 1px solid #e2e8f0; border-radius: 8px; padding: 4px; }
    .mp-veh-item { display: flex; align-items: center; gap: 8px; padding: 6px 8px; border-radius: 6px; cursor: pointer; font-size: 14px; }
    .mp-veh-item:hover { background: #f1f5f9; }
    .mp-plate { display: inline-block; background: #e2e8f0; color: #475569; padding: 1px 6px; border-radius: 4px; font-size: 11px; font-weight: 600; }
    .mp-field { display: flex; flex-direction: column; }
    .mp-date { padding: 9px 12px; border: 1px solid #e2e8f0; border-radius: 8px; font-size: 14px; }
    .mp-btn-load { margin-top: 4px; padding: 11px 18px; background: #2563eb; color: #fff; border: none; border-radius: 8px; font-weight: 600; cursor: pointer; }
    .mp-btn-load:disabled { background: #94a3b8; cursor: not-allowed; }
    .mp-error { color: #dc2626; font-size: 13px; }
    .mp-controls { display: flex; align-items: center; gap: 14px; background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 12px 16px; margin-bottom: 10px; flex-wrap: wrap; }
    .mp-play { width: 42px; height: 42px; border-radius: 50%; border: none; background: #2563eb; color: #fff; font-size: 18px; cursor: pointer; }
    .mp-scrub { flex: 1; min-width: 200px; accent-color: #2563eb; }
    .mp-time { font-variant-numeric: tabular-nums; font-size: 13px; color: #1e293b; font-weight: 600; min-width: 160px; }
    .mp-speeds { display: flex; gap: 4px; }
    .mp-speed { padding: 6px 10px; border: 1px solid #e2e8f0; background: #f8fafc; border-radius: 6px; cursor: pointer; font-size: 13px; }
    .mp-speed.active { background: #2563eb; color: #fff; border-color: #2563eb; }
    .mp-legend { display: flex; flex-wrap: wrap; gap: 8px; margin-bottom: 10px; }
    .mp-chip { display: inline-flex; align-items: center; gap: 6px; padding: 4px 10px; background: #fff; border: 2px solid; border-radius: 16px; font-size: 12px; color: #1e293b; }
    .mp-dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
    .mp-map { width: 100%; height: 62vh; border-radius: 12px; overflow: hidden; border: 1px solid #e2e8f0; }
  `]
})
export class MultiPlaybackComponent implements OnInit, OnDestroy {
  vehicles: any[] = [];
  selectedIds = new Set<number>();
  search = '';
  fromStr = '';
  toStr = '';

  loading = false;
  loaded = false;
  loadError = '';

  tracks: VehicleTrack[] = [];
  private map: L.Map | null = null;

  playing = false;
  progress = 0;             // 0..1 sur l'axe de temps commun
  speed = 1;
  speeds = [0.5, 1, 2, 4, 8, 16];
  currentTimeLabel = '';

  private minT = 0;
  private maxT = 0;
  private timer: any = null;
  private readonly TICK_MS = 50;     // 20 fps
  private readonly BASE_MS = 60000;  // plage entière jouée en 60 s à 1×

  private readonly palette = [
    '#2563eb', '#16a34a', '#dc2626', '#d97706', '#7c3aed',
    '#0891b2', '#db2777', '#65a30d', '#ea580c', '#4f46e5'
  ];

  constructor(private router: Router, private api: ApiService) {}

  ngOnInit() {
    if (!this.api.isAuthenticated()) { this.router.navigate(['/login']); return; }
    const now = new Date();
    const yesterday = new Date(now.getTime() - 24 * 3600 * 1000);
    this.toStr = this.toLocalDateTime(now);
    this.fromStr = this.toLocalDateTime(yesterday);
    this.api.getVehicles().subscribe({
      next: (v) => this.vehicles = v || [],
      error: (e) => console.error('Erreur chargement véhicules', e)
    });
  }

  filteredVehicles() {
    const q = this.search.trim().toLowerCase();
    if (!q) return this.vehicles;
    return this.vehicles.filter(v =>
      (v.name || '').toLowerCase().includes(q) || (v.plate || '').toLowerCase().includes(q));
  }

  toggle(id: number) {
    if (this.selectedIds.has(id)) this.selectedIds.delete(id);
    else this.selectedIds.add(id);
  }

  load() {
    if (this.selectedIds.size === 0) return;
    this.loadError = '';
    this.loading = true;
    this.stop();

    const from = new Date(this.fromStr);
    const to = new Date(this.toStr);
    const ids = Array.from(this.selectedIds);

    const calls = ids.map(id =>
      this.api.getVehicleHistory(id, from, to, 2000, false).pipe(catchError(() => of([] as any[])))
    );

    forkJoin(calls).subscribe({
      next: (results) => {
        this.tracks = [];
        results.forEach((positions: any[], i: number) => {
          const id = ids[i];
          const veh = this.vehicles.find(v => +v.id === id);
          const points: TrackPoint[] = (positions || [])
            .map((p: any) => ({
              t: new Date(p.recordedAt).getTime(),
              lat: p.latitude, lng: p.longitude, speed: p.speedKph || 0
            }))
            .filter(p => !isNaN(p.t) && p.lat != null && p.lng != null)
            .sort((a, b) => a.t - b.t);
          if (points.length === 0) return;
          this.tracks.push({
            id, name: veh?.name || ('#' + id), plate: veh?.plate || '',
            color: this.palette[this.tracks.length % this.palette.length],
            points, idx: 0, curSpeed: 0, marker: null, trail: null
          });
        });

        this.loading = false;

        if (this.tracks.length === 0) {
          this.loaded = false;
          this.loadError = 'Aucune position trouvée pour les véhicules / la période choisis.';
          return;
        }

        this.minT = Math.min(...this.tracks.map(t => t.points[0].t));
        this.maxT = Math.max(...this.tracks.map(t => t.points[t.points.length - 1].t));
        this.progress = 0;
        this.loaded = true;

        const missing = this.selectedIds.size - this.tracks.length;
        if (missing > 0) this.loadError = `${missing} véhicule(s) sans position sur la période (ignoré).`;

        setTimeout(() => this.initMap(), 0);
      },
      error: (e) => {
        this.loading = false;
        this.loadError = "Erreur de chargement de l'historique.";
        console.error(e);
      }
    });
  }

  private initMap() {
    if (this.map) { try { this.map.remove(); } catch {} this.map = null; }
    const el = document.getElementById('mp-map');
    if (!el) return;

    this.map = L.map(el, { zoomControl: true });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap', maxZoom: 19
    }).addTo(this.map);

    const allLatLngs: L.LatLngExpression[] = [];
    for (const t of this.tracks) {
      t.idx = 0;
      t.trail = L.polyline([], { color: t.color, weight: 3, opacity: 0.85 }).addTo(this.map);
      const start = t.points[0];
      t.marker = L.marker([start.lat, start.lng], { icon: this.makeIcon(t) }).addTo(this.map);
      t.points.forEach(p => allLatLngs.push([p.lat, p.lng]));
    }

    if (allLatLngs.length) {
      try { this.map.fitBounds(L.latLngBounds(allLatLngs), { padding: [40, 40] }); }
      catch { this.map.setView(allLatLngs[0], 13); }
    }

    this.render(this.minT, false);
    this.play();   // démarre automatiquement
  }

  private makeIcon(t: VehicleTrack): L.DivIcon {
    const label = (t.plate || t.name || '').toString().slice(0, 10);
    return L.divIcon({
      className: '',
      html: `<div style="display:flex;align-items:center;gap:4px;white-space:nowrap;">
               <span style="width:14px;height:14px;border-radius:50%;background:${t.color};border:2px solid #fff;box-shadow:0 0 0 1px ${t.color};"></span>
               <span style="background:${t.color};color:#fff;font-size:10px;font-weight:700;padding:1px 5px;border-radius:8px;">${label}</span>
             </div>`,
      iconSize: [0, 0], iconAnchor: [7, 7]
    });
  }

  togglePlay() { this.playing ? this.pause() : this.play(); }

  play() {
    if (!this.loaded) return;
    if (this.progress >= 1) this.progress = 0;
    this.playing = true;
    if (this.timer) clearInterval(this.timer);
    this.timer = setInterval(() => this.tick(), this.TICK_MS);
  }

  pause() {
    this.playing = false;
    if (this.timer) { clearInterval(this.timer); this.timer = null; }
  }

  stop() {
    this.pause();
    this.progress = 0;
  }

  setSpeed(s: number) { this.speed = s; }

  onSeek(ev: Event) {
    const val = +(ev.target as HTMLInputElement).value;
    this.progress = Math.max(0, Math.min(1, val / 1000));
    const time = this.minT + this.progress * (this.maxT - this.minT);
    this.render(time, false);   // saut → on reconstruit les tracés
  }

  private tick() {
    if (!this.loaded) return;
    this.progress += (this.TICK_MS / this.BASE_MS) * this.speed;
    if (this.progress >= 1) { this.progress = 1; this.pause(); }
    const time = this.minT + this.progress * (this.maxT - this.minT);
    this.render(time, true);
  }

  /** Positionne tous les véhicules au temps donné. incremental=true => trace au fil de l'eau. */
  private render(time: number, incremental: boolean) {
    for (const t of this.tracks) {
      const pts = t.points;

      if (incremental) {
        while (t.idx < pts.length - 1 && pts[t.idx + 1].t <= time) {
          t.idx++;
          t.trail?.addLatLng([pts[t.idx].lat, pts[t.idx].lng]);
        }
      } else {
        // saut : recalcule l'index et reconstruit le tracé jusqu'à `time`
        let i = 0;
        while (i < pts.length - 1 && pts[i + 1].t <= time) i++;
        t.idx = i;
        t.trail?.setLatLngs(pts.slice(0, i + 1).map(p => [p.lat, p.lng] as L.LatLngExpression));
      }

      // position interpolée entre l'échantillon courant et le suivant
      const cur = pts[t.idx];
      const next = pts[t.idx + 1];
      let lat = cur.lat, lng = cur.lng, spd = cur.speed;
      if (next && time >= cur.t && next.t > cur.t) {
        const f = Math.max(0, Math.min(1, (time - cur.t) / (next.t - cur.t)));
        lat = cur.lat + (next.lat - cur.lat) * f;
        lng = cur.lng + (next.lng - cur.lng) * f;
        spd = cur.speed + (next.speed - cur.speed) * f;
      }
      t.curSpeed = spd;
      t.marker?.setLatLng([lat, lng]);
    }
    this.currentTimeLabel = new Date(time).toLocaleString('fr-FR', {
      day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
  }

  private toLocalDateTime(d: Date): string {
    const pad = (n: number) => n.toString().padStart(2, '0');
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
  }

  ngOnDestroy() {
    this.pause();
    if (this.map) { try { this.map.remove(); } catch {} this.map = null; }
  }
}
