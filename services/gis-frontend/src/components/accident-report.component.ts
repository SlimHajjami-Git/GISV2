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
                <div class="brand-mark">C</div>
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
              <strong>mercredi 14 avril 2026 à 16 heures 02</strong>, sur la commune de
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
              Le graphique ci-dessous représente la vitesse du véhicule entre 15h55 et 16h10.
              On observe une conduite stable autour de 80&ndash;88 km/h, suivie d'une
              <strong>chute brutale</strong> à 16h02 qui ne correspond pas à un freinage
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
                <line x1="465" y1="30" x2="465" y2="220" class="impact-mark"/>
                <text x="472" y="45" class="impact-lbl">Impact · 16h 02</text>

                <!-- Speed line -->
                <path
                  d="M 60,52 L 160,56 L 260,63 L 360,68 L 440,72 L 460,74 L 465,180 L 475,212 L 500,218 L 600,220 L 720,220 L 780,220"
                  class="speed-line"
                />

                <!-- Dots -->
                <circle cx="60"  cy="52"  r="3" class="dot"/>
                <circle cx="160" cy="56"  r="3" class="dot"/>
                <circle cx="260" cy="63"  r="3" class="dot"/>
                <circle cx="360" cy="68"  r="3" class="dot"/>
                <circle cx="440" cy="72"  r="3" class="dot"/>
                <circle cx="465" cy="180" r="4.5" class="dot-impact"/>
                <circle cx="475" cy="212" r="3" class="dot"/>
                <circle cx="600" cy="220" r="3" class="dot"/>
                <circle cx="780" cy="220" r="3" class="dot"/>

                <!-- X axis labels -->
                <text x="60"  y="245" class="ax-lbl">15h 55</text>
                <text x="260" y="245" class="ax-lbl">16h 00</text>
                <text x="465" y="245" class="ax-lbl" text-anchor="middle">16h 03</text>
                <text x="660" y="245" class="ax-lbl" text-anchor="middle">16h 07</text>
                <text x="780" y="245" class="ax-lbl" text-anchor="end">16h 10</text>
              </svg>
              <figcaption class="chart-cap">
                Profil de vitesse du véhicule — le trait pointillé rouge marque le moment de
                l'impact (16h 02 min 52 s).
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

          <!-- Explication transparente -->
          <section class="sec sec-why">
            <div class="sec-num">07</div>
            <h2 class="sec-h">Pourquoi vous n'avez pas été prévenu au moment des faits</h2>

            <p>
              Nous souhaitons être pleinement transparents avec vous sur ce point, car nous
              comprenons votre frustration.
            </p>

            <p>
              Le boîtier GPS installé sur ce véhicule appartient à une génération de matériel
              qui <strong>ne dispose pas d'une fonction d'alerte d'accident intégrée</strong>.
              Ce type de boîtier enregistre en permanence la position, la vitesse et les
              mouvements du véhicule, mais il ne possède pas, à l'intérieur de son
              électronique, de dispositif capable de reconnaître un choc et d'envoyer une
              alerte instantanée à notre plateforme.
            </p>

            <p>
              C'est la raison pour laquelle <strong>aucune notification automatique ne vous a
              été adressée au moment où l'accident s'est produit</strong>. La plateforme
              fonctionnait normalement, les données du véhicule étaient bien reçues, mais
              aucun mécanisme n'était en place pour interpréter ces données en temps réel et
              en tirer une alerte.
            </p>

            <div class="apology">
              Nous sommes sincèrement désolés de cette limite et de la situation dans laquelle
              elle vous a placé.
            </div>
          </section>

          <hr class="rule"/>

          <!-- Engagement -->
          <section class="sec sec-commit">
            <div class="sec-num">08</div>
            <h2 class="sec-h">Ce que nous mettons en place</h2>

            <p>
              Nos équipes déploient actuellement un nouveau système de détection d'accidents
              qui fonctionnera <strong>côté serveur</strong>, indépendamment du matériel
              embarqué dans les véhicules. Concrètement, ce système analysera les données
              envoyées par l'ensemble de votre flotte, en temps réel, et reconnaîtra
              automatiquement les situations compatibles avec un choc violent.
            </p>

            <ul class="commit-list">
              <li><strong>Analyse en temps réel</strong> des données envoyées par chaque véhicule, en continu.</li>
              <li><strong>Détection automatique</strong> de tout profil correspondant à un choc violent ou à un retournement.</li>
              <li><strong>Notification immédiate</strong> sur la plateforme, par email, et à terme par SMS, aux personnes désignées.</li>
              <li><strong>Couverture totale</strong> de votre flotte — y compris les véhicules équipés de boîtiers anciens, comme celui concerné par le présent rapport.</li>
            </ul>

            <p class="commit-deadline">
              Le déploiement est en cours et sera opérationnel dans les prochains jours.
            </p>
          </section>

          <!-- Signature / footer -->
          <footer class="doc-footer">
            <div class="sign-line"></div>
            <div class="sign-block">
              <div class="sign-brand">
                <div class="sign-logo">C</div>
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
      height: 44px;
      background: var(--ink);
      color: var(--paper);
      font-family: 'Newsreader', serif;
      font-weight: 500;
      font-size: 26px;
      font-style: italic;
      display: flex;
      align-items: center;
      justify-content: center;
      font-variation-settings: 'opsz' 36;
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

    /* Why not notified - apology block */
    .apology {
      margin-top: 20px;
      padding: 20px 24px;
      background: var(--alert-bg);
      border-left: 2px solid var(--alert);
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 18;
      font-size: 17px;
      line-height: 1.55;
      color: var(--ink);
    }

    /* Commitment list */
    .commit-list {
      list-style: none;
      padding: 0;
      margin: 24px 0;
    }
    .commit-list li {
      position: relative;
      padding: 14px 0 14px 36px;
      border-bottom: 1px solid var(--line-soft);
      font-size: 15px;
      line-height: 1.65;
      color: var(--ink);
    }
    .commit-list li:last-child { border-bottom: none; }
    .commit-list li::before {
      content: '';
      position: absolute;
      left: 4px;
      top: 22px;
      width: 18px;
      height: 1px;
      background: var(--alert);
    }
    .commit-list strong { font-weight: 600; }
    .commit-deadline {
      margin-top: 20px;
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-variation-settings: 'opsz' 16;
      font-size: 15.5px;
      color: var(--ink-soft);
      padding: 14px 0;
      border-top: 1px solid var(--line-soft);
      border-bottom: 1px solid var(--line-soft);
      text-align: center;
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
      height: 34px;
      background: var(--ink);
      color: var(--paper);
      font-family: 'Newsreader', serif;
      font-style: italic;
      font-weight: 500;
      font-size: 20px;
      display: flex;
      align-items: center;
      justify-content: center;
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

  readonly reference = '2026-04-14-118013';
  readonly issueDate = '15 avril 2026';
  readonly vehicleLabel = '118013';
  readonly impactLat = 35.61365;
  readonly impactLon = 10.74298;
  readonly confidence = 97;

  readonly story: NarrativeEvent[] = [
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

  readonly indicators: Indicator[] = [
    { label: 'Heure de l\'impact', value: '16h 02 min 52 s', hint: 'Heure locale de Tunis' },
    { label: 'Vitesse avant l\'impact', value: '88 km/h', hint: 'Mesurée 4 minutes avant' },
    { label: 'Vitesse au moment de l\'impact', value: '16 km/h', hint: 'Chute brutale et non-maîtrisée' },
    { label: 'Temps jusqu\'à l\'arrêt complet', value: '5 min 6 s', hint: 'Entre 15h58 et 16h04' },
    { label: 'Durée d\'inclinaison anormale', value: '4 minutes', hint: 'Forte suspicion de retournement' },
    { label: 'Coupure du contact', value: '16h 08', hint: 'Soit 6 minutes après l\'impact' },
  ];

  readonly reasons = [
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
  private subs: Subscription[] = [];

  constructor(
    private apiService: ApiService,
    private route: ActivatedRoute,
    private cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
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

    L.marker([this.impactLat, this.impactLon], { icon: impactIcon })
      .addTo(this.map)
      .bindPopup(`
        <div style="font-family: 'Manrope', sans-serif; font-size: 12px; color: #18181b; line-height: 1.5;">
          <div style="font-weight: 600; color: #991b1b; margin-bottom: 4px;">Lieu de l'incident</div>
          <div>14 avril 2026 · 16h 02</div>
          <div style="font-size: 11px; color: #52525b; margin-top: 4px;">
            ${this.impactLat.toFixed(5)} °N · ${this.impactLon.toFixed(5)} °E
          </div>
        </div>
      `);

    // Optionally load real trajectory for context — gracefully fails if API is down
    const from = new Date('2026-04-14T14:00:00');
    const to = new Date('2026-04-14T18:00:00');
    const sub = this.apiService.getDeviceHistory('118013', from, to, 3000).subscribe({
      next: (positions) => this.drawTrajectory(positions),
      error: () => { /* ignore — marker alone is enough */ },
    });
    this.subs.push(sub);
  }

  private drawTrajectory(positions: PositionDto[]): void {
    if (!this.map || !positions || positions.length < 2) return;

    const coords: L.LatLngTuple[] = positions
      .filter((p) => p.latitude && p.longitude)
      .map((p) => [p.latitude, p.longitude] as L.LatLngTuple);

    if (coords.length < 2) return;

    L.polyline(coords, {
      color: '#991b1b',
      weight: 2,
      opacity: 0.4,
      dashArray: '2 6',
    }).addTo(this.map);

    const bounds = L.latLngBounds(coords);
    bounds.extend([this.impactLat, this.impactLon]);
    this.map.fitBounds(bounds, { padding: [30, 30], maxZoom: 15 });
  }
}
