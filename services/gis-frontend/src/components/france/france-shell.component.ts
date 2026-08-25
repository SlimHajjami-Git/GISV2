import {
  AfterViewInit, Component, ElementRef, OnDestroy, ViewEncapsulation, inject
} from '@angular/core';
import {
  NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet
} from '@angular/router';
import { Subscription, filter } from 'rxjs';
import { RegionService } from '../../services/region.service';
import { FranceHeaderComponent } from './france-header.component';

/**
 * Coque du site commercial France : en-tête, pied de page, et la feuille de
 * style commune aux huit pages.
 *
 * <p><b>Pourquoi ViewEncapsulation.None.</b> Les pages sont des enfants du
 * routeur ; Angular leur attribue un identifiant d'encapsulation distinct de
 * celui de la coque, si bien que des styles encapsulés ici ne les
 * atteindraient jamais. On sert donc la feuille sans encapsulation — ce qui
 * la rendrait globale. Chaque sélecteur est pour cette raison préfixé par
 * <code>.fr</code>, la classe du conteneur ci-dessous : sans ce garde-fou,
 * des règles comme <code>section</code>, <code>details</code> ou
 * <code>footer a</code> repeindraient toute l'application de gestion, qui est
 * claire alors que ce site est sombre.</p>
 *
 * <p><b>Thème unique et assumé.</b> Le site ne suit pas le thème du lecteur :
 * c'est une identité de marque, pas un document. Toutes les couleurs sont donc
 * peintes explicitement, y compris le fond, pour que la page tienne quel que
 * soit le réglage du navigateur.</p>
 */
@Component({
  selector: 'app-france-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet, FranceHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr">
      <app-france-header></app-france-header>

      <main>
        <router-outlet></router-outlet>
      </main>

      <footer class="site">
        <div class="shell">
          <div class="foot-grid">
            <div class="foot-brand">
              <a class="brand" routerLink="/fr">
                <img class="mark" src="/assets/calypso-logo.svg" alt="Calypso" width="504" height="170">
              </a>
            </div>
            <div>
              <h4>Produit</h4>
              <ul>
                <li><a routerLink="/fr/fonctionnalites">Fonctionnalités</a></li>
                <li><a routerLink="/fr/tarifs">Tarifs</a></li>
                <li><a routerLink="/fr/calypso-auto">Calypso Auto</a></li>
                <li><a routerLink="/fr/contact">Contact</a></li>
              </ul>
            </div>
            <div>
              <h4>Accès</h4>
              <ul>
                <li><a routerLink="/login">Se connecter</a></li>
                <li><a routerLink="/inscription">Essayer gratuitement</a></li>
              </ul>
            </div>
            <div>
              <h4>Légal</h4>
              <ul>
                <li><a routerLink="/fr/mentions-legales">Mentions légales</a></li>
                <li><a routerLink="/fr/confidentialite">Politique de confidentialité</a></li>
                <li><a routerLink="/fr/rgpd">RGPD</a></li>
                <li><a routerLink="/fr/cookies">Cookies</a></li>
              </ul>
            </div>
          </div>
          <div class="foot-bottom">© 2026 Calypso. Tous droits réservés.</div>
        </div>
      </footer>
    </div>
  `,
  styles: [`
    /* ==================================================================
       Identité Calypso — Europe. Fond nuit, dégradé bleu → violet du logo,
       accents colorés par domaine (carburant vert, réparation ambre,
       échéance violet, IA rose). D'après la maquette fournie.
       ================================================================== */
    .fr {
      --bg:        #05070F;
      --bg-2:      #080D1B;
      --bg-3:      #0C1225;
      --surface:   rgba(255,255,255,.035);
      --surface-2: rgba(255,255,255,.06);
      --line:      rgba(255,255,255,.08);
      --line-2:    rgba(255,255,255,.15);
      --txt:       #FFFFFF;
      --txt-soft:  #9AA7BD;
      --txt-faint: #6B7A94;
      --blue:      #3B82F6;
      --blue-2:    #60A5FA;
      --indigo:    #4F46E5;
      --violet:    #8B5CF6;
      --violet-2:  #A78BFA;
      --teal:      #34D399;
      --amber:     #F59E0B;
      --pink:      #EC4899;
      --grad:      linear-gradient(135deg, #4F46E5 0%, #7C3AED 100%);
      --grad-soft: linear-gradient(90deg, #60A5FA 0%, #A78BFA 100%);
      --r:         16px;
      --r-sm:      10px;
      --glow:      0 0 0 1px rgba(255,255,255,.06), 0 18px 50px rgba(0,0,0,.55);
    }
    .fr, .fr * { box-sizing: border-box; }
    .fr {
      margin: 0; background: var(--bg); color: var(--txt);
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      font-size: 16.5px; line-height: 1.66; -webkit-font-smoothing: antialiased;
      overflow-x: hidden;
    }
    .fr .shell { max-width: 1320px; margin: 0 auto; padding: 0 40px; }
    @media (max-width: 640px) { .fr .shell { padding: 0 22px; } }

    /* ---------- boutons ---------- */
    .fr .btn {
      display: inline-flex; align-items: center; justify-content: center;
      font-size: 13.5px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase;
      padding: 15px 28px; border-radius: 12px; text-decoration: none;
      border: 1.5px solid transparent; cursor: pointer;
      transition: transform .18s ease, box-shadow .18s ease, border-color .18s ease, background .18s ease;
    }
    .fr .btn-grad { background: var(--grad); color: #fff; box-shadow: 0 8px 26px rgba(99,72,235,.38); }
    .fr .btn-grad:hover { transform: translateY(-2px); box-shadow: 0 12px 34px rgba(99,72,235,.5); }
    .fr .btn-line { background: transparent; color: #fff; border-color: var(--line-2); }
    .fr .btn-line:hover { border-color: rgba(255,255,255,.4); transform: translateY(-2px); }
    .fr .btn-sm { padding: 11px 18px; font-size: 12px; border-radius: 10px; }
    .fr a:focus-visible, .fr button:focus-visible, .fr summary:focus-visible {
      outline: 2px solid var(--violet-2); outline-offset: 3px; border-radius: 8px;
    }

    /* ---------- en-tête ---------- */
    .fr header.site {
      position: sticky; top: 0; z-index: 60;
      background: rgba(5,7,15,.82); backdrop-filter: blur(14px);
      border-bottom: 1px solid var(--line);
    }
    .fr .bar { display: flex; align-items: center; gap: 34px; height: 78px; }
    .fr .brand { display: flex; align-items: center; gap: 11px; text-decoration: none; }
    .fr .mark { height: 30px; width: auto; flex-shrink: 0; display: block; }
    /* Le mot « CALYPSO » fait partie du logo officiel : plus de texte separe. */
    .fr .brand-word { color: #fff; font-size: 21px; font-weight: 800; letter-spacing: .06em; }
    .fr .nav { display: flex; gap: 30px; margin-left: auto; align-items: center; }
    .fr .nav a { color: var(--txt-soft); text-decoration: none; font-size: 14.5px; font-weight: 600; }
    .fr .nav a:hover { color: #fff; }
    .fr .nav a.on { color: #fff; }
    .fr .nav a.sep { color: #fff; }
    @media (max-width: 1080px) { .fr .nav .hide-sm { display: none; } .fr .bar { gap: 16px; } }

    /* ---------- rythme des sections ---------- */
    .fr section { padding: 108px 0; position: relative; }
    .fr section.tight { padding: 86px 0; }
    .fr .band { background: var(--bg); }
    .fr .band-2 { background: var(--bg-2); }
    .fr .band-glow {
      background:
        radial-gradient(900px 460px at 78% 20%, rgba(124,58,237,.16), transparent 62%),
        radial-gradient(760px 420px at 12% 30%, rgba(59,130,246,.13), transparent 60%),
        var(--bg);
    }
    .fr .sec-head { max-width: 780px; margin: 0 auto 62px; text-align: center; }
    .fr .sec-head h2 {
      font-size: clamp(30px, 4.2vw, 44px); font-weight: 800; letter-spacing: -.03em;
      line-height: 1.14; margin: 0 0 15px; text-wrap: balance; color: #fff;
    }
    .fr .sec-head p { font-size: 17.5px; color: var(--txt-soft); margin: 0; }
    .fr .grad-txt {
      background: var(--grad-soft); -webkit-background-clip: text; background-clip: text;
      color: transparent;
    }

    /* étiquette en pilule, comme la maquette */
    .fr .pill {
      display: inline-flex; align-items: center; gap: 9px;
      border: 1px solid rgba(96,165,250,.34); background: rgba(59,130,246,.10);
      color: var(--blue-2); border-radius: 99px; padding: 8px 17px;
      font-size: 11.5px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase;
    }

    /* ---------- HERO ---------- */
    .fr .hero { padding: 74px 0 0; }
    .fr .hero-grid {
      display: grid; grid-template-columns: 1fr 1.22fr; gap: 52px; align-items: center;
    }
    .fr .hero h1 {
      font-size: clamp(38px, 5.1vw, 58px); font-weight: 800; letter-spacing: -.035em;
      line-height: 1.08; margin: 26px 0 22px; text-wrap: balance;
    }
    .fr .hero p.lede { font-size: 17.5px; color: var(--txt-soft); margin: 0 0 22px; max-width: 52ch; }
    .fr .ai-line {
      display: flex; align-items: center; gap: 10px; margin: 0 0 30px;
      color: var(--blue-2); font-weight: 700; font-size: 16px;
    }
    .fr .hero-cta { display: flex; gap: 14px; flex-wrap: wrap; margin-bottom: 26px; }
    .fr .badges { display: flex; gap: 26px; flex-wrap: wrap; margin: 0; padding: 0; list-style: none; }
    .fr .badges li {
      display: flex; align-items: center; gap: 9px;
      font-size: 14.5px; color: var(--txt-soft); font-weight: 500;
    }
    .fr .badges svg { flex-shrink: 0; }

    /* ---------- visuel orbital ---------- */
    .fr .orbit { position: relative; aspect-ratio: 1 / .88; width: 100%; }
    .fr .rings { position: absolute; inset: 0; display: grid; place-items: center; }
    .fr .ring {
      position: absolute; border-radius: 50%; border: 1px solid rgba(96,165,250,.16);
    }
    .fr .ring.r1 { width: 33%; height: 37%; border-color: rgba(139,92,246,.20);
      border-bottom-color: rgba(167,139,250,.72); border-right-color: rgba(236,72,153,.45);
      transform: rotate(14deg); }
    .fr .ring.r2 { width: 50%; height: 56%; border-color: rgba(96,165,250,.16);
      border-top-color: rgba(96,165,250,.72); border-left-color: rgba(96,165,250,.40);
      transform: rotate(-26deg); }
    .fr .ring.r3 { width: 66%; height: 74%; border-style: dashed; border-color: rgba(148,163,184,.16); }
    .fr .halo {
      position: absolute; width: 58%; height: 65%; border-radius: 50%;
      background: radial-gradient(circle, rgba(79,70,229,.30) 0%, rgba(124,58,237,.10) 45%, transparent 70%);
      filter: blur(6px);
    }
    .fr .orbit-core {
      position: absolute; left: 50%; top: 50%; transform: translate(-50%, -50%);
      display: flex; flex-direction: column; align-items: center; gap: 6px; text-align: center;
    }
    .fr .orbit-core img { width: 230px; height: auto; display: block; }
    .fr .orbit-core span { font-size: 26px; font-weight: 800; letter-spacing: .15em; color: #fff; }
    .fr .o-card {
      position: absolute; width: 30%; min-width: 172px;
      background: rgba(12,18,37,.88); border: 1px solid var(--line-2);
      border-radius: 14px; padding: 12px 14px; box-shadow: var(--glow);
      display: flex; gap: 11px; align-items: flex-start;
      backdrop-filter: blur(6px);
    }
    .fr .o-card .ic {
      width: 34px; height: 34px; border-radius: 10px; flex-shrink: 0;
      display: flex; align-items: center; justify-content: center;
    }
    .fr .o-card .k {
      font-size: 10px; font-weight: 800; letter-spacing: .13em; text-transform: uppercase;
      color: var(--txt-faint); display: block; margin-bottom: 2px;
    }
    .fr .o-card .v { font-size: 12px; color: var(--txt-soft); line-height: 1.38; display: block; }
    .fr .o-card .m { font-size: 12.5px; font-weight: 700; display: block; }
    .fr .c1 { top: 0;   left: 35%; }
    .fr .c2 { top: 31%; left: 0;   }
    .fr .c3 { top: 31%; left: 70%; }
    .fr .c4 { top: 66%; left: 3%;  }
    .fr .c5 { top: 66%; left: 67%; }
    .fr .c6 { top: 86%; left: 35%; }

    /* ---------- bandeau des univers ---------- */
    .fr .strip-wrap { border-top: 1px solid var(--line); margin-top: 64px; }
    .fr .chevron { display: grid; place-items: center; margin: -12px 0 0; }
    .fr .chevron span {
      width: 26px; height: 26px; border-radius: 50%; background: var(--bg);
      border: 1px solid var(--line); display: grid; place-items: center;
    }
    .fr .strip { display: grid; grid-template-columns: repeat(6, 1fr); }
    .fr .strip a, .fr .strip div {
      text-align: center; padding: 34px 10px; text-decoration: none;
      border-right: 1px solid var(--line); color: var(--txt-soft);
      display: flex; flex-direction: column; align-items: center; gap: 12px;
      font-size: 14.5px; font-weight: 600; transition: color .18s ease, background .18s ease;
    }
    .fr .strip > *:last-child { border-right: 0; }
    .fr .strip a:hover { color: #fff; background: rgba(255,255,255,.02); }
    @media (max-width: 980px) {
      .fr .hero-grid { grid-template-columns: 1fr; gap: 52px; }
      .fr .orbit { aspect-ratio: auto; display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
      .fr .rings, .fr .halo { display: none; }
      .fr .orbit-core { position: static; transform: none; grid-column: 1 / -1; margin-bottom: 8px; }
      .fr .o-card { position: static; width: auto; }
      .fr .strip { grid-template-columns: repeat(3, 1fr); }
      .fr .strip > * { border-bottom: 1px solid var(--line); }
      .fr section { padding: 78px 0; }
    }
    @media (max-width: 560px) {
      .fr .orbit { grid-template-columns: 1fr; }
      .fr .strip { grid-template-columns: repeat(2, 1fr); }
    }

    /* ---------- cartes génériques ---------- */
    .fr .cards { display: grid; grid-template-columns: repeat(3, 1fr); gap: 22px; max-width: 1160px; margin: 0 auto; }
    .fr .card {
      background: var(--surface); border: 1px solid var(--line); border-radius: var(--r);
      padding: 30px 28px; transition: border-color .2s ease, transform .2s ease;
    }
    .fr .card:hover { border-color: var(--line-2); transform: translateY(-3px); }
    .fr .card .ic {
      width: 44px; height: 44px; border-radius: 12px; background: rgba(255,255,255,.05);
      display: flex; align-items: center; justify-content: center; margin-bottom: 16px;
    }
    .fr .card .k { font-size: 11px; font-weight: 800; letter-spacing: .14em; margin-bottom: 9px; }
    .fr .card h3 { font-size: 17.5px; font-weight: 700; margin: 0 0 9px; color: #fff; }
    .fr .card p { margin: 0; color: var(--txt-soft); font-size: 15.5px; }
    @media (max-width: 900px) { .fr .cards { grid-template-columns: 1fr; } }

    /* ---------- page Fonctionnalités ---------- */
    .fr .feat-page {
      display: grid; grid-template-columns: 1fr 1.06fr; gap: 62px;
      align-items: center; max-width: 1180px; margin: 0 auto;
    }
    .fr .feat-list { display: flex; flex-direction: column; gap: 28px; }
    .fr .feat-row { display: flex; gap: 17px; align-items: flex-start; }
    .fr .feat-row .ic {
      width: 44px; height: 44px; border-radius: 12px; background: rgba(59,130,246,.12);
      border: 1px solid rgba(96,165,250,.20);
      display: flex; align-items: center; justify-content: center; flex-shrink: 0;
    }
    .fr .feat-row h3 { font-size: 17px; font-weight: 700; margin: 0 0 5px; color: #fff; }
    .fr .feat-row p { margin: 0; color: var(--txt-soft); font-size: 15.5px; line-height: 1.58; }
    @media (max-width: 900px) { .fr .feat-page { grid-template-columns: 1fr; gap: 40px; } }

    /* Quatre fonctions complementaires depuis que « Application mobile » a ete
       retiree de la liste commerciale publique : la grille suit, sinon la
       derniere colonne resterait vide. */
    .fr .more { display: grid; grid-template-columns: repeat(4, 1fr); gap: 18px; max-width: 900px; margin: 0 auto; }
    .fr .more-item {
      text-align: center; display: flex; flex-direction: column; align-items: center; gap: 12px;
      font-size: 14.5px; font-weight: 600; color: var(--txt-soft);
    }
    @media (max-width: 860px) { .fr .more { grid-template-columns: repeat(2, 1fr); gap: 28px; } }

    /* ---------- captures produit ---------- */
    .fr .shot img, .fr .shot-img {
      display: block; width: 100%; height: auto; border-radius: var(--r);
      border: 1px solid var(--line-2); box-shadow: var(--glow);
    }
    .fr .gallery { display: grid; grid-template-columns: repeat(3, 1fr); gap: 26px; max-width: 1220px; margin: 0 auto; }
    .fr .gallery figure { margin: 0; }
    .fr .gallery figcaption { margin-top: 11px; font-size: 13.5px; font-weight: 600; color: var(--txt-soft); text-align: center; }
    @media (max-width: 900px) { .fr .gallery { grid-template-columns: 1fr 1fr; } }
    @media (max-width: 560px) { .fr .gallery { grid-template-columns: 1fr; } }

    /* ---------- tarifs ---------- */
    .fr .tabs { display: flex; gap: 8px; justify-content: center; margin: 0 0 40px; }
    .fr .tab {
      background: var(--surface); border: 1px solid var(--line); color: var(--txt-soft);
      font-family: inherit; font-size: 14px; font-weight: 700; padding: 12px 22px;
      border-radius: 99px; cursor: pointer; transition: all .18s ease;
    }
    .fr .tab:hover { color: #fff; border-color: var(--line-2); }
    .fr .tab[aria-selected="true"] { background: var(--grad); border-color: transparent; color: #fff; }
    /* Offre unique : une seule carte, centree, plus large qu un demi-ecran
       mais pas etalee — au-dela l oeil ne relie plus le prix a la liste. */
    .fr .offer { max-width: 660px; margin: 0 auto; }
    .fr .ticks.two { display: grid; grid-template-columns: 1fr 1fr; gap: 10px 26px; }
    @media (max-width: 560px) { .fr .ticks.two { grid-template-columns: 1fr; } }

    /* ---------- tarifs : deux cartes ---------- */
    .fr .plan-top { display: flex; align-items: center; gap: 13px; margin-bottom: 4px; }
    .fr .plan-ic {
      width: 46px; height: 46px; border-radius: 50%; flex: none;
      display: grid; place-items: center;
      background: rgba(255,255,255,.05); border: 1px solid var(--line-2);
    }
    .fr .plan-ic svg { width: 21px; height: 21px; }
    .fr .price .u { font-size: 14.5px; }
    .fr .pill-note {
      display: inline-flex; align-items: center; justify-content: center;
      width: 100%; padding: 12px 16px; margin: 16px 0 22px;
      border: 1px solid var(--line-2); border-radius: 12px;
      font-size: 14.5px; font-weight: 700; color: var(--txt-soft);
    }
    .fr .pill-note.accent { border-color: rgba(139,92,246,.45); color: var(--violet-2); }
    /* Le bouton porte deux lignes : l action, puis la reassurance. */
    .fr .plan-cta { flex-direction: column; gap: 3px; padding: 14px 24px; }
    .fr .plan-cta small { font-size: 11.5px; font-weight: 600; letter-spacing: 0; text-transform: none; opacity: .82; }

    /* ---------- bandeau de reassurance ---------- */
    .fr .reassure {
      display: grid; grid-template-columns: 1fr 1fr; gap: 28px;
      max-width: 1060px; margin: 40px auto 0; padding: 26px 30px;
      background: var(--surface); border: 1px solid var(--line); border-radius: var(--r);
    }
    .fr .re-item { display: flex; gap: 16px; align-items: flex-start; }
    .fr .re-ic { width: 48px; height: 48px; border-radius: 50%; flex: none; display: grid; place-items: center; }
    .fr .re-ic svg { width: 22px; height: 22px; }
    .fr .re-item h3 { font-size: 16.5px; font-weight: 700; margin: 0 0 5px; color: #fff; }
    .fr .re-item p { margin: 0; font-size: 14.5px; color: var(--txt-soft); }
    @media (max-width: 820px) { .fr .reassure { grid-template-columns: 1fr; gap: 22px; } }

    .fr .plans { display: grid; grid-template-columns: repeat(2, 1fr); gap: 26px; max-width: 1000px; margin: 0 auto; }
    .fr .plan {
      background: var(--surface); border: 1px solid var(--line); border-radius: var(--r);
      padding: 36px 32px; position: relative; display: flex; flex-direction: column;
    }
    .fr .plan.reco { border-color: rgba(139,92,246,.45); background: rgba(124,58,237,.07); }
    .fr .plan .badge {
      position: absolute; top: -13px; left: 32px; background: var(--grad); color: #fff;
      font-size: 10.5px; font-weight: 800; letter-spacing: .13em; padding: 6px 14px; border-radius: 99px;
    }
    .fr .plan .term { font-size: 12.5px; font-weight: 800; letter-spacing: .14em; text-transform: uppercase; color: var(--txt-faint); }
    .fr .price { display: flex; align-items: baseline; gap: 8px; margin: 14px 0 4px; }
    .fr .price .n { font-size: 48px; font-weight: 800; letter-spacing: -.035em; color: #fff; font-variant-numeric: tabular-nums; }
    .fr .price .u { color: var(--txt-soft); font-size: 15.5px; }
    .fr .plan .sub { color: var(--txt-soft); font-size: 15px; margin: 0 0 6px; }
    .fr .plan .flex { font-size: 14px; font-weight: 600; color: var(--txt-faint); margin: 0 0 22px; }
    .fr .plan .same { font-size: 14.5px; color: var(--txt-soft); margin: 0 0 18px; padding-top: 18px; border-top: 1px solid var(--line); }
    .fr .plan .btn { margin-top: auto; }
    .fr .ticks { list-style: none; padding: 0; margin: 0 0 26px; display: flex; flex-direction: column; gap: 10px; }
    .fr .ticks li { display: flex; gap: 11px; align-items: flex-start; font-size: 15px; color: var(--txt-soft); }
    .fr .ticks li::before {
      content: ""; width: 16px; height: 16px; border-radius: 50%; flex-shrink: 0; margin-top: 5px;
      background: rgba(52,211,153,.16);
      box-shadow: inset 0 0 0 1px rgba(52,211,153,.5);
    }
    @media (max-width: 820px) { .fr .plans { grid-template-columns: 1fr; } }

    /* ---------- Calypso Auto ---------- */
    .fr .auto-title { font-size: clamp(38px,5vw,60px); font-weight: 800; letter-spacing: -.035em; margin: 0 0 14px; color: #fff; }
    .fr .auto-sub { font-size: clamp(19px,2.2vw,25px); font-weight: 700; color: #fff; margin: 0 0 16px; }

    /* Panneaux d etape. Le vehicule se decrit AVANT la question : les deux
       blocs sont donc visuellement distincts et ordonnes. */
    .fr .panel-block {
      background: var(--surface); border: 1px solid var(--line);
      border-radius: var(--r); padding: 30px 32px; margin-bottom: 22px;
    }
    .fr .panel-block h3 { font-size: 19px; font-weight: 700; margin: 0 0 6px; color: #fff; }
    .fr .panel-lede { color: var(--txt-soft); margin: 0 0 18px; font-size: 15px; }
    .fr .panel-block > h3 + .veh-grid { margin-top: 22px; }
    .fr .veh-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 16px; margin-top: 22px; }
    .fr .veh-field label { display: block; font-size: 12.5px; font-weight: 700; color: var(--txt-soft); margin-bottom: 8px; }
    .fr .veh-field input, .fr .veh-field select {
      width: 100%; padding: 12px 14px; font-family: inherit; font-size: 14.5px;
      color: #fff; background: rgba(0,0,0,.32);
      border: 1px solid var(--line-2); border-radius: var(--r-sm);
      box-sizing: border-box; transition: border-color .16s ease;
    }
    .fr .veh-field select { appearance: none; -webkit-appearance: none; padding-right: 36px; cursor: pointer; }
    .fr .veh-field input::placeholder { color: var(--txt-faint); }
    .fr .veh-field input:focus, .fr .veh-field select:focus { outline: none; border-color: var(--violet); }
    .fr .select-wrap { position: relative; }
    .fr .select-wrap::after {
      content: ""; position: absolute; right: 15px; top: 50%; margin-top: -5px;
      width: 8px; height: 8px; border-right: 2px solid var(--txt-soft);
      border-bottom: 2px solid var(--txt-soft); transform: rotate(45deg); pointer-events: none;
    }
    @media (max-width: 1100px) { .fr .veh-grid { grid-template-columns: repeat(3, 1fr); } }
    @media (max-width: 700px) { .fr .veh-grid { grid-template-columns: 1fr 1fr; } }

    /* Zone de question */
    .fr .ask { display: flex; gap: 12px; align-items: stretch; }
    .fr .ask input {
      flex: 1; min-width: 0; padding: 16px 18px; font-family: inherit; font-size: 15.5px;
      color: #fff; background: rgba(0,0,0,.32);
      border: 1px solid var(--line-2); border-radius: 12px;
    }
    .fr .ask input::placeholder { color: var(--txt-faint); }
    .fr .ask input:focus { outline: none; border-color: var(--violet); }
    .fr .ask button {
      flex: none; width: 54px; border: 0; border-radius: 12px; cursor: pointer;
      background: var(--grad); color: #fff; display: grid; place-items: center;
    }
    .fr .ask button:disabled { opacity: .5; cursor: not-allowed; }
    .fr .ask button svg { width: 20px; height: 20px; }
    .fr .chip-q { cursor: pointer; font-family: inherit; }
    .fr .chip-q:hover:not(:disabled) { border-color: var(--line-2); color: #fff; }
    .fr .chip-q:disabled { opacity: .5; cursor: not-allowed; }
    .fr .answer-wait { margin: 20px 0 0; color: var(--txt-soft); font-size: 15px; }
    .fr .answer {
      margin-top: 20px; padding: 22px 24px; white-space: pre-wrap; line-height: 1.66;
      background: rgba(0,0,0,.28); border: 1px solid var(--line-2);
      border-radius: 12px; color: var(--txt-soft); font-size: 15px;
    }
    .fr .auto-hero { display: grid; grid-template-columns: 1.05fr .95fr; gap: 64px; align-items: center; max-width: 1120px; margin: 0 auto; }
    .fr .bot-wrap { max-width: 320px; justify-self: center; }
    .fr .chips { display: flex; flex-wrap: wrap; gap: 10px; justify-content: center; max-width: 880px; margin: 0 auto; }
    .fr .chip-q {
      background: var(--surface); border: 1px solid var(--line); border-radius: 99px;
      padding: 10px 18px; font-size: 14px; color: var(--txt-soft);
    }
    .fr .steps { display: grid; grid-template-columns: repeat(3, 1fr); gap: 24px; max-width: 940px; margin: 44px auto 0; }
    .fr .step { text-align: center; }
    .fr .step .n { font-size: 12px; font-weight: 800; letter-spacing: .14em; }
    .fr .step h4 { font-size: 16.5px; font-weight: 700; margin: 8px 0 6px; color: #fff; }
    .fr .step p { font-size: 14.5px; color: var(--txt-soft); margin: 0; }
    @media (max-width: 860px) { .fr .auto-hero { grid-template-columns: 1fr; gap: 36px; } .fr .steps { grid-template-columns: 1fr; } .fr .bot-wrap { max-width: 230px; } }

    /* ---------- FAQ ---------- */
    .fr .faq { max-width: 880px; margin: 0 auto; }
    .fr details { border: 1px solid var(--line); border-radius: 12px; background: var(--surface); margin-bottom: 12px; }
    .fr details summary {
      cursor: pointer; list-style: none; padding: 20px 52px 20px 22px; position: relative;
      font-weight: 700; font-size: 16px; color: #fff;
    }
    .fr details summary::-webkit-details-marker { display: none; }
    .fr details summary::after {
      content: "+"; position: absolute; right: 20px; top: 50%; transform: translateY(-50%);
      font-size: 22px; color: var(--violet-2); line-height: 1;
    }
    .fr details[open] summary::after { content: "−"; }
    .fr details .ans { padding: 0 22px 20px; color: var(--txt-soft); margin: 0; }
    .fr details .ans a { color: var(--blue-2); }

    /* Formulaire seul, sans colonne de coordonnees : le document maitre
       interdit tout bloc de contact direct. */
    .fr .contact-form.solo { max-width: 640px; margin: 0 auto; }

    /* Etat « Message envoye ! » */
    .fr .sent-state { max-width: 560px; margin: 0 auto; text-align: center; }
    .fr .sent-ic {
      width: 74px; height: 74px; border-radius: 50%; margin: 0 auto 24px;
      display: grid; place-items: center;
      background: rgba(52,211,153,.12); border: 1px solid rgba(52,211,153,.35);
    }
    .fr .sent-ic svg { width: 34px; height: 34px; }
    .fr .sent-state h2 { font-size: clamp(28px,4vw,40px); font-weight: 800; letter-spacing: -.03em; margin: 0 0 14px; color: #fff; }
    .fr .sent-state p { color: var(--txt-soft); margin: 0 0 30px; font-size: 16.5px; }

    /* ---------- contact ---------- */
    .fr .contact-grid { display: grid; grid-template-columns: .85fr 1.15fr; gap: 56px; max-width: 1120px; margin: 0 auto; align-items: start; }
    .fr .contact-info { display: flex; flex-direction: column; gap: 26px; }
    .fr .ci { display: flex; gap: 15px; align-items: flex-start; }
    .fr .ci .ic {
      width: 40px; height: 40px; border-radius: 11px; background: rgba(59,130,246,.12);
      border: 1px solid rgba(96,165,250,.20);
      display: flex; align-items: center; justify-content: center; flex-shrink: 0;
    }
    .fr .ci h4 { font-size: 14px; font-weight: 700; margin: 0 0 4px; color: #fff; }
    .fr .ci p { margin: 0; font-size: 14.5px; color: var(--txt-soft); }
    .fr .contact-form { background: var(--surface); border: 1px solid var(--line); border-radius: var(--r); padding: 30px; }
    .fr .contact-form label { display: block; font-size: 13.5px; font-weight: 700; margin: 0 0 7px; color: #fff; }
    .fr .contact-form label + input, .fr .contact-form label + textarea { margin-bottom: 18px; }
    .fr .contact-form input, .fr .contact-form textarea {
      width: 100%; border: 1px solid var(--line-2); border-radius: var(--r-sm);
      padding: 12px 15px; font-family: inherit; font-size: 15px; color: #fff;
      background: rgba(0,0,0,.32); transition: border-color .16s ease;
    }
    .fr .contact-form input::placeholder, .fr .contact-form textarea::placeholder { color: var(--txt-faint); }
    .fr .contact-form input:focus, .fr .contact-form textarea:focus { outline: none; border-color: var(--violet); }
    .fr .contact-form textarea { resize: vertical; }
    @media (max-width: 860px) { .fr .contact-grid { grid-template-columns: 1fr; gap: 36px; } }

    /* ---------- pages légales ---------- */
    .fr .legal { max-width: 820px; }
    .fr .legal h2 { font-size: clamp(28px, 4vw, 40px); font-weight: 800; letter-spacing: -.03em; margin: 0 0 8px; color: #fff; }
    .fr .legal h3 { font-size: 18px; font-weight: 700; margin: 34px 0 10px; color: #fff; }
    .fr .legal p, .fr .legal li { color: var(--txt-soft); }
    .fr .legal p { margin: 0 0 12px; }
    .fr .legal a { color: var(--blue-2); }
    .fr .legal-date { font-size: 14.5px; color: var(--txt-faint); }
    .fr .legal-warn {
      background: rgba(245,158,11,.09); border: 1px solid rgba(245,158,11,.32);
      color: #FBCE7A; border-radius: 12px; padding: 17px 20px; margin: 22px 0 8px; font-size: 15px;
    }
    .fr .legal-warn strong { color: #FFD89B; }

    /* ---------- appel à l'action final ---------- */
    .fr .final { text-align: center; }
    .fr .final h2 {
      font-size: clamp(30px, 4.4vw, 46px); font-weight: 800; letter-spacing: -.03em;
      margin: 0 0 26px; color: #fff; text-wrap: balance;
    }

    /* ---------- pied de page ---------- */
    .fr footer.site { background: var(--bg-2); border-top: 1px solid var(--line); padding: 64px 0 34px; }
    .fr .foot-grid { display: grid; grid-template-columns: 1.5fr repeat(3, 1fr); gap: 36px; margin-bottom: 44px; }
    .fr .foot-brand p { margin: 15px 0 0; font-size: 14.5px; max-width: 32ch; color: var(--txt-faint); }
    .fr footer h4 { color: #fff; font-size: 12.5px; font-weight: 800; letter-spacing: .14em; text-transform: uppercase; margin: 0 0 15px; }
    .fr footer ul { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 11px; }
    .fr footer a { color: var(--txt-soft); text-decoration: none; font-size: 14.5px; }
    .fr footer a:hover { color: #fff; }
    .fr .foot-bottom { border-top: 1px solid var(--line); padding-top: 24px; font-size: 13.5px; color: var(--txt-faint); }
    @media (max-width: 900px) { .fr .foot-grid { grid-template-columns: 1fr 1fr; gap: 30px; } }

    /* ---------- apparition au défilement ---------- */
    .fr .rise { opacity: 0; transform: translateY(16px); transition: opacity .6s ease, transform .6s ease; }
    .fr .rise.seen { opacity: 1; transform: none; }
    @media (prefers-reduced-motion: reduce) {
      .fr .rise { opacity: 1; transform: none; transition: none; }
    }
  `]
})
export class FranceShellComponent implements AfterViewInit, OnDestroy {
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly router = inject(Router);
  private readonly region = inject(RegionService);

  private observer?: IntersectionObserver;
  private nav?: Subscription;

  ngAfterViewInit(): void {
    // Entrer sur le site France suffit a basculer les ecrans partages
    // (inscription, assistant, confirmation) dans leur habillage europeen,
    // faute de quoi le parcours se casserait au premier clic sortant.
    this.region.markFranceVisit();

    this.reveal();
    // Les pages sont des enfants du routeur : à chaque navigation, de nouveaux
    // blocs `.rise` apparaissent dans le DOM. Sans ce réabonnement ils
    // resteraient invisibles — le CSS les masque en attendant d'être révélés.
    this.nav = this.router.events
      .pipe(filter(e => e instanceof NavigationEnd))
      .subscribe(() => {
        window.scrollTo({ top: 0, behavior: 'auto' });
        // Le DOM de la nouvelle page n'est peint qu'après le tour de boucle.
        setTimeout(() => this.reveal(), 0);
      });
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    this.nav?.unsubscribe();
  }

  /** Fait apparaître les blocs au défilement, ou tout de suite si l'animation
   *  est refusée ou l'API absente. */
  private reveal(): void {
    const blocks = Array.from(
      this.host.nativeElement.querySelectorAll<HTMLElement>('.rise:not(.seen)'));
    if (!blocks.length) return;

    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce || !('IntersectionObserver' in window)) {
      blocks.forEach(b => b.classList.add('seen'));
      return;
    }

    this.observer ??= new IntersectionObserver(entries => {
      entries.forEach(e => {
        if (e.isIntersecting) {
          e.target.classList.add('seen');
          this.observer?.unobserve(e.target);
        }
      });
    }, { threshold: 0.12 });

    blocks.forEach(b => this.observer!.observe(b));
  }
}
