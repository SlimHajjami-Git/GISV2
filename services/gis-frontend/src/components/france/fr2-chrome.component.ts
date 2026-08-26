import { Component, Input, ViewEncapsulation, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RegionService } from '../../services/region.service';

/**
 * Habillage commun du site vitrine europeen — en-tete, decor orbital et pieds
 * de page, d'apres les captures validees.
 *
 * Les captures n'ont PAS un habillage unique : l'accueil a un pied a quatre
 * colonnes, Calypso Auto un pied en ligne, les ecrans d'authentification un
 * pied riche (colonnes + carte « Donnees securisees » + reseaux sociaux), les
 * pages legales un pied minimal. Chaque page choisit donc sa variante, et la
 * fidelite se juge ecran par ecran.
 *
 * L'echelle de l'habillage est celle de l'ACCUEIL (maquette 1024, seule page
 * ou l'en-tete a ete valide au pixel) : --uc = 1 px de cette maquette,
 * plafonne pour ne pas grossir au-dela de 1400 px d'ecran.
 *
 * Chaque page enveloppe son contenu dans <div class="fr2-page"> : c'est lui
 * qui porte le container-query dont dependent toutes les unites cqw.
 */

@Component({
  selector: 'app-fr2-header',
  standalone: true,
  imports: [RouterLink],
  encapsulation: ViewEncapsulation.None,
  template: `
    <header class="fr2-hd">
      <a class="fr2-brand" routerLink="/fr">
        <img src="/assets/calypso-logo.svg" alt="Calypso">
      </a>
      <nav class="fr2-nav">
        @if (accueil) { <a routerLink="/fr">Accueil</a> }
        <a routerLink="/fr/fonctionnalites">Fonctionnalités</a>
        <a routerLink="/fr/tarifs">Tarifs</a>
        <a routerLink="/fr/calypso-auto">Calypso Auto</a>
        <a routerLink="/fr/contact">Contact</a>
      </nav>
      <div class="fr2-hd-right">
        <a routerLink="/login" class="fr2-signin">Se connecter</a>
        <a routerLink="/inscription" class="fr2-cta">Essayer gratuitement</a>
      </div>
    </header>
  `,
  styles: [`
    /* Base commune a toutes les pages fr2. */
    .fr2-page {
      container-type: inline-size;
      --uc: min(0.09766cqw, 1.367px);
      display: block; position: relative; overflow: hidden;
      background: #05070F; color: #fff; min-height: 100vh;
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      -webkit-font-smoothing: antialiased;
    }
    .fr2-page *, .fr2-page *::before, .fr2-page *::after { box-sizing: border-box; }
    .fr2-page a { text-decoration: none; }

    /* ── EN-TETE : 58 px sur la maquette de l'accueil ── */
    .fr2-hd {
      position: relative; z-index: 2;
      height: calc(58 * var(--uc)); display: flex; align-items: center;
      padding: 0 3.418cqw; border-bottom: 1px solid rgba(255,255,255,.07);
    }
    .fr2-brand img { height: calc(26 * var(--uc)); width: auto; display: block; }
    .fr2-nav { display: flex; gap: calc(26 * var(--uc)); margin: 0 auto; }
    .fr2-nav a { color: #9AA7BD; font-size: calc(12.5 * var(--uc)); font-weight: 600; transition: color .15s; }
    .fr2-nav a:hover { color: #fff; }
    .fr2-hd-right { display: flex; align-items: center; gap: calc(18 * var(--uc)); }
    .fr2-signin { color: #fff; font-size: calc(12.5 * var(--uc)); font-weight: 600; }
    .fr2-cta {
      background: linear-gradient(135deg,#4F46E5,#7C3AED); color: #fff;
      font-size: calc(11.5 * var(--uc)); font-weight: 700; white-space: nowrap;
      padding: calc(8 * var(--uc)) calc(14 * var(--uc)); border-radius: calc(8 * var(--uc));
    }
    @media (max-width: 700px) {
      .fr2-hd { height: auto; flex-wrap: wrap; gap: calc(10 * var(--uc)); padding-top: calc(12 * var(--uc)); padding-bottom: calc(12 * var(--uc)); }
      .fr2-nav { order: 3; width: 100%; margin: 0; justify-content: space-between; }
      .fr2-hd-right { margin-left: auto; }
    }
  `]
})
export class Fr2HeaderComponent {
  @Input() accueil = false;

  constructor(region: RegionService) {
    // Quiconque voit une page du site europeen reste sur le parcours europeen
    // pour la session : /login et /inscription gardent alors cet habillage.
    region.markFranceVisit();
  }
}

@Component({
  selector: 'app-fr2-deco',
  standalone: true,
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr2-deco" aria-hidden="true">
      <svg class="l" viewBox="0 0 420 900" fill="none">
        <circle cx="-40" cy="450" r="420" stroke="rgba(139,92,246,.16)" stroke-width="1" stroke-dasharray="4 8"/>
        <circle cx="-40" cy="450" r="300" stroke="rgba(96,165,250,.12)" stroke-width="1"/>
        <circle cx="-40" cy="450" r="190" fill="rgba(59,130,246,.05)"/>
        <circle cx="150" cy="205" r="5" fill="#C084FC"/><circle cx="150" cy="205" r="10" fill="rgba(192,132,252,.25)"/>
        <circle cx="255" cy="330" r="4" fill="#A78BFA"/><circle cx="255" cy="330" r="9" fill="rgba(167,139,250,.22)"/>
        <circle cx="75" cy="565" r="4.5" fill="#60A5FA"/><circle cx="75" cy="565" r="10" fill="rgba(96,165,250,.22)"/>
        <circle cx="215" cy="680" r="3.5" fill="#3B82F6"/>
      </svg>
      <svg class="r" viewBox="0 0 420 900" fill="none">
        <circle cx="460" cy="430" r="430" stroke="rgba(139,92,246,.18)" stroke-width="1" stroke-dasharray="4 8"/>
        <circle cx="460" cy="430" r="310" stroke="rgba(148,163,184,.10)" stroke-width="1"/>
        <circle cx="460" cy="430" r="200" fill="rgba(124,58,237,.06)"/>
        <circle cx="260" cy="255" r="4" fill="#A78BFA"/><circle cx="260" cy="255" r="9" fill="rgba(167,139,250,.22)"/>
        <circle cx="330" cy="500" r="5" fill="#C084FC"/><circle cx="330" cy="500" r="11" fill="rgba(192,132,252,.25)"/>
        <circle cx="185" cy="620" r="4" fill="#60A5FA"/><circle cx="185" cy="620" r="9" fill="rgba(96,165,250,.2)"/>
        <circle cx="290" cy="740" r="3.5" fill="#E879F9"/>
      </svg>
    </div>
  `,
  styles: [`
    .fr2-deco { position: absolute; inset: 0; z-index: 0; pointer-events: none; }
    .fr2-deco .l, .fr2-deco .r { position: absolute; top: calc(58 * var(--uc)); height: calc(100% - 58 * var(--uc)); width: 30cqw; }
    .fr2-deco .l { left: 0; }
    .fr2-deco .r { right: 0; }
  `]
})
export class Fr2DecoComponent {}

@Component({
  selector: 'app-fr2-footer',
  standalone: true,
  imports: [RouterLink],
  encapsulation: ViewEncapsulation.None,
  template: `
    @switch (variant) {

      <!-- ── Pied de l'ACCUEIL : 4 colonnes (valide au pixel) ── -->
      @case ('columns') {
        <footer class="fr2-ft">
          <div class="fr2-ft-grid">
            <div>
              <img class="fl" src="/assets/calypso-logo.svg" alt="Calypso">
              <p>La solution intelligente pour<br>gérer votre parc automobile.</p>
              <div class="fr2-soc">
                <span aria-hidden="true"><svg viewBox="0 0 24 24" fill="currentColor"><path d="M6.5 8.5H3.8V20h2.7zM5.1 7.3a1.6 1.6 0 1 0 0-3.2 1.6 1.6 0 0 0 0 3.2zM20.2 13.7c0-3-1.6-4.4-3.7-4.4a3.2 3.2 0 0 0-2.9 1.6V8.5H11V20h2.7v-6c0-1.6.8-2.5 2-2.5s1.8.9 1.8 2.5v6h2.7z"/></svg></span>
                <a href="mailto:contact@belive.tn" aria-label="Écrire à Calypso"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3 7 9 6 9-6"/></svg></a>
              </div>
            </div>
            <div>
              <h4>PRODUIT</h4>
              <a routerLink="/fr/fonctionnalites">Fonctionnalités</a>
              <a routerLink="/fr/tarifs">Tarifs</a>
              <a routerLink="/fr/calypso-auto">Calypso Auto</a>
            </div>
            <div>
              <h4>CALYPSO</h4>
              <a routerLink="/fr/contact">Contact</a>
              <a routerLink="/login">Se connecter</a>
              <a routerLink="/inscription">Essayer gratuitement</a>
            </div>
            <div>
              <h4>LÉGAL</h4>
              <a routerLink="/fr/mentions-legales">Mentions légales</a>
              <a routerLink="/fr/confidentialite">Politique de confidentialité</a>
              <a routerLink="/fr/rgpd">RGPD</a>
              <a routerLink="/fr/cookies">Cookies</a>
            </div>
          </div>
          <div class="fr2-ft-b">© 2026 Calypso. Tous droits réservés.</div>
        </footer>
      }

      <!-- ── Pied RICHE des ecrans d'authentification et de contact ── -->
      @case ('rich') {
        <footer class="fr2-fr">
          <div class="fr2-fr-grid">
            <div class="id">
              <img src="/assets/calypso-logo.svg" alt="Calypso">
              <p>La plateforme intelligente pour gérer<br>vos entretiens, réparations et carburant.</p>
              @if (perks) {
                <ul class="perks">
                  <li><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M14.5 6.5a4 4 0 0 0-5.6 4.9L4 16.3V20h3.7l4.9-4.9a4 4 0 0 0 4.9-5.6L14.6 12l-2.6-2.6z"/></svg>Entretien simplifié</li>
                  <li><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="7" cy="7" r="3"/><path d="M9.2 9.2 20 20M20 14.5V20h-5.5"/></svg>Réparations maîtrisées</li>
                  <li><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M5 20V6a2 2 0 0 1 2-2h5a2 2 0 0 1 2 2v14M4 20h11M14 11h2.5a2 2 0 0 1 2 2v3a1.5 1.5 0 0 0 3 0V9l-2.5-2.5"/></svg>Carburant optimisé</li>
                  <li><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M5 20V10M12 20V4M19 20v-8"/></svg>Décisions éclairées</li>
                </ul>
              }
            </div>
            <div>
              <h4>Produit</h4>
              <a routerLink="/fr/fonctionnalites">Fonctionnalités</a>
              <a routerLink="/fr/tarifs">Tarifs</a>
              <a routerLink="/fr/calypso-auto">Calypso Auto</a>
            </div>
            <div>
              <h4>Calypso</h4>
              <a routerLink="/fr">Accueil</a>
              <a routerLink="/fr/contact">Contact</a>
              <a routerLink="/inscription">Essayer gratuitement</a>
            </div>
            <div>
              <h4>Légal</h4>
              <a routerLink="/fr/confidentialite">Confidentialité</a>
              <a routerLink="/fr/mentions-legales">Mentions légales</a>
              <a routerLink="/fr/rgpd">RGPD</a>
            </div>
            <div class="safe">
              <span class="sh"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg></span>
              <div>
                <h5>Données sécurisées</h5>
                <p>Vos données sont hébergées<br>en toute sécurité.</p>
              </div>
            </div>
          </div>
          <div class="fr2-fr-b">
            <span>© 2026 Calypso. Tous droits réservés.</span>
            <span class="soc">
              <svg viewBox="0 0 24 24" fill="currentColor"><path d="M6.5 8.5H3.8V20h2.7zM5.1 7.3a1.6 1.6 0 1 0 0-3.2 1.6 1.6 0 0 0 0 3.2zM20.2 13.7c0-3-1.6-4.4-3.7-4.4a3.2 3.2 0 0 0-2.9 1.6V8.5H11V20h2.7v-6c0-1.6.8-2.5 2-2.5s1.8.9 1.8 2.5v6h2.7z"/></svg>
              <svg viewBox="0 0 24 24" fill="currentColor"><path d="M21.6 7.2a2.5 2.5 0 0 0-1.8-1.8C18.3 5 12 5 12 5s-6.3 0-7.8.4a2.5 2.5 0 0 0-1.8 1.8A26 26 0 0 0 2 12c0 1.6.1 3.2.4 4.8a2.5 2.5 0 0 0 1.8 1.8c1.5.4 7.8.4 7.8.4s6.3 0 7.8-.4a2.5 2.5 0 0 0 1.8-1.8c.3-1.6.4-3.2.4-4.8s-.1-3.2-.4-4.8zM10 15.5v-7l6 3.5z"/></svg>
              <svg viewBox="0 0 24 24" fill="currentColor"><path d="M17.8 3h3l-6.6 7.6L22 21h-6.1l-4.8-6.3L5.6 21h-3l7-8.1L2 3h6.2l4.3 5.7zm-1 16.2h1.7L7.1 4.7H5.3z"/></svg>
            </span>
          </div>
        </footer>
      }

      <!-- ── Pied EN LIGNE de Calypso Auto ── -->
      @case ('inline') {
        <footer class="fr2-fi">
          <img src="/assets/calypso-logo.svg" alt="Calypso">
          <span class="cp">© 2026 Calypso. Tous droits réservés.</span>
          <nav>
            <a routerLink="/fr/confidentialite">Confidentialité</a>
            <a routerLink="/fr/mentions-legales">Mentions légales</a>
            <a routerLink="/fr/rgpd">RGPD</a>
          </nav>
          <span class="lang">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a15 15 0 0 1 0 18M12 3a15 15 0 0 0 0 18"/></svg>
            Français
          </span>
        </footer>
      }

      <!-- ── Pied MINIMAL des pages legales ── -->
      @case ('minimal') {
        <footer class="fr2-fm">
          <img src="/assets/calypso-logo.svg" alt="Calypso">
          <nav>
            @for (l of legalLinks; track l.to) {
              @if (!$first) { <span class="sep">|</span> }
              <a [routerLink]="l.to">{{ l.t }}</a>
            }
          </nav>
        </footer>
      }
    }
  `,
  styles: [`
    /* ── colonnes (accueil) ── */
    .fr2-ft { position: relative; z-index: 1; padding: calc(18 * var(--uc)) 3.418cqw calc(14 * var(--uc)); border-top: 1px solid rgba(255,255,255,.07); }
    .fr2-ft-grid { display: grid; grid-template-columns: 1.6fr 1fr 1fr 1.3fr; gap: calc(22 * var(--uc)); }
    .fr2-ft .fl { height: calc(22 * var(--uc)); width: auto; display: block; margin-bottom: calc(9 * var(--uc)); }
    .fr2-ft p { margin: 0; font-size: calc(10.5 * var(--uc)); line-height: calc(15 * var(--uc)); color: #6B7A94; }
    .fr2-ft h4 { margin: 0 0 calc(8 * var(--uc)); font-size: calc(10 * var(--uc)); font-weight: 800; letter-spacing: .1em; color: #A78BFA; }
    .fr2-ft-grid a { display: block; font-size: calc(11 * var(--uc)); line-height: calc(15 * var(--uc)); color: #9AA7BD; margin-bottom: calc(5 * var(--uc)); }
    .fr2-ft-grid a:hover { color: #fff; }
    .fr2-ft-b {
      margin-top: calc(14 * var(--uc)); padding-top: calc(12 * var(--uc));
      border-top: 1px solid rgba(255,255,255,.07);
      font-size: calc(10.5 * var(--uc)); color: #6B7A94;
      text-align: center; /* centre, comme sur la maquette */
    }
    .fr2-soc { display: flex; gap: calc(8 * var(--uc)); margin-top: calc(10 * var(--uc)); }
    .fr2-soc span, .fr2-soc a {
      width: calc(22 * var(--uc)); height: calc(22 * var(--uc)); border-radius: calc(5 * var(--uc));
      background: rgba(255,255,255,.06); border: 1px solid rgba(255,255,255,.1);
      display: grid; place-items: center; color: #C7D2E4;
    }
    .fr2-soc svg { width: calc(11 * var(--uc)); height: calc(11 * var(--uc)); }

    /* ── riche (authentification / contact) — maquette 1402 ── */
    .fr2-fr { position: relative; z-index: 1; margin-top: auto; padding: calc(30 * var(--uc)) 3.3cqw calc(16 * var(--uc)); border-top: 1px solid rgba(255,255,255,.07); background: rgba(3,5,12,.55); }
    .fr2-fr-grid { display: grid; grid-template-columns: 1.9fr 0.9fr 0.9fr 0.9fr 1.6fr; gap: calc(20 * var(--uc)); align-items: start; }
    .fr2-fr .id img { height: calc(24 * var(--uc)); display: block; }
    .fr2-fr .id p { margin: calc(12 * var(--uc)) 0 0; font-size: calc(11.5 * var(--uc)); line-height: calc(17 * var(--uc)); color: #9AA7BD; }
    .fr2-fr .perks { list-style: none; margin: calc(14 * var(--uc)) 0 0; padding: 0; display: grid; grid-template-columns: auto auto; gap: calc(8 * var(--uc)) calc(22 * var(--uc)); }
    .fr2-fr .perks li { display: flex; align-items: center; gap: calc(7 * var(--uc)); font-size: calc(10.5 * var(--uc)); color: #9AA7BD; white-space: nowrap; }
    .fr2-fr .perks svg { width: calc(13 * var(--uc)); height: calc(13 * var(--uc)); flex: none; }
    .fr2-fr h4 { margin: 0 0 calc(10 * var(--uc)); font-size: calc(12 * var(--uc)); font-weight: 800; color: #fff; }
    .fr2-fr-grid a { display: block; font-size: calc(11.5 * var(--uc)); line-height: calc(16 * var(--uc)); color: #9AA7BD; margin-bottom: calc(7 * var(--uc)); }
    .fr2-fr-grid a:hover { color: #fff; }
    .fr2-fr .safe {
      display: flex; gap: calc(12 * var(--uc)); align-items: center;
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.08);
      border-radius: calc(11 * var(--uc)); padding: calc(16 * var(--uc)) calc(18 * var(--uc));
    }
    .fr2-fr .sh {
      width: calc(40 * var(--uc)); height: calc(40 * var(--uc)); border-radius: 50%; flex: none;
      background: rgba(139,92,246,.12); border: 1px solid rgba(167,139,250,.3);
      display: grid; place-items: center;
    }
    .fr2-fr .sh svg { width: calc(19 * var(--uc)); height: calc(19 * var(--uc)); }
    .fr2-fr h5 { margin: 0; font-size: calc(12 * var(--uc)); font-weight: 800; color: #fff; }
    .fr2-fr .safe p { margin: calc(4 * var(--uc)) 0 0; font-size: calc(10.5 * var(--uc)); line-height: calc(14 * var(--uc)); color: #9AA7BD; }
    .fr2-fr-b {
      margin-top: calc(20 * var(--uc)); padding-top: calc(12 * var(--uc));
      border-top: 1px solid rgba(255,255,255,.06);
      display: flex; align-items: center; justify-content: space-between;
      font-size: calc(10.5 * var(--uc)); color: #6B7A94;
    }
    .fr2-fr .soc { display: flex; gap: calc(12 * var(--uc)); color: #C7D2E4; }
    .fr2-fr .soc svg { width: calc(15 * var(--uc)); height: calc(15 * var(--uc)); }

    /* ── en ligne (Calypso Auto) ── */
    .fr2-fi {
      position: relative; z-index: 1;
      display: flex; align-items: center; gap: calc(16 * var(--uc));
      padding: calc(16 * var(--uc)) 3.418cqw; border-top: 1px solid rgba(255,255,255,.07);
    }
    .fr2-fi img { height: calc(20 * var(--uc)); }
    .fr2-fi .cp { font-size: calc(10.5 * var(--uc)); color: #6B7A94; }
    .fr2-fi nav { margin: 0 auto; display: flex; gap: calc(26 * var(--uc)); }
    .fr2-fi a { font-size: calc(11 * var(--uc)); color: #9AA7BD; }
    .fr2-fi a:hover { color: #fff; }
    .fr2-fi .lang { display: flex; align-items: center; gap: calc(6 * var(--uc)); font-size: calc(11 * var(--uc)); color: #C7D2E4; }
    .fr2-fi .lang svg { width: calc(14 * var(--uc)); height: calc(14 * var(--uc)); }

    /* ── minimal (pages legales) ── */
    .fr2-fm {
      position: relative; z-index: 1;
      display: flex; align-items: center;
      padding: calc(14 * var(--uc)) 3.418cqw; border-top: 1px solid rgba(255,255,255,.07);
    }
    .fr2-fm img { height: calc(20 * var(--uc)); }
    .fr2-fm nav { margin-left: auto; display: flex; align-items: center; gap: calc(12 * var(--uc)); }
    .fr2-fm a { font-size: calc(10.5 * var(--uc)); color: #9AA7BD; }
    .fr2-fm a:hover { color: #fff; }
    .fr2-fm .sep { color: #3B4256; }

    @media (max-width: 900px) {
      .fr2-ft-grid { grid-template-columns: 1fr 1fr; }
      .fr2-fr-grid { grid-template-columns: 1fr 1fr; }
      .fr2-fr .safe { grid-column: 1 / -1; }
      .fr2-fi { flex-wrap: wrap; }
      .fr2-fm { flex-direction: column; gap: calc(10 * var(--uc)); }
      .fr2-fm nav { margin: 0; flex-wrap: wrap; justify-content: center; }
    }
  `]
})
export class Fr2FooterComponent {
  /** columns = accueil · rich = authentification/contact · inline = auto · minimal = legal */
  @Input() variant: 'columns' | 'rich' | 'inline' | 'minimal' = 'columns';
  /** Affiche les quatre mini-arguments sous le logo (capture « compte pret »). */
  @Input() perks = false;

  readonly legalLinks = [
    { t: 'Politique de confidentialité', to: '/fr/confidentialite' },
    { t: 'Mentions légales', to: '/fr/mentions-legales' },
    { t: 'RGPD', to: '/fr/rgpd' },
    { t: 'Cookies', to: '/fr/cookies' }
  ];
}
