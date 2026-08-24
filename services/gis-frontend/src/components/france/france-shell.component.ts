import {
  AfterViewInit, Component, ElementRef, OnDestroy, ViewEncapsulation, inject
} from '@angular/core';
import {
  NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet
} from '@angular/router';
import { Subscription, filter } from 'rxjs';

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
 * <code>footer a</code> repeindraient toute l'application de gestion.</p>
 */
@Component({
  selector: 'app-france-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr">
      <header class="site">
        <div class="shell bar">
          <a class="brand" routerLink="/fr" aria-label="Calypso, accueil">
            <svg width="30" height="30" viewBox="0 0 32 32" fill="none" aria-hidden="true">
              <defs>
                <linearGradient id="cg" x1="0" y1="0" x2="32" y2="32">
                  <stop offset="0" stop-color="#1B4FD8"/>
                  <stop offset="1" stop-color="#23A6C9"/>
                </linearGradient>
              </defs>
              <path d="M26 8.5A11 11 0 1 0 26 23.5" stroke="url(#cg)" stroke-width="5"
                    stroke-linecap="round" fill="none"/>
            </svg>
            <span class="brand-word">Calypso</span>
          </a>
          <nav class="nav">
            <a class="hide-sm" routerLink="/fr/fonctionnalites" routerLinkActive="on">Fonctionnalités</a>
            <a class="hide-sm" routerLink="/fr/tarifs" routerLinkActive="on">Tarifs</a>
            <a class="hide-sm" routerLink="/fr/calypso-auto" routerLinkActive="on">Calypso Auto</a>
            <a class="hide-sm" routerLink="/fr/contact" routerLinkActive="on">Contact</a>
            <a routerLink="/login">Se connecter</a>
            <a class="btn btn-primary btn-sm" routerLink="/inscription">Essayer gratuitement</a>
          </nav>
        </div>
      </header>

      <main>
        <router-outlet></router-outlet>
      </main>

      <footer class="site">
        <div class="shell">
          <div class="foot-grid">
            <div class="foot-brand">
              <span class="brand-word">Calypso</span>
              <p>La solution intelligente pour gérer votre parc automobile.</p>
            </div>
            <div>
              <h4>Produit</h4>
              <ul>
                <li><a routerLink="/fr/fonctionnalites">Fonctionnalités</a></li>
                <li><a routerLink="/fr/tarifs">Tarifs</a></li>
                <li><a routerLink="/fr/calypso-auto">Calypso Auto</a></li>
              </ul>
            </div>
            <div>
              <h4>Société</h4>
              <ul>
                <li><a routerLink="/fr/contact">Contact</a></li>
                <li><a routerLink="/login">Se connecter</a></li>
                <li><a routerLink="/inscription">Essayer gratuitement</a></li>
              </ul>
            </div>
            <div>
              <h4>Légal</h4>
              <ul>
                <li><a routerLink="/fr/mentions-legales">Mentions légales</a></li>
                <li><a routerLink="/fr/confidentialite">Politique de confidentialité</a></li>
              </ul>
            </div>
          </div>
          <div class="foot-bottom">© 2026 Calypso. Tous droits réservés.</div>
        </div>
      </footer>
    </div>
  `,
  styles: [`
    /* ------------------------------------------------------------------
       Identité Calypso, d'après la planche de maquettes :
       accueil CLAIR (blanc → bleu très pâle), bandeau BLEU NUIT réservé
       aux sections d'accroche, bouton bleu franc pour le CTA unique.
       Les accents cyan et turquoise viennent du dégradé du logo officiel.
       Le site assume une identité de marque unique : toutes les couleurs
       sont peintes explicitement et ne suivent pas le thème du lecteur.
       ------------------------------------------------------------------ */
    .fr {
      --white:      #FFFFFF;
      --sky:        #F2F7FD;
      --sky-2:      #E6EFFA;
      --navy:       #0C1A33;
      --navy-2:     #13263F;
      --navy-3:     #1D3556;
      --blue:       #1B4FD8;
      --blue-dark:  #163FAD;
      --blue-soft:  #E8EFFE;
      --cyan:       #23A6C9;
      --teal:       #5FE3BD;
      --violet:     #6B5CE7;
      --ink:        #0E1A2B;
      --ink-soft:   #52637A;
      --ink-faint:  #8A99AC;
      --rule:       #DCE6F2;
      --rule-dark:  #24405F;
      --sh-sm:      0 1px 2px rgba(12,26,51,.05), 0 4px 14px rgba(12,26,51,.05);
      --sh-md:      0 2px 8px rgba(12,26,51,.07), 0 20px 48px rgba(12,26,51,.11);
      --r:          14px;
      --r-sm:       10px;
    }
    .fr, .fr * { box-sizing: border-box; }
    .fr {
      margin: 0; background: var(--white); color: var(--ink);
      font-family: "Source Sans 3", system-ui, -apple-system, "Segoe UI", sans-serif;
      font-size: 17px; line-height: 1.65; -webkit-font-smoothing: antialiased;
    }
    .fr h1, .fr h2, .fr h3, .fr h4, .fr .brand-word, .fr .btn, .fr .eyebrow, .fr .nav a, .fr .tab { font-family: Manrope, system-ui, sans-serif; }
    .fr .shell { max-width: 1140px; margin: 0 auto; padding: 0 24px; }
    .fr .eyebrow { font-size: 12px; font-weight: 700; letter-spacing: .15em; text-transform: uppercase; margin: 0 0 14px; }
  
    /* boutons */
    .fr .btn {
      display: inline-flex; align-items: center; justify-content: center;
      font-size: 15px; font-weight: 700; padding: 14px 26px; border-radius: var(--r-sm);
      text-decoration: none; border: 1.5px solid transparent; cursor: pointer;
      transition: transform .18s ease, background .18s ease, border-color .18s ease, color .18s ease;
    }
    .fr .btn-primary { background: var(--blue); color: var(--white); }
    .fr .btn-primary:hover { background: var(--blue-dark); transform: translateY(-1px); }
    .fr .btn-ghost { background: var(--white); color: var(--ink); border-color: var(--rule); }
    .fr .btn-ghost:hover { border-color: var(--blue); color: var(--blue); transform: translateY(-1px); }
    .fr .btn-light { background: var(--white); color: var(--blue); }
    .fr .btn-light:hover { transform: translateY(-1px); }
    .fr .btn-sm { padding: 10px 18px; font-size: 14px; }
    .fr a:focus-visible, .fr button:focus-visible, .fr summary:focus-visible { outline: 3px solid var(--cyan); outline-offset: 3px; border-radius: 6px; }
  
    /* header clair, comme la maquette */
    .fr header.site {
      position: sticky; top: 0; z-index: 50; background: rgba(255,255,255,.94);
      backdrop-filter: blur(10px); border-bottom: 1px solid var(--rule);
    }
    .fr .bar { display: flex; align-items: center; gap: 30px; height: 70px; }
    .fr .brand { display: flex; align-items: center; gap: 10px; text-decoration: none; }
    .fr .brand-word { color: var(--ink); font-size: 20px; font-weight: 800; letter-spacing: -.02em; }
    .fr .nav { display: flex; gap: 24px; margin-left: auto; align-items: center; }
    .fr .nav a { color: var(--ink-soft); text-decoration: none; font-size: 14.5px; font-weight: 600; }
    .fr .nav a:hover { color: var(--blue); }
    @media (max-width: 980px) { .fr .nav .hide-sm { display: none; } .fr .bar { gap: 14px; } }
  
    .fr section { padding: 112px 0; }
    .fr section.tight { padding: 90px 0; }
    .fr .band-sky { background: linear-gradient(180deg, var(--sky) 0%, var(--white) 100%); }
    .fr .band-white { background: var(--white); }
    .fr .band-pale { background: var(--sky); }
    .fr .band-navy { background: var(--navy); color: #fff; }
  
    .fr .sec-head { max-width: 680px; margin: 0 auto 60px; text-align: center; }
    .fr .sec-head h2 { font-size: clamp(29px, 4.1vw, 42px); font-weight: 800; letter-spacing: -.025em; line-height: 1.14; margin: 0 0 14px; text-wrap: balance; }
    .fr .sec-head p { font-size: 18px; color: var(--ink-soft); margin: 0; }
    .fr .band-navy .sec-head p { color: rgba(255,255,255,.74); }
  
    /* ---------- 01 · HERO (clair) ---------- */
    .fr .hero { padding: 88px 0 96px; }
    .fr .hero-grid { display: grid; grid-template-columns: 1fr 1.06fr; gap: 56px; align-items: center; }
    .fr .hero h1 { font-size: clamp(36px, 4.9vw, 52px); font-weight: 800; letter-spacing: -.03em; line-height: 1.1; margin: 0 0 20px; text-wrap: balance; }
    .fr .hero h1 .accent { color: var(--blue); }
    .fr .hero p.lede { font-size: 18px; color: var(--ink-soft); margin: 0 0 12px; max-width: 50ch; }
    .fr .hero p.incl { font-size: 16px; font-weight: 600; color: var(--ink); margin: 0 0 26px; }
    .fr .hero-cta { display: flex; gap: 13px; flex-wrap: wrap; margin-bottom: 16px; }
    .fr .hero-note { font-size: 14.5px; color: var(--ink-faint); display: flex; align-items: center; gap: 10px; flex-wrap: wrap; margin: 0; }
    .fr .hero-note .dot { width: 4px; height: 4px; border-radius: 50%; background: var(--cyan); }
  
    .fr .shot { position: relative; }
    .fr .panel { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-md); padding: 22px; }
    .fr .panel-top { display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px; }
    .fr .panel-title { font-family: Manrope, sans-serif; font-weight: 700; font-size: 15px; }
    .fr .panel-date { font-size: 12.5px; color: var(--ink-faint); }
    .fr .kpis { display: grid; grid-template-columns: repeat(3, 1fr); gap: 10px; margin-bottom: 16px; }
    .fr .kpi { background: var(--sky); border-radius: var(--r-sm); padding: 12px 13px; }
    .fr .kpi .lbl { font-size: 10.5px; letter-spacing: .09em; text-transform: uppercase; color: var(--ink-faint); font-weight: 600; }
    .fr .kpi .val { font-family: "IBM Plex Mono", monospace; font-size: 21px; font-weight: 500; margin-top: 2px; font-variant-numeric: tabular-nums; }
    .fr .kpi.a .val { color: var(--blue); } .kpi.b .val { color: var(--cyan); } .kpi.c .val { color: var(--violet); }
    .fr .rows { display: flex; flex-direction: column; gap: 9px; }
    .fr .row { display: flex; align-items: center; gap: 12px; font-size: 13.5px; }
    .fr .row .name { width: 126px; color: var(--ink-soft); flex-shrink: 0; }
    .fr .bar-track { flex: 1; height: 7px; background: var(--sky-2); border-radius: 99px; overflow: hidden; }
    .fr .bar-fill { height: 100%; border-radius: 99px; }
    .fr .row .pct { font-family: "IBM Plex Mono", monospace; font-size: 12px; color: var(--ink-faint); width: 42px; text-align: right; font-variant-numeric: tabular-nums; }
    /* mobile posé sur le tableau de bord, comme la maquette */
    .fr .phone {
      position: absolute; right: -14px; bottom: -30px; width: 132px;
      background: var(--navy); border-radius: 18px; padding: 9px 8px;
      box-shadow: var(--sh-md); border: 3px solid var(--navy-2);
    }
    .fr .phone .p-h { color: #fff; font-family: Manrope, sans-serif; font-size: 10px; font-weight: 700; padding: 3px 5px 7px; }
    .fr .phone .p-k { background: rgba(255,255,255,.09); border-radius: 7px; padding: 7px 8px; margin-bottom: 5px; }
    .fr .phone .p-k .l { font-size: 7.5px; letter-spacing: .06em; text-transform: uppercase; color: rgba(255,255,255,.5); font-weight: 600; }
    .fr .phone .p-k .v { font-family: "IBM Plex Mono", monospace; font-size: 13px; color: #fff; }
    @media (max-width: 940px) { .fr .hero-grid { grid-template-columns: 1fr; gap: 44px; } .fr .phone { display: none; } .fr section { padding: 80px 0; } }
  
    /* ---------- rangée des univers ---------- */
    .fr .univers { display: grid; grid-template-columns: repeat(5, 1fr); gap: 14px; max-width: 980px; margin: 0 auto; }
    .fr .uni { text-align: center; padding: 22px 12px; border-radius: var(--r); transition: background .18s ease; }
    .fr .uni:hover { background: var(--sky); }
    .fr .uni .ic { width: 44px; height: 44px; border-radius: 12px; background: var(--blue-soft); display: flex; align-items: center; justify-content: center; margin: 0 auto 12px; }
    .fr .uni h3 { font-size: 15.5px; font-weight: 700; margin: 0 0 5px; }
    .fr .uni p { font-size: 13.5px; color: var(--ink-soft); margin: 0; line-height: 1.5; }
    @media (max-width: 860px) { .fr .univers { grid-template-columns: repeat(2, 1fr); } }
  
    /* ---------- onglets ---------- */
    .fr .tabs { display: flex; gap: 8px; justify-content: center; flex-wrap: wrap; margin: 48px 0 34px; }
    .fr .tab { background: var(--white); border: 1.5px solid var(--rule); color: var(--ink-soft); font-size: 14.5px; font-weight: 600; padding: 11px 20px; border-radius: 99px; cursor: pointer; transition: all .18s ease; }
    .fr .tab:hover { border-color: var(--blue); color: var(--blue); }
    .fr .tab[aria-selected="true"] { background: var(--navy); border-color: var(--navy); color: #fff; }
    .fr .stage { max-width: 940px; margin: 0 auto; }
    .fr .stage-panel { display: none; }
    .fr .stage-panel.on { display: block; animation: rise .34s ease both; }
    .fr .stage-inner { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-md); padding: 30px; display: grid; grid-template-columns: 1fr 1.2fr; gap: 32px; align-items: center; }
    .fr .stage-copy h3 { font-size: 22px; font-weight: 800; letter-spacing: -.02em; margin: 0 0 10px; }
    .fr .stage-copy p { color: var(--ink-soft); margin: 0 0 15px; font-size: 16px; }
    .fr .ticks { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 8px; }
    .fr .ticks li { display: flex; gap: 10px; align-items: flex-start; font-size: 15px; color: var(--ink-soft); }
    .fr .ticks svg { flex-shrink: 0; margin-top: 4px; }
    .fr .chartbox { background: var(--sky); border-radius: var(--r-sm); padding: 18px; }
    .fr .chartbox .cap { font-size: 11px; letter-spacing: .09em; text-transform: uppercase; color: var(--ink-faint); font-weight: 600; margin-bottom: 12px; }
    @media (max-width: 820px) { .fr .stage-inner { grid-template-columns: 1fr; } }
  
    /* ---------- 03 · intelligence (bandeau bleu nuit) ---------- */
    .fr .icards { display: grid; grid-template-columns: repeat(3, 1fr); gap: 18px; margin-bottom: 44px; }
    .fr .icard { background: var(--navy-2); border: 1px solid var(--rule-dark); border-radius: var(--r); padding: 26px 24px; }
    .fr .icard .ic { width: 40px; height: 40px; border-radius: 11px; background: rgba(255,255,255,.07); display: flex; align-items: center; justify-content: center; margin-bottom: 14px; }
    .fr .icard h3 { font-size: 16.5px; font-weight: 700; margin: 0 0 8px; color: #fff; }
    .fr .icard p { margin: 0; color: rgba(255,255,255,.72); font-size: 15px; }
    .fr .icard .k { font-family: Manrope, sans-serif; font-size: 11px; font-weight: 700; letter-spacing: .14em; margin-bottom: 8px; }
    .fr .icard:nth-child(1) .k { color: var(--teal); }
    .fr .icard:nth-child(2) .k { color: var(--cyan); }
    .fr .icard:nth-child(3) .k { color: var(--violet); }
    @media (max-width: 800px) { .fr .icards { grid-template-columns: 1fr; } }
  
    .fr .demo { max-width: 620px; margin: 0 auto; background: var(--navy-2); border: 1px solid var(--rule-dark); border-radius: var(--r); overflow: hidden; }
    .fr .demo-head { display: flex; align-items: center; gap: 10px; padding: 14px 20px; border-bottom: 1px solid var(--rule-dark); font-family: Manrope, sans-serif; font-weight: 700; font-size: 14.5px; }
    .fr .demo-body { padding: 20px; display: flex; flex-direction: column; gap: 13px; }
    .fr .bubble { border-radius: 12px; padding: 13px 16px; font-size: 15px; max-width: 84%; }
    .fr .bubble.q { background: var(--navy-3); color: #fff; align-self: flex-end; border-bottom-right-radius: 4px; }
    .fr .bubble.a { background: rgba(255,255,255,.05); border: 1px solid var(--rule-dark); color: rgba(255,255,255,.86); border-bottom-left-radius: 4px; }
    .fr .bubble.a ul { margin: 9px 0 0; padding-left: 18px; }
    .fr .demo-foot { padding: 12px 20px; border-top: 1px solid var(--rule-dark); font-size: 13px; color: rgba(255,255,255,.5); text-align: center; }
  
    /* ---------- 04 · tarifs ---------- */
    .fr .included { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); padding: 24px 28px; margin: 0 auto 28px; max-width: 940px; box-shadow: var(--sh-sm); }
    .fr .included h3 { margin: 0 0 15px; font-size: 15.5px; font-weight: 800; text-align: center; }
    .fr .feat-grid { display: flex; flex-wrap: wrap; gap: 9px; justify-content: center; }
    .fr .feat { display: inline-flex; align-items: center; gap: 8px; background: var(--sky); border-radius: 99px; padding: 8px 15px; font-size: 14.5px; color: var(--ink-soft); }
    .fr .feat .d { width: 5px; height: 5px; border-radius: 50%; background: var(--cyan); }
    .fr .plans { display: grid; grid-template-columns: repeat(2, 1fr); gap: 22px; max-width: 940px; margin: 0 auto; }
    .fr .plan { background: var(--white); border: 1.5px solid var(--rule); border-radius: var(--r); padding: 32px 30px; position: relative; box-shadow: var(--sh-sm); display: flex; flex-direction: column; }
    .fr .plan.reco { border-color: var(--blue); box-shadow: var(--sh-md); }
    .fr .plan .badge { position: absolute; top: -13px; left: 30px; background: var(--blue); color: #fff; font-family: Manrope, sans-serif; font-size: 11px; font-weight: 700; letter-spacing: .12em; padding: 6px 13px; border-radius: 99px; }
    .fr .plan .term { font-family: Manrope, sans-serif; font-size: 12.5px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; color: var(--ink-faint); }
    .fr .price { display: flex; align-items: baseline; gap: 7px; margin: 13px 0 4px; }
    .fr .price .n { font-family: Manrope, sans-serif; font-size: 44px; font-weight: 800; letter-spacing: -.03em; }
    .fr .price .u { color: var(--ink-soft); font-size: 15.5px; }
    .fr .plan .sub { color: var(--ink-soft); font-size: 15px; margin: 0 0 6px; }
    .fr .plan .save { font-size: 14px; font-weight: 600; color: var(--cyan); margin: 0 0 20px; }
    .fr .plan .flex { font-size: 14px; font-weight: 600; color: var(--ink-faint); margin: 0 0 20px; }
    .fr .plan .same { font-size: 14.5px; color: var(--ink-soft); margin: 0 0 22px; padding-top: 18px; border-top: 1px solid var(--rule); }
    .fr .plan .btn { margin-top: auto; }
    @media (max-width: 780px) { .fr .plans { grid-template-columns: 1fr; } }
  
    /* ---------- 05 · calypso auto ---------- */
    .fr .auto-card { max-width: 960px; margin: 0 auto; background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-md); padding: 36px; display: grid; grid-template-columns: 1.05fr .95fr; gap: 40px; align-items: center; }
    .fr .auto-card .eyebrow { color: var(--violet); }
    .fr .auto-card h3 { font-size: 25px; font-weight: 800; letter-spacing: -.025em; margin: 0 0 12px; text-wrap: balance; }
    .fr .auto-card > div > p { color: var(--ink-soft); margin: 0 0 20px; }
    .fr .themes { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
    .fr .theme { background: var(--sky); border-radius: var(--r-sm); padding: 14px 16px; font-weight: 600; font-size: 15px; display: flex; align-items: center; gap: 10px; }
    .fr .theme .d { width: 6px; height: 6px; border-radius: 50%; }
    .fr .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .fr .chip-q { background: var(--sky); border: 1px solid var(--rule); border-radius: 99px; padding: 8px 14px; font-size: 13.5px; color: var(--ink-soft); }
    .fr .steps { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; max-width: 800px; margin: 40px auto 0; }
    .fr .step { text-align: center; }
    .fr .step .n { font-family: Manrope, sans-serif; font-size: 12px; font-weight: 700; color: var(--blue); letter-spacing: .1em; }
    .fr .step h4 { font-size: 15.5px; font-weight: 700; margin: 6px 0 5px; }
    .fr .step p { font-size: 14px; color: var(--ink-soft); margin: 0; }
    @media (max-width: 820px) { .fr .auto-card { grid-template-columns: 1fr; padding: 28px; } .fr .steps { grid-template-columns: 1fr; } }
    .fr .bridge { text-align: center; margin-top: 28px; font-size: 15.5px; color: var(--ink-soft); }
    .fr .bridge a { color: var(--blue); font-weight: 600; }
  
    /* ---------- 06 · confiance ---------- */
    .fr .trust { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; max-width: 940px; margin: 0 auto; }
    .fr .trust-item { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); padding: 26px 24px; }
    .fr .trust-item h3 { font-size: 17px; font-weight: 800; margin: 13px 0 8px; }
    .fr .trust-item p { margin: 0; color: var(--ink-soft); font-size: 15.5px; }
    @media (max-width: 780px) { .fr .trust { grid-template-columns: 1fr; } }
  
    /* ---------- 07 · faq ---------- */
    .fr .faq { max-width: 780px; margin: 0 auto; }
    .fr details { border-bottom: 1px solid var(--rule); }
    .fr details summary { cursor: pointer; list-style: none; padding: 20px 40px 20px 0; position: relative; font-family: Manrope, sans-serif; font-weight: 700; font-size: 16.5px; }
    .fr details summary::-webkit-details-marker { display: none; }
    .fr details summary::after { content: "+"; position: absolute; right: 6px; top: 50%; transform: translateY(-50%); font-size: 22px; color: var(--blue); }
    .fr details[open] summary::after { content: "−"; }
    .fr details .ans { padding: 0 40px 20px 0; color: var(--ink-soft); margin: 0; }
    .fr .todo { display: inline-block; margin-top: 6px; font-size: 13px; font-weight: 600; background: #FFF4E0; color: #92600C; border: 1px solid #F0D9A8; padding: 6px 12px; border-radius: 8px; }
  
    /* ---------- 08 · cta ---------- */
    .fr .final { text-align: center; }
    .fr .final h2 { font-size: clamp(29px, 4.3vw, 44px); font-weight: 800; letter-spacing: -.03em; margin: 0 0 24px; color: #fff; text-wrap: balance; }
  
    /* ---------- 09 · footer ---------- */
    .fr footer.site { background: #08131F; color: rgba(255,255,255,.66); padding: 60px 0 32px; }
    .fr .foot-grid { display: grid; grid-template-columns: 1.4fr repeat(4, 1fr); gap: 32px; margin-bottom: 42px; }
    .fr .foot-brand .brand-word { color: #fff; }
    .fr .foot-brand p { margin: 13px 0 0; font-size: 14.5px; max-width: 30ch; color: rgba(255,255,255,.5); }
    .fr footer h4 { font-family: Manrope, sans-serif; color: #fff; font-size: 12.5px; font-weight: 700; letter-spacing: .13em; text-transform: uppercase; margin: 0 0 14px; }
    .fr footer ul { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 10px; }
    .fr footer a { color: rgba(255,255,255,.66); text-decoration: none; font-size: 14.5px; }
    .fr footer a:hover { color: #fff; }
    .fr .foot-bottom { border-top: 1px solid rgba(255,255,255,.1); padding-top: 22px; font-size: 13.5px; color: rgba(255,255,255,.44); }
    @media (max-width: 900px) { .fr .foot-grid { grid-template-columns: 1fr 1fr; gap: 28px; } }
  
    .fr .rise { opacity: 0; transform: translateY(14px); transition: opacity .55s ease, transform .55s ease; }
    .fr .rise.seen { opacity: 1; transform: none; }
    @keyframes rise { from { opacity: 0; transform: translateY(10px); } to { opacity: 1; transform: none; } }
    @media (prefers-reduced-motion: reduce) { .fr .rise { opacity: 1; transform: none; transition: none; } .fr .stage-panel.on { animation: none; } .fr { scroll-behavior: auto; } }
    .fr { scroll-behavior: smooth; }
  
    /* ---------- navigation entre pages ---------- */
    .fr .page[hidden] { display: none; }
    .fr .nav a.on { color: var(--blue); }
  
    /* ---------- page Fonctionnalités ---------- */
    .fr .feat-page { display: grid; grid-template-columns: 1fr 1.05fr; gap: 46px; align-items: center; max-width: 1000px; margin: 0 auto; }
    .fr .feat-list { display: flex; flex-direction: column; gap: 26px; }
    .fr .feat-row { display: flex; gap: 16px; align-items: flex-start; }
    .fr .feat-row .ic { width: 42px; height: 42px; border-radius: 11px; background: var(--blue-soft); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .fr .feat-row h3 { font-size: 16.5px; font-weight: 700; margin: 0 0 4px; }
    .fr .feat-row p { margin: 0; color: var(--ink-soft); font-size: 15px; line-height: 1.55; }
    @media (max-width: 880px) { .fr .feat-page { grid-template-columns: 1fr; gap: 36px; } }
  
    .fr .more { display: grid; grid-template-columns: repeat(5, 1fr); gap: 16px; }
    .fr .more-item { text-align: center; display: flex; flex-direction: column; align-items: center; gap: 11px; font-size: 14.5px; font-weight: 600; color: rgba(255,255,255,.84); }
    @media (max-width: 820px) { .fr .more { grid-template-columns: repeat(2, 1fr); gap: 26px; } }
  
    /* ---------- page Calypso Auto ---------- */
    .fr .auto-hero { display: grid; grid-template-columns: 1.05fr .95fr; gap: 50px; align-items: center; max-width: 980px; margin: 0 auto; }
    .fr .bot-wrap { max-width: 300px; justify-self: center; }
    @media (max-width: 820px) { .fr .auto-hero { grid-template-columns: 1fr; gap: 32px; } .fr .bot-wrap { max-width: 220px; } }
  
    /* ---------- page Contact ---------- */
    .fr .contact-grid { display: grid; grid-template-columns: .85fr 1.15fr; gap: 44px; max-width: 980px; margin: 0 auto; align-items: start; }
    .fr .contact-info { display: flex; flex-direction: column; gap: 24px; }
    .fr .ci { display: flex; gap: 14px; align-items: flex-start; }
    .fr .ci .ic { width: 38px; height: 38px; border-radius: 10px; background: var(--blue-soft); display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
    .fr .ci h4 { font-family: Manrope, sans-serif; font-size: 14px; font-weight: 700; margin: 0 0 4px; letter-spacing: 0; text-transform: none; color: var(--ink); }
    .fr .ci p { margin: 0; font-size: 14.5px; color: var(--ink-soft); }
    .fr .contact-form { background: var(--white); border: 1px solid var(--rule); border-radius: var(--r); box-shadow: var(--sh-sm); padding: 28px; }
    .fr .contact-form label { display: block; font-family: Manrope, sans-serif; font-size: 13.5px; font-weight: 700; margin: 0 0 6px; }
    .fr .contact-form label + input, .fr .contact-form label + textarea { margin-bottom: 18px; }
    .fr .contact-form input, .fr .contact-form textarea {
      width: 100%; border: 1.5px solid var(--rule); border-radius: var(--r-sm);
      padding: 11px 14px; font-family: inherit; font-size: 15px; color: var(--ink);
      background: var(--white); transition: border-color .16s ease;
    }
    .fr .contact-form input:focus, .fr .contact-form textarea:focus { outline: none; border-color: var(--blue); }
    .fr .contact-form textarea { resize: vertical; }
    @media (max-width: 820px) { .fr .contact-grid { grid-template-columns: 1fr; gap: 32px; } }
  
    /* ---------- pages légales ---------- */
    .fr .legal { max-width: 760px; }
    .fr .legal h2 { font-family: Manrope, sans-serif; font-size: clamp(28px, 4vw, 38px); font-weight: 800; letter-spacing: -.025em; margin: 0 0 8px; }
    .fr .legal h3 { font-size: 17.5px; font-weight: 700; margin: 32px 0 10px; }
    .fr .legal p { color: var(--ink-soft); margin: 0 0 12px; }
    .fr .legal-date { font-size: 14.5px; }
    .fr .legal-warn {
      background: #FFF4E0; border: 1px solid #F0D9A8; color: #6E4906;
      border-radius: var(--r-sm); padding: 16px 20px; margin: 22px 0 6px; font-size: 15px;
    }
    .fr .legal .todo { display: block; }

    /* ---------- captures produit réelles ---------- */
    .fr .shot img, .fr .shot-img {
      display: block; width: 100%; height: auto; border-radius: var(--r);
      border: 1px solid var(--rule); box-shadow: var(--sh-md); background: var(--white);
    }
    .fr .shot-cap { margin: 10px 0 0; font-size: 13px; color: var(--ink-faint); text-align: center; }
    .fr .gallery { display: grid; grid-template-columns: repeat(3, 1fr); gap: 20px; max-width: 1040px; margin: 0 auto; }
    .fr .gallery figure { margin: 0; }
    .fr .gallery figcaption { margin-top: 9px; font-size: 13.5px; font-weight: 600; color: var(--ink-soft); text-align: center; }
    @media (max-width: 880px) { .fr .gallery { grid-template-columns: 1fr 1fr; } }
    @media (max-width: 560px) { .fr .gallery { grid-template-columns: 1fr; } }

    /* Le défilement doux appartient au document, pas au conteneur. */
    .fr { scroll-behavior: auto; }
  `]
})
export class FranceShellComponent implements AfterViewInit, OnDestroy {
  private readonly host: ElementRef<HTMLElement> = inject(ElementRef);
  private readonly router = inject(Router);

  private observer?: IntersectionObserver;
  private nav?: Subscription;

  ngAfterViewInit(): void {
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
