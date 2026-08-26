import { Component, Input, ViewEncapsulation } from '@angular/core';
import { Fr2HeaderComponent, Fr2DecoComponent, Fr2FooterComponent } from './fr2-chrome.component';

/**
 * Les quatre pages legales du site europeen, d'apres la planche « LEGAL »
 * (mosaique 2 x 2, chaque ecran dessine a ~1528 px et reduit de moitie).
 *
 * Structure commune relevee : en-tete avec « Accueil », decor orbital,
 * bloc de titre a GAUCHE (icone ronde + titre dont le dernier mot est violet
 * + sous-titre), contenu en panneaux, pied minimal dont les liens EXCLUENT
 * la page courante — c'est ainsi sur les quatre ecrans de la planche.
 *
 * L'illustration de la page cookies (cookie croque + fenetre a interrupteur)
 * est redessinee en SVG : la decouper de la mosaique a demi-echelle aurait
 * donne une image floue une fois agrandie.
 */

@Component({
  selector: 'app-fr2-legal-frame',
  standalone: true,
  imports: [Fr2HeaderComponent, Fr2DecoComponent, Fr2FooterComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr2-page">
      <app-fr2-header [accueil]="true" />
      <app-fr2-deco />
      <main class="fl2">
        <header class="fl2-head">
          <span class="fl2-ic"><ng-content select="[icon]" /></span>
          <div>
            <h1><ng-content select="[titre]" /></h1>
            <p><ng-content select="[sous-titre]" /></p>
          </div>
        </header>
        <ng-content />
      </main>
      <app-fr2-footer variant="minimal" />
    </div>
  `,
  styles: [`
    /* Maquette a 1528 px : 1 unite = 1 px de la planche a cette largeur. */
    .fl2 {
      --g: min(0.06545cqw, 1.05px);
      position: relative; z-index: 1; display: block;
      padding: calc(44 * var(--g)) 4.3cqw calc(56 * var(--g));
      min-height: calc(100vh - 160px);
    }
    .fl2-head { display: flex; align-items: center; gap: calc(30 * var(--g)); margin: 0 auto; width: 100%; }
    .fl2-ic {
      width: calc(110 * var(--g)); height: calc(110 * var(--g)); border-radius: 50%; flex: none;
      background: rgba(139,92,246,.1); border: 1px solid rgba(167,139,250,.4);
      display: grid; place-items: center;
      box-shadow: 0 0 calc(34 * var(--g)) rgba(139,92,246,.22);
    }
    .fl2-ic svg { width: calc(50 * var(--g)); height: calc(50 * var(--g)); }
    .fl2-head h1 { margin: 0; font-size: calc(42 * var(--g)); font-weight: 800; letter-spacing: -.02em; color: #fff; }
    .fl2-head h1 em {
      font-style: normal;
      background: linear-gradient(90deg,#8B5CF6,#A78BFA);
      -webkit-background-clip: text; background-clip: text; color: transparent;
    }
    .fl2-head p { margin: calc(12 * var(--g)) 0 0; font-size: calc(15.5 * var(--g)); line-height: calc(23 * var(--g)); color: #C7D2E4; }

    /* ── Panneau-liste : rangees icone + titre + texte, separees d'un trait ── */
    .fl2-list {
      margin: calc(38 * var(--g)) auto 0;
      background: rgba(255,255,255,.024); border: 1px solid rgba(255,255,255,.08);
      border-radius: calc(14 * var(--g)); padding: calc(10 * var(--g)) calc(30 * var(--g));
    }
    .fl2-row { display: flex; gap: calc(20 * var(--g)); padding: calc(22 * var(--g)) 0; }
    .fl2-row + .fl2-row { border-top: 1px solid rgba(255,255,255,.07); }
    .fl2-row .ri {
      width: calc(56 * var(--g)); height: calc(56 * var(--g)); border-radius: 50%; flex: none;
      background: rgba(139,92,246,.1); border: 1px solid rgba(167,139,250,.35);
      display: grid; place-items: center;
    }
    .fl2-row .ri svg { width: calc(25 * var(--g)); height: calc(25 * var(--g)); }
    .fl2-row h3 { margin: calc(4 * var(--g)) 0 0; font-size: calc(16 * var(--g)); font-weight: 800; color: #fff; }
    .fl2-row p { margin: calc(8 * var(--g)) 0 0; font-size: calc(13.5 * var(--g)); line-height: calc(21 * var(--g)); color: #9AA7BD; }

    /* Rangee a libelle : intitule a gauche, texte a droite (mentions legales). */
    .fl2-row.lab { align-items: center; }
    .fl2-row.lab .rl { flex: none; width: calc(190 * var(--g)); display: flex; align-items: center; gap: calc(14 * var(--g)); }
    .fl2-row.lab h3 { margin: 0; font-size: calc(15 * var(--g)); }
    .fl2-row.lab p { margin: 0; flex: 1; }

    .fl2-foot-line {
      margin: calc(26 * var(--g)) 0 0; text-align: center;
      font-size: calc(13.5 * var(--g)); color: #9AA7BD;
    }

    /* ── Cartes RGPD : six colonnes ── */
    .fl2-cards { display: grid; grid-template-columns: repeat(6, 1fr); gap: calc(16 * var(--g)); margin-top: calc(38 * var(--g)); }
    .fl2-card {
      background: rgba(255,255,255,.024); border: 1px solid rgba(255,255,255,.08);
      border-radius: calc(12 * var(--g)); padding: calc(26 * var(--g)) calc(16 * var(--g));
      text-align: center;
    }
    .fl2-card .ci {
      width: calc(84 * var(--g)); height: calc(84 * var(--g)); border-radius: 50%;
      margin: 0 auto; display: grid; place-items: center;
      background: rgba(139,92,246,.1); border: 1px solid rgba(167,139,250,.35);
    }
    .fl2-card .ci svg { width: calc(36 * var(--g)); height: calc(36 * var(--g)); }
    .fl2-card h3 { margin: calc(18 * var(--g)) 0 0; font-size: calc(14.5 * var(--g)); font-weight: 800; color: #fff; }
    .fl2-card p { margin: calc(10 * var(--g)) 0 0; font-size: calc(11.5 * var(--g)); line-height: calc(17 * var(--g)); color: #9AA7BD; }

    .fl2-banner {
      margin: calc(30 * var(--g)) auto 0;
      display: flex; align-items: center; gap: calc(16 * var(--g));
      background: rgba(79,70,229,.12); border: 1px solid rgba(99,102,241,.3);
      border-radius: calc(12 * var(--g)); padding: calc(18 * var(--g)) calc(24 * var(--g));
      font-size: calc(13.5 * var(--g)); line-height: calc(20 * var(--g)); color: #C7D2E4;
    }
    .fl2-banner .bi {
      width: calc(40 * var(--g)); height: calc(40 * var(--g)); border-radius: 50%; flex: none;
      background: rgba(96,165,250,.12); border: 1px solid rgba(96,165,250,.35);
      display: grid; place-items: center;
    }
    .fl2-banner .bi svg { width: calc(19 * var(--g)); height: calc(19 * var(--g)); }

    /* ── Cookies : liste a gauche, illustration a droite ── */
    .fl2-duo { display: grid; grid-template-columns: 1.55fr 1fr; gap: calc(40 * var(--g)); align-items: center; }
    .fl2-art { display: grid; place-items: center; }
    .fl2-art svg { width: calc(360 * var(--g)); height: auto; }

    @media (max-width: 980px) {
      .fl2-cards { grid-template-columns: repeat(3, 1fr); }
      .fl2-duo { grid-template-columns: 1fr; }
      .fl2-art { order: -1; }
    }
    @media (max-width: 640px) {
      .fl2-head { flex-direction: column; align-items: flex-start; }
      .fl2-cards { grid-template-columns: 1fr 1fr; }
      .fl2-row.lab { flex-direction: column; align-items: flex-start; gap: calc(10 * var(--g)); }
    }
  `]
})
export class Fr2LegalFrameComponent {}

/* ══════════════ POLITIQUE DE CONFIDENTIALITÉ ══════════════ */
@Component({
  selector: 'app-france-privacy',
  standalone: true,
  imports: [Fr2LegalFrameComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <app-fr2-legal-frame>
      <svg icon viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.5"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><rect x="9.4" y="10.6" width="5.2" height="4.6" rx="1"/><path d="M10.5 10.6V9.4a1.5 1.5 0 0 1 3 0v1.2"/></svg>
      <span titre>Politique de <em>confidentialité</em></span>
      <span sous-titre>La présente politique décrit la manière dont Calypso collecte, utilise et protège<br>vos données personnelles lorsque vous utilisez notre plateforme.</span>

      <div class="fl2-list" style="max-width: 64.3cqw">
        <div class="fl2-row">
          <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="12" cy="9" r="3.2"/><path d="M5.5 19.5a6.8 6.8 0 0 1 13 0"/></svg></span>
          <div>
            <h3>1. Données collectées</h3>
            <p>Nous collectons uniquement les données nécessaires au fonctionnement de nos services :
               informations de compte, données techniques, données d'utilisation et données liées
               à la gestion de votre flotte.</p>
          </div>
        </div>
        <div class="fl2-row">
          <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M5 20V10M12 20V4M19 20v-8"/></svg></span>
          <div>
            <h3>2. Utilisation des données</h3>
            <p>Vos données sont utilisées pour fournir, améliorer et sécuriser la plateforme Calypso,
               personnaliser votre expérience et communiquer avec vous si nécessaire.</p>
          </div>
        </div>
        <div class="fl2-row">
          <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><rect x="5" y="10.5" width="14" height="9.5" rx="2.2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/></svg></span>
          <div>
            <h3>3. Protection des données</h3>
            <p>Nous mettons en œuvre des mesures techniques et organisationnelles appropriées
               pour protéger vos données contre tout accès non autorisé, perte ou divulgation.</p>
          </div>
        </div>
        <div class="fl2-row">
          <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="9" cy="9" r="2.8"/><circle cx="16.5" cy="10" r="2.3"/><path d="M3.5 19a5.5 5.5 0 0 1 11 0M13.5 19a4.6 4.6 0 0 1 7-4"/></svg></span>
          <div>
            <h3>4. Partage des données</h3>
            <p>Vos données ne sont jamais vendues. Elles ne sont partagées qu'avec des prestataires
               de confiance strictement nécessaires au fonctionnement de la plateforme.</p>
          </div>
        </div>
      </div>

      <p class="fl2-foot-line">Pour toute question concernant vos données, contactez-nous via le formulaire de contact.</p>
    </app-fr2-legal-frame>
  `
})
export class FrancePrivacyComponent {}

/* ══════════════ MENTIONS LÉGALES ══════════════ */
@Component({
  selector: 'app-france-legal',
  standalone: true,
  imports: [Fr2LegalFrameComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <app-fr2-legal-frame>
      <svg icon viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.5"><path d="M12 4v16M7 20h10M12 6.5 6 8m6-1.5L18 8M6 8l-2.5 5.5a3.6 3.6 0 0 0 5 0zM18 8l-2.5 5.5a3.6 3.6 0 0 0 5 0z"/></svg>
      <span titre>Mentions <em>légales</em></span>
      <span sous-titre>Les présentes mentions légales régissent l'utilisation du site web<br>et de la plateforme Calypso.</span>

      <div class="fl2-list" style="max-width: 64.3cqw">
        @for (r of rows; track r.t) {
          <div class="fl2-row lab">
            <span class="rl">
              <span class="ri">
                @switch (r.k) {
                  @case ('edit') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M7 3h7l4 4v14H7z"/><path d="M14 3v4h4M10 12h5M10 16h5"/></svg> }
                  @case ('host') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><rect x="4" y="4" width="16" height="6.5" rx="1.5"/><rect x="4" y="13.5" width="16" height="6.5" rx="1.5"/><path d="M7.5 7.2h.01M7.5 16.7h.01"/></svg> }
                  @case ('ip') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="12" cy="12" r="9"/><path d="M14.5 9.3a3.4 3.4 0 1 0 0 5.4"/></svg> }
                  @case ('resp') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg> }
                  @case ('law') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="m5 8 7-4 7 4M6.5 8v9M12 8v9M17.5 8v9M4 19.5h16"/></svg> }
                }
              </span>
              <h3>{{ r.t }}</h3>
            </span>
            <p>{{ r.d }}</p>
          </div>
        }
      </div>
    </app-fr2-legal-frame>
  `
})
export class FranceLegalComponent {
  readonly rows = [
    { k: 'edit', t: 'Éditeur du site', d: "Calypso est une plateforme éditée et opérée par ses propriétaires. L'ensemble du site et de la plateforme est la propriété exclusive de Calypso." },
    { k: 'host', t: 'Hébergement', d: 'La plateforme Calypso est hébergée sur des infrastructures sécurisées répondant aux normes en vigueur.' },
    { k: 'ip',   t: 'Propriété intellectuelle', d: 'Tous les contenus présents sur ce site (textes, images, logos, icônes, etc.) sont la propriété exclusive de Calypso. Toute reproduction est interdite.' },
    { k: 'resp', t: 'Responsabilité', d: "Calypso met tout en œuvre pour assurer l'exactitude des informations fournies. Cependant, des erreurs peuvent survenir. L'utilisateur reste responsable de l'usage qu'il fait des informations et de la plateforme." },
    { k: 'law',  t: 'Droit applicable', d: 'Les présentes mentions sont régies par les lois en vigueur. Tout litige sera soumis aux tribunaux compétents.' }
  ];
}

/* ══════════════ CONFORMITÉ RGPD ══════════════ */
@Component({
  selector: 'app-france-rgpd',
  standalone: true,
  imports: [Fr2LegalFrameComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <app-fr2-legal-frame>
      <svg icon viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.5"><circle cx="12" cy="12" r="9"/><rect x="9.6" y="10.8" width="4.8" height="4.2" rx="0.9"/><path d="M10.6 10.8V9.7a1.4 1.4 0 0 1 2.8 0v1.1"/><path d="M12 4.2v1.2M12 18.6v1.2M4.2 12h1.2M18.6 12h1.2M6.5 6.5l.85.85M16.65 16.65l.85.85M17.5 6.5l-.85.85M7.35 16.65l-.85.85" stroke-width="1.2"/></svg>
      <span titre>Conformité <em>RGPD</em></span>
      <span sous-titre>Calypso respecte le Règlement Général sur la Protection des Données (RGPD)<br>afin de garantir vos droits et la protection de vos données personnelles.</span>

      <div class="fl2-cards">
        @for (c of cards; track c.t) {
          <div class="fl2-card">
            <span class="ci">
              @switch (c.k) {
                @case ('eye') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><path d="M2 12s3.6-6.5 10-6.5S22 12 22 12s-3.6 6.5-10 6.5S2 12 2 12z"/><circle cx="12" cy="12" r="2.8"/></svg> }
                @case ('target') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><circle cx="12" cy="12" r="8.5"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1.6"/></svg> }
                @case ('shield') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/></svg> }
                @case ('person') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><circle cx="12" cy="9" r="3.2"/><path d="M5.5 19.5a6.8 6.8 0 0 1 13 0"/></svg> }
                @case ('folder') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><path d="M4 7a2 2 0 0 1 2-2h4l2 2.5h6a2 2 0 0 1 2 2V17a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z"/></svg> }
                @case ('mail') { <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.6"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg> }
              }
            </span>
            <h3>{{ c.t }}</h3>
            <p>{{ c.d }}</p>
          </div>
        }
      </div>

      <div class="fl2-banner" style="max-width: 55cqw">
        <span class="bi"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><circle cx="12" cy="12" r="9"/><path d="M12 8v.5M12 11v5"/></svg></span>
        <span>En utilisant Calypso, vous bénéficiez d'une plateforme conçue dans le respect de votre vie privée
        et conforme aux exigences du RGPD.</span>
      </div>
    </app-fr2-legal-frame>
  `
})
export class FranceRgpdComponent {
  readonly cards = [
    { k: 'eye',    t: 'Transparence', d: "Nous vous informons de manière claire sur la collecte et l'utilisation de vos données." },
    { k: 'target', t: 'Finalité', d: 'Vos données sont collectées pour des finalités déterminées, explicites et légitimes.' },
    { k: 'shield', t: 'Sécurité', d: 'Nous appliquons des mesures de sécurité adaptées pour protéger vos données.' },
    { k: 'person', t: 'Vos droits', d: "Vous disposez d'un droit d'accès, de rectification, de suppression et de portabilité de vos données." },
    { k: 'folder', t: 'Conservation', d: 'Vos données sont conservées uniquement le temps nécessaire aux finalités pour lesquelles elles ont été collectées.' },
    { k: 'mail',   t: 'Contact DPO', d: 'Pour toute question relative à vos données personnelles, contactez-nous via notre formulaire de contact.' }
  ];
}

/* ══════════════ POLITIQUE DE COOKIES ══════════════ */
@Component({
  selector: 'app-france-cookies',
  standalone: true,
  imports: [Fr2LegalFrameComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <app-fr2-legal-frame>
      <svg icon viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.5"><path d="M20.8 13A9 9 0 1 1 11 3.2a3.6 3.6 0 0 0 4.2 4.6A3.6 3.6 0 0 0 20.8 13z"/><circle cx="9" cy="10" r=".9" fill="#A78BFA"/><circle cx="13" cy="14.5" r=".9" fill="#A78BFA"/><circle cx="8.5" cy="15.5" r=".7" fill="#A78BFA"/></svg>
      <span titre>Politique de <em>cookies</em></span>
      <span sous-titre>Cette politique explique comment Calypso utilise les cookies et technologies similaires<br>lorsque vous visitez notre site ou utilisez notre plateforme.</span>

      <div class="fl2-duo">
        <div class="fl2-list">
          <div class="fl2-row">
            <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="12" cy="12" r="9"/><path d="M9.6 9.2a2.5 2.5 0 0 1 4.8 1c0 1.6-2.4 2-2.4 3.3M12 16.8v.01"/></svg></span>
            <div>
              <h3>Qu'est-ce qu'un cookie ?</h3>
              <p>Un cookie est un petit fichier texte déposé sur votre appareil pour améliorer
                 votre expérience utilisateur et analyser l'utilisation du site.</p>
            </div>
          </div>
          <div class="fl2-row">
            <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M5 7h14M5 12h14M5 17h14"/><circle cx="9" cy="7" r="1.7" fill="#0B1020"/><circle cx="15" cy="12" r="1.7" fill="#0B1020"/><circle cx="8" cy="17" r="1.7" fill="#0B1020"/></svg></span>
            <div>
              <h3>Types de cookies utilisés</h3>
              <p>Cookies essentiels : nécessaires au bon fonctionnement du site.<br>
                 Cookies analytiques : nous aident à comprendre comment vous utilisez Calypso
                 afin d'améliorer nos services.</p>
            </div>
          </div>
          <div class="fl2-row">
            <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><circle cx="12" cy="12" r="3.1"/><path d="M12 2.8v2.4M12 18.8v2.4M2.8 12h2.4M18.8 12h2.4M5.5 5.5l1.7 1.7M16.8 16.8l1.7 1.7M18.5 5.5l-1.7 1.7M7.2 16.8l-1.7 1.7"/></svg></span>
            <div>
              <h3>Gestion des cookies</h3>
              <p>Vous pouvez accepter, refuser ou personnaliser l'utilisation des cookies
                 à tout moment via le bandeau de gestion des cookies sur notre site.</p>
            </div>
          </div>
          <div class="fl2-row">
            <span class="ri"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg></span>
            <div>
              <h3>Consentement</h3>
              <p>En continuant à naviguer sur notre site ou à utiliser Calypso,<br>
                 vous acceptez l'utilisation des cookies conformément à cette politique.</p>
            </div>
          </div>
        </div>

        <!-- Illustration : cookie croque + fenetre a interrupteur, comme la planche. -->
        <div class="fl2-art" aria-hidden="true">
          <svg viewBox="0 0 360 420" fill="none">
            <defs>
              <linearGradient id="ck" x1="0" y1="0" x2="1" y2="1">
                <stop offset="0" stop-color="#C084FC"/><stop offset="1" stop-color="#7C3AED"/>
              </linearGradient>
            </defs>
            <path d="M235 105a72 72 0 1 1-50-69 29 29 0 0 0 33 37 29 29 0 0 0 17 32z"
                  stroke="url(#ck)" stroke-width="5" fill="rgba(139,92,246,.06)"/>
            <path d="M228 41a20 20 0 0 1-20 20" stroke="url(#ck)" stroke-width="5" stroke-linecap="round"/>
            <circle cx="140" cy="95" r="7" fill="#A78BFA"/>
            <circle cx="175" cy="130" r="6" fill="#8B5CF6"/>
            <circle cx="130" cy="135" r="5" fill="#C084FC"/>
            <circle cx="160" cy="70" r="5" fill="#A78BFA"/>
            <rect x="60" y="215" width="250" height="160" rx="18"
                  stroke="url(#ck)" stroke-width="4" fill="rgba(139,92,246,.05)"/>
            <path d="M60 258h250" stroke="rgba(167,139,250,.55)" stroke-width="3"/>
            <circle cx="84" cy="237" r="5" fill="#A78BFA"/><circle cx="102" cy="237" r="5" fill="#8B5CF6"/><circle cx="120" cy="237" r="5" fill="#C084FC"/>
            <rect x="88" y="282" width="130" height="12" rx="6" fill="rgba(167,139,250,.5)"/>
            <rect x="88" y="308" width="170" height="12" rx="6" fill="rgba(167,139,250,.32)"/>
            <rect x="222" y="336" width="60" height="26" rx="13" fill="rgba(139,92,246,.35)" stroke="#A78BFA" stroke-width="2.5"/>
            <circle cx="269" cy="349" r="9" fill="#C4B5FD"/>
          </svg>
        </div>
      </div>
    </app-fr2-legal-frame>
  `
})
export class FranceCookiesComponent {}
