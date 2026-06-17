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
 * Playback multi-véhicules : rejoue simultanément plusieurs véhicules sur un axe de
 * temps commun. Reprend le design du Tracer Playback (carte plein écran + panneau
 * flottant indigo). Vue dédiée — le playback mono-véhicule du monitoring est intact.
 */
@Component({
  selector: 'app-multi-playback',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="mp-page">
        <div id="mp-map" class="mp-map-full"></div>

        <div class="mp-panel" [class.loaded]="loaded">
          <!-- Header -->
          <div class="mp-header">
            <div class="mp-htitle">
              <span class="mp-havatar">🎬</span>
              <div>
                <div class="mp-htitle-text">Playback multi-véhicules</div>
                <div class="mp-hsub">Rejeu simultané sur une ligne de temps commune</div>
              </div>
            </div>
          </div>

          <!-- Vehicle selection -->
          <div class="mp-section">
            <label class="mp-label">Véhicules — {{ selectedIds.size }} sélectionné(s)</label>
            <div class="mp-search">
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="2"><circle cx="11" cy="11" r="8"/><path d="M21 21l-4.35-4.35"/></svg>
              <input class="mp-search-input" type="text" placeholder="Rechercher un véhicule…" [(ngModel)]="search">
            </div>
            <div class="mp-veh-list">
              @for (v of filteredVehicles(); track v.id) {
                <div class="mp-veh-card" [class.selected]="selectedIds.has(+v.id)" (click)="toggle(+v.id)">
                  <div class="mp-veh-icon">🚚</div>
                  <div class="mp-veh-info">
                    <span class="mp-veh-plate">{{ v.plate || v.name }}</span>
                    <span class="mp-veh-model">{{ v.name }}</span>
                  </div>
                  @if (selectedIds.has(+v.id)) {
                    <svg class="mp-veh-check" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#6366f1" stroke-width="2.5"><polyline points="20 6 9 17 4 12"/></svg>
                  }
                </div>
              }
              @if (filteredVehicles().length === 0) {
                <div class="mp-veh-empty">Aucun véhicule</div>
              }
            </div>
          </div>

          <!-- Dates + launch -->
          <div class="mp-section">
            <div class="mp-date-field">
              <span class="mp-date-label">De</span>
              <input type="datetime-local" lang="fr-FR" class="mp-date" [(ngModel)]="fromStr">
            </div>
            <div class="mp-date-field">
              <span class="mp-date-label">À</span>
              <input type="datetime-local" lang="fr-FR" class="mp-date" [(ngModel)]="toStr">
            </div>
            <button class="mp-launch" (click)="load()" [disabled]="loading || selectedIds.size === 0">
              <span *ngIf="loading" class="mp-spinner"></span>
              <svg *ngIf="!loading" width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><polygon points="5 3 19 12 5 21 5 3"/></svg>
              {{ loading ? 'Chargement…' : (loaded ? 'Relancer' : 'Lancer le playback') }}
            </button>
            @if (loadError) { <div class="mp-error">{{ loadError }}</div> }
          </div>

          <!-- Player -->
          @if (loaded) {
            <div class="mp-section mp-legend">
              @for (t of tracks; track t.id) {
                <div class="mp-leg-row">
                  <span class="mp-leg-dot" [style.background]="t.color"></span>
                  <span class="mp-leg-name">{{ t.name }}</span>
                  <span class="mp-leg-speed">{{ t.curSpeed | number:'1.0-0' }} <small>km/h</small></span>
                </div>
              }
            </div>

            <div class="mp-progress">
              <div class="pb-progress-track">
                <div class="pb-progress-fill" [style.width.%]="progress * 100"></div>
                <input type="range" class="pb-progress-input" min="0" max="1000" [value]="progress * 1000" (input)="onSeek($event)">
              </div>
              <div class="pb-progress-info"><span>{{ currentTimeLabel }}</span></div>
            </div>

            <div class="pb-transport">
              <button class="pb-ctrl" (click)="restart()" title="Revenir au début">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="11 19 2 12 11 5 11 19"/><polyline points="22 19 13 12 22 5 22 19"/></svg>
              </button>
              <button class="pb-play" [class.playing]="playing" (click)="togglePlay()">
                <svg *ngIf="!playing" width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><polygon points="6 4 20 12 6 20 6 4"/></svg>
                <svg *ngIf="playing" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><rect x="6" y="5" width="4" height="14" rx="1"/><rect x="14" y="5" width="4" height="14" rx="1"/></svg>
              </button>
            </div>

            <div class="pb-speeds">
              @for (s of speeds; track s) {
                <button [class.active]="speed === s" (click)="setSpeed(s)">{{ s }}×</button>
              }
            </div>
          }
        </div>

        <!-- Map zoom controls -->
        <div class="mp-map-ctrls">
          <button class="mp-map-ctrl" title="Zoom +" (click)="map?.zoomIn()">+</button>
          <button class="mp-map-ctrl" title="Zoom -" (click)="map?.zoomOut()">−</button>
        </div>
      </div>
    </app-layout>
  `,
  styles: [`
    .mp-page { position: relative; height: calc(100vh - 64px); min-height: 520px; overflow: hidden; }
    .mp-map-full { position: absolute; inset: 0; z-index: 0; }

    .mp-map-ctrls { position: absolute; top: 12px; right: 12px; z-index: 15; display: flex; flex-direction: column; gap: 4px; background: #fff; border-radius: 10px; padding: 6px; box-shadow: 0 2px 12px rgba(0,0,0,0.15); }
    .mp-map-ctrl { width: 34px; height: 34px; border: none; background: transparent; border-radius: 6px; cursor: pointer; font-size: 18px; color: #475569; }
    .mp-map-ctrl:hover { background: #f1f5f9; color: #1e293b; }

    /* Floating panel — same look as Tracer Playback */
    .mp-panel { position: absolute; top: 12px; left: 12px; z-index: 10; width: 340px; background: #fff; display: flex; flex-direction: column; border-radius: 14px; box-shadow: 0 4px 24px rgba(0,0,0,0.12); overflow: hidden; }
    .mp-panel.loaded { max-height: calc(100vh - 90px); overflow-y: auto; scrollbar-width: thin; }
    .mp-panel.loaded::-webkit-scrollbar { width: 4px; }
    .mp-panel.loaded::-webkit-scrollbar-thumb { background: rgba(99,102,241,0.2); border-radius: 4px; }

    .mp-header { background: linear-gradient(135deg, #6366f1 0%, #818cf8 50%, #a78bfa 100%); padding: 14px 16px; color: #fff; }
    .mp-htitle { display: flex; align-items: center; gap: 10px; }
    .mp-havatar { width: 34px; height: 34px; background: rgba(255,255,255,0.2); border-radius: 9px; display: flex; align-items: center; justify-content: center; font-size: 18px; }
    .mp-htitle-text { font-size: 15px; font-weight: 700; }
    .mp-hsub { font-size: 11px; color: rgba(255,255,255,0.85); margin-top: 1px; }

    .mp-section { padding: 12px 16px; border-bottom: 1px solid #f1f5f9; }
    .mp-label { display: block; font-size: 11px; font-weight: 700; color: #64748b; text-transform: uppercase; letter-spacing: 0.5px; margin-bottom: 8px; }

    .mp-search { display: flex; align-items: center; gap: 8px; padding: 7px 10px; border: 1px solid #e2e8f0; border-radius: 8px; background: #f8fafc; margin-bottom: 8px; }
    .mp-search-input { border: none; outline: none; background: transparent; flex: 1; font-size: 13px; color: #1e293b; }
    .mp-veh-list { max-height: 200px; overflow-y: auto; display: flex; flex-direction: column; gap: 4px; }
    .mp-veh-card { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border-radius: 8px; cursor: pointer; transition: all 0.12s; border: 2px solid transparent; }
    .mp-veh-card:hover { background: #f8fafc; }
    .mp-veh-card.selected { background: #eef2ff; border-color: #6366f1; }
    .mp-veh-icon { width: 32px; height: 32px; border-radius: 8px; display: flex; align-items: center; justify-content: center; flex-shrink: 0; background: #f1f5f9; font-size: 15px; }
    .mp-veh-info { flex: 1; min-width: 0; }
    .mp-veh-plate { display: block; font-size: 13px; font-weight: 600; color: #1e293b; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .mp-veh-model { display: block; font-size: 11px; color: #94a3b8; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .mp-veh-check { flex-shrink: 0; }
    .mp-veh-empty { padding: 16px; text-align: center; font-size: 12px; color: #94a3b8; }

    .mp-date-field { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
    .mp-date-label { font-size: 11px; color: #94a3b8; font-weight: 600; width: 22px; }
    .mp-date { flex: 1; min-width: 0; padding: 8px 10px; border: 1px solid #e2e8f0; border-radius: 8px; font-size: 12px; color: #1e293b; background: #f8fafc; outline: none; box-sizing: border-box; }
    .mp-date:focus { border-color: #6366f1; box-shadow: 0 0 0 3px rgba(99,102,241,0.08); }
    .mp-launch { display: flex; align-items: center; justify-content: center; gap: 8px; width: 100%; margin-top: 4px; padding: 11px 20px; background: #6366f1; color: #fff; border: none; border-radius: 10px; font-size: 14px; font-weight: 600; cursor: pointer; transition: all 0.15s; }
    .mp-launch:hover:not(:disabled) { background: #4f46e5; }
    .mp-launch:disabled { opacity: 0.5; cursor: not-allowed; }
    .mp-spinner { width: 16px; height: 16px; border: 2px solid rgba(255,255,255,0.3); border-top-color: #fff; border-radius: 50%; animation: mp-spin 0.6s linear infinite; }
    @keyframes mp-spin { to { transform: rotate(360deg); } }
    .mp-error { margin-top: 8px; font-size: 12px; color: #dc2626; }

    /* Legend */
    .mp-legend { display: flex; flex-direction: column; gap: 6px; }
    .mp-leg-row { display: flex; align-items: center; gap: 8px; font-size: 13px; }
    .mp-leg-dot { width: 12px; height: 12px; border-radius: 50%; flex-shrink: 0; box-shadow: 0 0 0 2px #fff, 0 0 0 3px currentColor; }
    .mp-leg-name { flex: 1; color: #1e293b; font-weight: 500; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .mp-leg-speed { font-variant-numeric: tabular-nums; font-weight: 700; color: #6366f1; }
    .mp-leg-speed small { font-weight: 500; color: #94a3b8; }

    /* Progress (reprend pb-progress du Tracer Playback) */
    .mp-progress { padding: 12px 16px 4px; }
    .pb-progress-track { position: relative; height: 6px; background: #e2e8f0; border-radius: 3px; }
    .pb-progress-fill { position: absolute; top: 0; left: 0; height: 100%; background: linear-gradient(90deg, #6366f1, #818cf8); border-radius: 3px; transition: width 0.1s linear; pointer-events: none; }
    .pb-progress-input { position: absolute; top: -6px; left: 0; width: 100%; height: 18px; -webkit-appearance: none; appearance: none; background: transparent; outline: none; cursor: pointer; margin: 0; }
    .pb-progress-input::-webkit-slider-thumb { -webkit-appearance: none; width: 14px; height: 14px; border-radius: 50%; background: #fff; border: 3px solid #6366f1; cursor: pointer; box-shadow: 0 1px 6px rgba(99,102,241,0.3); }
    .pb-progress-input::-moz-range-thumb { width: 14px; height: 14px; border-radius: 50%; background: #fff; border: 3px solid #6366f1; cursor: pointer; }
    .pb-progress-info { display: flex; justify-content: center; font-size: 11px; color: #64748b; margin-top: 6px; font-weight: 600; font-variant-numeric: tabular-nums; }

    .pb-transport { display: flex; align-items: center; justify-content: center; gap: 8px; padding: 10px 14px 6px; }
    .pb-ctrl { width: 34px; height: 34px; border: none; background: #f1f5f9; border-radius: 10px; cursor: pointer; display: flex; align-items: center; justify-content: center; color: #64748b; transition: all 0.15s; }
    .pb-ctrl:hover { background: #e2e8f0; color: #1e293b; transform: scale(1.05); }
    .pb-play { width: 48px; height: 48px; border: none; background: linear-gradient(135deg, #6366f1, #818cf8); border-radius: 50%; cursor: pointer; display: flex; align-items: center; justify-content: center; color: #fff; box-shadow: 0 4px 16px rgba(99,102,241,0.35); transition: all 0.2s; margin: 0 4px; }
    .pb-play:hover { transform: scale(1.08); box-shadow: 0 6px 20px rgba(99,102,241,0.45); }
    .pb-play.playing { background: linear-gradient(135deg, #f97316, #fb923c); box-shadow: 0 4px 16px rgba(249,115,22,0.35); }

    .pb-speeds { display: flex; justify-content: center; gap: 4px; padding: 4px 14px 16px; }
    .pb-speeds button { padding: 4px 12px; border: 1.5px solid #e2e8f0; background: #fff; border-radius: 20px; font-size: 11px; font-weight: 600; color: #64748b; cursor: pointer; transition: all 0.15s; }
    .pb-speeds button.active { background: linear-gradient(135deg, #6366f1, #818cf8); color: #fff; border-color: transparent; box-shadow: 0 2px 8px rgba(99,102,241,0.25); }
    .pb-speeds button:hover:not(.active) { background: #f8fafc; border-color: #c7d2fe; color: #6366f1; }
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
  map: L.Map | null = null;

  playing = false;
  progress = 0;
  speed = 1;
  speeds = [0.5, 1, 2, 4, 8, 16];
  currentTimeLabel = '';

  private minT = 0;
  private maxT = 0;
  private timer: any = null;
  private readonly TICK_MS = 50;
  private readonly BASE_MS = 60000;

  private readonly palette = [
    '#6366f1', '#16a34a', '#dc2626', '#d97706', '#0891b2',
    '#db2777', '#65a30d', '#7c3aed', '#ea580c', '#0ea5e9'
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
        this.loadError = missing > 0 ? `${missing} véhicule(s) sans position sur la période (ignoré).` : '';

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

    this.map = L.map(el, { zoomControl: false });
    L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '© OpenStreetMap', maxZoom: 19
    }).addTo(this.map);

    const allLatLngs: L.LatLngExpression[] = [];
    for (const t of this.tracks) {
      t.idx = 0;
      t.trail = L.polyline([], { color: t.color, weight: 4, opacity: 0.85 }).addTo(this.map);
      const start = t.points[0];
      t.marker = L.marker([start.lat, start.lng], { icon: this.makeIcon(t) }).addTo(this.map);
      t.points.forEach(p => allLatLngs.push([p.lat, p.lng]));
    }

    if (allLatLngs.length) {
      try { this.map.fitBounds(L.latLngBounds(allLatLngs), { padding: [60, 60] }); }
      catch { this.map.setView(allLatLngs[0], 13); }
    }

    this.render(this.minT, false);
    this.play();
  }

  private makeIcon(t: VehicleTrack): L.DivIcon {
    const label = (t.plate || t.name || '').toString().slice(0, 10);
    return L.divIcon({
      className: '',
      html: `<div style="display:flex;align-items:center;gap:4px;white-space:nowrap;transform:translate(-7px,-7px);">
               <span style="width:16px;height:16px;border-radius:50%;background:${t.color};border:3px solid #fff;box-shadow:0 1px 4px rgba(0,0,0,0.4);"></span>
               <span style="background:${t.color};color:#fff;font-size:10px;font-weight:700;padding:2px 6px;border-radius:8px;box-shadow:0 1px 3px rgba(0,0,0,0.3);">${label}</span>
             </div>`,
      iconSize: [0, 0], iconAnchor: [0, 0]
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

  stop() { this.pause(); this.progress = 0; }

  restart() {
    this.progress = 0;
    this.render(this.minT, false);
  }

  setSpeed(s: number) { this.speed = s; }

  onSeek(ev: Event) {
    const val = +(ev.target as HTMLInputElement).value;
    this.progress = Math.max(0, Math.min(1, val / 1000));
    this.render(this.minT + this.progress * (this.maxT - this.minT), false);
  }

  private tick() {
    if (!this.loaded) return;
    this.progress += (this.TICK_MS / this.BASE_MS) * this.speed;
    if (this.progress >= 1) { this.progress = 1; this.pause(); }
    this.render(this.minT + this.progress * (this.maxT - this.minT), true);
  }

  private render(time: number, incremental: boolean) {
    for (const t of this.tracks) {
      const pts = t.points;
      if (incremental) {
        while (t.idx < pts.length - 1 && pts[t.idx + 1].t <= time) {
          t.idx++;
          t.trail?.addLatLng([pts[t.idx].lat, pts[t.idx].lng]);
        }
      } else {
        let i = 0;
        while (i < pts.length - 1 && pts[i + 1].t <= time) i++;
        t.idx = i;
        t.trail?.setLatLngs(pts.slice(0, i + 1).map(p => [p.lat, p.lng] as L.LatLngExpression));
      }
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
