import { Component, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * Page témoin : reproduction de la capture ACCUEIL.png, à comparer avant
 * adoption.
 *
 * <p><b>Canevas FIXE de 1024 px.</b> La capture est dessinée à cette largeur ;
 * toutes les valeurs ci-dessous en sont relevées au pixel — conteneur de
 * 953 px centré (marges de 35), panneaux de 62 / 302 / 156 / 124 / 79 / 102 px
 * de haut, séparés de 14 px. Une mise en page fluide ne peut pas être comparée
 * à une image : à chaque largeur elle donnerait un résultat différent, et
 * « identique » n'aurait plus de sens. Sous 1024 px la page se réduit
 * proportionnellement, ce qui préserve les rapports.</p>
 *
 * <p>Mesures faites en détectant les bandes de texte clair sur le fond nuit et
 * les bordures des panneaux, pas à l'œil.</p>
 */
@Component({
  selector: 'app-test-accueil',
  standalone: true,
  imports: [RouterLink],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="tc-scale">
      <div class="tc">

        <!-- ══ EN-TÊTE — hauteur 58 ══ -->
        <header class="tc-hd">
          <a class="tc-brand" routerLink="/fr">
            <img src="/assets/calypso-logo.svg" alt="Calypso">
          </a>
          <nav class="tc-nav">
            <a routerLink="/fr/fonctionnalites">Fonctionnalités</a>
            <a routerLink="/fr/tarifs">Tarifs</a>
            <a routerLink="/fr/calypso-auto">Calypso Auto</a>
            <a routerLink="/fr/contact">Contact</a>
          </nav>
          <div class="tc-hd-right">
            <a routerLink="/login" class="tc-signin">Se connecter</a>
            <a routerLink="/inscription" class="tc-cta">Essayer gratuitement</a>
          </div>
        </header>

        <!-- ══ HERO ══ -->
        <section class="tc-hero">
          <div class="tc-hero-txt">
            <span class="tc-pill">LOGICIEL DE GESTION DE PARC AUTOMOBILE</span>
            <h1>
              Gérez votre parc.<br>
              Maîtrisez vos coûts.<br>
              <span class="g">Anticipez</span> vos entretiens.
            </h1>
            <p class="tc-lede">
              Calypso centralise vos véhicules, entretiens, réparations,<br>
              carburant, dépenses et échéances dans une seule<br>
              plateforme intelligente.
            </p>
            <p class="tc-ai">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/></svg>
              Intelligence artificielle intégrée
            </p>
            <div class="tc-btns">
              <a routerLink="/inscription" class="tc-b1">ESSAYER GRATUITEMENT</a>
              <a href="#tc-produit" class="tc-b2">DÉCOUVRIR CALYPSO</a>
            </div>
            <ul class="tc-badges">
              <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>7 jours gratuits</li>
              <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans carte bancaire</li>
              <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans engagement</li>
            </ul>
          </div>

          <div class="tc-diag" aria-label="Les six univers couverts par Calypso">
            <span class="tc-ring r1"></span>
            <span class="tc-ring r2"></span>
            <span class="tc-glow"></span>
            <div class="tc-core">
              <img src="/assets/calypso-logo.svg" alt="Calypso">
            </div>
            <div class="tc-node n1"><span class="i" style="border-color:rgba(96,165,250,.5);background:rgba(59,130,246,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg></span><span class="l">ENTRETIEN</span></div>
            <div class="tc-node n2"><span class="i" style="border-color:rgba(52,211,153,.5);background:rgba(52,211,153,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg></span><span class="l">CARBURANT</span></div>
            <div class="tc-node n3"><span class="i" style="border-color:rgba(167,139,250,.5);background:rgba(139,92,246,.15)"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="12" cy="12" r="3.2"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1"/></svg></span><span class="l">RÉPARATION</span></div>
            <div class="tc-node n4"><span class="i" style="border-color:rgba(96,165,250,.5);background:rgba(59,130,246,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span><span class="l">ÉCHÉANCE</span></div>
            <div class="tc-node n5"><span class="i" style="border-color:rgba(236,72,153,.45);background:rgba(236,72,153,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="1.8"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg></span><span class="l">IA</span></div>
            <div class="tc-node n6"><span class="i" style="border-color:rgba(129,140,248,.5);background:rgba(99,102,241,.15)"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.8"><path d="M3 13h18M5 13l1.6-5.2A2 2 0 0 1 8.5 6.4h7a2 2 0 0 1 1.9 1.4L19 13v5h-2.2v-2H7.2v2H5z"/><circle cx="7.8" cy="16" r="1.1"/><circle cx="16.2" cy="16" r="1.1"/></svg></span><span class="l">PARC</span></div>
          </div>
        </section>

        <!-- ══ BANDEAU DES UNIVERS — panneau h=62 ══ -->
        <div class="tc-panel tc-strip">
          <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg><span>ENTRETIENS</span></a>
          <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="12" cy="12" r="3.2"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1"/></svg><span>RÉPARATIONS</span></a>
          <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg><span>CARBURANT</span></a>
          <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.8"><path d="M12 2v20M17 6H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg><span>DÉPENSES</span></a>
          <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg><span>ÉCHÉANCES</span></a>
          <a routerLink="/fr/calypso-auto"><svg viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="1.8"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg><span>IA</span></a>
        </div>

        <!-- ══ PRODUIT RÉEL — panneau h=302 ══ -->
        <div class="tc-panel tc-prod" id="tc-produit">
          <div class="tc-prod-txt">
            <h2>Tout votre parc.<br>Un seul endroit.</h2>
            <p>Découvrez Calypso à travers<br>son interface réelle.</p>
          </div>
          <div class="tc-prod-shot">
            <img src="/assets/france/produit-tableau-de-bord-sombre.webp"
                 alt="Tableau de bord Calypso : véhicules, coût total, consommation, alertes,
                      répartition des coûts, entretiens et échéances à venir.">
          </div>
        </div>

        <!-- ══ ANTICIPATION — panneau h=156 ══ -->
        <div class="tc-panel tc-anti">
          <div class="tc-anti-txt">
            <h2>Ne vous contentez<br>plus de suivre.<br><span class="g2">Anticipez.</span></h2>
            <p>Calypso analyse les données de votre parc pour vous aider à détecter les anomalies, anticiper les entretiens et mieux maîtriser vos coûts.</p>
          </div>
          <div class="tc-anti-card">
            <span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span>
            <h3>ANTICIPER</h3>
            <p>Entretiens et échéances<br>à venir.</p>
          </div>
          <div class="tc-anti-card">
            <span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#F59E0B" stroke-width="1.8"><path d="M12 4.2 21 19H3z"/><path d="M12 10v4"/></svg></span>
            <h3>DÉTECTER</h3>
            <p>Anomalies et situations<br>nécessitant votre attention.</p>
          </div>
          <div class="tc-anti-card">
            <span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M4 20V9M10 20V4M16 20v-7M22 20H2"/></svg></span>
            <h3>COMPRENDRE</h3>
            <p>Coûts, dépenses, consommation<br>et informations du parc.</p>
          </div>
        </div>

        <!-- ══ APERÇU TARIFAIRE — panneau h=124 ══ -->
        <div class="tc-panel tc-price">
          <h2>Une gestion complète.<br>Un tarif simple.</h2>
          <div class="tc-price-n">
            <span class="from">À partir de</span>
            <span class="n">3 €</span>
            <span class="u">/ véhicule / mois</span>
          </div>
          <p class="tc-price-txt">Toutes les fonctionnalités essentielles pour gérer votre parc, réunies dans une seule solution.</p>
          <ul class="tc-price-list">
            <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>7 jours gratuits</li>
            <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans carte bancaire</li>
            <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans engagement pendant l'essai</li>
          </ul>
          <div class="tc-price-cta">
            <a routerLink="/inscription" class="tc-b1">ESSAYER GRATUITEMENT</a>
            <a routerLink="/fr/tarifs" class="tc-b2">VOIR LES TARIFS</a>
          </div>
        </div>

        <!-- ══ MISE EN ROUTE — panneau h=79 ══ -->
        <div class="tc-panel tc-start">
          <h2>Commencez<br>simplement.</h2>
          <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.7"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span><div><h3>7 JOURS GRATUITS</h3><p>Découvrez Calypso avec<br>votre propre parc.</p></div></div>
          <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.7"><rect x="2.5" y="5" width="19" height="14" rx="2.5"/><path d="M2.5 10h19"/></svg></span><div><h3>SANS CARTE BANCAIRE</h3><p>Aucun moyen de paiement<br>demandé pour commencer.</p></div></div>
          <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M5 15.5 8.5 19M4.5 19.5 9 15M14 4.5c3.5-1.5 6 1 4.5 4.5-1.2 2.8-4.6 6-8 8.5L7 14C9.5 10.6 11.2 5.7 14 4.5z"/></svg></span><div><h3>MISE EN ROUTE RAPIDE</h3><p>Créez votre compte et<br>ajoutez vos véhicules.</p></div></div>
          <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.7"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg></span><div><h3>VOS DONNÉES RESTENT LES VÔTRES</h3><p>Vos données sont sécurisées<br>et restent votre propriété.</p></div></div>
        </div>

        <!-- ══ APPEL FINAL — panneau h=102 ══ -->
        <div class="tc-panel tc-final">
          <h2>Prêt à simplifier la gestion<br>de votre parc ?</h2>
          <p>Découvrez Calypso gratuitement<br>pendant 7 jours.</p>
          <div class="tc-final-cta">
            <a routerLink="/inscription" class="tc-b1">ESSAYER GRATUITEMENT</a>
            <small>Sans carte bancaire &nbsp;•&nbsp; Sans engagement</small>
          </div>
        </div>

        <!-- ══ PIED DE PAGE ══ -->
        <footer class="tc-foot">
          <div class="tc-foot-grid">
            <div>
              <img class="fl" src="/assets/calypso-logo.svg" alt="Calypso">
              <p>La solution intelligente pour<br>gérer votre parc automobile.</p>
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
          <div class="tc-foot-b">© 2026 Calypso. Tous droits réservés.</div>
        </footer>

      </div>
    </div>
  `,
  styles: [`
    /* ================================================================
       Page témoin — valeurs relevées sur ACCUEIL.png (1024 × 1536).
       Fidèle au pixel à 1024 px, pleine largeur au-delà.
       ================================================================ */
    .tc-scale {
      background: #05070F; min-height: 100vh; width: 100%;
      /* Deux familles de valeurs, volontairement distinctes :
         - en % (cqw) : largeurs de colonnes et de panneaux, pour que la page
           occupe tout l ecran sans bande vide ;
         - en unites (--u) : typographie, hauteurs, rayons, cercles.
         A 1024 px, 1 unite vaut 1 cqw : le rendu est celui de la maquette. */
      container-type: inline-size;
      /* 1 px de la maquette = 1 unite. L unite suit la largeur (la page occupe
         donc tout l ecran) mais cesse de grandir a 1400 px : au-dela, ce sont
         les colonnes qui s etirent, pas la taille du texte. */
      --u: min(1cqw, 14px);
    }
    .tc {
      width: 100%; position: relative;
      background: #05070F; color: #fff;
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      -webkit-font-smoothing: antialiased;
    }
    .tc *, .tc *::before, .tc *::after { box-sizing: border-box; }
    .tc a { text-decoration: none; }

    /* ── EN-TÊTE : 58 px ── */
    .tc-hd {
      height: calc(5.6641 * var(--u)); display: flex; align-items: center;
      padding: 0 3.418cqw; border-bottom: 1px solid rgba(255,255,255,.07);
    }
    .tc-brand img { height: calc(2.5391 * var(--u)); width: auto; display: block; }
    .tc-nav { display: flex; gap: calc(2.5391 * var(--u)); margin: 0 auto; }
    .tc-nav a { color: #9AA7BD; font-size: calc(1.2207 * var(--u)); font-weight: 600; }
    .tc-nav a:hover { color: #fff; }
    .tc-hd-right { display: flex; align-items: center; gap: calc(1.7578 * var(--u)); }
    .tc-signin { color: #fff; font-size: calc(1.2207 * var(--u)); font-weight: 600; }
    .tc-cta {
      background: linear-gradient(135deg,#4F46E5,#7C3AED); color: #fff;
      font-size: calc(1.123 * var(--u)); font-weight: 700; padding: calc(0.7813 * var(--u)) calc(1.3672 * var(--u)); border-radius: calc(0.7813 * var(--u));
    }

    /* ── HERO : pilule y≈102, titre 145/192/241, boutons ≈416, badges ≈469 ── */
    .tc-hero { position: relative; min-height: calc(42.5781 * var(--u));
      padding: calc(3.9453 * var(--u)) 3.418cqw 0; }
    .tc-hero-txt { width: 45.8984cqw; }
    .tc-pill {
      display: inline-block; border: 1px solid rgba(96,165,250,.34);
      background: rgba(59,130,246,.10); color: #60A5FA;
      border-radius: calc(9.668 * var(--u)); padding: calc(0.4883 * var(--u)) calc(1.2695 * var(--u));
      font-size: calc(0.9277 * var(--u)); font-weight: 700; letter-spacing: .1em;
    }
    .tc h1 {
      margin: calc(1.1133 * var(--u)) 0 0; font-size: calc(3.418 * var(--u)); line-height: calc(4.6875 * var(--u));
      font-weight: 800; letter-spacing: -.03em; color: #fff;
    }
    .tc h1 .g { background: linear-gradient(90deg,#7C7CF8,#A78BFA);
      -webkit-background-clip: text; background-clip: text; color: transparent; }
    .tc-lede { margin: calc(0.9766 * var(--u)) 0 0; font-size: calc(1.3672 * var(--u)); line-height: calc(2.0508 * var(--u)); color: #9AA7BD; }
    .tc-ai {
      display: flex; align-items: center; gap: calc(0.7813 * var(--u)); margin: calc(1.1621 * var(--u)) 0 0;
      font-size: calc(1.3184 * var(--u)); font-weight: 700; color: #7C7CF8;
    }
    .tc-ai svg { width: calc(1.5625 * var(--u)); height: calc(1.5625 * var(--u)); }
    .tc-btns { display: flex; gap: calc(1.1719 * var(--u)); margin-top: calc(2.3242 * var(--u)); }
    .tc-b1, .tc-b2 {
      display: inline-flex; align-items: center; justify-content: center;
      height: calc(3.9063 * var(--u)); padding: 0 calc(1.9531 * var(--u)); border-radius: calc(0.7813 * var(--u));
      font-size: calc(1.0742 * var(--u)); font-weight: 700; letter-spacing: .05em;
    }
    .tc-b1 { background: linear-gradient(135deg,#4F46E5,#7C3AED); color: #fff; }
    .tc-b2 { border: 1px solid rgba(255,255,255,.18); color: #fff; }
    .tc-badges { display: flex; gap: calc(1.9531 * var(--u)); list-style: none; margin: calc(1.5527 * var(--u)) 0 0; padding: 0; }
    .tc-badges li { display: flex; align-items: center; gap: calc(0.5859 * var(--u)); font-size: calc(1.123 * var(--u)); color: #9AA7BD; }
    .tc-badges svg { width: calc(1.2695 * var(--u)); height: calc(1.2695 * var(--u)); flex: none; }

    /* ── DIAGRAMME : x 520..990, y 75..470 ── */
    .tc-diag { position: absolute; left: 50.7813cqw;
      margin-left: calc((44.9219cqw - 44.9219 * var(--u)) / 2); top: calc(1.709 * var(--u)); width: calc(44.9219 * var(--u)); height: calc(36.6797 * var(--u)); }
    .tc-ring { position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%);
      border-radius: 50%; border: 1px solid rgba(96,165,250,.16); }
    .tc-ring.r1 { width: calc(29.2969 * var(--u)); height: calc(29.2969 * var(--u)); border-color: rgba(139,92,246,.22); }
    .tc-ring.r2 { width: calc(21.4844 * var(--u)); height: calc(21.4844 * var(--u)); border-style: dashed; border-color: rgba(148,163,184,.14); }
    .tc-glow {
      position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%);
      width: calc(23.4375 * var(--u)); height: calc(23.4375 * var(--u)); border-radius: 50%;
      background: radial-gradient(circle, rgba(79,70,229,.26) 0%, transparent 68%);
    }
    .tc-core { position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%); }
    .tc-core img { width: calc(14.6484 * var(--u)); height: auto; display: block; }
    .tc-node {
      position: absolute; transform: translate(-50%,-50%);
      display: flex; flex-direction: column; align-items: center; gap: calc(0.6836 * var(--u));
    }
    .tc-node .i {
      width: calc(4.4922 * var(--u)); height: calc(4.4922 * var(--u)); border-radius: 50%;
      display: grid; place-items: center; border: 1.5px solid;
    }
    .tc-node .i svg { width: calc(2.0508 * var(--u)); height: calc(2.0508 * var(--u)); }
    .tc-node .l { font-size: calc(0.8789 * var(--u)); font-weight: 800; letter-spacing: .1em; color: #9AA7BD; white-space: nowrap; }
    .tc-node.n1 { left: 50%;  top: 8%;  }
    .tc-node.n2 { left: 86%;  top: 29%; }
    .tc-node.n3 { left: 86%;  top: 71%; }
    .tc-node.n4 { left: 50%;  top: 92%; }
    .tc-node.n5 { left: 14%;  top: 71%; }
    .tc-node.n6 { left: 14%;  top: 29%; }

    /* ── PANNEAUX : 953 px de large, 14 px d'écart ── */
    .tc-panel {
      width: 93.0664cqw; margin: 0 auto calc(1.3672 * var(--u));
      background: rgba(255,255,255,.028);
      border: 1px solid rgba(255,255,255,.075);
      border-radius: calc(1.3672 * var(--u));
    }

    /* Bandeau : h 62 */
    .tc-strip { min-height: calc(6.0547 * var(--u)); display: grid; grid-template-columns: repeat(6,1fr); }
    .tc-strip a {
      display: flex; align-items: center; justify-content: center; gap: calc(0.7813 * var(--u));
      color: #9AA7BD; font-size: calc(1.0742 * var(--u)); font-weight: 700; letter-spacing: .06em;
      border-right: 1px solid rgba(255,255,255,.075);
    }
    .tc-strip a:last-child { border-right: 0; }
    .tc-strip a:hover { color: #fff; }
    .tc-strip svg { width: calc(1.6602 * var(--u)); height: calc(1.6602 * var(--u)); flex: none; }

    /* Produit : h 302 */
    .tc-prod { min-height: calc(29.4922 * var(--u)); display: grid; grid-template-columns: 24.4141cqw 1fr; gap: calc(2.1484 * var(--u)); padding: calc(2.1484 * var(--u)); }
    .tc-prod-txt { align-self: center; }
    .tc-prod-txt h2 { margin: 0; font-size: calc(2.1484 * var(--u)); line-height: calc(2.832 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .tc-prod-txt p { margin: calc(1.9531 * var(--u)) 0 0; font-size: calc(1.2695 * var(--u)); line-height: calc(1.9531 * var(--u)); color: #9AA7BD; }
    .tc-prod-shot { height: calc(25.1953 * var(--u)); overflow: hidden; border-radius: calc(0.9766 * var(--u)); border: 1px solid rgba(255,255,255,.12); }
    .tc-prod-shot img { width: 100%; height: 100%; object-fit: cover; object-position: top left; display: block; }

    /* Anticipation : h 156, texte + 3 cartes */
    .tc-anti { min-height: calc(15.2344 * var(--u)); display: grid; grid-template-columns: 34.668cqw repeat(3,1fr); gap: calc(1.3672 * var(--u)); padding: calc(1.7578 * var(--u)); }
    .tc-anti-txt h2 { margin: 0; font-size: calc(1.8555 * var(--u)); line-height: calc(2.4414 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .tc-anti-txt h2 .g2 { color: #C084FC; }
    .tc-anti-txt p { margin: calc(1.1719 * var(--u)) 0 0; font-size: calc(1.0254 * var(--u)); line-height: calc(1.4648 * var(--u)); color: #9AA7BD; }
    .tc-anti-card {
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.07);
      border-radius: calc(0.9766 * var(--u)); padding: calc(1.3672 * var(--u)) calc(1.1719 * var(--u)); text-align: center;
    }
    .tc-anti-card .ic { display: block; width: calc(2.5391 * var(--u)); height: calc(2.5391 * var(--u)); margin: calc(0.1953 * var(--u)) auto calc(0.9766 * var(--u)); }
    .tc-anti-card .ic svg { width: calc(2.5391 * var(--u)); height: calc(2.5391 * var(--u)); }
    .tc-anti-card h3 { margin: 0 0 calc(0.6836 * var(--u)); font-size: calc(1.0254 * var(--u)); font-weight: 800; letter-spacing: .06em; }
    .tc-anti-card p { margin: 0; font-size: calc(0.9277 * var(--u)); line-height: calc(1.3672 * var(--u)); color: #9AA7BD; }

    /* Aperçu tarifaire : h 124 */
    .tc-price {
      min-height: calc(12.1094 * var(--u)); display: grid;
      grid-template-columns: 26.3672cqw 11.4258cqw 1fr auto 14.3555cqw; gap: calc(1.7578 * var(--u));
      align-items: center; padding: 0 calc(2.1484 * var(--u));
      background: linear-gradient(115deg, rgba(79,70,229,.24), rgba(124,58,237,.14));
      border-color: rgba(139,92,246,.3);
    }
    .tc-price h2 { margin: 0; font-size: calc(1.9531 * var(--u)); line-height: calc(2.5391 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .tc-price-n { text-align: center; }
    .tc-price-n .from { display: block; font-size: calc(1.0742 * var(--u)); color: #9AA7BD; }
    .tc-price-n .n { display: block; font-size: calc(3.8086 * var(--u)); font-weight: 800; letter-spacing: -.04em; line-height: 1.05; }
    .tc-price-n .u { display: block; font-size: calc(1.0742 * var(--u)); color: #9AA7BD; }
    .tc-price-txt { margin: 0; font-size: calc(1.0742 * var(--u)); line-height: calc(1.5625 * var(--u)); color: #9AA7BD; }
    .tc-price-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: calc(0.6836 * var(--u)); }
    .tc-price-list li { display: flex; align-items: center; gap: calc(0.6836 * var(--u)); font-size: calc(1.0254 * var(--u)); color: #C7D2E4; white-space: nowrap; }
    .tc-price-list svg { width: calc(1.1719 * var(--u)); height: calc(1.1719 * var(--u)); flex: none; }
    .tc-price-cta { display: flex; flex-direction: column; gap: calc(0.7813 * var(--u)); }

    /* Mise en route : h 79 */
    .tc-start { min-height: calc(7.7148 * var(--u)); display: grid; grid-template-columns: 18.0664cqw repeat(4,1fr); gap: calc(1.3672 * var(--u)); align-items: center; padding: 0 calc(1.9531 * var(--u)); }
    .tc-start h2 { margin: 0; font-size: calc(1.8555 * var(--u)); line-height: calc(2.3438 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .tc-start .it { display: flex; gap: calc(0.8789 * var(--u)); align-items: flex-start; }
    .tc-start .ic { width: calc(2.1484 * var(--u)); height: calc(2.1484 * var(--u)); flex: none; }
    .tc-start .ic svg { width: calc(2.1484 * var(--u)); height: calc(2.1484 * var(--u)); }
    .tc-start h3 { margin: 0 0 calc(0.3906 * var(--u)); font-size: calc(0.8789 * var(--u)); font-weight: 800; letter-spacing: .05em; }
    .tc-start p { margin: 0; font-size: calc(0.8789 * var(--u)); line-height: calc(1.2695 * var(--u)); color: #9AA7BD; }

    /* Appel final : h 102 */
    .tc-final { min-height: calc(9.9609 * var(--u)); display: grid; grid-template-columns: 1fr 29.2969cqw 23.4375cqw; gap: calc(2.1484 * var(--u)); align-items: center; padding: 0 calc(2.5391 * var(--u)); }
    .tc-final h2 { margin: 0; font-size: calc(2.0508 * var(--u)); line-height: calc(2.7344 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .tc-final > p { margin: 0; font-size: calc(1.2695 * var(--u)); line-height: calc(1.8555 * var(--u)); color: #9AA7BD; }
    .tc-final-cta { display: flex; flex-direction: column; align-items: center; gap: calc(0.7813 * var(--u)); }
    .tc-final-cta small { font-size: calc(0.9766 * var(--u)); color: #6B7A94; white-space: nowrap; }

    /* Pied de page */
    .tc-foot { padding: calc(1.7578 * var(--u)) 3.418cqw calc(1.3672 * var(--u)); border-top: 1px solid rgba(255,255,255,.07); }
    .tc-foot-grid { display: grid; grid-template-columns: 1.6fr 1fr 1fr 1.3fr; gap: calc(2.1484 * var(--u)); }
    .tc-foot .fl { height: calc(2.1484 * var(--u)); width: auto; display: block; margin-bottom: calc(0.8789 * var(--u)); }
    .tc-foot p { margin: 0; font-size: calc(1.0254 * var(--u)); line-height: calc(1.4648 * var(--u)); color: #6B7A94; }
    .tc-foot h4 { margin: 0 0 calc(0.7813 * var(--u)); font-size: calc(0.9766 * var(--u)); font-weight: 800; letter-spacing: .1em; color: #A78BFA; }
    .tc-foot-grid a { display: block; font-size: calc(1.0742 * var(--u)); line-height: calc(1.4648 * var(--u)); color: #9AA7BD; margin-bottom: calc(0.4883 * var(--u)); }
    .tc-foot-grid a:hover { color: #fff; }
    .tc-foot-b { margin-top: calc(1.3672 * var(--u)); padding-top: calc(1.1719 * var(--u)); border-top: 1px solid rgba(255,255,255,.07); font-size: calc(1.0254 * var(--u)); color: #6B7A94; }

    /* Sous 700 px, la maquette de bureau ne tient plus : on rend la main à une
       pile verticale plutôt que de réduire jusqu'à l'illisible. Une réduction
       proportionnelle jusqu'au téléphone donnerait un titre de 12 px. */
    @media (max-width: 700px) {
      .tc-hero { min-height: 0; padding-bottom: calc(4 * var(--u)); }
      .tc-hero-txt, .tc-diag { position: static; width: auto; }
      .tc-diag { height: calc(70 * var(--u)); margin-top: calc(6 * var(--u)); }
      .tc-strip, .tc-prod, .tc-anti, .tc-price, .tc-start, .tc-final { min-height: 0; }
      .tc-strip { grid-template-columns: repeat(3, 1fr); }
      .tc-strip a:nth-child(3) { border-right: 0; }
      .tc-prod, .tc-anti, .tc-price, .tc-start, .tc-final { grid-template-columns: 1fr; }
      .tc-prod, .tc-price, .tc-start, .tc-final { padding: calc(4 * var(--u)); }
      .tc-foot-grid { grid-template-columns: 1fr 1fr; }
    }
  `]
})
export class TestAccueilComponent {}
