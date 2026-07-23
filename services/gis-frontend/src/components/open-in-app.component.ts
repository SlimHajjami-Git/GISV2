import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';

/**
 * Page interstitielle publique derrière le QR du popup monitoring
 * (`/l/vehicle/:id?lat=..&lng=..`).
 *
 * Un QR encodant directement `calypso://…` n'est pas ouvert par tous les
 * lecteurs QR (beaucoup ignorent les schémas custom) ; une URL https, si.
 * Cette page tente alors d'ouvrir l'appli mobile Calypso zoomée sur le
 * véhicule et propose un repli Google Maps pour un destinataire sans
 * l'appli (remorqueur…). Aucune donnée sensible : uniquement l'id et les
 * coordonnées déjà présentes dans le QR.
 *
 * Sur Android on passe par une URL `intent://` (fiable dans Chrome, avec
 * repli automatique vers Maps si l'appli n'est pas installée) ; ailleurs,
 * schéma `calypso://` direct.
 */
@Component({
  selector: 'app-open-in-app',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="oia-page">
      <div class="oia-card">
        <div class="oia-logo">
          <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2"><rect x="1" y="3" width="15" height="13" rx="2"/><path d="M16 8h4l3 3v5a2 2 0 0 1-2 2h-1"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/></svg>
        </div>
        <h1>Position du véhicule</h1>
        <p class="oia-sub">Ouverture de l'application Calypso…</p>
        <a class="oia-btn oia-btn-app" [href]="appLink">Ouvrir dans l'appli Calypso</a>
        <a class="oia-btn oia-btn-maps" *ngIf="mapsLink" [href]="mapsLink">Ouvrir dans Google Maps</a>
        <p class="oia-note">Sans l'appli, utilisez Google Maps pour voir la position.</p>
      </div>
    </div>
  `,
  styles: [`
    .oia-page {
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      background: linear-gradient(160deg, #0d1425 0%, #16213a 100%);
      padding: 24px;
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
    }
    .oia-card {
      background: #fff;
      border-radius: 20px;
      padding: 32px 28px;
      max-width: 360px;
      width: 100%;
      text-align: center;
      box-shadow: 0 20px 60px rgba(0,0,0,.35);
    }
    .oia-logo {
      width: 60px; height: 60px;
      margin: 0 auto 16px;
      background: linear-gradient(135deg, #4f46e5, #4338ca);
      border-radius: 16px;
      display: flex; align-items: center; justify-content: center;
    }
    h1 { margin: 0 0 6px; font-size: 20px; color: #0f172a; }
    .oia-sub { margin: 0 0 22px; font-size: 13px; color: #64748b; }
    .oia-btn {
      display: block;
      padding: 13px 16px;
      border-radius: 12px;
      font-size: 15px;
      font-weight: 700;
      text-decoration: none;
      margin-bottom: 10px;
    }
    .oia-btn-app { background: #4f46e5; color: #fff; }
    .oia-btn-maps { background: #1a73e8; color: #fff; }
    .oia-note { margin: 12px 0 0; font-size: 12px; color: #94a3b8; }
  `]
})
export class OpenInAppComponent implements OnInit {
  appLink = '';
  mapsLink = '';

  constructor(private route: ActivatedRoute) {}

  ngOnInit() {
    const id = this.route.snapshot.paramMap.get('id') || '';
    const qp = this.route.snapshot.queryParamMap;
    const lat = qp.get('lat');
    const lng = qp.get('lng');
    const hasCoords = !!(lat && lng && !isNaN(parseFloat(lat)) && !isNaN(parseFloat(lng)));
    const coords = hasCoords ? `?lat=${encodeURIComponent(lat!)}&lng=${encodeURIComponent(lng!)}` : '';
    this.mapsLink = hasCoords ? `https://www.google.com/maps?q=${lat},${lng}` : '';

    const scheme = `calypso://vehicle/${encodeURIComponent(id)}${coords}`;
    const isAndroid = /android/i.test(navigator.userAgent);
    this.appLink = isAndroid
      ? `intent://vehicle/${encodeURIComponent(id)}${coords}#Intent;scheme=calypso;package=tn.belive.gisv2;`
        + (this.mapsLink ? `S.browser_fallback_url=${encodeURIComponent(this.mapsLink)};` : '')
        + 'end'
      : scheme;

    // Tentative automatique. Certains navigateurs exigent un geste
    // utilisateur pour un schéma custom — les boutons restent le plan B.
    setTimeout(() => {
      try { window.location.href = this.appLink; } catch { /* bouton en secours */ }
    }, 400);
  }
}
