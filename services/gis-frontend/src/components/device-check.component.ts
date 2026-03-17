import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpBackend } from '@angular/common/http';
import { environment } from '../environments/environment';

@Component({
  selector: 'app-device-check',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="page-bg">
      <div class="container">
        <!-- Header -->
        <div class="header">
          <div class="logo-row">
            <svg width="36" height="36" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <circle cx="12" cy="12" r="10"/><polygon points="16.24 7.76 14.12 14.12 7.76 16.24 9.88 9.88 16.24 7.76"/>
            </svg>
            <h1>Diagnostic Boîtier GPS</h1>
          </div>
          <p class="subtitle">Vérifiez l'état de connexion d'un boîtier en saisissant son IMEI ou la matricule du véhicule</p>
        </div>

        <!-- Search -->
        <div class="search-box">
          <div class="input-row">
            <div class="input-group">
              <svg class="input-icon" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="2"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
              <input
                type="text"
                [(ngModel)]="query"
                (keyup.enter)="search()"
                placeholder="IMEI ou Matricule..."
                class="search-input"
                [disabled]="loading"
                autocomplete="off"
                autocapitalize="characters"
                spellcheck="false"
              />
            </div>
            <button (click)="search()" class="search-btn" [disabled]="loading || !query.trim()">
              <svg *ngIf="!loading" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="btn-icon"><circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/></svg>
              <span *ngIf="!loading">Rechercher</span>
              <span *ngIf="loading" class="spinner"></span>
            </button>
          </div>
        </div>

        <!-- DEBUG -->
        <div style="color:#0f0;background:#111;padding:12px;border-radius:8px;margin-bottom:12px;font-family:monospace;font-size:12px;word-break:break-all;">
          <div>loading: {{ loading }}</div>
          <div>error: {{ error }}</div>
          <div>result: {{ result | json }}</div>
        </div>

        <!-- Result -->
        <div *ngIf="result" class="result-card" [class.stale]="result.isStale" [class.offline]="!result.connected && result.hasGps" [class.no-gps]="!result.hasGps || !result.found">

          <!-- Not found -->
          <div *ngIf="!result.found" class="status-block not-found">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="15" y1="9" x2="9" y2="15"/><line x1="9" y1="9" x2="15" y2="15"/></svg>
            <h2>Introuvable</h2>
            <p>{{ result.message }}</p>
          </div>

          <!-- Found but no GPS -->
          <div *ngIf="result.found && !result.hasGps" class="status-block no-device">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#f59e0b" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
            <h2>Pas de boîtier GPS</h2>
            <p>{{ result.message }}</p>
            <div class="info-row" *ngIf="result.plate"><span class="label">Matricule</span><span class="value">{{ result.plate }}</span></div>
          </div>

          <!-- Found with GPS but no frames -->
          <div *ngIf="result.found && result.hasGps && !result.connected && result.lastPosition == null" class="status-block no-frames">
            <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="2"><path d="M1 1l22 22"/><path d="M16.72 11.06A10.94 10.94 0 0 1 19 12.55"/><path d="M5 12.55a10.94 10.94 0 0 1 5.17-2.39"/><path d="M10.71 5.05A16 16 0 0 1 22.56 9"/><path d="M1.42 9a15.91 15.91 0 0 1 4.7-2.88"/><path d="M8.53 16.11a6 6 0 0 1 6.95 0"/><line x1="12" y1="20" x2="12.01" y2="20"/></svg>
            <h2>Aucune trame trouvée</h2>
            <p>{{ result.message }}</p>
            <div class="device-info">
              <div class="info-row"><span class="label">IMEI</span><span class="value mono">{{ result.imei }}</span></div>
              <div class="info-row" *ngIf="result.plate"><span class="label">Matricule</span><span class="value">{{ result.plate }}</span></div>
              <div class="info-row" *ngIf="result.model"><span class="label">Modèle</span><span class="value">{{ result.model }}</span></div>
              <div class="info-row" *ngIf="result.firmwareVersion"><span class="label">Firmware</span><span class="value mono">{{ result.firmwareVersion }}</span></div>
            </div>
          </div>

          <!-- Found with data -->
          <div *ngIf="result.found && result.hasGps && result.lastPosition" class="status-block has-data">
            <!-- Connection badge -->
            <div class="connection-badge" [class.connected]="result.connected" [class.stale]="result.isStale">
              <div class="badge-dot"></div>
              <span>{{ result.connected ? 'Connecté' : 'Hors ligne' }}</span>
              <span class="badge-time">{{ result.minutesSinceLastFrame | number:'1.0-0' }} min</span>
            </div>

            <!-- Device identity -->
            <div class="identity-grid">
              <div class="id-card">
                <span class="id-label">IMEI</span>
                <span class="id-value mono">{{ result.imei }}</span>
              </div>
              <div class="id-card" *ngIf="result.mat">
                <span class="id-label">MAT</span>
                <span class="id-value">{{ result.mat }}</span>
              </div>
              <div class="id-card" *ngIf="result.plate">
                <span class="id-label">Matricule</span>
                <span class="id-value">{{ result.plate }}</span>
              </div>
              <div class="id-card" *ngIf="result.vehicleName">
                <span class="id-label">Véhicule</span>
                <span class="id-value">{{ result.vehicleName }}</span>
              </div>
              <div class="id-card" *ngIf="result.firmwareVersion">
                <span class="id-label">Firmware</span>
                <span class="id-value mono">{{ result.firmwareVersion }}</span>
              </div>
              <div class="id-card" *ngIf="result.model">
                <span class="id-label">Modèle</span>
                <span class="id-value">{{ result.model }}</span>
              </div>
              <div class="id-card" *ngIf="result.fuelSensorMode">
                <span class="id-label">Capteur carburant</span>
                <span class="id-value mono">{{ result.fuelSensorMode }}</span>
              </div>
            </div>

            <!-- Telemetry cards -->
            <div class="telemetry-grid">
              <!-- Ignition -->
              <div class="tele-card" [class.on]="result.lastPosition.ignitionOn" [class.off]="!result.lastPosition.ignitionOn">
                <div class="tele-icon">
                  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" [attr.stroke]="result.lastPosition.ignitionOn ? '#22c55e' : '#ef4444'" stroke-width="2">
                    <path d="M12 2v10"/><path d="M18.4 6.6a9 9 0 1 1-12.8 0"/>
                  </svg>
                </div>
                <div class="tele-label">Contact</div>
                <div class="tele-value" [style.color]="result.lastPosition.ignitionOn ? '#22c55e' : '#ef4444'">
                  {{ result.lastPosition.ignitionOn ? 'Allumé' : 'Éteint' }}
                </div>
              </div>

              <!-- Speed -->
              <div class="tele-card">
                <div class="tele-icon">
                  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#3b82f6" stroke-width="2">
                    <path d="M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0-18 0"/><path d="M12 12l3.5-3.5"/><path d="M16.5 7.5l0 .01"/>
                  </svg>
                </div>
                <div class="tele-label">Vitesse</div>
                <div class="tele-value">{{ result.lastPosition.speedKph != null ? (result.lastPosition.speedKph | number:'1.0-0') + ' km/h' : 'N/A' }}</div>
              </div>

              <!-- Fuel -->
              <div class="tele-card" [class.fuel-low]="result.lastPosition.fuelPercent != null && result.lastPosition.fuelPercent < 20">
                <div class="tele-icon">
                  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" [attr.stroke]="result.lastPosition.fuelPercent != null && result.lastPosition.fuelPercent < 20 ? '#ef4444' : '#f59e0b'" stroke-width="2">
                    <path d="M3 22V6a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v16"/><path d="M15 11h3.5a2 2 0 0 1 2 2v3a1.5 1.5 0 0 0 3 0v-7l-3-3"/>
                  </svg>
                </div>
                <div class="tele-label">Carburant</div>
                <div class="tele-value">
                  {{ result.lastPosition.fuelPercent != null ? result.lastPosition.fuelPercent + '%' : 'N/A' }}
                  <span class="tele-sub" *ngIf="result.lastPosition.fuelRaw != null">(raw: {{ result.lastPosition.fuelRaw }})</span>
                </div>
              </div>

              <!-- Odometer -->
              <div class="tele-card">
                <div class="tele-icon">
                  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#8b5cf6" stroke-width="2">
                    <path d="M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0-18 0"/><path d="M12 7v5"/><path d="M8 21h8"/>
                  </svg>
                </div>
                <div class="tele-label">Odomètre</div>
                <div class="tele-value">{{ result.lastPosition.odometerKm != null ? (result.lastPosition.odometerKm | number:'1.0-0') + ' km' : 'N/A' }}</div>
              </div>

              <!-- GPS Coordinates -->
              <div class="tele-card wide">
                <div class="tele-icon">
                  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="#06b6d4" stroke-width="2">
                    <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/>
                  </svg>
                </div>
                <div class="tele-label">Coordonnées GPS</div>
                <div class="tele-value mono">{{ result.lastPosition.latitude | number:'1.5-5' }}, {{ result.lastPosition.longitude | number:'1.5-5' }}</div>
                <div class="tele-sub" *ngIf="result.lastPosition.address">{{ result.lastPosition.address }}</div>
                <div class="tele-sub">
                  Satellites: {{ result.lastPosition.satellites ?? 'N/A' }} &middot;
                  GPS {{ result.lastPosition.isValid ? 'Valide' : 'Invalide' }}
                </div>
              </div>

              <!-- Last Frame -->
              <div class="tele-card wide" [class.stale-frame]="result.isStale">
                <div class="tele-icon">
                  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" [attr.stroke]="result.isStale ? '#94a3b8' : '#22c55e'" stroke-width="2">
                    <circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>
                  </svg>
                </div>
                <div class="tele-label">Dernière trame reçue</div>
                <div class="tele-value" [style.color]="result.isStale ? '#94a3b8' : '#1e293b'">{{ formatDate(result.lastPosition.recordedAt) }}</div>
                <div class="tele-sub" [style.color]="result.isStale ? '#ef4444' : '#22c55e'">
                  il y a {{ result.minutesSinceLastFrame | number:'1.0-0' }} minutes
                  {{ result.isStale ? '⚠ Signal perdu (> 40 min)' : '✓ Signal récent' }}
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Error -->
        <div *ngIf="error" class="error-card">
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#ef4444" stroke-width="2"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>
          <span>{{ error }}</span>
        </div>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    * { box-sizing: border-box; }
    .page-bg {
      min-height: 100vh; min-height: 100dvh;
      background: linear-gradient(135deg, #0f172a 0%, #1e293b 50%, #0f172a 100%);
      padding: 40px 20px;
      padding: max(20px, env(safe-area-inset-top)) max(16px, env(safe-area-inset-right)) max(20px, env(safe-area-inset-bottom)) max(16px, env(safe-area-inset-left));
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      -webkit-text-size-adjust: 100%;
    }
    .container { max-width: 720px; margin: 0 auto; width: 100%; }
    .header { text-align: center; margin-bottom: 28px; }
    .logo-row { display: flex; align-items: center; justify-content: center; gap: 10px; margin-bottom: 8px; }
    .logo-row h1 { font-size: 22px; font-weight: 700; color: #f1f5f9; margin: 0; }
    .subtitle { color: #94a3b8; font-size: 13px; margin: 0; line-height: 1.4; }

    .search-box { margin-bottom: 24px; }
    .input-row { display: flex; gap: 10px; }
    .input-group {
      flex: 1; display: flex; align-items: center; background: #1e293b; border: 1px solid #334155;
      border-radius: 12px; overflow: hidden; transition: border-color 0.2s; min-width: 0;
    }
    .input-group:focus-within { border-color: #3b82f6; box-shadow: 0 0 0 3px rgba(59,130,246,0.15); }
    .input-icon { margin-left: 12px; flex-shrink: 0; }
    .search-input {
      flex: 1; min-width: 0; background: transparent; border: none; outline: none;
      color: #f1f5f9; font-size: 16px; padding: 14px 10px;
      font-family: inherit; -webkit-appearance: none;
    }
    .search-input::placeholder { color: #64748b; }
    .search-btn {
      background: #3b82f6; color: white; border: none; padding: 14px 20px;
      font-size: 14px; font-weight: 600; cursor: pointer; transition: background 0.2s;
      border-radius: 12px; display: flex; align-items: center; justify-content: center; gap: 6px;
      white-space: nowrap; flex-shrink: 0; min-height: 48px;
    }
    .search-btn:hover:not(:disabled) { background: #2563eb; }
    .search-btn:disabled { opacity: 0.5; cursor: not-allowed; }
    .search-btn:active:not(:disabled) { transform: scale(0.97); }
    .btn-icon { flex-shrink: 0; }
    .spinner {
      width: 18px; height: 18px; border: 2px solid rgba(255,255,255,0.3);
      border-top-color: white; border-radius: 50%; animation: spin 0.6s linear infinite; display: inline-block;
    }
    @keyframes spin { to { transform: rotate(360deg); } }

    .result-card {
      background: #1e293b; border: 1px solid #334155; border-radius: 16px;
      padding: 24px; animation: fadeIn 0.3s ease; overflow: hidden;
    }
    @keyframes fadeIn { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: translateY(0); } }

    .status-block { text-align: center; }
    .status-block.has-data { text-align: left; }
    .status-block.not-found svg, .status-block.no-device svg, .status-block.no-frames svg { margin-bottom: 12px; }
    .status-block h2 { color: #f1f5f9; font-size: 20px; margin: 0 0 6px; }
    .status-block p { color: #94a3b8; font-size: 14px; margin: 0 0 16px; }

    .device-info { display: flex; flex-direction: column; gap: 8px; margin-top: 16px; }
    .info-row { display: flex; justify-content: space-between; padding: 10px 12px; background: #0f172a; border-radius: 8px; }
    .info-row .label { color: #94a3b8; font-size: 13px; }
    .info-row .value { color: #f1f5f9; font-size: 13px; font-weight: 600; word-break: break-all; }

    .connection-badge {
      display: inline-flex; align-items: center; gap: 8px;
      padding: 10px 16px; border-radius: 20px; margin-bottom: 20px;
      font-size: 14px; font-weight: 600;
    }
    .connection-badge.connected { background: rgba(34,197,94,0.12); color: #22c55e; }
    .connection-badge.stale { background: rgba(148,163,184,0.12); color: #94a3b8; }
    .badge-dot { width: 10px; height: 10px; border-radius: 50%; flex-shrink: 0; }
    .connection-badge.connected .badge-dot { background: #22c55e; box-shadow: 0 0 8px rgba(34,197,94,0.5); animation: pulse-green 2s infinite; }
    .connection-badge.stale .badge-dot { background: #94a3b8; }
    @keyframes pulse-green { 0%, 100% { opacity: 1; } 50% { opacity: 0.5; } }
    .badge-time { font-size: 12px; opacity: 0.7; }

    .identity-grid {
      display: grid; grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
      gap: 8px; margin-bottom: 16px;
    }
    .id-card {
      background: #0f172a; border-radius: 10px; padding: 12px;
      display: flex; flex-direction: column; gap: 4px; min-width: 0;
    }
    .id-label { color: #64748b; font-size: 11px; text-transform: uppercase; letter-spacing: 0.5px; }
    .id-value { color: #f1f5f9; font-size: 13px; font-weight: 600; word-break: break-all; overflow-wrap: anywhere; }

    .telemetry-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
    .tele-card {
      background: #0f172a; border-radius: 12px; padding: 14px;
      border: 1px solid #1e293b; transition: border-color 0.2s; min-width: 0;
    }
    .tele-card.wide { grid-column: 1 / -1; }
    .tele-card.on { border-color: rgba(34,197,94,0.3); }
    .tele-card.off { border-color: rgba(239,68,68,0.3); }
    .tele-card.fuel-low { border-color: rgba(239,68,68,0.3); }
    .tele-card.stale-frame { border-color: rgba(148,163,184,0.3); background: rgba(148,163,184,0.05); }
    .tele-icon { margin-bottom: 6px; }
    .tele-label { color: #64748b; font-size: 11px; margin-bottom: 4px; }
    .tele-value { color: #f1f5f9; font-size: 15px; font-weight: 600; word-break: break-all; overflow-wrap: anywhere; }
    .tele-sub { color: #94a3b8; font-size: 11px; margin-top: 4px; line-height: 1.4; word-break: break-word; }

    .mono { font-family: 'SF Mono', 'Fira Code', 'Consolas', monospace; letter-spacing: 0.3px; font-size: 0.92em; }

    .error-card {
      display: flex; align-items: center; gap: 10px;
      background: rgba(239,68,68,0.1); border: 1px solid rgba(239,68,68,0.3);
      border-radius: 12px; padding: 14px 16px; color: #fca5a5; font-size: 14px;
    }

    /* ── Mobile ── */
    @media (max-width: 480px) {
      .page-bg { padding: 16px 12px; }
      .logo-row h1 { font-size: 18px; }
      .logo-row svg { width: 28px; height: 28px; }
      .subtitle { font-size: 12px; }
      .input-row { flex-direction: column; gap: 8px; }
      .search-btn { width: 100%; justify-content: center; padding: 14px; border-radius: 12px; min-height: 50px; font-size: 15px; }
      .search-btn .btn-icon { display: none; }
      .result-card { padding: 16px; border-radius: 12px; }
      .identity-grid { grid-template-columns: 1fr 1fr; gap: 6px; }
      .id-card { padding: 10px; }
      .id-value { font-size: 12px; }
      .telemetry-grid { grid-template-columns: 1fr 1fr; gap: 8px; }
      .tele-card { padding: 12px; }
      .tele-card.wide { grid-column: 1 / -1; }
      .tele-value { font-size: 14px; }
      .tele-icon svg { width: 22px; height: 22px; }
      .connection-badge { font-size: 13px; padding: 8px 14px; }
    }
    @media (max-width: 360px) {
      .identity-grid { grid-template-columns: 1fr; }
      .telemetry-grid { grid-template-columns: 1fr; }
      .tele-card.wide { grid-column: 1; }
    }
  `]
})
export class DeviceCheckComponent {
  query = '';
  loading = false;
  result: any = null;
  error = '';

  private http: HttpClient;

  constructor(handler: HttpBackend) {
    this.http = new HttpClient(handler);
  }

  search() {
    const q = this.query.trim();
    if (!q) return;
    this.loading = true;
    this.result = null;
    this.error = '';

    this.http.get(`${environment.apiUrl}/devicecheck/lookup`, { params: { q } }).subscribe({
      next: (data: any) => {
        this.result = data;
        this.loading = false;
      },
      error: (err) => {
        this.error = err?.error?.error || err?.message || 'Erreur de connexion au serveur.';
        this.loading = false;
      }
    });
  }

  formatDate(iso: string): string {
    if (!iso) return 'N/A';
    return new Date(iso).toLocaleString('fr-FR', {
      day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit', second: '2-digit'
    });
  }
}
