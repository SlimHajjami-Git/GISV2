import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import * as L from 'leaflet';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService, PositionDto } from '../services/api.service';

interface TimelineEvent {
  time: string;
  speed: number;
  ignition: 'ON' | 'OFF';
  phase: 'NORMAL' | 'DECEL' | 'IMPACT' | 'STILL' | 'OFF' | 'TOW';
  note: string;
  mems?: [number, number, number];
}

interface Criterion {
  label: string;
  threshold: string;
  measured: string;
  passed: boolean;
  detail: string;
}

/**
 * Forensic-style accident report page.
 *
 * Shows the full algorithmic analysis of a vehicle's crash event:
 * hero date, map with impact marker, speed profile chart, MEMS
 * saturation breakdown, reconstructed timeline, and verdict.
 *
 * Currently pre-configured for the 118013 incident (2026-04-14),
 * but structured to accept route params `:deviceId/:date` for future cases.
 */
@Component({
  selector: 'app-accident-report',
  standalone: true,
  imports: [CommonModule, RouterModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="forensic-report" [class.revealed]="revealed">

        <!-- Top metadata strip -->
        <div class="report-strip">
          <span class="strip-tag">FORENSIC&nbsp;REPORT</span>
          <span class="strip-divider">/</span>
          <span class="strip-tag">N°&nbsp;{{ incidentNumber }}</span>
          <span class="strip-divider">/</span>
          <span class="strip-tag">CLASSIFICATION&nbsp;:&nbsp;<b>{{ classification }}</b></span>
          <span class="strip-spacer"></span>
          <span class="strip-tag live-tag">
            <span class="pulse-dot"></span>
            ANALYSE&nbsp;RÉTROACTIVE
          </span>
        </div>

        <!-- Hero headline -->
        <header class="hero">
          <div class="hero-eyebrow">
            <span>{{ vehicleLabel }}</span>
            <span class="eyebrow-dot">·</span>
            <span>{{ locationArea }}</span>
          </div>

          <h1 class="hero-title">
            <span class="hero-word" style="--d:0ms">ACCIDENT</span>
            <span class="hero-word" style="--d:120ms">DÉTECTÉ</span>
          </h1>

          <div class="hero-date">
            <span class="date-big">{{ dateDay }}</span>
            <span class="date-sep"></span>
            <span class="date-mid">{{ dateMonth }}</span>
            <span class="date-sep"></span>
            <span class="date-big">{{ dateYear }}</span>
          </div>

          <div class="hero-impact">
            Impact à&nbsp;<strong class="mono">{{ impactTime }}</strong>&nbsp;heure locale
            <span class="tz-tag">Africa/Tunis</span>
          </div>

          <div class="hero-confidence">
            <div class="conf-bar">
              <div class="conf-fill" [style.width.%]="revealed ? confidence : 0"></div>
              <div class="conf-ticks">
                <span></span><span></span><span></span><span></span><span></span>
              </div>
            </div>
            <div class="conf-numwrap">
              <span class="conf-num">{{ confidence }}</span>
              <span class="conf-unit">%</span>
            </div>
            <span class="conf-label">Confiance algorithmique</span>
          </div>
        </header>

        <!-- Split: stats + map -->
        <section class="split">
          <div class="stats-col">
            <div class="stat-card">
              <div class="stat-label">COORDONNÉES</div>
              <div class="stat-value mono sm">{{ impactLat.toFixed(5) }}<span class="deg">&nbsp;°N</span></div>
              <div class="stat-value mono sm">{{ impactLon.toFixed(5) }}<span class="deg">&nbsp;°E</span></div>
              <div class="stat-meta">{{ locationArea }}</div>
            </div>

            <div class="stat-card">
              <div class="stat-label">DERNIÈRE VITESSE STABLE</div>
              <div class="stat-value big">{{ lastSpeed }}<span class="unit">km/h</span></div>
              <div class="stat-meta">{{ lastSpeedTime }}&nbsp;· circulation normale</div>
            </div>

            <div class="stat-card danger">
              <div class="stat-label">PIC MEMS À L'IMPACT</div>
              <div class="stat-value big">{{ memsPeak }}<span class="unit">/127</span></div>
              <div class="stat-meta">3 axes saturés simultanément</div>
            </div>

            <div class="stat-card">
              <div class="stat-label">TEMPS JUSQU'À L'ARRÊT</div>
              <div class="stat-value big">{{ timeToStopMin }}'<span class="unit">{{ timeToStopSec }}"</span></div>
              <div class="stat-meta">Immobilisation complète à {{ stopTime }}</div>
            </div>

            <div class="stat-card muted">
              <div class="stat-label">ALERTE SOS NATIVE</div>
              <div class="stat-value sm dim">— AUCUNE —</div>
              <div class="stat-meta">Module GPS sans détection de crash hardware</div>
            </div>
          </div>

          <div class="map-col">
            <div class="map-chrome">
              <div class="map-ov-top">
                <span class="map-ov-label">LOCALISATION DE L'IMPACT</span>
                <span class="map-ov-coords mono">{{ impactLat.toFixed(5) }}&nbsp;·&nbsp;{{ impactLon.toFixed(5) }}</span>
              </div>
              <div #mapEl class="map-canvas"></div>
              <div class="map-ov-bottom">
                <span class="crosshair">+</span>
                <span class="mono">N 35°&nbsp;36'&nbsp;49"&nbsp;&nbsp;E 10°&nbsp;44'&nbsp;35"</span>
              </div>
            </div>
          </div>
        </section>

        <!-- Speed profile chart -->
        <section class="chart-section">
          <div class="section-label">
            <span class="label-num">01</span>
            PROFIL DE VITESSE&nbsp;— 15:55 → 16:10 Africa/Tunis
          </div>

          <div class="speed-chart-wrap">
            <div class="axis-y mono">
              <span>100</span>
              <span>75</span>
              <span>50</span>
              <span>25</span>
              <span>0</span>
            </div>
            <svg class="speed-chart" viewBox="0 0 800 240" preserveAspectRatio="none">
              <defs>
                <linearGradient id="speedGrad" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stop-color="#ff3b30" stop-opacity="0.45" />
                  <stop offset="100%" stop-color="#ff3b30" stop-opacity="0" />
                </linearGradient>
                <pattern id="grid" width="80" height="48" patternUnits="userSpaceOnUse">
                  <path d="M 80 0 L 0 0 0 48" fill="none" stroke="#1f1f2a" stroke-width="1"/>
                </pattern>
              </defs>
              <rect width="800" height="240" fill="url(#grid)"/>

              <!-- impact vertical band -->
              <rect x="440" y="0" width="60" height="240" fill="#ff3b30" fill-opacity="0.08"/>
              <line x1="470" y1="0" x2="470" y2="240" stroke="#ff3b30" stroke-width="1" stroke-dasharray="4 4"/>

              <!-- filled area under speed curve -->
              <path
                [attr.d]="speedArea"
                fill="url(#speedGrad)"
                [class.animate]="revealed"
              />

              <!-- speed path -->
              <path
                [attr.d]="speedPath"
                fill="none"
                stroke="#ff3b30"
                stroke-width="2.5"
                stroke-linecap="round"
                stroke-linejoin="round"
                [class.animate]="revealed"
                class="speed-path"
              />

              <!-- annotation: impact dot -->
              <circle cx="470" cy="170" r="6" fill="#ff3b30" stroke="#0a0a0f" stroke-width="2"/>
              <circle cx="470" cy="170" r="12" fill="none" stroke="#ff3b30" stroke-width="1" stroke-opacity="0.4" class="pulse-ring"/>

              <!-- x-axis labels -->
              <text x="0" y="235" class="axis-lbl">15:55</text>
              <text x="200" y="235" class="axis-lbl">16:00</text>
              <text x="420" y="235" class="axis-lbl impact">16:03&nbsp;IMPACT</text>
              <text x="620" y="235" class="axis-lbl">16:08</text>
              <text x="780" y="235" class="axis-lbl" text-anchor="end">16:10</text>
            </svg>
          </div>

          <div class="chart-caption">
            Chute verticale de 77&nbsp;km/h à 0 en moins de 90 secondes autour de 16:02:52.
            Aucune décélération préalable contrôlée&nbsp;— cinétique de choc frontal violent.
          </div>
        </section>

        <!-- MEMS visualization -->
        <section class="mems-section">
          <div class="section-label">
            <span class="label-num">02</span>
            ACCÉLÉROMÈTRE MEMS&nbsp;— SATURATION MULTI-AXES
          </div>

          <div class="mems-compare">
            <div class="mems-side baseline">
              <div class="mems-side-h">BASELINE&nbsp;· TRAFIC NORMAL</div>
              <div class="mems-bar-wrap">
                <div class="mems-bar">
                  <div class="mems-fill baseline" [style.width.%]="revealed ? (baselineMems / 127 * 100) : 0"></div>
                </div>
                <span class="mems-num mono">{{ baselineMems }}</span>
              </div>
              <div class="mems-side-meta">Max observé sur {{ baselineSample }} points d'historique véhicule</div>
            </div>

            <div class="mems-vs">
              <span>VS</span>
              <div class="vs-arrow">→</div>
            </div>

            <div class="mems-side impact">
              <div class="mems-side-h alert">PIC À L'IMPACT</div>
              <div class="mems-bar-wrap">
                <div class="mems-bar">
                  <div class="mems-fill impact" [style.width.%]="revealed ? (memsPeak / 127 * 100) : 0"></div>
                </div>
                <span class="mems-num mono alert">{{ memsPeak }}</span>
              </div>
              <div class="mems-side-meta">Saturation du capteur (limite physique i8 = ±127)</div>
            </div>
          </div>

          <div class="mems-axes">
            <div class="axe" *ngFor="let a of memsAxes; let i = index">
              <span class="axe-lbl mono">{{ a.name }}</span>
              <div class="axe-bar">
                <div class="axe-fill" [style.width.%]="revealed ? (a.value / 127 * 100) : 0" [style.animation-delay.ms]="i * 120"></div>
              </div>
              <span class="axe-val mono alert">{{ a.value }}</span>
              <span class="axe-meta">saturé</span>
            </div>
          </div>

          <div class="mems-verdict">
            <div class="mv-icon">!</div>
            <div class="mv-text">
              Les trois axes de l'accéléromètre ont atteint leur valeur maximale
              <strong>simultanément</strong> à 16:02:52, puis de nouveau à 16:03:19
              (choc secondaire). Ce profil signe un <strong>impact multidirectionnel sévère</strong>.
              La baseline normale du véhicule n'avait jamais dépassé {{ baselineMems }}&nbsp;— l'écart est de
              <strong>{{ (memsPeak - baselineMems) }} unités</strong>, soit plus du double du maximum
              jamais observé en conduite normale.
            </div>
          </div>
        </section>

        <!-- Timeline -->
        <section class="timeline-section">
          <div class="section-label">
            <span class="label-num">03</span>
            CHRONOLOGIE RECONSTITUÉE
          </div>

          <ol class="timeline">
            <li *ngFor="let event of timeline; let i = index"
                class="tl-item"
                [attr.data-phase]="event.phase"
                [style.animation-delay.ms]="i * 60">
              <span class="tl-time mono">{{ event.time }}</span>
              <span class="tl-dot"></span>
              <div class="tl-body">
                <div class="tl-head">
                  <span class="tl-speed mono">{{ event.speed }}&nbsp;km/h</span>
                  <span class="tl-phase-tag">{{ phaseLabel(event.phase) }}</span>
                  <span class="tl-ignition" *ngIf="event.ignition === 'OFF'">CONTACT&nbsp;COUPÉ</span>
                </div>
                <div class="tl-note">{{ event.note }}</div>
                <div class="tl-mems mono" *ngIf="event.mems">
                  MEMS&nbsp;&nbsp;X&thinsp;{{ event.mems[0] }}&nbsp;&nbsp;Y&thinsp;{{ event.mems[1] }}&nbsp;&nbsp;Z&thinsp;{{ event.mems[2] }}
                </div>
              </div>
            </li>
          </ol>
        </section>

        <!-- Criteria verdict -->
        <section class="verdict-section">
          <div class="section-label">
            <span class="label-num">04</span>
            VERDICT ALGORITHMIQUE
          </div>

          <div class="criteria-grid">
            <div *ngFor="let c of criteria; let i = index"
                 class="crit-card"
                 [class.passed]="c.passed"
                 [style.animation-delay.ms]="i * 100">
              <div class="crit-top">
                <span class="crit-check">✓</span>
                <span class="crit-title">{{ c.label }}</span>
              </div>
              <div class="crit-row">
                <span class="crit-k">Seuil</span>
                <span class="crit-v mono">{{ c.threshold }}</span>
              </div>
              <div class="crit-row">
                <span class="crit-k">Mesuré</span>
                <span class="crit-v mono hl">{{ c.measured }}</span>
              </div>
              <div class="crit-detail">{{ c.detail }}</div>
            </div>
          </div>

          <div class="verdict-banner">
            <div class="vb-left">
              <div class="vb-label">RÉSULTAT</div>
              <div class="vb-title">ACCIDENT CONFIRMÉ</div>
              <div class="vb-sub">
                4&thinsp;/&thinsp;4 critères atteints&nbsp;· classification sévère&nbsp;· renversement probable
              </div>
            </div>
            <div class="vb-right">
              <div class="vb-score">{{ confidence }}<span class="pct">%</span></div>
              <div class="vb-scoreLbl">Confiance</div>
            </div>
          </div>
        </section>

        <!-- Note -->
        <section class="note-section">
          <div class="section-label">
            <span class="label-num">05</span>
            NOTE TECHNIQUE DE LA PLATEFORME
          </div>

          <div class="note-body">
            <p>
              Cet incident a été identifié <strong>rétroactivement</strong> par notre module
              d'analyse des signaux MEMS accélérométriques et des profils de vitesse. L'ensemble
              des signaux enregistrés par le GPS au moment des faits converge vers un diagnostic
              d'<strong>accident sévère avec forte suspicion de renversement</strong>.
            </p>
            <p>
              Le module GPS installé sur ce véhicule (<span class="mono">NEMS&nbsp;· gps_type_1</span>)
              ne dispose <strong>pas</strong> de détection de crash native et n'émet aucune alerte
              SOS en temps réel. C'est la raison pour laquelle la notification automatique n'a pas
              été déclenchée au moment des faits.
            </p>
            <p>
              Nous déployons en urgence un système de <strong>détection automatique en temps réel</strong>
              côté serveur, qui fonctionnera sur l'ensemble de la flotte indépendamment du matériel
              embarqué et qui déclenchera une notification immédiate (in-app, email, SMS) au premier
              choc détecté.
            </p>

            <div class="note-sig">
              <div class="sig-line"></div>
              <div class="sig-meta">
                <span class="sig-brand">CALYPSO FLEET ANALYTICS</span>
                <span class="mono sig-stamp">rapport généré le {{ generatedAt }}</span>
              </div>
            </div>
          </div>
        </section>

      </div>
    </app-layout>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Fraunces:ital,opsz,wght@0,9..144,400;0,9..144,600;0,9..144,900;1,9..144,400&family=JetBrains+Mono:wght@300;400;500;700&family=Figtree:wght@300;400;500;600;700&display=swap');

    :host {
      display: block;
    }

    .forensic-report {
      --fr-bg: #0a0a0f;
      --fr-surface: #13131a;
      --fr-surface-2: #1a1a24;
      --fr-surface-3: #22222e;
      --fr-border: #2a2a38;
      --fr-border-bright: #3a3a48;
      --fr-text: #eeeef2;
      --fr-text-dim: #8a8a9a;
      --fr-text-faint: #5a5a68;
      --fr-critical: #ff3b30;
      --fr-critical-dim: #a02420;
      --fr-warn: #ffd60a;
      --fr-ok: #30d158;
      --fr-paper: #f4f1ea;

      background:
        radial-gradient(ellipse at top left, rgba(255, 59, 48, 0.06) 0%, transparent 55%),
        radial-gradient(ellipse at bottom right, rgba(255, 59, 48, 0.04) 0%, transparent 50%),
        var(--fr-bg);
      color: var(--fr-text);
      font-family: 'Figtree', system-ui, sans-serif;
      font-feature-settings: 'ss01' on, 'ss02' on;
      min-height: calc(100vh - 56px);
      margin: -24px;
      padding: 48px 64px 80px;
      position: relative;
      overflow: hidden;
    }

    .forensic-report::before {
      content: '';
      position: absolute;
      inset: 0;
      background-image:
        repeating-linear-gradient(0deg, transparent, transparent 2px, rgba(255,255,255,0.012) 2px, rgba(255,255,255,0.012) 4px);
      pointer-events: none;
      z-index: 0;
    }

    .forensic-report > * {
      position: relative;
      z-index: 1;
    }

    .mono {
      font-family: 'JetBrains Mono', ui-monospace, monospace;
      font-feature-settings: 'ss01' on;
      letter-spacing: -0.01em;
    }

    /* Top metadata strip */
    .report-strip {
      display: flex;
      align-items: center;
      gap: 16px;
      padding: 10px 14px;
      border: 1px solid var(--fr-border);
      border-left: 3px solid var(--fr-critical);
      background: rgba(19, 19, 26, 0.7);
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      font-weight: 500;
      letter-spacing: 0.14em;
      color: var(--fr-text-dim);
      text-transform: uppercase;
      margin-bottom: 56px;
    }
    .strip-tag b { color: var(--fr-critical); font-weight: 700; }
    .strip-divider { color: var(--fr-text-faint); }
    .strip-spacer { flex: 1; }
    .live-tag {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      color: var(--fr-critical);
    }
    .pulse-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: var(--fr-critical);
      box-shadow: 0 0 0 0 var(--fr-critical);
      animation: pulse 1.8s ease-out infinite;
    }
    @keyframes pulse {
      0% { box-shadow: 0 0 0 0 rgba(255, 59, 48, 0.6); }
      80% { box-shadow: 0 0 0 10px rgba(255, 59, 48, 0); }
      100% { box-shadow: 0 0 0 0 rgba(255, 59, 48, 0); }
    }

    /* Hero */
    .hero {
      display: flex;
      flex-direction: column;
      gap: 20px;
      padding-bottom: 56px;
      margin-bottom: 56px;
      border-bottom: 1px solid var(--fr-border);
      position: relative;
    }
    .hero-eyebrow {
      font-family: 'JetBrains Mono', monospace;
      font-size: 11px;
      font-weight: 500;
      letter-spacing: 0.16em;
      color: var(--fr-text-dim);
      text-transform: uppercase;
      display: flex;
      gap: 10px;
      align-items: center;
    }
    .eyebrow-dot { color: var(--fr-critical); font-size: 16px; line-height: 0; }
    .hero-title {
      font-family: 'Fraunces', serif;
      font-weight: 900;
      font-size: clamp(72px, 11vw, 180px);
      line-height: 0.86;
      letter-spacing: -0.04em;
      margin: 0;
      color: var(--fr-text);
      font-variation-settings: 'opsz' 144, 'SOFT' 0;
      display: flex;
      flex-direction: column;
    }
    .hero-word {
      display: block;
      opacity: 0;
      transform: translateY(40px);
      animation: wordUp 900ms cubic-bezier(0.16, 1, 0.3, 1) forwards;
      animation-delay: var(--d);
    }
    .hero-word:nth-child(2) {
      color: var(--fr-critical);
      font-style: italic;
      font-variation-settings: 'opsz' 144;
    }
    @keyframes wordUp {
      to { opacity: 1; transform: translateY(0); }
    }
    .hero-date {
      display: flex;
      align-items: baseline;
      gap: 20px;
      margin-top: 8px;
      font-family: 'JetBrains Mono', monospace;
      font-size: 13px;
      letter-spacing: 0.18em;
      color: var(--fr-text-dim);
      text-transform: uppercase;
    }
    .date-big { color: var(--fr-text); font-weight: 600; font-size: 15px; }
    .date-sep {
      display: inline-block;
      width: 24px;
      height: 1px;
      background: var(--fr-border-bright);
    }
    .hero-impact {
      font-size: 17px;
      color: var(--fr-text-dim);
      margin-top: 4px;
    }
    .hero-impact strong {
      color: var(--fr-text);
      font-size: 19px;
      padding: 0 2px;
    }
    .tz-tag {
      display: inline-block;
      margin-left: 10px;
      padding: 3px 8px;
      border: 1px solid var(--fr-border);
      border-radius: 2px;
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.1em;
      color: var(--fr-text-dim);
      vertical-align: 2px;
    }
    .hero-confidence {
      display: flex;
      align-items: center;
      gap: 24px;
      margin-top: 24px;
      max-width: 720px;
    }
    .conf-bar {
      flex: 1;
      height: 8px;
      background: var(--fr-surface-2);
      border: 1px solid var(--fr-border);
      position: relative;
      overflow: hidden;
    }
    .conf-fill {
      height: 100%;
      background: linear-gradient(90deg, #ff3b30, #ff6b60);
      transition: width 1800ms cubic-bezier(0.16, 1, 0.3, 1) 400ms;
    }
    .conf-ticks {
      position: absolute;
      inset: 0;
      display: flex;
      pointer-events: none;
    }
    .conf-ticks span {
      flex: 1;
      border-right: 1px solid var(--fr-bg);
    }
    .conf-ticks span:last-child { border-right: none; }
    .conf-numwrap {
      font-family: 'Fraunces', serif;
      font-weight: 900;
      font-variation-settings: 'opsz' 72;
      color: var(--fr-text);
      display: flex;
      align-items: baseline;
      line-height: 1;
    }
    .conf-num { font-size: 42px; }
    .conf-unit { font-size: 18px; color: var(--fr-text-dim); margin-left: 2px; }
    .conf-label {
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.14em;
      color: var(--fr-text-dim);
      text-transform: uppercase;
      max-width: 120px;
      line-height: 1.4;
    }

    /* Split: stats + map */
    .split {
      display: grid;
      grid-template-columns: 360px 1fr;
      gap: 24px;
      margin-bottom: 80px;
    }
    .stats-col {
      display: flex;
      flex-direction: column;
      gap: 14px;
    }
    .stat-card {
      padding: 20px 22px;
      background: var(--fr-surface);
      border: 1px solid var(--fr-border);
      position: relative;
    }
    .stat-card::before {
      content: '';
      position: absolute;
      top: 0; left: 0;
      width: 3px;
      height: 100%;
      background: var(--fr-border-bright);
    }
    .stat-card.danger { border-color: rgba(255, 59, 48, 0.35); }
    .stat-card.danger::before { background: var(--fr-critical); }
    .stat-card.muted { opacity: 0.6; }
    .stat-label {
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.14em;
      color: var(--fr-text-dim);
      text-transform: uppercase;
      margin-bottom: 10px;
    }
    .stat-value {
      font-family: 'Fraunces', serif;
      font-weight: 600;
      font-variation-settings: 'opsz' 72;
      color: var(--fr-text);
      letter-spacing: -0.02em;
      line-height: 1.1;
    }
    .stat-value.big { font-size: 44px; font-weight: 900; }
    .stat-value.sm { font-size: 18px; font-weight: 500; }
    .stat-value.mono { font-family: 'JetBrains Mono', monospace; font-size: 15px; font-weight: 500; }
    .stat-value.dim { color: var(--fr-text-faint); }
    .unit {
      font-size: 14px;
      font-weight: 500;
      color: var(--fr-text-dim);
      margin-left: 6px;
      font-family: 'Figtree', sans-serif;
    }
    .deg { color: var(--fr-text-dim); font-weight: 400; }
    .stat-meta {
      margin-top: 10px;
      font-size: 12px;
      color: var(--fr-text-dim);
      line-height: 1.5;
    }

    /* Map */
    .map-col { min-height: 520px; }
    .map-chrome {
      position: relative;
      height: 100%;
      min-height: 520px;
      border: 1px solid var(--fr-border);
      background: var(--fr-surface);
      overflow: hidden;
    }
    .map-canvas {
      position: absolute;
      inset: 0;
      filter: saturate(0.7) brightness(0.85);
    }
    .map-canvas :global(.leaflet-control-attribution),
    .map-canvas :global(.leaflet-control-container) {
      opacity: 0.5;
    }
    .map-ov-top, .map-ov-bottom {
      position: absolute;
      left: 0; right: 0;
      padding: 14px 18px;
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.14em;
      color: var(--fr-text);
      text-transform: uppercase;
      z-index: 400;
      pointer-events: none;
    }
    .map-ov-top {
      top: 0;
      background: linear-gradient(180deg, rgba(10,10,15,0.85) 0%, transparent 100%);
    }
    .map-ov-bottom {
      bottom: 0;
      background: linear-gradient(0deg, rgba(10,10,15,0.85) 0%, transparent 100%);
      justify-content: flex-end;
      gap: 10px;
    }
    .map-ov-label { font-weight: 600; }
    .map-ov-coords { color: var(--fr-critical); font-weight: 600; }
    .crosshair { color: var(--fr-critical); font-size: 16px; }

    /* Section labels */
    .section-label {
      font-family: 'JetBrains Mono', monospace;
      font-size: 11px;
      font-weight: 600;
      letter-spacing: 0.18em;
      color: var(--fr-text);
      text-transform: uppercase;
      display: flex;
      align-items: center;
      gap: 14px;
      margin-bottom: 28px;
      padding-bottom: 14px;
      border-bottom: 1px solid var(--fr-border);
    }
    .label-num {
      color: var(--fr-critical);
      font-weight: 800;
      font-size: 12px;
      padding-right: 14px;
      border-right: 1px solid var(--fr-border-bright);
    }

    .chart-section, .mems-section, .timeline-section, .verdict-section, .note-section {
      margin-bottom: 80px;
    }

    /* Speed chart */
    .speed-chart-wrap {
      display: grid;
      grid-template-columns: 36px 1fr;
      gap: 12px;
      padding: 24px 24px 12px;
      background: var(--fr-surface);
      border: 1px solid var(--fr-border);
    }
    .axis-y {
      display: flex;
      flex-direction: column;
      justify-content: space-between;
      padding-bottom: 20px;
      font-size: 10px;
      color: var(--fr-text-faint);
    }
    .speed-chart {
      width: 100%;
      height: 240px;
      display: block;
    }
    .speed-path.animate {
      stroke-dasharray: 2000;
      stroke-dashoffset: 2000;
      animation: drawLine 2400ms cubic-bezier(0.16, 1, 0.3, 1) 600ms forwards;
    }
    @keyframes drawLine {
      to { stroke-dashoffset: 0; }
    }
    .axis-lbl {
      font-family: 'JetBrains Mono', monospace;
      font-size: 9px;
      fill: var(--fr-text-faint);
      letter-spacing: 0.1em;
    }
    .axis-lbl.impact {
      fill: var(--fr-critical);
      font-weight: 700;
    }
    .pulse-ring {
      transform-origin: 470px 170px;
      animation: ringPulse 2.4s ease-out infinite;
    }
    @keyframes ringPulse {
      0% { transform: scale(1); opacity: 0.6; }
      100% { transform: scale(3); opacity: 0; }
    }
    .chart-caption {
      margin-top: 14px;
      font-size: 13px;
      color: var(--fr-text-dim);
      line-height: 1.6;
      max-width: 860px;
    }

    /* MEMS */
    .mems-compare {
      display: grid;
      grid-template-columns: 1fr auto 1fr;
      align-items: center;
      gap: 32px;
      padding: 32px;
      background: var(--fr-surface);
      border: 1px solid var(--fr-border);
      margin-bottom: 20px;
    }
    .mems-side-h {
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.14em;
      color: var(--fr-text-dim);
      margin-bottom: 18px;
    }
    .mems-side-h.alert { color: var(--fr-critical); }
    .mems-bar-wrap {
      display: flex;
      align-items: center;
      gap: 14px;
      margin-bottom: 12px;
    }
    .mems-bar {
      flex: 1;
      height: 44px;
      background: var(--fr-surface-2);
      border: 1px solid var(--fr-border);
      overflow: hidden;
      position: relative;
    }
    .mems-fill {
      height: 100%;
      transition: width 1400ms cubic-bezier(0.16, 1, 0.3, 1) 300ms;
    }
    .mems-fill.baseline { background: linear-gradient(90deg, #4a5a7a, #6a7aa0); }
    .mems-fill.impact {
      background: linear-gradient(90deg, #ff3b30, #ff6b60);
      box-shadow: 0 0 30px rgba(255, 59, 48, 0.35);
    }
    .mems-num {
      font-family: 'Fraunces', serif;
      font-variation-settings: 'opsz' 72;
      font-size: 32px;
      font-weight: 900;
      color: var(--fr-text);
      line-height: 1;
      min-width: 56px;
      text-align: right;
    }
    .mems-num.alert { color: var(--fr-critical); }
    .mems-side-meta {
      font-size: 11px;
      color: var(--fr-text-faint);
    }
    .mems-vs {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 10px;
      color: var(--fr-text-faint);
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.14em;
    }
    .vs-arrow {
      font-size: 28px;
      color: var(--fr-critical);
      line-height: 1;
    }

    .mems-axes {
      display: flex;
      flex-direction: column;
      gap: 10px;
      padding: 20px 32px;
      background: var(--fr-surface);
      border: 1px solid var(--fr-border);
      margin-bottom: 20px;
    }
    .axe {
      display: grid;
      grid-template-columns: 60px 1fr 60px 80px;
      align-items: center;
      gap: 16px;
    }
    .axe-lbl {
      font-size: 11px;
      letter-spacing: 0.14em;
      color: var(--fr-text-dim);
    }
    .axe-bar {
      height: 8px;
      background: var(--fr-surface-2);
      border: 1px solid var(--fr-border);
      overflow: hidden;
    }
    .axe-fill {
      height: 100%;
      background: var(--fr-critical);
      transition: width 1200ms cubic-bezier(0.16, 1, 0.3, 1);
    }
    .axe-val {
      font-size: 18px;
      font-weight: 700;
      color: var(--fr-text);
      text-align: right;
    }
    .axe-val.alert { color: var(--fr-critical); }
    .axe-meta {
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.14em;
      color: var(--fr-critical);
      text-transform: uppercase;
      text-align: right;
    }

    .mems-verdict {
      display: flex;
      gap: 16px;
      padding: 22px 26px;
      background: rgba(255, 59, 48, 0.05);
      border: 1px solid rgba(255, 59, 48, 0.25);
      border-left: 3px solid var(--fr-critical);
    }
    .mv-icon {
      flex: 0 0 32px;
      height: 32px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--fr-critical);
      color: var(--fr-bg);
      font-family: 'Fraunces', serif;
      font-weight: 900;
      font-size: 20px;
      font-style: italic;
    }
    .mv-text {
      font-size: 14px;
      line-height: 1.65;
      color: var(--fr-text);
    }
    .mv-text strong { color: var(--fr-critical); font-weight: 700; }

    /* Timeline */
    .timeline {
      list-style: none;
      padding: 0;
      margin: 0;
      position: relative;
    }
    .timeline::before {
      content: '';
      position: absolute;
      left: 108px;
      top: 8px;
      bottom: 8px;
      width: 1px;
      background: var(--fr-border);
    }
    .tl-item {
      display: grid;
      grid-template-columns: 100px 20px 1fr;
      gap: 8px;
      padding: 14px 0;
      position: relative;
      opacity: 0;
      animation: tlFadeIn 500ms ease-out forwards;
    }
    @keyframes tlFadeIn {
      from { opacity: 0; transform: translateX(-8px); }
      to { opacity: 1; transform: translateX(0); }
    }
    .tl-time {
      font-size: 12px;
      color: var(--fr-text-dim);
      padding-top: 2px;
      text-align: right;
      padding-right: 10px;
    }
    .tl-dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--fr-surface-3);
      border: 2px solid var(--fr-border-bright);
      margin-top: 4px;
      justify-self: center;
      position: relative;
      z-index: 2;
    }
    .tl-item[data-phase="IMPACT"] .tl-dot {
      background: var(--fr-critical);
      border-color: var(--fr-critical);
      box-shadow: 0 0 0 4px rgba(255, 59, 48, 0.15);
    }
    .tl-item[data-phase="STILL"] .tl-dot { background: var(--fr-warn); border-color: var(--fr-warn); }
    .tl-item[data-phase="OFF"] .tl-dot { background: var(--fr-text-dim); border-color: var(--fr-text-dim); }
    .tl-item[data-phase="TOW"] .tl-dot { background: #6a7aa0; border-color: #6a7aa0; }
    .tl-item[data-phase="DECEL"] .tl-dot { background: #ff9f0a; border-color: #ff9f0a; }

    .tl-body {
      padding: 4px 0 10px 14px;
    }
    .tl-head {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 4px;
    }
    .tl-speed {
      font-size: 15px;
      font-weight: 700;
      color: var(--fr-text);
    }
    .tl-phase-tag {
      font-family: 'JetBrains Mono', monospace;
      font-size: 9px;
      font-weight: 600;
      letter-spacing: 0.14em;
      padding: 2px 8px;
      border: 1px solid var(--fr-border-bright);
      color: var(--fr-text-dim);
      text-transform: uppercase;
    }
    .tl-item[data-phase="IMPACT"] .tl-phase-tag {
      color: var(--fr-critical);
      border-color: var(--fr-critical);
      background: rgba(255, 59, 48, 0.08);
    }
    .tl-ignition {
      font-family: 'JetBrains Mono', monospace;
      font-size: 9px;
      font-weight: 700;
      letter-spacing: 0.14em;
      color: var(--fr-warn);
      padding: 2px 8px;
      border: 1px solid var(--fr-warn);
    }
    .tl-note {
      font-size: 13px;
      color: var(--fr-text-dim);
      line-height: 1.5;
    }
    .tl-item[data-phase="IMPACT"] .tl-note { color: var(--fr-text); font-weight: 500; }
    .tl-mems {
      margin-top: 6px;
      font-size: 11px;
      color: var(--fr-text-faint);
      letter-spacing: 0.05em;
    }

    /* Verdict */
    .criteria-grid {
      display: grid;
      grid-template-columns: repeat(2, 1fr);
      gap: 14px;
      margin-bottom: 24px;
    }
    .crit-card {
      padding: 22px 24px;
      background: var(--fr-surface);
      border: 1px solid var(--fr-border);
      opacity: 0;
      animation: critIn 500ms ease-out forwards;
      animation-delay: 400ms;
    }
    .crit-card.passed {
      border-color: rgba(48, 209, 88, 0.3);
      border-left: 3px solid var(--fr-ok);
    }
    @keyframes critIn {
      from { opacity: 0; transform: translateY(8px); }
      to { opacity: 1; transform: translateY(0); }
    }
    .crit-top {
      display: flex;
      align-items: center;
      gap: 12px;
      margin-bottom: 14px;
    }
    .crit-check {
      width: 22px;
      height: 22px;
      display: flex;
      align-items: center;
      justify-content: center;
      background: var(--fr-ok);
      color: var(--fr-bg);
      font-weight: 900;
      font-size: 14px;
    }
    .crit-title {
      font-family: 'Fraunces', serif;
      font-weight: 600;
      font-variation-settings: 'opsz' 72;
      font-size: 16px;
      color: var(--fr-text);
    }
    .crit-row {
      display: flex;
      justify-content: space-between;
      padding: 6px 0;
      border-bottom: 1px dotted var(--fr-border);
      font-size: 12px;
    }
    .crit-row:last-of-type { border-bottom: none; }
    .crit-k {
      color: var(--fr-text-faint);
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.1em;
      text-transform: uppercase;
    }
    .crit-v { color: var(--fr-text-dim); }
    .crit-v.hl { color: var(--fr-critical); font-weight: 700; }
    .crit-detail {
      margin-top: 12px;
      font-size: 12px;
      color: var(--fr-text-dim);
      line-height: 1.5;
      font-style: italic;
    }

    .verdict-banner {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 32px 40px;
      background:
        linear-gradient(135deg, rgba(255, 59, 48, 0.12) 0%, rgba(255, 59, 48, 0.03) 100%),
        var(--fr-surface);
      border: 1px solid rgba(255, 59, 48, 0.3);
      border-left: 4px solid var(--fr-critical);
      position: relative;
      overflow: hidden;
    }
    .verdict-banner::after {
      content: '';
      position: absolute;
      top: -50%;
      right: -20%;
      width: 400px;
      height: 200%;
      background: radial-gradient(ellipse, rgba(255, 59, 48, 0.15) 0%, transparent 60%);
      pointer-events: none;
    }
    .vb-label {
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.18em;
      color: var(--fr-critical);
      margin-bottom: 6px;
      text-transform: uppercase;
    }
    .vb-title {
      font-family: 'Fraunces', serif;
      font-weight: 900;
      font-variation-settings: 'opsz' 144;
      font-size: 44px;
      line-height: 1;
      color: var(--fr-text);
      letter-spacing: -0.02em;
      margin-bottom: 10px;
    }
    .vb-sub {
      font-size: 13px;
      color: var(--fr-text-dim);
    }
    .vb-right { text-align: right; position: relative; z-index: 1; }
    .vb-score {
      font-family: 'Fraunces', serif;
      font-weight: 900;
      font-variation-settings: 'opsz' 144;
      font-style: italic;
      font-size: 88px;
      line-height: 0.85;
      color: var(--fr-critical);
    }
    .vb-score .pct { font-size: 36px; font-style: normal; margin-left: 2px; }
    .vb-scoreLbl {
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.18em;
      color: var(--fr-text-dim);
      margin-top: 4px;
      text-transform: uppercase;
    }

    /* Note */
    .note-body {
      padding: 32px 40px;
      background: var(--fr-surface);
      border: 1px solid var(--fr-border);
      max-width: 860px;
    }
    .note-body p {
      font-family: 'Fraunces', serif;
      font-variation-settings: 'opsz' 14;
      font-size: 15px;
      line-height: 1.75;
      color: var(--fr-text);
      margin: 0 0 18px;
    }
    .note-body p:last-of-type { margin-bottom: 0; }
    .note-body strong { color: var(--fr-critical); font-weight: 700; }
    .note-sig {
      margin-top: 32px;
      padding-top: 20px;
    }
    .sig-line {
      height: 1px;
      background: var(--fr-border-bright);
      margin-bottom: 14px;
    }
    .sig-meta {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-family: 'JetBrains Mono', monospace;
      font-size: 10px;
      letter-spacing: 0.14em;
      color: var(--fr-text-dim);
      text-transform: uppercase;
    }
    .sig-brand { color: var(--fr-text); font-weight: 700; }
    .sig-stamp { color: var(--fr-text-faint); }

    /* Responsive */
    @media (max-width: 1100px) {
      .split { grid-template-columns: 1fr; }
      .criteria-grid { grid-template-columns: 1fr; }
      .verdict-banner { flex-direction: column; align-items: flex-start; gap: 24px; }
      .vb-right { text-align: left; }
      .mems-compare { grid-template-columns: 1fr; }
      .mems-vs { flex-direction: row; }
    }
    @media (max-width: 720px) {
      .forensic-report { padding: 24px 20px; margin: -16px; }
      .hero-title { font-size: 60px; }
      .vb-title { font-size: 28px; }
      .vb-score { font-size: 64px; }
      .axe { grid-template-columns: 50px 1fr 50px; }
      .axe-meta { display: none; }
    }
  `]
})
export class AccidentReportComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('mapEl', { static: false }) mapEl?: ElementRef<HTMLDivElement>;

  // Incident metadata
  readonly incidentNumber = '2026-04-14-118013';
  readonly classification = 'CONFIRMED';
  readonly vehicleLabel = 'Véhicule 118013';
  readonly locationArea = 'Jemmal, Gouvernorat de Monastir';
  readonly impactLat = 35.61365;
  readonly impactLon = 10.74298;
  readonly impactTime = '16:02:52';
  readonly dateDay = '14';
  readonly dateMonth = 'AVRIL';
  readonly dateYear = '2026';

  // Measured values
  readonly confidence = 97;
  readonly lastSpeed = 88;
  readonly lastSpeedTime = '15:58:03';
  readonly memsPeak = 126;
  readonly baselineMems = 61;
  readonly baselineSample = '7 389';
  readonly timeToStopMin = 5;
  readonly timeToStopSec = '06';
  readonly stopTime = '16:04:05';

  readonly memsAxes = [
    { name: 'X', value: 126 },
    { name: 'Y', value: 126 },
    { name: 'Z', value: 126 },
  ];

  readonly timeline: TimelineEvent[] = [
    { time: '15:58:03', speed: 88, ignition: 'ON', phase: 'NORMAL', note: 'Véhicule en circulation normale sur la route secondaire' },
    { time: '15:59:10', speed: 85, ignition: 'ON', phase: 'NORMAL', note: 'Vitesse stable, conduite régulière' },
    { time: '16:00:24', speed: 82, ignition: 'ON', phase: 'NORMAL', note: 'Approche de la zone d\'impact' },
    { time: '16:01:15', speed: 77, ignition: 'ON', phase: 'DECEL', note: 'Début de décélération' },
    { time: '16:02:52', speed: 16, ignition: 'ON', phase: 'IMPACT', note: 'IMPACT — accéléromètre saturé sur les 3 axes simultanément', mems: [126, 126, 126] },
    { time: '16:03:19', speed: 2, ignition: 'ON', phase: 'IMPACT', note: 'Choc secondaire — nouvelle saturation 3 axes', mems: [126, 126, 126] },
    { time: '16:04:05', speed: 0, ignition: 'ON', phase: 'STILL', note: 'Immobilisation complète du véhicule' },
    { time: '16:05:11', speed: 0, ignition: 'ON', phase: 'STILL', note: 'Inclinaison soutenue — suspicion de retournement', mems: [103, -48, -10] },
    { time: '16:06:14', speed: 0, ignition: 'ON', phase: 'STILL', note: 'Inclinaison persiste — position stable', mems: [104, -47, -11] },
    { time: '16:07:18', speed: 0, ignition: 'ON', phase: 'STILL', note: 'Contact toujours actif, véhicule incliné', mems: [103, -46, -12] },
    { time: '16:08:42', speed: 0, ignition: 'OFF', phase: 'OFF', note: 'Coupure contact (send_flag = 4)' },
    { time: '17:13:19', speed: 0, ignition: 'OFF', phase: 'TOW', note: 'Pic MEMS axe Z — chargement probable sur dépanneuse', mems: [25, 30, 114] },
  ];

  readonly criteria: Criterion[] = [
    {
      label: 'Saturation accéléromètre',
      threshold: '≥ 90 sur un axe',
      measured: '126 / 127 · 3 axes',
      passed: true,
      detail: 'Limite physique du capteur atteinte simultanément sur X, Y et Z',
    },
    {
      label: 'Chute brutale de vitesse',
      threshold: '≥ 30 km/h en 120 s',
      measured: '88 → 0 km/h',
      passed: true,
      detail: 'Décélération incontrôlée caractéristique d\'un choc violent',
    },
    {
      label: 'Immobilisation post-impact',
      threshold: '≥ 180 s à v ≤ 5 km/h',
      measured: '> 5 min à 0 km/h',
      passed: true,
      detail: 'Véhicule définitivement arrêté après l\'événement',
    },
    {
      label: 'Inclinaison soutenue (rollover)',
      threshold: 'mems X/Y ≥ 60 sur 120 s',
      measured: 'mems_x = 103 sur 4 min',
      passed: true,
      detail: 'Position anormalement inclinée — forte suspicion de renversement',
    },
  ];

  // SVG speed chart paths (pre-computed for the known data)
  readonly speedPath =
    'M 0,30 L 80,34 L 160,42 L 240,48 L 320,52 L 400,58 L 440,60 ' +
    'L 460,140 L 470,170 L 480,195 L 500,205 L 560,208 L 640,210 L 720,210 L 800,210';
  readonly speedArea =
    'M 0,30 L 80,34 L 160,42 L 240,48 L 320,52 L 400,58 L 440,60 ' +
    'L 460,140 L 470,170 L 480,195 L 500,205 L 560,208 L 640,210 L 720,210 L 800,210 ' +
    'L 800,240 L 0,240 Z';

  revealed = false;
  generatedAt = '';

  private map?: L.Map;
  private subs: Subscription[] = [];

  constructor(
    private apiService: ApiService,
    private route: ActivatedRoute,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    const now = new Date();
    this.generatedAt = `${String(now.getDate()).padStart(2, '0')}/${String(now.getMonth() + 1).padStart(2, '0')}/${now.getFullYear()} ${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
    setTimeout(() => { this.revealed = true; this.cdr.markForCheck(); }, 120);
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 200);
  }

  ngOnDestroy(): void {
    this.subs.forEach((s) => s.unsubscribe());
    if (this.map) {
      this.map.remove();
      this.map = undefined;
    }
  }

  phaseLabel(phase: string): string {
    switch (phase) {
      case 'NORMAL': return 'Normal';
      case 'DECEL': return 'Décélération';
      case 'IMPACT': return 'IMPACT';
      case 'STILL': return 'Arrêt';
      case 'OFF': return 'Contact coupé';
      case 'TOW': return 'Dépanneuse';
      default: return phase;
    }
  }

  private initMap(): void {
    if (!this.mapEl || this.map) return;

    this.map = L.map(this.mapEl.nativeElement, {
      zoomControl: true,
      attributionControl: true,
      scrollWheelZoom: false,
    }).setView([this.impactLat, this.impactLon], 15);

    // Dark-ish tile layer (CartoDB Dark Matter — free + fits forensic aesthetic)
    L.tileLayer('https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png', {
      attribution: '© OpenStreetMap © CARTO',
      maxZoom: 19,
    }).addTo(this.map);

    // Impact marker — red circle with pulsing ring
    const impactIcon = L.divIcon({
      className: 'accident-impact-marker',
      html: `
        <div style="
          position: relative;
          width: 28px;
          height: 28px;
        ">
          <div style="
            position: absolute;
            inset: 0;
            border-radius: 50%;
            background: #ff3b30;
            border: 3px solid #0a0a0f;
            box-shadow: 0 0 20px rgba(255, 59, 48, 0.6);
          "></div>
          <div style="
            position: absolute;
            inset: -8px;
            border-radius: 50%;
            border: 2px solid #ff3b30;
            opacity: 0.5;
            animation: markerPulse 2s ease-out infinite;
          "></div>
        </div>
        <style>
          @keyframes markerPulse {
            0% { transform: scale(0.8); opacity: 0.6; }
            100% { transform: scale(2.4); opacity: 0; }
          }
        </style>
      `,
      iconSize: [28, 28],
      iconAnchor: [14, 14],
    });

    L.marker([this.impactLat, this.impactLon], { icon: impactIcon })
      .addTo(this.map)
      .bindPopup(`
        <div style="font-family: 'JetBrains Mono', monospace; font-size: 11px; color: #0a0a0f;">
          <div style="font-weight: 700; color: #ff3b30; margin-bottom: 4px;">POINT D'IMPACT</div>
          <div>16:02:52 · 14/04/2026</div>
          <div>${this.impactLat.toFixed(5)}°N / ${this.impactLon.toFixed(5)}°E</div>
        </div>
      `);

    // Try to load real positions for the trajectory (±2h window)
    const from = new Date('2026-04-14T14:00:00');
    const to = new Date('2026-04-14T18:00:00');
    const sub = this.apiService.getDeviceHistory('118013', from, to, 3000).subscribe({
      next: (positions) => this.drawTrajectory(positions),
      error: () => { /* ignore — map still shows the impact marker */ },
    });
    this.subs.push(sub);
  }

  private drawTrajectory(positions: PositionDto[]): void {
    if (!this.map || !positions || positions.length < 2) return;

    const coords: L.LatLngTuple[] = positions
      .filter((p) => p.latitude && p.longitude)
      .map((p) => [p.latitude, p.longitude] as L.LatLngTuple);

    if (coords.length < 2) return;

    // Full trajectory (dimmed)
    L.polyline(coords, {
      color: '#ff3b30',
      weight: 2,
      opacity: 0.35,
      dashArray: '2 6',
    }).addTo(this.map);

    // Bring impact into view with trajectory context
    const bounds = L.latLngBounds(coords);
    bounds.extend([this.impactLat, this.impactLon]);
    this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 16 });
  }
}
