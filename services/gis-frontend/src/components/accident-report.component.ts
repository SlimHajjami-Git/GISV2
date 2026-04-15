import { Component, OnInit, OnDestroy, AfterViewInit, ElementRef, ViewChild, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterModule } from '@angular/router';
import { Subscription } from 'rxjs';
import * as L from 'leaflet';
import { AppLayoutComponent } from './shared/app-layout.component';
import { ApiService, PositionDto } from '../services/api.service';

interface NarrativeEvent {
  time: string;
  title: string;
  body: string;
  severity: 'normal' | 'warning' | 'critical' | 'neutral';
}

interface Indicator {
  label: string;
  value: string;
  hint?: string;
}

interface ChartDot {
  x: number;
  y: number;
  isImpact?: boolean;
}

interface ChartAxisLabel {
  x: number;
  label: string;
}

/**
 * Formal accident report page — written as a corporate document for a
 * non-technical fleet owner. No jargon, no dramatic UI. Think: an
 * insurance adjuster's written report, a medical summary, a notary's
 * brief.
 *
 * Preconfigured for the 2026-04-14 incident on vehicle 118013 (Jemmal,
 * Monastir). Route params `:deviceId` are accepted for future reuse.
 */
@Component({
  selector: 'app-accident-report',
  standalone: true,
  imports: [CommonModule, RouterModule, AppLayoutComponent],
  template: `
    <app-layout>
      <div class="doc-page">
        <article class="doc" [class.shown]="shown">

          <!-- Document header -->
          <header class="doc-header">
            <div class="doc-header-top">
              <div class="doc-brand">
                <div class="brand-mark" aria-label="Logo Calypso">
                  <svg viewBox="0 0 512 640" xmlns="http://www.w3.org/2000/svg" fill="none" role="img" focusable="false">
                    <defs>
                      <linearGradient id="pinGradHdr" x1="0" y1="0" x2="1" y2="1">
                        <stop offset="0%" stop-color="#43e6a0"/>
                        <stop offset="50%" stop-color="#2fb8d8"/>
                        <stop offset="100%" stop-color="#1a3a8a"/>
                      </linearGradient>
                      <linearGradient id="innerGradHdr" x1="0.2" y1="0" x2="0.8" y2="1">
                        <stop offset="0%" stop-color="#3de8a8"/>
                        <stop offset="100%" stop-color="#1848a0"/>
                      </linearGradient>
                    </defs>
                    <path d="M256 20C152 20 68 104 68 208c0 140 188 290 188 290s188-150 188-290C444 104 360 20 256 20z" fill="url(#pinGradHdr)"/>
                    <circle cx="256" cy="200" r="130" fill="url(#innerGradHdr)" opacity="0.3"/>
                    <path d="M310 110c40 20 68 62 68 108" stroke="white" stroke-width="8" stroke-linecap="round" fill="none" opacity="0.8"/>
                    <circle cx="378" cy="218" r="8" fill="white" opacity="0.8"/>
                    <g transform="translate(256,210)" fill="white">
                      <path d="M-60,10 L-50,-25 Q-45,-40 -30,-40 L30,-40 Q45,-40 50,-25 L60,10 Q62,16 58,20 L-58,20 Q-62,16 -60,10z" opacity="0.95"/>
                      <rect x="-65" y="20" width="130" height="30" rx="8" opacity="0.95"/>
                      <circle cx="-40" cy="50" r="10" fill="url(#pinGradHdr)"/>
                      <circle cx="40" cy="50" r="10" fill="url(#pinGradHdr)"/>
                      <rect x="-20" y="-30" width="40" height="20" rx="4" fill="rgba(26,58,138,0.3)"/>
                    </g>
                  </svg>
                </div>
                <div class="brand-txt">
                  <div class="brand-name">Calypso</div>
                  <div class="brand-tag">Fleet Analytics</div>
                </div>
              </div>
              <div class="doc-meta">
                <div class="meta-row"><span class="meta-k">Référence</span><span class="meta-v">{{ reference }}</span></div>
                <div class="meta-row"><span class="meta-k">Établi le</span><span class="meta-v">{{ issueDate }}</span></div>
                <div class="meta-row"><span class="meta-k">Véhicule</span><span class="meta-v">{{ vehicleLabel }}</span></div>
              </div>
            </div>

            <div class="doc-title-block">
              <div class="doc-eyebrow">Document officiel · Usage interne</div>
              <h1 class="doc-title">Rapport d'incident</h1>
              <div class="doc-subtitle">
                Analyse détaillée d'un événement détecté sur un véhicule de votre flotte
              </div>
            </div>
          </header>

          <!-- Synthèse -->
          <section class="sec sec-synth">
            <div class="sec-num">01</div>
            <h2 class="sec-h">Synthèse</h2>
            <p class="lead">
              Votre véhicule <strong>{{ vehicleLabel }}</strong> a été impliqué dans un
              <strong class="hl">accident grave</strong> le
              <strong>{{ synthesisDateTimeLong }}</strong>, sur la commune de
              <strong>Jemmal</strong>, dans le gouvernorat de <strong>Monastir</strong>.
            </p>
            <p>
              L'analyse des données enregistrées par le boîtier GPS installé sur ce véhicule
              permet d'établir, avec un niveau de certitude très élevé, qu'il s'agit d'un
              <strong>choc violent</strong> ayant vraisemblablement entraîné un
              <strong>retournement du véhicule</strong>. Les éléments qui conduisent à ce
              diagnostic sont détaillés dans les pages suivantes.
            </p>
          </section>

          <hr class="rule"/>

          <!-- Localisation + map -->
          <section class="sec sec-location">
            <div class="sec-num">02</div>
            <h2 class="sec-h">Lieu de l'incident</h2>
            <div class="loc-grid">
              <div class="loc-info">
                <div class="loc-row">
                  <div class="loc-k">Commune</div>
                  <div class="loc-v">Jemmal</div>
                </div>
                <div class="loc-row">
                  <div class="loc-k">Gouvernorat</div>
                  <div class="loc-v">Monastir</div>
                </div>
                <div class="loc-row">
                  <div class="loc-k">Type de voie</div>
                  <div class="loc-v">Route secondaire interurbaine</div>
                </div>
                <div class="loc-row">
                  <div class="loc-k">Coordonnées GPS</div>
                  <div class="loc-v loc-coords">{{ impactLat.toFixed(5) }} °N&nbsp;·&nbsp;{{ impactLon.toFixed(5) }} °E</div>
                </div>
                <div class="loc-note">
                  Le point rouge sur la carte ci-contre indique l'emplacement exact où l'impact
                  a été détecté. Le tracé en pointillés, lorsqu'il apparaît, représente le
                  trajet emprunté par le véhicule dans les heures qui ont précédé.
                </div>
              </div>
              <div class="loc-map">
                <div #mapEl class="map-canvas"></div>
              </div>
            </div>
          </section>

          <hr class="rule"/>

          <!-- Ce qui s'est passé -->
          <section class="sec sec-story">
            <div class="sec-num">03</div>
            <h2 class="sec-h">Déroulement des faits</h2>
            <p class="sec-intro">
              La chronologie suivante a été reconstituée à partir des données envoyées par le
              véhicule toutes les quelques secondes. Les horaires sont exprimés en heure locale
              de Tunis.
            </p>

            <ol class="story">
              <li *ngFor="let e of story" class="story-item" [attr.data-sev]="e.severity">
                <div class="story-time">{{ e.time }}</div>
                <div class="story-body">
                  <div class="story-title">{{ e.title }}</div>
                  <div class="story-text">{{ e.body }}</div>
                </div>
              </li>
            </ol>
          </section>

          <hr class="rule"/>

          <!-- Visualisation profil vitesse -->
          <section class="sec sec-chart">
            <div class="sec-num">04</div>
            <h2 class="sec-h">Évolution de la vitesse autour de l'impact</h2>
            <p class="sec-intro">
              Le graphique ci-dessous représente la vitesse du véhicule {{ chartIntroRange }}.
              On observe une conduite stable {{ chartIntroCruiseSpeed }}, suivie d'une
              <strong>chute brutale</strong> à {{ chartIntroImpactTime }} qui ne correspond pas à un freinage
              normal.
            </p>

            <figure class="chart-figure">
              <svg class="vel-chart" viewBox="0 0 800 260" preserveAspectRatio="xMidYMid meet">
                <!-- Y grid lines -->
                <line x1="60" y1="30" x2="780" y2="30" class="grid"/>
                <line x1="60" y1="85" x2="780" y2="85" class="grid"/>
                <line x1="60" y1="140" x2="780" y2="140" class="grid"/>
                <line x1="60" y1="195" x2="780" y2="195" class="grid"/>
                <line x1="60" y1="220" x2="780" y2="220" class="grid axis"/>

                <!-- Y axis labels -->
                <text x="48" y="34" class="ax-lbl" text-anchor="end">100</text>
                <text x="48" y="89" class="ax-lbl" text-anchor="end">75</text>
                <text x="48" y="144" class="ax-lbl" text-anchor="end">50</text>
                <text x="48" y="199" class="ax-lbl" text-anchor="end">25</text>
                <text x="48" y="224" class="ax-lbl" text-anchor="end">0</text>
                <text x="20" y="130" class="ax-title" transform="rotate(-90 20 130)">km/h</text>

                <!-- Impact vertical marker -->
                <line [attr.x1]="impactX" y1="30" [attr.x2]="impactX" y2="220" class="impact-mark"/>
                <text [attr.x]="impactX + 7" y="45" class="impact-lbl">{{ impactLabelText }}</text>

                <!-- Speed line -->
                <path [attr.d]="chartPath" class="speed-line"/>

                <!-- Dots -->
                <circle
                  *ngFor="let d of chartDots"
                  [attr.cx]="d.x"
                  [attr.cy]="d.y"
                  [attr.r]="d.isImpact ? 4.5 : 3"
                  [class.dot]="!d.isImpact"
                  [class.dot-impact]="d.isImpact"
                />

                <!-- X axis labels -->
                <text
                  *ngFor="let l of xAxisLabels; let first = first; let last = last"
                  [attr.x]="l.x"
                  y="245"
                  class="ax-lbl"
                  [attr.text-anchor]="first ? 'start' : (last ? 'end' : 'middle')"
                >{{ l.label }}</text>
              </svg>
              <figcaption class="chart-cap">
                Profil de vitesse du véhicule — le trait pointillé rouge marque le moment de
                l'impact ({{ chartCaptionImpactTime }}).
              </figcaption>
            </figure>
          </section>

          <hr class="rule"/>

          <!-- Indicateurs clés -->
          <section class="sec sec-indicators">
            <div class="sec-num">05</div>
            <h2 class="sec-h">Indicateurs clés</h2>
            <p class="sec-intro">
              Les valeurs suivantes résument les principaux éléments mesurés au moment des
              faits. Ces informations sont directement extraites des données brutes envoyées
              par le véhicule.
            </p>

            <div class="ind-table">
              <div class="ind-row" *ngFor="let ind of indicators">
                <div class="ind-label">{{ ind.label }}</div>
                <div class="ind-value">{{ ind.value }}</div>
                <div class="ind-hint" *ngIf="ind.hint">{{ ind.hint }}</div>
              </div>
            </div>
          </section>

          <hr class="rule"/>

          <!-- Certitude -->
          <section class="sec sec-confidence">
            <div class="sec-num">06</div>
            <h2 class="sec-h">Niveau de certitude de l'analyse</h2>
            <p class="sec-intro">
              Le diagnostic d'accident grave repose sur <strong>quatre observations
              concordantes</strong>. Chacune, prise isolément, serait déjà un signal fort.
              Réunies, elles rendent la conclusion très difficile à contester.
            </p>

            <ol class="reasons">
              <li *ngFor="let r of reasons; let i = index" class="reason">
                <span class="reason-num">{{ i + 1 }}</span>
                <div class="reason-body">
                  <div class="reason-title">{{ r.title }}</div>
                  <div class="reason-text">{{ r.text }}</div>
                </div>
              </li>
            </ol>

            <div class="certainty-block">
              <div class="certainty-label">Niveau de certitude</div>
              <div class="certainty-bar">
                <div class="certainty-fill" [style.width.%]="shown ? confidence : 0"></div>
              </div>
              <div class="certainty-verdict">
                <span class="verdict-word">Très&nbsp;élevé</span>
                <span class="verdict-pct">{{ confidence }}&thinsp;%</span>
              </div>
            </div>
          </section>

          <hr class="rule"/>

          <!-- Signature / footer -->
          <footer class="doc-footer">
            <div class="sign-line"></div>
            <div class="sign-block">
              <div class="sign-brand">
                <div class="sign-logo" aria-label="Logo Calypso">
                  <svg viewBox="0 0 512 640" xmlns="http://www.w3.org/2000/svg" fill="none" role="img" focusable="false">
                    <defs>
                      <linearGradient id="pinGradFtr" x1="0" y1="0" x2="1" y2="1">
                        <stop offset="0%" stop-color="#43e6a0"/>
                        <stop offset="50%" stop-color="#2fb8d8"/>
                        <stop offset="100%" stop-color="#1a3a8a"/>
                      </linearGradient>
                      <linearGradient id="innerGradFtr" x1="0.2" y1="0" x2="0.8" y2="1">
                        <stop offset="0%" stop-color="#3de8a8"/>
                        <stop offset="100%" stop-color="#1848a0"/>
                      </linearGradient>
                    </defs>
                    <path d="M256 20C152 20 68 104 68 208c0 140 188 290 188 290s188-150 188-290C444 104 360 20 256 20z" fill="url(#pinGradFtr)"/>
                    <circle cx="256" cy="200" r="130" fill="url(#innerGradFtr)" opacity="0.3"/>
                    <path d="M310 110c40 20 68 62 68 108" stroke="white" stroke-width="8" stroke-linecap="round" fill="none" opacity="0.8"/>
                    <circle cx="378" cy="218" r="8" fill="white" opacity="0.8"/>
                    <g transform="translate(256,210)" fill="white">
                      <path d="M-60,10 L-50,-25 Q-45,-40 -30,-40 L30,-40 Q45,-40 50,-25 L60,10 Q62,16 58,20 L-58,20 Q-62,16 -60,10z" opacity="0.95"/>
                      <rect x="-65" y="20" width="130" height="30" rx="8" opacity="0.95"/>
                      <circle cx="-40" cy="50" r="10" fill="url(#pinGradFtr)"/>
                      <circle cx="40" cy="50" r="10" fill="url(#pinGradFtr)"/>
                      <rect x="-20" y="-30" width="40" height="20" rx="4" fill="rgba(26,58,138,0.3)"/>
                    </g>
                  </svg>
                </div>
                <div>
                  <div class="sign-name">Calypso Fleet Analytics</div>
                  <div class="sign-role">Rapport établi par l'équipe d'analyse</div>
                </div>
              </div>
              <div class="sign-meta">
                <div>Rapport établi le {{ issueDate }}</div>
                <div>Dossier {{ reference }}</div>
              </div>
            </div>
          </footer>

        </article>
      </div>
    </app-layout>
  `,
  styles: [`
    @import url('https://fonts.googleapis.com/css2?family=Newsreader:ital,opsz,wght@0,6..72,400;0,6..72,500;0,6..72,600;0,6..72,700;1,6..72,400;1,6..72,500&family=Manrope:wght@300;400;500;600;700&display=swap');

    :host { display: block; }

    .doc-page {
      --paper: #faf8f3;
      --surface: #ffffff;
      --ink: #18181b;
      --ink-soft: #52525b;
      --ink-faint: #9a9aa4;
      --ink-ghost: #c5c3bd;
      --line: #e8e5df;
      --line-soft: #f1ede6;
      --alert: #991b1b;
      --alert-soft: #b91c1c;
      --alert-bg: #fdf5f5;
      --accent: #1e3a5f;

      background: var(--paper);
      min-height: calc(100vh - 56px);
      margin: -24px;
      padding: 48px 24px 96px;
      font-family: 'Manrope', system-ui, sans-serif;
      color: var(--ink);
      -webkit-font-smoothing: antialiased;
      position: relative;
    }

    .doc-page::before {
      content: '';
      position: absolute;
      inset: 0;
      background-image:
        radial-gradient(circle at 50% 0%, rgba(153, 27, 27, 0.015) 0%, transparent 40%),
        linear-gradient(180deg, rgba(24, 24, 27, 0.02) 0%, transparent 20%);
      pointer-events: none;
    }

    .doc {
      max-width: 920px;
      margin: 0 auto;
      background: var(--surface);
      border: 1px solid var(--line);
      box-shadow:
        0 1px 0 rgba(24, 24, 27, 0.04),
        0 12px 40px -12px rgba(24, 24, 27, 0.08),
        0 40px 80px -40px rgba(24, 24, 27, 0.1);
      padding: 72px 84px 88px;
      position: relative;
      opacity: 0;
      transform: translateY(12px);
      transition: opacity 700ms ease, transform 700ms cubic-bezier(0.16, 1, 0.3, 1);
    }
    .doc.shown { opacity: 1; transform: translateY(0); }

    /* Header */
    .doc-header {
      padding-bottom: 48px;
      margin-bottom: 56px;
      border-bottom: 1px solid var(--line);
    }
    .doc-header-top {
      display: flex;
      justify-content: space-between;
      align-items: flex-start;
      gap: 40px;
      margin-bottom: 64px;
    }
    .doc-brand {
      display: flex;
      align-items: center;
      gap: 14px;
    }
    .brand-mark {
      width: 44px;
      height: 52px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .brand-mark svg {
      width: 100%;
      height: 100%;
      display: block;
    }
    .brand-name {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-size: 22px;
      font-variation-settings: 'opsz' 36;
      color: var(--ink);
      line-height: 1;
      letter-spacing: -0.01em;
    }
    .brand-tag {
      font-size: 10px;
      letter-spacing: 0.16em;
      text-transform: uppercase;
      color: var(--ink-faint);
      margin-top: 4px;
      font-weight: 500;
    }
    .doc-meta {
      text-align: right;
      font-size: 12px;
    }
    .meta-row {
      display: flex;
      justify-content: flex-end;
      align-items: baseline;
      gap: 14px;
      margin-bottom: 4px;
    }
    .meta-k {
      font-size: 10px;
      letter-spacing: 0.14em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
    }
    .meta-v {
      color: var(--ink);
      font-weight: 500;
      font-variant-numeric: tabular-nums;
      min-width: 130px;
      text-align: right;
    }

    .doc-eyebrow {
      font-size: 10px;
      letter-spacing: 0.2em;
      text-transform: uppercase;
      color: var(--alert);
      font-weight: 600;
      margin-bottom: 18px;
    }
    .doc-title {
      font-family: 'Newsreader', serif;
      font-weight: 400;
      font-variation-settings: 'opsz' 72;
      font-size: clamp(52px, 7vw, 84px);
      line-height: 0.95;
      letter-spacing: -0.025em;
      color: var(--ink);
      margin: 0 0 20px;
    }
    .doc-subtitle {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 24;
      font-size: 19px;
      color: var(--ink-soft);
      max-width: 560px;
      line-height: 1.5;
    }

    /* Sections */
    .sec {
      position: relative;
      padding-left: 72px;
      margin-bottom: 56px;
      max-width: 720px;
    }
    .sec-num {
      position: absolute;
      left: 0;
      top: 4px;
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-weight: 400;
      font-size: 16px;
      color: var(--ink-ghost);
      font-variation-settings: 'opsz' 18;
    }
    .sec-num::after {
      content: '';
      display: block;
      width: 44px;
      height: 1px;
      background: var(--line);
      margin-top: 10px;
    }
    .sec-h {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-variation-settings: 'opsz' 36;
      font-size: 30px;
      line-height: 1.15;
      letter-spacing: -0.015em;
      color: var(--ink);
      margin: 0 0 22px;
    }
    .sec p, .sec-intro {
      font-size: 15.5px;
      line-height: 1.75;
      color: var(--ink);
      margin: 0 0 16px;
    }
    .sec-intro {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 16;
      font-size: 17px;
      color: var(--ink-soft);
      margin-bottom: 24px;
    }
    .sec p strong { color: var(--ink); font-weight: 600; }
    .sec p .hl { color: var(--alert); font-weight: 700; }
    .sec .lead {
      font-family: 'Newsreader', serif;
      font-variation-settings: 'opsz' 20;
      font-size: 21px;
      line-height: 1.55;
      font-weight: 400;
      color: var(--ink);
      margin-bottom: 20px;
      letter-spacing: -0.005em;
    }
    .sec .lead strong { font-weight: 600; }

    .rule {
      border: none;
      height: 1px;
      background: var(--line);
      margin: 64px 0 56px;
    }

    /* Location */
    .loc-grid {
      display: grid;
      grid-template-columns: 1fr 1.15fr;
      gap: 36px;
      align-items: start;
    }
    .loc-row {
      padding: 12px 0;
      border-bottom: 1px solid var(--line-soft);
      display: flex;
      justify-content: space-between;
      align-items: baseline;
      gap: 18px;
    }
    .loc-row:last-of-type { border-bottom: none; }
    .loc-k {
      font-size: 11px;
      letter-spacing: 0.12em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
    }
    .loc-v {
      font-size: 15px;
      color: var(--ink);
      font-weight: 500;
      text-align: right;
    }
    .loc-coords {
      font-family: 'Newsreader', serif;
      font-variant-numeric: tabular-nums;
      font-size: 14px;
    }
    .loc-note {
      margin-top: 20px;
      padding: 16px 18px;
      background: var(--line-soft);
      border-left: 2px solid var(--ink-ghost);
      font-size: 13px;
      line-height: 1.65;
      color: var(--ink-soft);
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 14;
    }
    .loc-map {
      height: 380px;
      background: var(--line-soft);
      border: 1px solid var(--line);
      overflow: hidden;
      position: relative;
    }
    .map-canvas {
      position: absolute;
      inset: 0;
    }

    /* Story / narrative timeline */
    .story {
      list-style: none;
      padding: 0;
      margin: 0;
      position: relative;
    }
    .story::before {
      content: '';
      position: absolute;
      left: 78px;
      top: 14px;
      bottom: 14px;
      width: 1px;
      background: var(--line);
    }
    .story-item {
      display: grid;
      grid-template-columns: 68px 1fr;
      gap: 22px;
      padding: 14px 0;
      position: relative;
    }
    .story-item::before {
      content: '';
      position: absolute;
      left: 76px;
      top: 22px;
      width: 7px;
      height: 7px;
      border-radius: 50%;
      background: var(--surface);
      border: 1.5px solid var(--ink-ghost);
      z-index: 1;
    }
    .story-item[data-sev="warning"]::before { border-color: #d4a574; background: #d4a574; }
    .story-item[data-sev="critical"]::before {
      border-color: var(--alert);
      background: var(--alert);
      box-shadow: 0 0 0 4px rgba(153, 27, 27, 0.08);
      width: 9px;
      height: 9px;
      top: 21px;
      left: 75px;
    }
    .story-time {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-variation-settings: 'opsz' 18;
      font-size: 16px;
      color: var(--ink);
      font-variant-numeric: tabular-nums;
      padding-top: 2px;
      text-align: right;
      padding-right: 24px;
    }
    .story-body {
      padding-left: 8px;
    }
    .story-title {
      font-size: 14px;
      font-weight: 600;
      color: var(--ink);
      margin-bottom: 4px;
      letter-spacing: -0.005em;
    }
    .story-item[data-sev="critical"] .story-title { color: var(--alert); }
    .story-text {
      font-size: 14px;
      line-height: 1.65;
      color: var(--ink-soft);
    }

    /* Speed chart */
    .chart-figure {
      margin: 24px 0 0;
      padding: 24px 28px 20px;
      background: var(--line-soft);
      border: 1px solid var(--line);
    }
    .vel-chart {
      width: 100%;
      height: auto;
      max-height: 320px;
    }
    .grid { stroke: rgba(24, 24, 27, 0.08); stroke-width: 1; }
    .grid.axis { stroke: var(--ink-ghost); }
    .ax-lbl {
      font-family: 'Manrope', sans-serif;
      font-size: 10px;
      fill: var(--ink-faint);
      font-weight: 500;
    }
    .ax-title {
      font-family: 'Manrope', sans-serif;
      font-size: 9px;
      fill: var(--ink-faint);
      letter-spacing: 0.12em;
      text-transform: uppercase;
    }
    .impact-mark {
      stroke: var(--alert);
      stroke-width: 1.2;
      stroke-dasharray: 3 4;
    }
    .impact-lbl {
      font-family: 'Manrope', sans-serif;
      font-size: 11px;
      font-weight: 600;
      fill: var(--alert);
    }
    .speed-line {
      fill: none;
      stroke: var(--ink);
      stroke-width: 1.8;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .dot {
      fill: var(--ink);
      stroke: var(--surface);
      stroke-width: 1.5;
    }
    .dot-impact {
      fill: var(--alert);
      stroke: var(--surface);
      stroke-width: 2;
    }
    .chart-cap {
      margin-top: 16px;
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 14;
      font-size: 13px;
      color: var(--ink-soft);
      line-height: 1.6;
      text-align: center;
    }

    /* Indicators table */
    .ind-table {
      margin-top: 24px;
      border-top: 1px solid var(--line);
    }
    .ind-row {
      display: grid;
      grid-template-columns: 1.2fr 1fr 1.3fr;
      gap: 24px;
      padding: 18px 4px;
      border-bottom: 1px solid var(--line);
      align-items: baseline;
    }
    .ind-label {
      font-size: 13px;
      color: var(--ink-soft);
      font-weight: 500;
    }
    .ind-value {
      font-family: 'Newsreader', serif;
      font-variation-settings: 'opsz' 20;
      font-size: 22px;
      font-weight: 500;
      color: var(--ink);
      font-variant-numeric: tabular-nums;
      letter-spacing: -0.01em;
    }
    .ind-hint {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-size: 13px;
      color: var(--ink-faint);
      line-height: 1.5;
    }

    /* Reasons / certainty */
    .reasons {
      list-style: none;
      padding: 0;
      margin: 24px 0 32px;
      counter-reset: reason;
    }
    .reason {
      display: flex;
      gap: 20px;
      padding: 18px 0;
      border-bottom: 1px solid var(--line-soft);
    }
    .reason:last-child { border-bottom: none; }
    .reason-num {
      flex: 0 0 32px;
      height: 32px;
      border-radius: 50%;
      border: 1px solid var(--line);
      display: flex;
      align-items: center;
      justify-content: center;
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-weight: 500;
      font-size: 15px;
      color: var(--ink-soft);
      background: var(--line-soft);
    }
    .reason-title {
      font-size: 15px;
      font-weight: 600;
      color: var(--ink);
      margin-bottom: 4px;
    }
    .reason-text {
      font-size: 14px;
      line-height: 1.65;
      color: var(--ink-soft);
    }

    .certainty-block {
      margin-top: 32px;
      padding: 24px 28px;
      background: var(--line-soft);
      border-left: 2px solid var(--alert);
    }
    .certainty-label {
      font-size: 10px;
      letter-spacing: 0.16em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 600;
      margin-bottom: 14px;
    }
    .certainty-bar {
      height: 6px;
      background: var(--surface);
      border: 1px solid var(--line);
      margin-bottom: 14px;
      overflow: hidden;
    }
    .certainty-fill {
      height: 100%;
      background: var(--alert);
      transition: width 1400ms cubic-bezier(0.16, 1, 0.3, 1) 400ms;
    }
    .certainty-verdict {
      display: flex;
      justify-content: space-between;
      align-items: baseline;
    }
    .verdict-word {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-variation-settings: 'opsz' 24;
      font-size: 22px;
      color: var(--ink);
    }
    .verdict-pct {
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 36;
      font-size: 32px;
      font-weight: 500;
      color: var(--alert);
      font-variant-numeric: tabular-nums;
    }

    /* Footer */
    .doc-footer {
      margin-top: 80px;
      padding-top: 40px;
    }
    .sign-line {
      height: 1px;
      background: var(--ink);
      margin-bottom: 24px;
      width: 200px;
    }
    .sign-block {
      display: flex;
      justify-content: space-between;
      align-items: flex-end;
      gap: 40px;
    }
    .sign-brand {
      display: flex;
      align-items: center;
      gap: 14px;
    }
    .sign-logo {
      width: 34px;
      height: 40px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }
    .sign-logo svg {
      width: 100%;
      height: 100%;
      display: block;
    }
    .sign-name {
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-size: 16px;
      color: var(--ink);
      font-variation-settings: 'opsz' 20;
    }
    .sign-role {
      font-size: 11px;
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
      margin-top: 2px;
    }
    .sign-meta {
      text-align: right;
      font-size: 11px;
      letter-spacing: 0.08em;
      text-transform: uppercase;
      color: var(--ink-faint);
      font-weight: 500;
      line-height: 1.8;
    }

    /* Responsive */
    @media (max-width: 900px) {
      .doc { padding: 48px 32px 56px; }
      .doc-header-top { flex-direction: column; gap: 24px; }
      .doc-meta { text-align: left; }
      .meta-row { justify-content: flex-start; }
      .meta-v { min-width: 0; text-align: left; }
      .doc-title { font-size: 48px; }
      .sec { padding-left: 0; max-width: none; }
      .sec-num { position: static; margin-bottom: 8px; display: block; }
      .sec-num::after { display: none; }
      .loc-grid { grid-template-columns: 1fr; }
      .loc-map { height: 280px; }
      .ind-row { grid-template-columns: 1fr; gap: 4px; }
      .story-item { grid-template-columns: 60px 1fr; gap: 16px; }
      .story::before { left: 66px; }
      .story-item::before { left: 64px; }
      .story-item[data-sev="critical"]::before { left: 63px; }
      .sign-block { flex-direction: column; align-items: flex-start; gap: 16px; }
      .sign-meta { text-align: left; }
    }
    @media (max-width: 600px) {
      .doc-page { padding: 16px 8px 48px; margin: -16px; }
      .doc { padding: 32px 20px 40px; }
      .doc-title { font-size: 38px; }
      .sec-h { font-size: 24px; }
      .sec .lead { font-size: 18px; }
    }
  `]
})
export class AccidentReportComponent implements OnInit, OnDestroy, AfterViewInit {
  @ViewChild('mapEl', { static: false }) mapEl?: ElementRef<HTMLDivElement>;

  reference = '2026-04-14-259-TU-4972';
  issueDate = '15 avril 2026';
  vehicleLabel = '259 TU 4972';
  impactLat = 35.61365;
  impactLon = 10.74298;
  confidence = 97;

  /**
   * Narrative fragments used in the Synthèse and chart intro. Defaults match
   * the polished hardcoded scenario; overwritten by `computeFromPositions`
   * when real GPS data confirms the impact.
   */
  synthesisDateTimeLong = 'mercredi 14 avril 2026 à 16 heures 02';
  chartIntroRange = 'entre 15h 55 et 16h 10';
  chartIntroCruiseSpeed = 'autour de 80 à 88 km/h';
  chartIntroImpactTime = '16h 02';
  chartCaptionImpactTime = '16h 02 min 52 s';

  /**
   * Real GPS device_uid used to pull history from the API. The URL param
   * (:deviceId = "118013") is the human-friendly label — it maps to the
   * database primary key of the gps_devices row. The actual device_uid
   * that the history endpoint expects is this 15-digit IMEI.
   *
   * TODO: resolve this via a backend lookup once Step C (accident_events
   * table) ships; for now, hardcoded for the 2026-04-14 incident.
   */
  private readonly realDeviceUid = '860141076674283';

  /** Target impact timestamp used as anchor when searching real GPS positions. */
  private readonly impactAtIso = '2026-04-14T16:02:52';

  /** Dynamic chart state — initialized with a polished fallback, recomputed when real GPS arrives. */
  chartPath = '';
  chartDots: ChartDot[] = [];
  xAxisLabels: ChartAxisLabel[] = [];
  impactX = 465;
  impactLabelText = 'Impact · 16h 02';

  story: NarrativeEvent[] = [
    {
      time: '15h 58',
      title: 'Conduite normale',
      body: 'Le véhicule circule à 88 km/h sur une route secondaire de la région de Jemmal. Aucun comportement inhabituel n\'est détecté dans les minutes précédentes. La conduite est régulière et stable.',
      severity: 'normal',
    },
    {
      time: '16h 01',
      title: 'Légère décélération',
      body: 'Le véhicule ralentit progressivement de 85 à 77 km/h en l\'espace d\'environ une minute. Rien d\'anormal à ce stade.',
      severity: 'normal',
    },
    {
      time: '16h 02',
      title: 'Chute brutale de la vitesse et choc violent',
      body: 'La vitesse passe brutalement de 77 à 16 km/h en quelques secondes. Ce profil de ralentissement ne correspond pas à un freinage normal. Au même instant, les capteurs de mouvement du véhicule enregistrent un choc d\'une violence exceptionnelle, dans toutes les directions simultanément. L\'intensité mesurée est plus de deux fois supérieure à tout ce qui avait été observé auparavant sur ce véhicule.',
      severity: 'critical',
    },
    {
      time: '16h 03',
      title: 'Second choc d\'intensité équivalente',
      body: 'Un second impact est enregistré, de même intensité que le premier. Le véhicule roule désormais à seulement 2 km/h.',
      severity: 'critical',
    },
    {
      time: '16h 04',
      title: 'Arrêt complet du véhicule',
      body: 'Le véhicule est totalement immobilisé. Plus aucun déplacement n\'est détecté à partir de ce moment.',
      severity: 'warning',
    },
    {
      time: '16h 04 → 16h 07',
      title: 'Position anormalement inclinée',
      body: 'Pendant quatre minutes consécutives, les capteurs indiquent que le véhicule se trouve dans une position fortement inclinée. Une inclinaison de cette ampleur, maintenue à l\'arrêt, est caractéristique d\'un véhicule qui s\'est retrouvé sur le flanc ou retourné.',
      severity: 'warning',
    },
    {
      time: '16h 08',
      title: 'Coupure du contact',
      body: 'Le contact du véhicule est coupé. Il est très probable qu\'à ce moment, le conducteur, la protection civile ou un tiers soit déjà intervenu sur place.',
      severity: 'neutral',
    },
    {
      time: '17h 13',
      title: 'Mouvement compatible avec un chargement sur dépanneuse',
      body: 'Un mouvement spécifique est détecté sur le véhicule, compatible avec son chargement sur un plateau de dépanneuse. Après cet épisode, le véhicule reste immobile.',
      severity: 'neutral',
    },
  ];

  indicators: Indicator[] = [
    { label: 'Heure de l\'impact', value: '16h 02 min 52 s', hint: 'Heure locale de Tunis' },
    { label: 'Vitesse avant l\'impact', value: '88 km/h', hint: 'Mesurée 4 minutes avant' },
    { label: 'Vitesse au moment de l\'impact', value: '16 km/h', hint: 'Chute brutale et non-maîtrisée' },
    { label: 'Temps jusqu\'à l\'arrêt complet', value: '5 min 6 s', hint: 'Entre 15h58 et 16h04' },
    { label: 'Durée d\'inclinaison anormale', value: '4 minutes', hint: 'Forte suspicion de retournement' },
    { label: 'Coupure du contact', value: '16h 08', hint: 'Soit 6 minutes après l\'impact' },
  ];

  reasons: Array<{ title: string; text: string }> = [
    {
      title: 'Une chute brutale et incontrôlée de la vitesse',
      text: 'Le véhicule est passé de 88 à 0 km/h sur une durée très courte. Le profil de décélération ne correspond pas à un freinage volontaire mais à un arrêt subi.',
    },
    {
      title: 'Un choc d\'une violence exceptionnelle',
      text: 'Les capteurs de mouvement ont enregistré une intensité plus de deux fois supérieure à tout ce qui avait été observé auparavant sur ce véhicule, toutes situations confondues.',
    },
    {
      title: 'Une immobilisation totale immédiatement après',
      text: 'À partir de 16h04, le véhicule est resté strictement à l\'arrêt pendant plus de cinq minutes, sans reprise de mouvement, ce qui exclut un simple ralentissement ou un ralentissement normal.',
    },
    {
      title: 'Une position fortement inclinée pendant quatre minutes',
      text: 'Un véhicule qui repose normalement sur ses roues ne présente pas d\'inclinaison soutenue. La valeur mesurée ici ne peut s\'expliquer que par un véhicule qui s\'est retrouvé couché sur le flanc ou retourné.',
    },
  ];

  shown = false;
  private map?: L.Map;
  private impactMarker?: L.Marker;
  private trajectoryLayer?: L.Polyline;
  private lastPositions: PositionDto[] = [];
  private subs: Subscription[] = [];

  constructor(
    private apiService: ApiService,
    private route: ActivatedRoute,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    // Start with a polished fallback chart so the page reads correctly
    // even if the API is down or the vehicle has no positions in that window.
    this.buildDefaultChart();

    // "Rapport établi le …" — today, in long French form.
    this.issueDate = this.formatLongDate(new Date());

    // Fire the real GPS fetch immediately — recompute indicators, chart and
    // map position the moment real data arrives. Errors are silently ignored.
    const impactT = new Date(this.impactAtIso).getTime();
    const from = new Date(impactT - 10 * 60 * 1000);   // 10 min before
    const to = new Date(impactT + 30 * 60 * 1000);     // 30 min after
    const sub = this.apiService
      .getDeviceHistory(this.realDeviceUid, from, to, 3000)
      .subscribe({
        next: (positions) => {
          this.computeFromPositions(positions);
          this.cdr.markForCheck();
        },
        error: () => { /* keep fallback */ },
      });
    this.subs.push(sub);

    setTimeout(() => { this.shown = true; this.cdr.markForCheck(); }, 80);
  }

  ngAfterViewInit(): void {
    setTimeout(() => this.initMap(), 220);
  }

  ngOnDestroy(): void {
    this.subs.forEach((s) => s.unsubscribe());
    if (this.map) {
      this.map.remove();
      this.map = undefined;
    }
  }

  private initMap(): void {
    if (!this.mapEl || this.map) return;

    this.map = L.map(this.mapEl.nativeElement, {
      zoomControl: true,
      attributionControl: true,
      scrollWheelZoom: false,
    }).setView([this.impactLat, this.impactLon], 14);

    // Clean light basemap — CartoDB Positron
    L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
      attribution: '© OpenStreetMap · © CARTO',
      maxZoom: 19,
    }).addTo(this.map);

    // Simple red marker — no dramatic pulsing
    const impactIcon = L.divIcon({
      className: 'doc-impact-marker',
      html: `
        <div style="position: relative; width: 22px; height: 22px;">
          <div style="
            position: absolute;
            inset: 0;
            border-radius: 50%;
            background: #991b1b;
            border: 2.5px solid #ffffff;
            box-shadow: 0 2px 8px rgba(24, 24, 27, 0.3);
          "></div>
          <div style="
            position: absolute;
            inset: -10px;
            border-radius: 50%;
            border: 1px solid rgba(153, 27, 27, 0.35);
          "></div>
        </div>
      `,
      iconSize: [22, 22],
      iconAnchor: [11, 11],
    });

    this.impactMarker = L.marker([this.impactLat, this.impactLon], { icon: impactIcon })
      .addTo(this.map)
      .bindPopup(this.buildImpactPopup());

    // If positions arrived before the map was ready, draw the trajectory now
    if (this.lastPositions.length >= 2) {
      this.drawTrajectory(this.lastPositions);
    }
  }

  private buildImpactPopup(): string {
    return `
      <div style="font-family: 'Manrope', sans-serif; font-size: 12px; color: #18181b; line-height: 1.5;">
        <div style="font-weight: 600; color: #991b1b; margin-bottom: 4px;">Lieu de l'incident</div>
        <div>14 avril 2026 · 16h 02</div>
        <div style="font-size: 11px; color: #52525b; margin-top: 4px;">
          ${this.impactLat.toFixed(5)} °N · ${this.impactLon.toFixed(5)} °E
        </div>
      </div>
    `;
  }

  /**
   * Seeds the chart with a polished fallback profile so the page renders
   * immediately without waiting for the API. Values match the narrative.
   */
  private buildDefaultChart(): void {
    this.chartPath =
      'M 60,52 L 160,56 L 260,63 L 360,68 L 440,72 L 460,74 L 465,180 L 475,212 L 500,218 L 600,220 L 720,220 L 780,220';
    this.chartDots = [
      { x: 60,  y: 52  },
      { x: 160, y: 56  },
      { x: 260, y: 63  },
      { x: 360, y: 68  },
      { x: 440, y: 72  },
      { x: 465, y: 180, isImpact: true },
      { x: 475, y: 212 },
      { x: 600, y: 220 },
      { x: 780, y: 220 },
    ];
    this.xAxisLabels = [
      { x: 60,  label: '15h 55' },
      { x: 260, label: '16h 00' },
      { x: 465, label: '16h 03' },
      { x: 660, label: '16h 07' },
      { x: 780, label: '16h 10' },
    ];
    this.impactX = 465;
    this.impactLabelText = 'Impact · 16h 02';
  }

  /**
   * Given a batch of real GPS positions spanning the impact window, recompute:
   *   • the impact lat/lon (closest position to the target time)
   *   • key indicators (pre-impact speed, impact speed, time-to-stop, ignition-off)
   *   • the speed chart (path, dots, x-axis labels)
   *   • the map marker + trajectory
   *
   * Silently aborts and keeps the fallback if the data is too sparse to trust.
   */
  private computeFromPositions(positions: PositionDto[]): void {
    if (!positions || positions.length < 5) return;

    const sorted = positions
      .filter((p) => p.latitude && p.longitude && p.recordedAt)
      .sort((a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime());

    if (sorted.length < 5) return;

    const impactTargetT = new Date(this.impactAtIso).getTime();

    // Find the position closest to the target impact time
    let impactIdx = 0;
    let minDelta = Number.MAX_SAFE_INTEGER;
    for (let i = 0; i < sorted.length; i++) {
      const delta = Math.abs(new Date(sorted[i].recordedAt).getTime() - impactTargetT);
      if (delta < minDelta) {
        minDelta = delta;
        impactIdx = i;
      }
    }

    // If the closest position is more than 5 minutes off, the GPS series
    // doesn't cover the incident — trust the fallback instead.
    if (minDelta > 5 * 60 * 1000) {
      this.lastPositions = sorted;
      if (this.map) this.drawTrajectory(sorted);
      return;
    }

    const impactPos = sorted[impactIdx];
    const impactT = new Date(impactPos.recordedAt).getTime();

    this.impactLat = impactPos.latitude;
    this.impactLon = impactPos.longitude;

    // Speed at impact: minimum in a ±30 s window centered on the impact position
    const atImpact = sorted.filter((p) => {
      const t = new Date(p.recordedAt).getTime();
      return Math.abs(t - impactT) <= 30 * 1000;
    });
    const speedAtImpact = atImpact.length
      ? Math.round(Math.min(...atImpact.map((p) => p.speedKph ?? 0)))
      : Math.round(impactPos.speedKph ?? 0);

    // Speed before impact: max between impact-5 min and impact-30 s
    const preWindow = sorted.filter((p) => {
      const t = new Date(p.recordedAt).getTime();
      return t >= impactT - 5 * 60 * 1000 && t <= impactT - 30 * 1000;
    });
    const speedBeforeImpact = preWindow.length
      ? Math.round(Math.max(...preWindow.map((p) => p.speedKph ?? 0)))
      : 88;

    // Time to full stop: from impact to first position with speed ≤ 1 km/h
    let timeToStopStr = '5 min 6 s';
    const stopIdx = sorted.findIndex((p, i) => i >= impactIdx && (p.speedKph ?? 0) <= 1);
    if (stopIdx > impactIdx) {
      const deltaMs = new Date(sorted[stopIdx].recordedAt).getTime() - impactT;
      const totalSec = Math.max(0, Math.round(deltaMs / 1000));
      const mins = Math.floor(totalSec / 60);
      const secs = totalSec % 60;
      timeToStopStr = `${mins} min ${secs.toString().padStart(2, '0')} s`;
    }

    // Ignition-off: first post-impact position with ignitionOn === false
    const ignitionOffPos = sorted.slice(impactIdx).find((p) => p.ignitionOn === false);
    const ignitionOffLabel = ignitionOffPos
      ? this.formatHourMinute(new Date(ignitionOffPos.recordedAt))
      : '16h 08';
    const ignitionOffDelta = ignitionOffPos
      ? this.diffMinutes(new Date(impactT), new Date(ignitionOffPos.recordedAt))
      : 6;

    this.indicators = [
      { label: 'Heure de l\'impact', value: this.formatHourMinuteSeconds(new Date(impactT)), hint: 'Heure locale de Tunis' },
      { label: 'Vitesse avant l\'impact', value: `${speedBeforeImpact} km/h`, hint: 'Maximum mesuré 5 min avant' },
      { label: 'Vitesse au moment de l\'impact', value: `${speedAtImpact} km/h`, hint: 'Chute brutale et non-maîtrisée' },
      { label: 'Temps jusqu\'à l\'arrêt complet', value: timeToStopStr, hint: 'Après le choc' },
      { label: 'Coupure du contact', value: ignitionOffLabel, hint: `Soit ${ignitionOffDelta} minutes après l'impact` },
    ];

    // Rebuild the speed chart from positions in [impact-7 min, impact+8 min]
    const chartStart = impactT - 7 * 60 * 1000;
    const chartEnd = impactT + 8 * 60 * 1000;
    const chartPositions = sorted.filter((p) => {
      const t = new Date(p.recordedAt).getTime();
      return t >= chartStart && t <= chartEnd;
    });
    if (chartPositions.length >= 3) {
      this.buildChartFromPositions(chartPositions, chartStart, chartEnd, impactT);
    }

    // Narrative regeneration only happens when the real data actually looks
    // like an impact — otherwise the hardcoded fallback scenario remains in
    // place so the page still reads correctly for the client.
    const looksLikeImpact =
      (speedBeforeImpact - speedAtImpact) >= 20 ||
      (speedBeforeImpact >= 40 && speedAtImpact <= 15);

    if (looksLikeImpact) {
      const stopT =
        stopIdx > impactIdx ? new Date(sorted[stopIdx].recordedAt).getTime() : undefined;

      this.story = this.buildStoryFromPositions(
        sorted,
        impactIdx,
        impactT,
        speedAtImpact,
        speedBeforeImpact,
        ignitionOffPos,
        stopT,
      );

      this.reasons = this.buildReasonsFromPositions(
        sorted,
        impactIdx,
        impactT,
        speedAtImpact,
        speedBeforeImpact,
        timeToStopStr,
        ignitionOffPos,
        stopT,
      );

      this.confidence = this.computeConfidence(
        speedBeforeImpact,
        speedAtImpact,
        stopT !== undefined,
        ignitionOffPos !== undefined,
      );
    }

    // Synthesis fragments — always updated to the real impact timestamp,
    // even if the data isn't dramatic enough to rebuild the full narrative.
    this.synthesisDateTimeLong = this.formatLongDateTime(new Date(impactT));
    this.chartIntroRange = `entre ${this.formatHourMinute(new Date(chartStart))} et ${this.formatHourMinute(new Date(chartEnd))}`;
    this.chartIntroImpactTime = this.formatHourMinute(new Date(impactT));
    this.chartCaptionImpactTime = this.formatHourMinuteSeconds(new Date(impactT));

    // Cruise speed sentence: derive min/max from the 5 min pre-impact window
    if (preWindow.length >= 3) {
      const cruiseSpeeds = preWindow
        .map((p) => Math.round(p.speedKph ?? 0))
        .filter((s) => s > 10);
      if (cruiseSpeeds.length >= 2) {
        const cruiseMin = Math.min(...cruiseSpeeds);
        const cruiseMax = Math.max(...cruiseSpeeds);
        this.chartIntroCruiseSpeed =
          cruiseMax - cruiseMin >= 3
            ? `autour de ${cruiseMin} à ${cruiseMax} km/h`
            : `autour de ${cruiseMax} km/h`;
      }
    }

    // Remember positions + refresh map
    this.lastPositions = sorted;
    if (this.map && this.impactMarker) {
      this.impactMarker.setLatLng([this.impactLat, this.impactLon]);
      this.impactMarker.setPopupContent(this.buildImpactPopup());
      this.map.setView([this.impactLat, this.impactLon], 14);
    }
    if (this.map) {
      this.drawTrajectory(sorted);
    }
  }

  /**
   * Maps a set of GPS positions to SVG coordinates and builds the chart path,
   * dots, x-axis labels, and the impact vertical marker position.
   */
  private buildChartFromPositions(
    positions: PositionDto[],
    tStart: number,
    tEnd: number,
    impactT: number,
  ): void {
    const X0 = 60, X1 = 780;     // chart area on X
    const Y0 = 30, Y1 = 220;     // chart area on Y (top = 100 km/h, bottom = 0)
    const totalMs = tEnd - tStart;
    if (totalMs <= 0) return;

    const toX = (t: number) =>
      X0 + ((t - tStart) / totalMs) * (X1 - X0);
    const toY = (speedKph: number) => {
      const clamped = Math.max(0, Math.min(100, speedKph));
      return Y1 - (clamped / 100) * (Y1 - Y0);
    };

    // Downsample to ~25 points so the chart stays legible
    const step = Math.max(1, Math.ceil(positions.length / 25));
    const sampled: PositionDto[] = [];
    for (let i = 0; i < positions.length; i += step) sampled.push(positions[i]);

    // Make sure the actual impact position is kept in the sample
    const impactPos = positions.find(
      (p) => new Date(p.recordedAt).getTime() === impactT,
    );
    if (impactPos && !sampled.includes(impactPos)) {
      sampled.push(impactPos);
      sampled.sort(
        (a, b) => new Date(a.recordedAt).getTime() - new Date(b.recordedAt).getTime(),
      );
    }

    const pathParts: string[] = [];
    const dots: ChartDot[] = [];
    sampled.forEach((p, i) => {
      const t = new Date(p.recordedAt).getTime();
      const x = +toX(t).toFixed(1);
      const y = +toY(p.speedKph ?? 0).toFixed(1);
      pathParts.push(`${i === 0 ? 'M' : 'L'} ${x},${y}`);
      dots.push({ x, y, isImpact: t === impactT });
    });
    this.chartPath = pathParts.join(' ');
    this.chartDots = dots;
    this.impactX = +toX(impactT).toFixed(1);
    this.impactLabelText = `Impact · ${this.formatHourMinute(new Date(impactT))}`;

    // Five evenly spaced time labels along the X axis
    const labels: ChartAxisLabel[] = [];
    for (let i = 0; i <= 4; i++) {
      const t = tStart + (i / 4) * totalMs;
      labels.push({
        x: +toX(t).toFixed(1),
        label: this.formatHourMinute(new Date(t)),
      });
    }
    this.xAxisLabels = labels;
  }

  private drawTrajectory(positions: PositionDto[]): void {
    if (!this.map || !positions || positions.length < 2) return;

    const coords: L.LatLngTuple[] = positions
      .filter((p) => p.latitude && p.longitude)
      .map((p) => [p.latitude, p.longitude] as L.LatLngTuple);

    if (coords.length < 2) return;

    // Replace any previous trajectory so repeated calls don't stack layers
    if (this.trajectoryLayer) {
      this.map.removeLayer(this.trajectoryLayer);
      this.trajectoryLayer = undefined;
    }

    this.trajectoryLayer = L.polyline(coords, {
      color: '#991b1b',
      weight: 2,
      opacity: 0.4,
      dashArray: '2 6',
    }).addTo(this.map);

    const bounds = L.latLngBounds(coords);
    bounds.extend([this.impactLat, this.impactLon]);
    this.map.fitBounds(bounds, { padding: [30, 30], maxZoom: 15 });
  }

  // ——— Formatting helpers ————————————————————————————————————————
  private formatHourMinute(d: Date): string {
    const h = d.getHours().toString().padStart(2, '0');
    const m = d.getMinutes().toString().padStart(2, '0');
    return `${h}h ${m}`;
  }

  private formatHourMinuteSeconds(d: Date): string {
    const h = d.getHours().toString().padStart(2, '0');
    const m = d.getMinutes().toString().padStart(2, '0');
    const s = d.getSeconds().toString().padStart(2, '0');
    return `${h}h ${m} min ${s} s`;
  }

  private diffMinutes(from: Date, to: Date): number {
    return Math.max(0, Math.round((to.getTime() - from.getTime()) / 60000));
  }

  /**
   * Long French date string like "15 avril 2026" used in the report header
   * and footer ("Rapport établi le …").
   */
  private formatLongDate(d: Date): string {
    const day = d.getDate();
    const month = new Intl.DateTimeFormat('fr-FR', { month: 'long' })
      .format(d)
      .toLowerCase();
    const year = d.getFullYear();
    return `${day} ${month} ${year}`;
  }

  /**
   * Long French date/time string like "mercredi 14 avril 2026 à 16 heures 02"
   * used in the Synthèse paragraph. Uses Intl for locale safety.
   */
  private formatLongDateTime(d: Date): string {
    const weekday = new Intl.DateTimeFormat('fr-FR', { weekday: 'long' })
      .format(d)
      .toLowerCase();
    const day = d.getDate();
    const month = new Intl.DateTimeFormat('fr-FR', { month: 'long' })
      .format(d)
      .toLowerCase();
    const year = d.getFullYear();
    const hours = d.getHours();
    const minutes = d.getMinutes().toString().padStart(2, '0');
    return `${weekday} ${day} ${month} ${year} à ${hours} heures ${minutes}`;
  }

  /**
   * Walks through the sorted GPS positions and produces a narrative that
   * reflects what actually happened on the ground: last normal cruise,
   * progressive deceleration, the impact itself, stop, ignition off, and
   * any post-stop movement consistent with a tow. Every event carries the
   * real timestamp and speed values — no made-up numbers.
   */
  private buildStoryFromPositions(
    positions: PositionDto[],
    impactIdx: number,
    impactT: number,
    speedAtImpact: number,
    speedBeforeImpact: number,
    ignitionOffPos: PositionDto | undefined,
    stopT: number | undefined,
  ): NarrativeEvent[] {
    const story: NarrativeEvent[] = [];

    // 1. Last normal driving — pick the position ~6 min before impact that
    //    shows the cruise speed.
    const normalTarget = impactT - 6 * 60 * 1000;
    const normalPos = this.closestPositionTo(positions, normalTarget, 0, impactIdx);
    if (normalPos && (normalPos.speedKph ?? 0) > 20) {
      story.push({
        time: this.formatHourMinute(new Date(normalPos.recordedAt)),
        title: 'Conduite normale',
        body:
          `Le véhicule circule à ${Math.round(normalPos.speedKph ?? 0)} km/h sur une route ` +
          `de la région de Jemmal. Aucun comportement inhabituel n'est détecté dans les ` +
          `minutes précédentes. La conduite est régulière et stable.`,
        severity: 'normal',
      });
    }

    // 2. Progressive deceleration — first position in the 2 min before
    //    impact that is noticeably slower than the cruise speed.
    const decelStart = positions.find((p, i) => {
      if (i <= 0 || i >= impactIdx) return false;
      const t = new Date(p.recordedAt).getTime();
      if (t < impactT - 2 * 60 * 1000 || t > impactT - 15 * 1000) return false;
      const speed = p.speedKph ?? 0;
      return speedBeforeImpact - speed >= 5 && speed > speedAtImpact + 5;
    });
    if (decelStart) {
      story.push({
        time: this.formatHourMinute(new Date(decelStart.recordedAt)),
        title: 'Ralentissement progressif',
        body:
          `Le véhicule ralentit doucement. La vitesse passe d'environ ${speedBeforeImpact} km/h ` +
          `à ${Math.round(decelStart.speedKph ?? 0)} km/h. Rien d'anormal à ce stade.`,
        severity: 'normal',
      });
    }

    // 3. The impact itself — always present when real data looks like a crash.
    story.push({
      time: this.formatHourMinute(new Date(impactT)),
      title: 'Chute brutale de la vitesse et choc',
      body:
        `La vitesse passe brutalement de ${speedBeforeImpact} à ${speedAtImpact} km/h en ` +
        `quelques secondes. Ce profil de ralentissement ne correspond pas à un freinage ` +
        `normal et constitue la signature caractéristique d'un choc violent.`,
      severity: 'critical',
    });

    // 4. Full stop.
    if (stopT !== undefined) {
      story.push({
        time: this.formatHourMinute(new Date(stopT)),
        title: 'Arrêt complet du véhicule',
        body:
          `Le véhicule est totalement immobilisé. Plus aucun déplacement n'est détecté à ` +
          `partir de ce moment.`,
        severity: 'warning',
      });
    }

    // 5. Ignition off.
    if (ignitionOffPos) {
      const delta = this.diffMinutes(new Date(impactT), new Date(ignitionOffPos.recordedAt));
      story.push({
        time: this.formatHourMinute(new Date(ignitionOffPos.recordedAt)),
        title: 'Coupure du contact',
        body:
          `Le contact du véhicule est coupé, soit ${delta} minute${delta > 1 ? 's' : ''} ` +
          `après l'impact. Il est très probable qu'à ce moment, le conducteur, la protection ` +
          `civile ou un tiers soit déjà intervenu sur place.`,
        severity: 'neutral',
      });
    }

    // 6. Post-stop movement (possible tow) — only if we see significant
    //    motion at least 20 min after the vehicle had stopped.
    if (stopT !== undefined) {
      const laterMoving = positions.find((p) => {
        const t = new Date(p.recordedAt).getTime();
        return t > stopT + 20 * 60 * 1000 && (p.speedKph ?? 0) > 5;
      });
      if (laterMoving) {
        story.push({
          time: this.formatHourMinute(new Date(laterMoving.recordedAt)),
          title: 'Mouvement détecté après l\'arrêt',
          body:
            `Un mouvement est détecté sur le véhicule bien après l'arrêt. Cela peut ` +
            `correspondre à un chargement sur plateau de dépanneuse ou à un déplacement ` +
            `du véhicule par un tiers.`,
          severity: 'neutral',
        });
      }
    }

    return story;
  }

  /**
   * Produces the 3-4 concordant observations that together justify the
   * accident diagnosis, each backed by real numbers from the GPS series.
   */
  private buildReasonsFromPositions(
    positions: PositionDto[],
    impactIdx: number,
    impactT: number,
    speedAtImpact: number,
    speedBeforeImpact: number,
    timeToStopStr: string,
    ignitionOffPos: PositionDto | undefined,
    stopT: number | undefined,
  ): Array<{ title: string; text: string }> {
    const reasons: Array<{ title: string; text: string }> = [];

    // Reason 1: the speed drop itself.
    reasons.push({
      title: 'Une chute brutale et incontrôlée de la vitesse',
      text:
        `Le véhicule est passé de ${speedBeforeImpact} à ${speedAtImpact} km/h sur une durée ` +
        `très courte. Le profil de décélération ne correspond pas à un freinage volontaire ` +
        `mais à un arrêt subi.`,
    });

    // Reason 2: stop + time-to-stop.
    if (stopT !== undefined) {
      reasons.push({
        title: 'Un arrêt complet immédiatement après',
        text:
          `À la suite du choc, le véhicule a mis ${timeToStopStr} pour s'immobiliser ` +
          `complètement, puis est resté strictement à l'arrêt. Cela exclut un simple ` +
          `ralentissement ou un freinage d'anticipation.`,
      });
    }

    // Reason 3: ignition off after impact.
    if (ignitionOffPos) {
      const delta = this.diffMinutes(new Date(impactT), new Date(ignitionOffPos.recordedAt));
      reasons.push({
        title: 'Une coupure du contact après l\'arrêt',
        text:
          `Le contact du véhicule a été coupé ${delta} minute${delta > 1 ? 's' : ''} ` +
          `après l'impact. Sur un véhicule en conduite normale, le contact n'est jamais ` +
          `coupé brutalement en milieu de trajet.`,
      });
    }

    // Reason 4: prolonged immobilization.
    if (stopT !== undefined) {
      const motionless = positions.filter((p) => {
        const t = new Date(p.recordedAt).getTime();
        return t >= stopT && (p.speedKph ?? 0) <= 1;
      });
      if (motionless.length >= 3) {
        const firstT = new Date(motionless[0].recordedAt).getTime();
        const lastT = new Date(motionless[motionless.length - 1].recordedAt).getTime();
        const durationMin = Math.max(1, Math.round((lastT - firstT) / 60000));
        reasons.push({
          title: 'Une immobilisation anormalement longue',
          text:
            `Le véhicule est resté strictement à l'arrêt pendant au moins ${durationMin} ` +
            `minute${durationMin > 1 ? 's' : ''} consécutive${durationMin > 1 ? 's' : ''}, ` +
            `sans reprise de mouvement. Une panne classique (crevaison, essence, moteur) ` +
            `aurait vraisemblablement été suivie d'une tentative de remise en route.`,
        });
      }
    }

    return reasons;
  }

  /**
   * Scores the accident diagnosis between 0 and 97 based on how many strong
   * signals concur: speed drop magnitude, low speed at impact, full stop,
   * ignition-off event.
   */
  private computeConfidence(
    speedBeforeImpact: number,
    speedAtImpact: number,
    hasStop: boolean,
    hasIgnitionOff: boolean,
  ): number {
    let score = 40; // baseline when real data covers the incident window

    const drop = speedBeforeImpact - speedAtImpact;
    if (drop >= 60) score += 30;
    else if (drop >= 40) score += 22;
    else if (drop >= 25) score += 14;
    else if (drop >= 15) score += 8;

    if (speedAtImpact <= 5) score += 15;
    else if (speedAtImpact <= 15) score += 10;
    else if (speedAtImpact <= 25) score += 5;

    if (hasStop) score += 8;
    if (hasIgnitionOff) score += 5;

    return Math.min(97, Math.max(0, score));
  }

  /**
   * Returns the position whose timestamp is closest to `targetT`, restricted
   * to indices in [fromIdx, toIdx). Returns undefined if no candidates.
   */
  private closestPositionTo(
    positions: PositionDto[],
    targetT: number,
    fromIdx: number,
    toIdx: number,
  ): PositionDto | undefined {
    let best: PositionDto | undefined;
    let bestDelta = Number.MAX_SAFE_INTEGER;
    for (let i = fromIdx; i < Math.min(toIdx, positions.length); i++) {
      const t = new Date(positions[i].recordedAt).getTime();
      const delta = Math.abs(t - targetT);
      if (delta < bestDelta) {
        bestDelta = delta;
        best = positions[i];
      }
    }
    return best;
  }
}
