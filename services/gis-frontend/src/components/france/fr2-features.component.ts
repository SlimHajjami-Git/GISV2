import { Component, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Fr2HeaderComponent, Fr2FooterComponent } from './fr2-chrome.component';

/**
 * FONCTIONNALITÉS — reproduction de ECRANS SITE WEB CALYPSO/FONCTIONNALITES.
 *
 * Maquette de reference : 1536 x 1024 (rendu pleine largeur a ce format).
 * Reperes mesures par balayage de pixels, exprimes depuis le BAS DE L'EN-TETE
 * (y=65 sur l'image) : pastille 43, titre 92/149, paragraphe 219, mini-
 * arguments 340..377 ; cartes x 66..1478 en 6 colonnes de 233 px (ecart 10),
 * rangees de 177 px a y 424 et 611 ; bandeau final 815..925.
 *
 * L'illustration portable + telephone est DECOUPEE de la capture elle-meme
 * (assets/fr-features-hero.png, zone x 690..1500, y 75..490) : c'est la seule
 * facon d'etre fidele a une image produite hors CSS.
 *
 * Les « En savoir plus » sont des libelles non cliquables : le document maitre
 * interdit les douze pages de detail qu'ils supposeraient.
 */
@Component({
  selector: 'app-fr2-features',
  standalone: true,
  imports: [RouterLink, Fr2HeaderComponent, Fr2FooterComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr2-page">
      <app-fr2-header />
      <div class="ff">

      <!-- ══ HERO : texte a gauche, illustration decoupee a droite ══ -->
      <section class="ff-hero">
        <div class="ff-hero-txt">
          <span class="ff-pill">TOUTES LES FONCTIONNALITÉS</span>
          <h1>Tout ce qu'il vous faut<br>pour <span class="g">gérer</span> votre parc.</h1>
          <p class="ff-lede">
            Calypso centralise toutes les informations essentielles de votre parc<br>
            pour vous faire gagner du temps, réduire vos coûts et prendre les<br>
            meilleures décisions.
          </p>
          <div class="ff-minis">
            <div class="ff-mini">
              <span class="i b"><svg viewBox="0 0 24 24" fill="none" stroke="#60A5FA" stroke-width="2"><path d="M5 20V10M12 20V4M19 20v-8"/></svg></span>
              <div><h3>Modules complets</h3><p>12 modules intégrés</p></div>
            </div>
            <div class="ff-mini">
              <span class="i v"><svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.8"><path d="M7 3h7l4 4v14H7z"/><path d="M14 3v4h4M10 12h5M10 16h5"/></svg></span>
              <div><h3>Données centralisées</h3><p>Tout en un seul endroit</p></div>
            </div>
            <div class="ff-mini">
              <span class="i g"><svg viewBox="0 0 24 24" fill="none" stroke="#34D399" stroke-width="1.8"><path d="M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9z"/><circle cx="12" cy="10" r="1.6"/></svg></span>
              <div><h3>IA intégrée</h3><p>Analyse et anticipation</p></div>
            </div>
          </div>
        </div>
        <img class="ff-shot" src="/assets/fr-features-hero.png"
             alt="Tableau de bord Calypso sur ordinateur portable et mobile">
      </section>

      <!-- ══ GRILLE DES 12 MODULES : 2 rangees x 6 colonnes ══ -->
      <section class="ff-grid">
        @for (m of modules; track m.t) {
          <article class="ff-card">
            <header>
              <span class="ic" [class]="'ic ' + m.c">
                @switch (m.k) {
                  @case ('dash') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M5 20v-6M12 20V8M19 20v-9"/></svg> }
                  @case ('report') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M7 3h7l4 4v14H7z"/><path d="M14 3v4h4M10 12h5M10 16h5"/></svg> }
                  @case ('car') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M4 13l1.5-4.5A2 2 0 0 1 7.4 7h9.2a2 2 0 0 1 1.9 1.5L20 13v5h-2v-1.5H6V18H4z"/><circle cx="7.5" cy="14.5" r="1"/><circle cx="16.5" cy="14.5" r="1"/></svg> }
                  @case ('budget') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M12 3a9 9 0 1 0 9 9h-9z"/><path d="M14.5 3.3A9 9 0 0 1 20.7 9.5H14.5z"/></svg> }
                  @case ('wrench') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M14.5 6.5a4 4 0 0 0-5.6 4.9L4 16.3V20h3.7l4.9-4.9a4 4 0 0 0 4.9-5.6L14.6 12l-2.6-2.6z"/></svg> }
                  @case ('repair') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="7" cy="7" r="3"/><path d="M9.2 9.2 20 20M20 14.5V20h-5.5"/></svg> }
                  @case ('supplier') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="9" r="3.2"/><path d="M5.5 19.5a6.8 6.8 0 0 1 13 0"/></svg> }
                  @case ('expense') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="3" y="6" width="18" height="13" rx="2"/><path d="M3 10h18"/></svg> }
                  @case ('fuel') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M5 20V6a2 2 0 0 1 2-2h5a2 2 0 0 1 2 2v14M4 20h11M14 11h2.5a2 2 0 0 1 2 2v3a1.5 1.5 0 0 0 3 0V9l-2.5-2.5"/></svg> }
                  @case ('driver') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="9"/><circle cx="12" cy="10" r="2.6"/><path d="M6.5 18.5a6.5 6.5 0 0 1 11 0"/></svg> }
                  @case ('due') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="4" y="5" width="16" height="16" rx="2"/><path d="M4 10h16M9 3v4M15 3v4"/></svg> }
                  @case ('claim') { <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="M12 8v4M12 15.5v.5"/></svg> }
                }
              </span>
              <h2>{{ m.t }}</h2>
            </header>
            <p>{{ m.d }}</p>
            <span class="more">En savoir plus <span class="ar">→</span></span>
          </article>
        }
      </section>

      <!-- ══ APPEL FINAL ══ -->
      <section class="ff-cta">
        <span class="rk"><svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="1.7"><path d="M14 4c3 .5 5.5 3 6 6l-8.5 8.5-5.5-5.5zM6 15l-2 5 5-2M13.5 8.5a1.5 1.5 0 1 0 2 2"/></svg></span>
        <div class="tx">
          <h2>Prêt à découvrir toutes les fonctionnalités de Calypso&nbsp;?</h2>
          <p>Essayez gratuitement pendant 7 jours. Sans carte bancaire. Sans engagement.</p>
        </div>
        <div class="bt">
          <a routerLink="/inscription" class="b1">Essayer gratuitement</a>
          <a routerLink="/fr/tarifs" class="b2">Voir les tarifs</a>
        </div>
      </section>

      </div>
      <app-fr2-footer variant="columns" />
    </div>
  `,
  styles: [`
    .ff {
      /* Maquette a 1536 px : 1 unite = 1 px de l'image a cette largeur ;
         plafonnee a 1,05 px pour que rien ne parte en zoom au-dela. */
      --v: min(0.0651cqw, 1.05px);
      display: block; background: #05070F; color: #fff;
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      -webkit-font-smoothing: antialiased;
      padding-bottom: calc(34 * var(--v));
    }
    .ff *, .ff *::before, .ff *::after { box-sizing: border-box; }
    .ff a { text-decoration: none; }

    /* ── HERO : pastille y43, titre 92/149, paragraphe 219, minis 340 ── */
    .ff-hero { position: relative; min-height: calc(424 * var(--v)); padding: calc(43 * var(--v)) 4.297cqw 0; }
    .ff-hero-txt { position: relative; z-index: 1; width: 41cqw; min-width: calc(560 * var(--v)); }
    .ff-pill {
      display: inline-block; border: 1px solid rgba(167,139,250,.38);
      background: rgba(139,92,246,.10); color: #A78BFA;
      border-radius: calc(99 * var(--v)); padding: calc(6 * var(--v)) calc(14 * var(--v));
      font-size: calc(12 * var(--v)); font-weight: 700; letter-spacing: .12em;
    }
    .ff h1 {
      margin: calc(23 * var(--v)) 0 0; font-size: calc(44 * var(--v)); line-height: calc(57 * var(--v));
      font-weight: 800; letter-spacing: -.03em;
    }
    .ff h1 .g { background: linear-gradient(90deg,#7C7CF8,#A78BFA);
      -webkit-background-clip: text; background-clip: text; color: transparent; }
    .ff-lede { margin: calc(21 * var(--v)) 0 0; font-size: calc(16.5 * var(--v)); line-height: calc(29 * var(--v)); color: #9AA7BD; }
    .ff-minis { display: flex; gap: calc(46 * var(--v)); margin-top: calc(48 * var(--v)); }
    .ff-mini { display: flex; align-items: center; gap: calc(12 * var(--v)); white-space: nowrap; }
    .ff-mini .i {
      width: calc(38 * var(--v)); height: calc(38 * var(--v)); border-radius: calc(10 * var(--v));
      display: grid; place-items: center; flex: none;
    }
    .ff-mini .i svg { width: calc(21 * var(--v)); height: calc(21 * var(--v)); }
    .ff-mini .i.b { background: rgba(59,130,246,.14); }
    .ff-mini .i.v { background: rgba(139,92,246,.14); }
    .ff-mini .i.g { background: rgba(52,211,153,.12); }
    .ff-mini h3 { margin: 0; font-size: calc(15 * var(--v)); font-weight: 800; }
    .ff-mini p { margin: calc(2 * var(--v)) 0 0; font-size: calc(13 * var(--v)); color: #9AA7BD; }
    /* Illustration decoupee de la capture : x 690..1500, y 75..490. */
    .ff-shot {
      position: absolute; top: calc(10 * var(--v)); right: calc(36 * var(--v));
      width: calc(810 * var(--v)); height: auto; display: block;
    }

    /* ── GRILLE : x 66..1478, 6 x 233, ecart 10, rangees 177 ── */
    .ff-grid {
      width: 91.93cqw; margin: 0 auto;
      display: grid; grid-template-columns: repeat(6, 1fr); gap: calc(10 * var(--v));
    }
    .ff-card {
      min-height: calc(177 * var(--v));
      background: rgba(255,255,255,.028); border: 1px solid rgba(255,255,255,.075);
      border-radius: calc(12 * var(--v)); padding: calc(20 * var(--v));
      display: flex; flex-direction: column;
    }
    .ff-card header { display: flex; align-items: center; gap: calc(11 * var(--v)); }
    .ff-card .ic {
      width: calc(32 * var(--v)); height: calc(32 * var(--v)); border-radius: calc(9 * var(--v));
      display: grid; place-items: center; flex: none;
    }
    .ff-card .ic svg { width: calc(18 * var(--v)); height: calc(18 * var(--v)); }
    .ff-card .ic.blue   { background: rgba(59,130,246,.14);  color: #60A5FA; }
    .ff-card .ic.violet { background: rgba(139,92,246,.14);  color: #A78BFA; }
    .ff-card .ic.orange { background: rgba(249,115,22,.14);  color: #FB923C; }
    .ff-card .ic.green  { background: rgba(52,211,153,.12);  color: #34D399; }
    .ff-card .ic.red    { background: rgba(244,63,94,.13);   color: #FB7185; }
    .ff-card h2 { margin: 0; font-size: calc(16 * var(--v)); font-weight: 800; letter-spacing: -.01em; }
    .ff-card p { margin: calc(12 * var(--v)) 0 0; font-size: calc(13 * var(--v)); line-height: calc(20 * var(--v)); color: #9AA7BD; }
    .ff-card .more {
      margin-top: auto; padding-top: calc(12 * var(--v));
      font-size: calc(13 * var(--v)); font-weight: 700; color: #60A5FA;
    }
    .ff-card .more .ar { display: inline-block; transition: transform .15s; }
    .ff-card:hover .more .ar { transform: translateX(3px); }

    /* ── APPEL FINAL : y 815..925, h 110 ── */
    .ff-cta {
      width: 91.93cqw; min-height: calc(110 * var(--v)); margin: calc(26 * var(--v)) auto 0;
      background: rgba(255,255,255,.028); border: 1px solid rgba(255,255,255,.075);
      border-radius: calc(12 * var(--v));
      display: flex; align-items: center; gap: calc(20 * var(--v));
      padding: 0 calc(36 * var(--v));
    }
    .ff-cta .rk {
      width: calc(44 * var(--v)); height: calc(44 * var(--v)); border-radius: 50%; flex: none;
      background: rgba(255,255,255,.06); border: 1px solid rgba(255,255,255,.1);
      display: grid; place-items: center;
    }
    .ff-cta .rk svg { width: calc(22 * var(--v)); height: calc(22 * var(--v)); }
    .ff-cta h2 { margin: 0; font-size: calc(22 * var(--v)); font-weight: 800; letter-spacing: -.02em; }
    .ff-cta .tx p { margin: calc(7 * var(--v)) 0 0; font-size: calc(13.5 * var(--v)); color: #9AA7BD; }
    .ff-cta .bt { margin-left: auto; display: flex; gap: calc(14 * var(--v)); }
    .ff-cta .b1, .ff-cta .b2 {
      display: inline-flex; align-items: center; justify-content: center;
      height: calc(48 * var(--v)); padding: 0 calc(30 * var(--v)); border-radius: calc(9 * var(--v));
      font-size: calc(14.5 * var(--v)); font-weight: 700; white-space: nowrap;
    }
    .ff-cta .b1 { background: linear-gradient(135deg,#4F46E5,#7C3AED); color: #fff; }
    .ff-cta .b2 { border: 1px solid rgba(255,255,255,.18); color: #fff; }

    /* Sous 900 px la grille de 6 ne tient plus. */
    @media (max-width: 900px) {
      .ff-hero { min-height: 0; padding-bottom: calc(30 * var(--v)); }
      .ff-hero-txt { width: auto; min-width: 0; }
      .ff-shot { position: static; width: 100%; margin-top: calc(24 * var(--v)); }
      .ff-minis { flex-wrap: wrap; gap: calc(18 * var(--v)); }
      .ff-grid { grid-template-columns: repeat(2, 1fr); }
      .ff-cta { flex-wrap: wrap; padding: calc(20 * var(--v)) calc(24 * var(--v)); }
      .ff-cta .bt { margin-left: 0; width: 100%; }
      .ff-cta .b1, .ff-cta .b2 { flex: 1; }
    }
    @media (min-width: 901px) and (max-width: 1200px) {
      .ff-grid { grid-template-columns: repeat(3, 1fr); }
    }
  `]
})
export class Fr2FeaturesComponent {
  readonly modules = [
    { k: 'dash',     c: 'blue',   t: 'Tableau de bord', d: "Vue d'ensemble de votre parc et de vos indicateurs clés en temps réel." },
    { k: 'report',   c: 'violet', t: 'Rapports',        d: "Analysez l'activité, les coûts, la consommation et bien plus encore." },
    { k: 'car',      c: 'blue',   t: 'Véhicules',       d: 'Centralisez les informations de vos véhicules, leur kilométrage et leur historique.' },
    { k: 'budget',   c: 'orange', t: 'Budget',          d: 'Prévision budgétaire et suivi des dépenses pour mieux maîtriser vos coûts.' },
    { k: 'wrench',   c: 'green',  t: 'Entretiens',      d: 'Planifiez et suivez tous les entretiens préventifs de vos véhicules.' },
    { k: 'repair',   c: 'red',    t: 'Réparations',     d: 'Suivez les interventions, pièces remplacées et coûts associés.' },
    { k: 'supplier', c: 'violet', t: 'Fournisseurs',    d: 'Gérez vos prestataires et gardez toutes leurs informations à jour.' },
    { k: 'expense',  c: 'blue',   t: 'Dépenses',        d: 'Centralisez toutes les dépenses liées à votre parc automobile.' },
    { k: 'fuel',     c: 'green',  t: 'Carburants',      d: 'Suivez les pleins, la consommation et les coûts de carburant.' },
    { k: 'driver',   c: 'green',  t: 'Chauffeurs',      d: 'Gérez les conducteurs, leurs profils et les infractions.' },
    { k: 'due',      c: 'orange', t: 'Échéances',       d: 'Ne manquez plus aucune échéance : assurance, CT, vignette et plus.' },
    { k: 'claim',    c: 'red',    t: 'Sinistres',       d: 'Déclarez et suivez vos sinistres avec photos et documents.' }
  ];
}
