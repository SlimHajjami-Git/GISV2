import { Component, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Fr2HeaderComponent, Fr2FooterComponent } from './fr2-chrome.component';

/**
 * TARIFS — reproduction de ECRANS SITE WEB CALYPSO/TARIFS.
 *
 * Maquette de reference : 1536 x 1024. Reperes mesures (depuis le bas de
 * l'en-tete, y=69) : fil d'Ariane 35, titre 42..82, sous-titre 97, mention
 * violette 124 ; cartes de 430 px a x 317 et 788, de y 166 a 791 ; badge
 * « RECOMMANDÉE » a cheval sur le bord haut ; separateur sous le prix a 298 ;
 * boite d'economie 343..394 ; 9 lignes de liste espacees de 28 px des 425 ;
 * bouton 678..761 ; bandeau final 820..929.
 *
 * Une seule offre, deux rythmes de paiement — decision commerciale posee plus
 * tot dans le projet : 3 EUR/mois en annuel, 4 EUR/mois en semestriel.
 */
@Component({
  selector: 'app-fr2-pricing',
  standalone: true,
  imports: [RouterLink, Fr2HeaderComponent, Fr2FooterComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr2-page">
      <app-fr2-header />
      <div class="fp">

      <nav class="fp-bc"><a routerLink="/fr">Accueil</a> <span class="s">›</span> <b>Tarifs</b></nav>

      <header class="fp-head">
        <h1>Des tarifs simples et transparents</h1>
        <p>Toutes les fonctionnalités Calypso sont incluses.</p>
        <p class="v">Sans surprise.</p>
      </header>

      <section class="fp-cards">
        <!-- ── ANNUEL (recommande) ── -->
        <article class="fp-card reco">
          <span class="badge">RECOMMANDÉE</span>
          <header>
            <span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><rect x="4" y="5" width="16" height="16" rx="2"/><path d="M4 10h16M9 3v4M15 3v4"/></svg></span>
            <h2 class="vio">ANNUEL</h2>
          </header>
          <div class="price"><span class="n">3&nbsp;€</span><span class="u">/ véhicule / mois</span></div>
          <div class="alt">36&nbsp;€ / véhicule / an</div>
          <div class="chip vio-chip">
            <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z" transform="rotate(45 12 12)" opacity="0"/><path d="M4 10V5h5l11 11-5 5zM7.5 8.5h.01"/></svg>
            Économisez 25&nbsp;%
          </div>
          <ul>
            @for (f of features; track f) {
              <li><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>{{ f }}</li>
            }
          </ul>
          <a routerLink="/inscription" class="cta grad">
            <b>ESSAYER GRATUITEMENT</b>
            <small>Sans carte bancaire</small>
          </a>
        </article>

        <!-- ── SEMESTRIEL ── -->
        <article class="fp-card">
          <header>
            <span class="ic blue"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.7"><rect x="4" y="5" width="16" height="16" rx="2"/><path d="M4 10h16M9 3v4M15 3v4"/></svg></span>
            <h2 class="blu">SEMESTRIEL</h2>
          </header>
          <div class="price"><span class="n">4&nbsp;€</span><span class="u">/ véhicule / mois</span></div>
          <div class="alt">24&nbsp;€ / véhicule / semestre</div>
          <div class="chip blu-chip">
            <svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M20 11a8 8 0 0 0-14.9-3M4 13a8 8 0 0 0 14.9 3"/><path d="M5 4v4h4M19 20v-4h-4"/></svg>
            Plus de flexibilité
          </div>
          <ul>
            @for (f of features; track f) {
              <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>{{ f }}</li>
            }
          </ul>
          <a routerLink="/inscription" class="cta line">
            <b>ESSAYER GRATUITEMENT</b>
            <small>Sans carte bancaire</small>
          </a>
        </article>
      </section>

      <!-- ══ BANDEAU FINAL ══ -->
      <section class="fp-strip">
        <div class="half">
          <span class="rk"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.7"><path d="M14 4c3 .5 5.5 3 6 6l-8.5 8.5-5.5-5.5zM6 15l-2 5 5-2M13.5 8.5a1.5 1.5 0 1 0 2 2"/></svg></span>
          <div>
            <h3>Essayez Calypso gratuitement pendant 7 jours</h3>
            <p>Accédez à toutes les fonctionnalités. Sans carte bancaire. Sans engagement.</p>
          </div>
        </div>
        <div class="half">
          <span class="rk gr"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg></span>
          <div>
            <h3>Vos données sont sécurisées</h3>
            <p>Confidentialité garantie</p>
          </div>
        </div>
      </section>

      </div>
      <app-fr2-footer variant="columns" />
    </div>
  `,
  styles: [`
    .fp {
      /* Maquette a 1536 px, meme plafond que les fonctionnalites. */
      --v: min(0.0651cqw, 1.05px);
      display: block; background: #05070F; color: #fff;
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      -webkit-font-smoothing: antialiased;
      padding: calc(28 * var(--v)) 0 calc(26 * var(--v));
    }
    .fp *, .fp *::before, .fp *::after { box-sizing: border-box; }
    .fp a { text-decoration: none; }

    /* ── Fil d'Ariane : y 35 depuis l'en-tete, aligne au conteneur ── */
    .fp-bc { width: 91.93cqw; margin: 0 auto; font-size: calc(13.5 * var(--v)); color: #9AA7BD; }
    .fp-bc a { color: #9AA7BD; }
    .fp-bc a:hover { color: #fff; }
    .fp-bc .s { margin: 0 calc(6 * var(--v)); color: #6B7A94; }
    .fp-bc b { color: #A78BFA; font-weight: 600; }

    /* ── Titre : 42..82, sous-titre 97, mention 124 — le tout centre ── */
    .fp-head { text-align: center; margin-top: calc(-14 * var(--v)); }
    .fp-head h1 { margin: 0; font-size: calc(40 * var(--v)); font-weight: 800; letter-spacing: -.03em; }
    .fp-head p { margin: calc(14 * var(--v)) 0 0; font-size: calc(16 * var(--v)); color: #C7D2E4; }
    .fp-head .v { margin-top: calc(6 * var(--v)); color: #A78BFA; }

    /* ── Cartes : 430 px a x 317 / 788 -> ecart central 41 ── */
    .fp-cards {
      display: flex; justify-content: center; gap: calc(41 * var(--v));
      margin-top: calc(42 * var(--v));
    }
    .fp-card {
      position: relative; width: calc(430 * var(--v));
      background: rgba(255,255,255,.024); border: 1px solid rgba(255,255,255,.09);
      border-radius: calc(14 * var(--v));
      padding: calc(29 * var(--v)) calc(32 * var(--v)) calc(30 * var(--v));
    }
    .fp-card.reco { border-color: rgba(167,139,250,.75); box-shadow: 0 0 calc(34 * var(--v)) rgba(139,92,246,.14); }
    .badge {
      position: absolute; top: calc(-13 * var(--v)); left: 50%; transform: translateX(-50%);
      background: linear-gradient(135deg,#7C3AED,#A78BFA); color: #fff;
      font-size: calc(12 * var(--v)); font-weight: 800; letter-spacing: .1em;
      padding: calc(6 * var(--v)) calc(16 * var(--v)); border-radius: calc(8 * var(--v));
      white-space: nowrap;
    }
    .fp-card header { display: flex; align-items: center; gap: calc(14 * var(--v)); }
    .fp-card .ic {
      width: calc(46 * var(--v)); height: calc(46 * var(--v)); border-radius: 50%;
      display: grid; place-items: center;
      background: rgba(139,92,246,.13); border: 1px solid rgba(167,139,250,.3);
    }
    .fp-card .ic.blue { background: rgba(59,130,246,.12); border-color: rgba(96,165,250,.3); }
    .fp-card .ic svg { width: calc(22 * var(--v)); height: calc(22 * var(--v)); }
    .fp-card h2 { margin: 0; font-size: calc(19 * var(--v)); font-weight: 800; letter-spacing: .08em; }
    .fp-card h2.vio { color: #A78BFA; }
    .fp-card h2.blu { color: #60A5FA; }

    /* Prix : « 3 € » ~44 px, unite grise ; petit prix annuel sous separateur. */
    .price { display: flex; align-items: baseline; gap: calc(10 * var(--v)); margin-top: calc(3 * var(--v)); }
    .price .n { font-size: calc(44 * var(--v)); font-weight: 800; letter-spacing: -.03em; line-height: 1; }
    .price .u { font-size: calc(14.5 * var(--v)); color: #9AA7BD; }
    .alt {
      margin-top: calc(8 * var(--v)); padding-top: calc(8 * var(--v));
      border-top: 1px solid rgba(255,255,255,.09);
      text-align: center; font-size: calc(15 * var(--v)); color: #C7D2E4;
    }
    /* Boite d'avantage : y 343..394 -> h 51. */
    .chip {
      margin-top: calc(16 * var(--v)); height: calc(51 * var(--v));
      display: flex; align-items: center; justify-content: center; gap: calc(9 * var(--v));
      border-radius: calc(9 * var(--v)); font-size: calc(15 * var(--v)); font-weight: 700;
    }
    .chip svg { width: calc(18 * var(--v)); height: calc(18 * var(--v)); }
    .vio-chip { border: 1px solid rgba(167,139,250,.4); background: rgba(139,92,246,.08); color: #C4B5FD; }
    .blu-chip { border: 1px solid rgba(96,165,250,.35); background: rgba(59,130,246,.07); color: #93C5FD; }

    /* Liste : 9 lignes espacees de 28 px, glyphes ~15 px. */
    .fp-card ul { list-style: none; margin: calc(30 * var(--v)) 0 0; padding: 0; }
    .fp-card li {
      display: flex; align-items: center; gap: calc(11 * var(--v));
      font-size: calc(14.5 * var(--v)); color: #C7D2E4;
      min-height: calc(28 * var(--v));
    }
    .fp-card li svg { width: calc(15 * var(--v)); height: calc(15 * var(--v)); flex: none; }

    /* Bouton : boite 678..761 -> h 83, deux lignes. */
    .cta {
      margin-top: calc(8 * var(--v)); min-height: calc(83 * var(--v));
      display: flex; flex-direction: column; align-items: center; justify-content: center;
      gap: calc(3 * var(--v)); border-radius: calc(10 * var(--v));
    }
    .cta b { font-size: calc(15.5 * var(--v)); font-weight: 800; letter-spacing: .05em; }
    .cta small { font-size: calc(12.5 * var(--v)); opacity: .85; }
    .cta.grad { background: linear-gradient(135deg,#4F46E5,#7C3AED); color: #fff; }
    .cta.line { border: 1px solid rgba(96,165,250,.5); color: #93C5FD; }

    /* ── Bandeau final : y 820..929, deux moities separees d'un trait ── */
    .fp-strip {
      width: 91.93cqw; min-height: calc(109 * var(--v)); margin: calc(29 * var(--v)) auto 0;
      background: rgba(255,255,255,.028); border: 1px solid rgba(255,255,255,.075);
      border-radius: calc(12 * var(--v));
      display: grid; grid-template-columns: 1.55fr 1fr;
    }
    .fp-strip .half {
      display: flex; align-items: center; gap: calc(18 * var(--v));
      padding: calc(18 * var(--v)) calc(30 * var(--v));
    }
    .fp-strip .half + .half { border-left: 1px solid rgba(255,255,255,.075); }
    .fp-strip .rk {
      width: calc(48 * var(--v)); height: calc(48 * var(--v)); border-radius: 50%; flex: none;
      background: rgba(255,255,255,.06); border: 1px solid rgba(255,255,255,.1);
      display: grid; place-items: center;
    }
    .fp-strip .rk.gr { background: rgba(52,211,153,.1); border-color: rgba(52,211,153,.3); }
    .fp-strip .rk svg { width: calc(23 * var(--v)); height: calc(23 * var(--v)); }
    .fp-strip h3 { margin: 0; font-size: calc(19 * var(--v)); font-weight: 800; letter-spacing: -.01em; }
    .fp-strip p { margin: calc(6 * var(--v)) 0 0; font-size: calc(13.5 * var(--v)); color: #9AA7BD; }

    @media (max-width: 900px) {
      .fp-cards { flex-direction: column; align-items: center; gap: calc(34 * var(--v)); }
      .fp-card { width: min(calc(430 * var(--v)) * 2, 92cqw); }
      .fp-strip { grid-template-columns: 1fr; }
      .fp-strip .half + .half { border-left: 0; border-top: 1px solid rgba(255,255,255,.075); }
    }
  `]
})
export class Fr2PricingComponent {
  readonly features = [
    'Entretiens & échéances',
    'Réparations',
    'Carburant & consommation',
    'Dépenses & budget',
    'Tableau de bord & pilotage',
    'Intelligence Calypso',
    'Toutes les fonctionnalités Calypso',
    'Mises à jour incluses',
    '7 jours gratuits'
  ];
}
