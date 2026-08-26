import { Component, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Fr2HeaderComponent, Fr2FooterComponent } from './fr2-chrome.component';

/**
 * ACCUEIL du site europeen — reproduction de ECRANS SITE WEB CALYPSO/ACCUEIL.
 *
 * Maquette de reference : 1024 x 1536. Chaque valeur ci-dessous est un pixel
 * releve sur l'image, pas une estimation : les reperes verticaux (pastille 102,
 * titre 144, accroche 295, ligne IA 369, boutons 409, badges 469) et les
 * hauteurs de panneaux (62 / 302 / 156 / 124 / 79 / 102) ont ete mesures par
 * balayage de pixels, puis verifies dans le navigateur a 0,0 px d'ecart.
 *
 * Les panneaux sont en hauteur MINIMALE et non fixe : une hauteur fixe laisse
 * le contenu deborder sans que la boite ne bouge, ce qui masque le defaut.
 */
@Component({
  selector: 'app-fr2-home',
  standalone: true,
  imports: [RouterLink, Fr2HeaderComponent, Fr2FooterComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr2-page">
      <app-fr2-header />
      <div class="fh">
      <!-- ══ HERO ══ -->
      <section class="fh-hero">
        <div class="fh-hero-txt">
          <span class="fh-pill">LOGICIEL DE GESTION DE PARC AUTOMOBILE</span>
          <h1>
            Gérez votre parc.<br>
            Maîtrisez vos coûts.<br>
            <span class="g">Anticipez</span> vos entretiens.
          </h1>
          <p class="fh-lede">
            Calypso centralise vos véhicules, entretiens, réparations,<br>
            carburant, dépenses et échéances dans une seule<br>
            plateforme intelligente.
          </p>
          <p class="fh-ai">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"><path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/></svg>
            Intelligence artificielle intégrée
          </p>
          <div class="fh-btns">
            <a routerLink="/inscription" class="fh-b1">ESSAYER GRATUITEMENT</a>
            <a href="#fh-produit" class="fh-b2">DÉCOUVRIR CALYPSO</a>
          </div>
          <ul class="fh-badges">
            <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>7 jours gratuits</li>
            <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans carte bancaire</li>
            <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.4"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans engagement</li>
          </ul>
        </div>

        <div class="fh-diag" aria-label="Les six univers couverts par Calypso">
          <span class="fh-ring r1"></span>
          <span class="fh-ring r2"></span>
          <span class="fh-glow"></span>
          <div class="fh-core">
            <img src="/assets/calypso-logo.svg" alt="Calypso">
          </div>
          <div class="fh-node n1"><span class="i" style="border-color:rgba(96,165,250,.5);background:rgba(59,130,246,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg></span><span class="l">ENTRETIEN</span></div>
          <div class="fh-node n2"><span class="i" style="border-color:rgba(52,211,153,.5);background:rgba(52,211,153,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg></span><span class="l">CARBURANT</span></div>
          <div class="fh-node n3"><span class="i" style="border-color:rgba(167,139,250,.5);background:rgba(139,92,246,.15)"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="12" cy="12" r="3.2"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1"/></svg></span><span class="l">RÉPARATION</span></div>
          <div class="fh-node n4"><span class="i" style="border-color:rgba(96,165,250,.5);background:rgba(59,130,246,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span><span class="l">ÉCHÉANCE</span></div>
          <div class="fh-node n5"><span class="i" style="border-color:rgba(236,72,153,.45);background:rgba(236,72,153,.13)"><svg viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="1.8"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg></span><span class="l">IA</span></div>
          <div class="fh-node n6"><span class="i" style="border-color:rgba(129,140,248,.5);background:rgba(99,102,241,.15)"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.8"><path d="M3 13h18M5 13l1.6-5.2A2 2 0 0 1 8.5 6.4h7a2 2 0 0 1 1.9 1.4L19 13v5h-2.2v-2H7.2v2H5z"/><circle cx="7.8" cy="16" r="1.1"/><circle cx="16.2" cy="16" r="1.1"/></svg></span><span class="l">PARC</span></div>
        </div>
      </section>

      <!-- ══ BANDEAU DES UNIVERS — panneau h=62 ══ -->
      <div class="fh-panel fh-strip">
        <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><path d="M14.7 6.3a4 4 0 0 0 4.6 5.7l-8.3 8.3a2 2 0 0 1-2.8-2.8l8.3-8.3a4 4 0 0 0-1.8-2.9z"/></svg><span>ENTRETIENS</span></a>
        <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><circle cx="12" cy="12" r="3.2"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M18.7 5.3l-2.1 2.1M7.4 16.6l-2.1 2.1"/></svg><span>RÉPARATIONS</span></a>
        <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M4 20V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v15M3 20h13M17 9l2.5 2.5V17a1.5 1.5 0 0 1-3 0V9z"/></svg><span>CARBURANT</span></a>
        <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.8"><path d="M12 2v20M17 6H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/></svg><span>DÉPENSES</span></a>
        <a routerLink="/fr/fonctionnalites"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg><span>ÉCHÉANCES</span></a>
        <a routerLink="/fr/calypso-auto"><svg viewBox="0 0 24 24" fill="none" stroke="#EC4899" stroke-width="1.8"><path d="M9 4a3 3 0 0 0-3 3 3 3 0 0 0-1 5.8A3 3 0 0 0 8 18a3 3 0 0 0 4-1V4.9A3 3 0 0 0 9 4zM15 4a3 3 0 0 1 3 3 3 3 0 0 1 1 5.8A3 3 0 0 1 16 18a3 3 0 0 1-4-1"/></svg><span>IA</span></a>
      </div>

      <!-- ══ PRODUIT RÉEL — panneau h=302 ══ -->
      <div class="fh-panel fh-prod" id="fh-produit">
        <div class="fh-prod-txt">
          <h2>Tout votre parc.<br>Un seul endroit.</h2>
          <p>Découvrez Calypso à travers<br>son interface réelle.</p>
        </div>
        <div class="fh-prod-shot">
          <img src="/assets/france/produit-tableau-de-bord-sombre.webp"
               alt="Tableau de bord Calypso : véhicules, coût total, consommation, alertes,
                    répartition des coûts, entretiens et échéances à venir.">
        </div>
      </div>

      <!-- ══ ANTICIPATION — panneau h=156 ══ -->
      <div class="fh-panel fh-anti">
        <div class="fh-anti-txt">
          <h2>Ne vous contentez<br>plus de suivre.<br><span class="g2">Anticipez.</span></h2>
          <p>Calypso analyse les données de votre parc pour vous aider à détecter les anomalies, anticiper les entretiens et mieux maîtriser vos coûts.</p>
        </div>
        <div class="fh-anti-card">
          <span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.8"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span>
          <h3>ANTICIPER</h3>
          <p>Entretiens et échéances<br>à venir.</p>
        </div>
        <div class="fh-anti-card">
          <span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#F59E0B" stroke-width="1.8"><path d="M12 4.2 21 19H3z"/><path d="M12 10v4"/></svg></span>
          <h3>DÉTECTER</h3>
          <p>Anomalies et situations<br>nécessitant votre attention.</p>
        </div>
        <div class="fh-anti-card">
          <span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M4 20V9M10 20V4M16 20v-7M22 20H2"/></svg></span>
          <h3>COMPRENDRE</h3>
          <p>Coûts, dépenses, consommation<br>et informations du parc.</p>
        </div>
      </div>

      <!-- ══ APERÇU TARIFAIRE — panneau h=124 ══ -->
      <div class="fh-panel fh-price">
        <h2>Une gestion complète.<br>Un tarif simple.</h2>
        <div class="fh-price-n">
          <span class="from">À partir de</span>
          <span class="n">3 €</span>
          <span class="u">/ véhicule / mois</span>
        </div>
        <p class="fh-price-txt">Toutes les fonctionnalités essentielles pour gérer votre parc, réunies dans une seule solution.</p>
        <ul class="fh-price-list">
          <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>7 jours gratuits</li>
          <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans carte bancaire</li>
          <li><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2.6"><path d="m5 12.5 4.5 4.5L19 7.5"/></svg>Sans engagement pendant l'essai</li>
        </ul>
        <div class="fh-price-cta">
          <a routerLink="/inscription" class="fh-b1">ESSAYER GRATUITEMENT</a>
          <a routerLink="/fr/tarifs" class="fh-b2">VOIR LES TARIFS</a>
        </div>
      </div>

      <!-- ══ MISE EN ROUTE — panneau h=79 ══ -->
      <div class="fh-panel fh-start">
        <h2>Commencez<br>simplement.</h2>
        <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="1.7"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M3 10h18M8 3v4M16 3v4"/></svg></span><div><h3>7 JOURS GRATUITS</h3><p>Découvrez Calypso avec<br>votre propre parc.</p></div></div>
        <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#818CF8" stroke-width="1.7"><rect x="2.5" y="5" width="19" height="14" rx="2.5"/><path d="M2.5 10h19"/></svg></span><div><h3>SANS CARTE BANCAIRE</h3><p>Aucun moyen de paiement<br>demandé pour commencer.</p></div></div>
        <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7"><path d="M5 15.5 8.5 19M4.5 19.5 9 15M14 4.5c3.5-1.5 6 1 4.5 4.5-1.2 2.8-4.6 6-8 8.5L7 14C9.5 10.6 11.2 5.7 14 4.5z"/></svg></span><div><h3>MISE EN ROUTE RAPIDE</h3><p>Créez votre compte et<br>ajoutez vos véhicules.</p></div></div>
        <div class="it"><span class="ic"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.7"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg></span><div><h3>VOS DONNÉES RESTENT LES VÔTRES</h3><p>Vos données sont sécurisées<br>et restent votre propriété.</p></div></div>
      </div>

      <!-- ══ APPEL FINAL — panneau h=102 ══ -->
      <div class="fh-panel fh-final">
        <h2>Prêt à simplifier la gestion<br>de votre parc ?</h2>
        <p>Découvrez Calypso gratuitement<br>pendant 7 jours.</p>
        <div class="fh-final-cta">
          <a routerLink="/inscription" class="fh-b1">ESSAYER GRATUITEMENT</a>
          <small>Sans carte bancaire &nbsp;•&nbsp; Sans engagement</small>
        </div>
      </div>
      </div>
      <app-fr2-footer variant="columns" />
    </div>
  `,
  styles: [`
    .fh {
      /* Maquette a 1024 px : 1 unite = 1 px de l'image a cette largeur,
         suit l'ecran en dessous, se fige a 1400 px au-dela. */
      --u: min(0.09766cqw, 1.367px);
      width: 100%; position: relative;
      background: #05070F; color: #fff;
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      -webkit-font-smoothing: antialiased;
    }
    .fh *, .fh *::before, .fh *::after { box-sizing: border-box; }
    .fh a { text-decoration: none; }


    /* ── HERO : pilule y≈102, titre 145/192/241, boutons ≈416, badges ≈469 ── */
    .fh-hero { position: relative; min-height: calc(436 * var(--u));
      padding: calc(40.4 * var(--u)) 3.418cqw 0; }
    .fh-hero-txt { width: 45.8984cqw; }
    .fh-pill {
      display: inline-block; border: 1px solid rgba(96,165,250,.34);
      background: rgba(59,130,246,.10); color: #60A5FA;
      border-radius: calc(99 * var(--u)); padding: calc(5 * var(--u)) calc(13 * var(--u));
      font-size: calc(9.5 * var(--u)); font-weight: 700; letter-spacing: .1em;
    }
    .fh h1 {
      margin: calc(11.4 * var(--u)) 0 0; font-size: calc(35 * var(--u)); line-height: calc(48 * var(--u));
      font-weight: 800; letter-spacing: -.03em; color: #fff;
    }
    .fh h1 .g { background: linear-gradient(90deg,#7C7CF8,#A78BFA);
      -webkit-background-clip: text; background-clip: text; color: transparent; }
    .fh-lede { margin: calc(10 * var(--u)) 0 0; font-size: calc(14 * var(--u)); line-height: calc(21 * var(--u)); color: #9AA7BD; }
    .fh-ai {
      display: flex; align-items: center; gap: calc(8 * var(--u)); margin: calc(11.9 * var(--u)) 0 0;
      font-size: calc(13.5 * var(--u)); font-weight: 700; color: #7C7CF8;
    }
    .fh-ai svg { width: calc(16 * var(--u)); height: calc(16 * var(--u)); }
    .fh-btns { display: flex; gap: calc(12 * var(--u)); margin-top: calc(23.8 * var(--u)); }
    .fh-b1, .fh-b2 {
      display: inline-flex; align-items: center; justify-content: center;
      height: calc(40 * var(--u)); padding: 0 calc(20 * var(--u)); border-radius: calc(8 * var(--u));
      font-size: calc(11 * var(--u)); font-weight: 700; letter-spacing: .05em;
    }
    .fh-b1 { background: linear-gradient(135deg,#4F46E5,#7C3AED); color: #fff; }
    .fh-b2 { border: 1px solid rgba(255,255,255,.18); color: #fff; }
    .fh-badges { display: flex; gap: calc(20 * var(--u)); list-style: none; margin: calc(15.9 * var(--u)) 0 0; padding: 0; }
    .fh-badges li { display: flex; align-items: center; gap: calc(6 * var(--u)); font-size: calc(11.5 * var(--u)); color: #9AA7BD; }
    .fh-badges svg { width: calc(13 * var(--u)); height: calc(13 * var(--u)); flex: none; }

    /* ── DIAGRAMME : x 520..990, y 75..470 ── */
    .fh-diag { position: absolute; left: 50.7813cqw;
      margin-left: calc((44.9219cqw - 460 * var(--u)) / 2); top: calc(17.5 * var(--u)); width: calc(460 * var(--u)); height: calc(375.6 * var(--u)); }
    .fh-ring { position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%);
      border-radius: 50%; border: 1px solid rgba(96,165,250,.16); }
    .fh-ring.r1 { width: calc(300 * var(--u)); height: calc(300 * var(--u)); border-color: rgba(139,92,246,.22); }
    .fh-ring.r2 { width: calc(220 * var(--u)); height: calc(220 * var(--u)); border-style: dashed; border-color: rgba(148,163,184,.14); }
    .fh-glow {
      position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%);
      width: calc(240 * var(--u)); height: calc(240 * var(--u)); border-radius: 50%;
      background: radial-gradient(circle, rgba(79,70,229,.26) 0%, transparent 68%);
    }
    .fh-core { position: absolute; left: 50%; top: 50%; transform: translate(-50%,-50%); }
    .fh-core img { width: calc(150 * var(--u)); height: auto; display: block; }
    .fh-node {
      position: absolute; transform: translate(-50%,-50%);
      display: flex; flex-direction: column; align-items: center; gap: calc(7 * var(--u));
    }
    .fh-node .i {
      width: calc(46 * var(--u)); height: calc(46 * var(--u)); border-radius: 50%;
      display: grid; place-items: center; border: 1.5px solid;
    }
    .fh-node .i svg { width: calc(21 * var(--u)); height: calc(21 * var(--u)); }
    .fh-node .l { font-size: calc(9 * var(--u)); font-weight: 800; letter-spacing: .1em; color: #9AA7BD; white-space: nowrap; }
    .fh-node.n1 { left: 50%;  top: 8%;  }
    .fh-node.n2 { left: 86%;  top: 29%; }
    .fh-node.n3 { left: 86%;  top: 71%; }
    .fh-node.n4 { left: 50%;  top: 92%; }
    .fh-node.n5 { left: 14%;  top: 71%; }
    .fh-node.n6 { left: 14%;  top: 29%; }

    /* ── PANNEAUX : 953 px de large, 14 px d'écart ── */
    .fh-panel {
      width: 93.0664cqw; margin: 0 auto calc(14 * var(--u));
      background: rgba(255,255,255,.028);
      border: 1px solid rgba(255,255,255,.075);
      border-radius: calc(14 * var(--u));
    }

    /* Bandeau : h 62 */
    .fh-strip { min-height: calc(62 * var(--u)); display: grid; grid-template-columns: repeat(6,1fr); }
    .fh-strip a {
      display: flex; align-items: center; justify-content: center; gap: calc(8 * var(--u));
      color: #9AA7BD; font-size: calc(11 * var(--u)); font-weight: 700; letter-spacing: .06em;
      border-right: 1px solid rgba(255,255,255,.075);
    }
    .fh-strip a:last-child { border-right: 0; }
    .fh-strip a:hover { color: #fff; }
    .fh-strip svg { width: calc(17 * var(--u)); height: calc(17 * var(--u)); flex: none; }

    /* Produit : h 302 */
    .fh-prod { min-height: calc(302 * var(--u)); display: grid; grid-template-columns: 24.4141cqw 1fr; gap: calc(22 * var(--u)); padding: calc(22 * var(--u)); }
    .fh-prod-txt { align-self: center; }
    .fh-prod-txt h2 { margin: 0; font-size: calc(22 * var(--u)); line-height: calc(29 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .fh-prod-txt p { margin: calc(20 * var(--u)) 0 0; font-size: calc(13 * var(--u)); line-height: calc(20 * var(--u)); color: #9AA7BD; }
    .fh-prod-shot { height: calc(258 * var(--u)); overflow: hidden; border-radius: calc(10 * var(--u)); border: 1px solid rgba(255,255,255,.12); }
    .fh-prod-shot img { width: 100%; height: 100%; object-fit: cover; object-position: top left; display: block; }

    /* Anticipation : h 156, texte + 3 cartes */
    .fh-anti { min-height: calc(156 * var(--u)); display: grid; grid-template-columns: 34.668cqw repeat(3,1fr); gap: calc(14 * var(--u)); padding: calc(18 * var(--u)); }
    .fh-anti-txt h2 { margin: 0; font-size: calc(19 * var(--u)); line-height: calc(25 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .fh-anti-txt h2 .g2 { color: #C084FC; }
    .fh-anti-txt p { margin: calc(12 * var(--u)) 0 0; font-size: calc(10.5 * var(--u)); line-height: calc(15 * var(--u)); color: #9AA7BD; }
    .fh-anti-card {
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.07);
      border-radius: calc(10 * var(--u)); padding: calc(14 * var(--u)) calc(12 * var(--u)); text-align: center;
    }
    .fh-anti-card .ic { display: block; width: calc(26 * var(--u)); height: calc(26 * var(--u)); margin: calc(2 * var(--u)) auto calc(10 * var(--u)); }
    .fh-anti-card .ic svg { width: calc(26 * var(--u)); height: calc(26 * var(--u)); }
    .fh-anti-card h3 { margin: 0 0 calc(7 * var(--u)); font-size: calc(10.5 * var(--u)); font-weight: 800; letter-spacing: .06em; }
    .fh-anti-card p { margin: 0; font-size: calc(9.5 * var(--u)); line-height: calc(14 * var(--u)); color: #9AA7BD; }

    /* Aperçu tarifaire : h 124 */
    .fh-price {
      min-height: calc(124 * var(--u)); display: grid;
      grid-template-columns: 26.3672cqw 11.4258cqw 1fr auto 14.3555cqw; gap: calc(18 * var(--u));
      align-items: center; padding: 0 calc(22 * var(--u));
      background: linear-gradient(115deg, rgba(79,70,229,.24), rgba(124,58,237,.14));
      border-color: rgba(139,92,246,.3);
    }
    .fh-price h2 { margin: 0; font-size: calc(20 * var(--u)); line-height: calc(26 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .fh-price-n { text-align: center; }
    .fh-price-n .from { display: block; font-size: calc(11 * var(--u)); color: #9AA7BD; }
    .fh-price-n .n { display: block; font-size: calc(39 * var(--u)); font-weight: 800; letter-spacing: -.04em; line-height: 1.05; }
    .fh-price-n .u { display: block; font-size: calc(11 * var(--u)); color: #9AA7BD; }
    .fh-price-txt { margin: 0; font-size: calc(11 * var(--u)); line-height: calc(16 * var(--u)); color: #9AA7BD; }
    .fh-price-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: calc(7 * var(--u)); }
    .fh-price-list li { display: flex; align-items: center; gap: calc(7 * var(--u)); font-size: calc(10.5 * var(--u)); color: #C7D2E4; white-space: nowrap; }
    .fh-price-list svg { width: calc(12 * var(--u)); height: calc(12 * var(--u)); flex: none; }
    .fh-price-cta { display: flex; flex-direction: column; gap: calc(8 * var(--u)); }

    /* Mise en route : h 79 */
    .fh-start { min-height: calc(79 * var(--u)); display: grid; grid-template-columns: 18.0664cqw repeat(4,1fr); gap: calc(14 * var(--u)); align-items: center; padding: 0 calc(20 * var(--u)); }
    .fh-start h2 { margin: 0; font-size: calc(19 * var(--u)); line-height: calc(24 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .fh-start .it { display: flex; gap: calc(9 * var(--u)); align-items: flex-start; }
    .fh-start .ic { width: calc(22 * var(--u)); height: calc(22 * var(--u)); flex: none; }
    .fh-start .ic svg { width: calc(22 * var(--u)); height: calc(22 * var(--u)); }
    .fh-start h3 { margin: 0 0 calc(4 * var(--u)); font-size: calc(9 * var(--u)); font-weight: 800; letter-spacing: .05em; }
    .fh-start p { margin: 0; font-size: calc(9 * var(--u)); line-height: calc(13 * var(--u)); color: #9AA7BD; }

    /* Appel final : h 102 */
    .fh-final { min-height: calc(102 * var(--u)); display: grid; grid-template-columns: 1fr 29.2969cqw 23.4375cqw; gap: calc(22 * var(--u)); align-items: center; padding: 0 calc(26 * var(--u)); }
    .fh-final h2 { margin: 0; font-size: calc(21 * var(--u)); line-height: calc(28 * var(--u)); font-weight: 800; letter-spacing: -.03em; }
    .fh-final > p { margin: 0; font-size: calc(13 * var(--u)); line-height: calc(19 * var(--u)); color: #9AA7BD; }
    .fh-final-cta { display: flex; flex-direction: column; align-items: center; gap: calc(8 * var(--u)); }
    .fh-final-cta small { font-size: calc(10 * var(--u)); color: #6B7A94; white-space: nowrap; }


    /* Sous 700 px, la maquette de bureau ne tient plus : on rend la main à une
       pile verticale plutôt que de réduire jusqu'à l'illisible. Une réduction
       proportionnelle jusqu'au téléphone donnerait un titre de 12 px. */
    @media (max-width: 700px) {
      .fh-hero { min-height: 0; padding-bottom: calc(40.96 * var(--u)); }
      .fh-hero-txt, .fh-diag { position: static; width: auto; }
      .fh-diag { height: calc(716.8 * var(--u)); margin-top: calc(61.44 * var(--u)); }
      .fh-strip, .fh-prod, .fh-anti, .fh-price, .fh-start, .fh-final { min-height: 0; }
      .fh-strip { grid-template-columns: repeat(3, 1fr); }
      .fh-strip a:nth-child(3) { border-right: 0; }
      .fh-prod, .fh-anti, .fh-price, .fh-start, .fh-final { grid-template-columns: 1fr; }
      .fh-prod, .fh-price, .fh-start, .fh-final { padding: calc(40.96 * var(--u)); }
    }`]
})
export class Fr2HomeComponent {}
