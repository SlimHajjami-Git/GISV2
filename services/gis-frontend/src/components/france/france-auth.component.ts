import { Component, Input, ViewEncapsulation } from '@angular/core';
import { Fr2HeaderComponent, Fr2DecoComponent, Fr2FooterComponent } from './fr2-chrome.component';

/**
 * Habillage commun des ecrans d'authentification europeens — refait d'apres
 * les captures CONNEXION / CREER UN COMPTE / MOT DE PASSE OUBLIE /
 * REINITIALISER (maquettes 1402 x 1122).
 *
 * Reperes mesures sur CONNEXION : titre (glyphes) y 150..187 sous un en-tete
 * de 88, sous-titre 211..227 ; carte x 373..1027 (654 px), y 264..824 ;
 * champs de 46 px de haut a 47 px des bords ; bouton plein y 609..653,
 * « ou » 681..688, bouton contour 709..756, note 785..798.
 *
 * Le contrat de classes (fa-title, fa-card, fa-field, fa-btn…) est conserve :
 * les ecrans existants projettent leur contenu sans modification de logique.
 * L'icone au-dessus du titre (fa-badge hors carte) n'existe que sur MOT DE
 * PASSE OUBLIE et REINITIALISER ; sur CONNEXION l'avatar est DANS la carte.
 */
@Component({
  selector: 'app-france-auth',
  standalone: true,
  imports: [Fr2HeaderComponent, Fr2DecoComponent, Fr2FooterComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr2-page">
      <app-fr2-header [accueil]="true" />
      <app-fr2-deco />
      <main class="fa-main" [class.fa-wide]="wide">
        <ng-content></ng-content>
      </main>
      <app-fr2-footer variant="rich" [perks]="perks" />
    </div>
  `,
  styles: [`
    /* Maquette a 1402 px : 1 unite = 1 px de l'image a cette largeur,
       plafonnee a 1,15 px (l'equivalent d'un ecran de 1612). */
    .fa-main {
      --a: min(0.07133cqw, 1.15px);
      position: relative; z-index: 1;
      display: block; padding: calc(48 * var(--a)) 0 calc(52 * var(--a));
      text-align: center;
    }

    /* ── Icone ronde au-dessus du titre (mdp / reinitialisation) ── */
    .fa-badge {
      width: calc(88 * var(--a)); height: calc(88 * var(--a)); border-radius: 50%;
      margin: 0 auto calc(20 * var(--a));
      border: calc(2 * var(--a)) solid rgba(167,139,250,.75);
      display: grid; place-items: center;
      box-shadow: 0 0 calc(30 * var(--a)) rgba(139,92,246,.25);
    }
    .fa-badge svg { width: calc(38 * var(--a)); height: calc(38 * var(--a)); }

    /* ── Titre : 42 px, seconde partie violette ── */
    .fa-title {
      margin: 0; font-size: calc(42 * var(--a)); line-height: 1.15;
      font-weight: 800; letter-spacing: -.02em; color: #fff;
    }
    .fa-title em {
      font-style: normal;
      background: linear-gradient(90deg,#8B5CF6,#A78BFA);
      -webkit-background-clip: text; background-clip: text; color: transparent;
    }
    .fa-sub {
      margin: calc(16 * var(--a)) 0 0; font-size: calc(16 * var(--a));
      line-height: calc(24 * var(--a)); color: #C7D2E4;
    }

    /* ── Carte : 654 px centres, 1006 px en mode large (inscription) ── */
    .fa-card {
      width: calc(654 * var(--a)); max-width: 94cqw; margin: calc(38 * var(--a)) auto 0;
      background: rgba(10,13,26,.55);
      border: 1px solid rgba(167,139,250,.28); border-radius: calc(16 * var(--a));
      padding: calc(30 * var(--a)) calc(47 * var(--a)) calc(34 * var(--a));
      text-align: left;
      box-shadow: 0 calc(20 * var(--a)) calc(60 * var(--a)) rgba(2,4,10,.5);
    }
    .fa-wide .fa-card { width: calc(1006 * var(--a)); }

    /* Avatar dans la carte (connexion). */
    .fa-card .fa-badge { margin: calc(6 * var(--a)) auto calc(24 * var(--a)); }

    /* ── Partage formulaire / colonne laterale (inscription) ── */
    .fa-split { display: grid; grid-template-columns: 1.85fr 1fr; }
    .fa-col-form { padding: calc(30 * var(--a)) calc(34 * var(--a)) calc(34 * var(--a)); }
    .fa-col-side {
      border-left: 1px solid rgba(255,255,255,.08);
      padding: calc(30 * var(--a)) calc(28 * var(--a));
    }
    .fa-card-head { display: flex; align-items: center; gap: calc(14 * var(--a)); margin-bottom: calc(22 * var(--a)); }
    .fa-card-ic {
      width: calc(52 * var(--a)); height: calc(52 * var(--a)); border-radius: 50%; flex: none;
      border: calc(2 * var(--a)) solid rgba(167,139,250,.7);
      display: grid; place-items: center;
    }
    .fa-card-ic svg { width: calc(24 * var(--a)); height: calc(24 * var(--a)); }
    .fa-card-head h2 { margin: 0; font-size: calc(19 * var(--a)); font-weight: 800; color: #fff; }
    .fa-card-head p { margin: calc(4 * var(--a)) 0 0; font-size: calc(12.5 * var(--a)); color: #9AA7BD; }

    /* ── Champs : 46 px de haut ── */
    .fa-card label {
      display: block; font-size: calc(14 * var(--a)); font-weight: 700; color: #fff;
      margin: calc(18 * var(--a)) 0 calc(9 * var(--a));
    }
    .fa-card .req { color: #F87171; }
    .fa-field { position: relative; }
    .fa-field .pre {
      position: absolute; left: calc(14 * var(--a)); top: 50%; transform: translateY(-50%);
      width: calc(18 * var(--a)); height: calc(18 * var(--a)); pointer-events: none;
    }
    .fa-field input, .fa-field select {
      width: 100%; height: calc(46 * var(--a));
      background: rgba(255,255,255,.03); border: 1px solid rgba(255,255,255,.14);
      border-radius: calc(10 * var(--a)); color: #E7ECF5;
      font: inherit; font-size: calc(14 * var(--a));
      padding: 0 calc(40 * var(--a)) 0 calc(42 * var(--a));
      appearance: none; outline: none;
    }
    .fa-field select { color: #9AA7BD; }
    .fa-field select option { background: #0B1020; color: #E7ECF5; }
    .fa-field input::placeholder { color: #6B7A94; }
    .fa-field input:focus, .fa-field select:focus { border-color: rgba(167,139,250,.65); }
    .fa-chev {
      position: absolute; right: calc(16 * var(--a)); top: 50%;
      width: calc(8 * var(--a)); height: calc(8 * var(--a));
      border-right: 1.5px solid #9AA7BD; border-bottom: 1.5px solid #9AA7BD;
      transform: translateY(-70%) rotate(45deg); pointer-events: none;
    }
    .fa-eye {
      position: absolute; right: calc(6 * var(--a)); top: 50%; transform: translateY(-50%);
      background: none; border: 0; cursor: pointer; color: #6B7A94;
      padding: calc(8 * var(--a));
    }
    .fa-eye svg { width: calc(19 * var(--a)); height: calc(19 * var(--a)); display: block; }
    .fa-eye:hover { color: #C7D2E4; }

    /* Prefixe telephonique (inscription). */
    .fa-dial { display: flex; gap: calc(10 * var(--a)); }
    .fa-dial .cc { position: relative; flex: none; width: calc(118 * var(--a)); }
    .fa-dial .cc select { padding: 0 calc(28 * var(--a)) 0 calc(12 * var(--a)); color: #E7ECF5; }
    .fa-dial .fa-field { flex: 1; }

    /* ── Ligne « se souvenir / oublie » ── */
    .fa-row {
      display: flex; align-items: center; justify-content: space-between;
      margin-top: calc(18 * var(--a)); font-size: calc(14 * var(--a));
    }
    .fa-check { display: flex; align-items: center; gap: calc(9 * var(--a)); color: #E7ECF5; cursor: pointer; }
    .fa-check input {
      width: calc(16 * var(--a)); height: calc(16 * var(--a)); margin: 0;
      accent-color: #7C3AED; cursor: pointer;
    }
    .fa-link { color: #A78BFA; text-decoration: underline; text-underline-offset: 3px; }
    .fa-link:hover { color: #C4B5FD; }

    /* ── Boutons ── */
    .fa-btn {
      display: flex; align-items: center; justify-content: center; gap: calc(10 * var(--a));
      width: 100%; min-height: calc(44 * var(--a)); margin-top: calc(24 * var(--a));
      border: 0; border-radius: calc(10 * var(--a)); cursor: pointer;
      font: inherit; font-size: calc(15 * var(--a)); font-weight: 700;
    }
    .fa-btn svg { width: calc(18 * var(--a)); height: calc(18 * var(--a)); }
    .fa-btn.grad { background: linear-gradient(120deg,#3B3BF0,#7C3AED 55%,#8B5CF6); color: #fff; }
    .fa-btn.grad:disabled { opacity: .55; cursor: default; }
    .fa-btn.line {
      background: transparent; border: 1px solid rgba(167,139,250,.55); color: #C4B5FD;
      min-height: calc(47 * var(--a));
    }
    .fa-btn.line:hover { border-color: rgba(196,181,253,.8); }
    .fa-or {
      display: flex; align-items: center; gap: calc(14 * var(--a));
      margin-top: calc(22 * var(--a)); color: #6B7A94; font-size: calc(13 * var(--a));
    }
    .fa-or::before, .fa-or::after { content: ''; flex: 1; height: 1px; background: rgba(255,255,255,.1); }
    .fa-foot-note {
      margin: calc(26 * var(--a)) 0 0; text-align: center;
      font-size: calc(14 * var(--a)); color: #C7D2E4;
    }

    /* ── Etats ── */
    .fa-error {
      margin-top: calc(18 * var(--a)); padding: calc(11 * var(--a)) calc(14 * var(--a));
      background: rgba(244,63,94,.08); border: 1px solid rgba(244,63,94,.4);
      border-radius: calc(9 * var(--a)); color: #FDA4AF; font-size: calc(13.5 * var(--a));
    }
    .fa-ok {
      margin: 0 0 calc(14 * var(--a)); padding: calc(12 * var(--a)) calc(14 * var(--a));
      background: rgba(52,211,153,.08); border: 1px solid rgba(52,211,153,.4);
      border-radius: calc(9 * var(--a)); color: #6EE7B7;
      font-size: calc(14 * var(--a)); font-weight: 700;
    }
    .fa-hint { margin: calc(6 * var(--a)) 0 0; font-size: calc(12.5 * var(--a)); color: #9AA7BD; }

    /* ── Regles de mot de passe : grille deux colonnes cochable ── */
    .fa-rules {
      list-style: none; margin: calc(14 * var(--a)) 0 0; padding: 0;
      display: grid; grid-template-columns: 1fr 1fr; gap: calc(9 * var(--a)) calc(18 * var(--a));
    }
    .fa-rules li {
      display: flex; align-items: center; gap: calc(8 * var(--a));
      font-size: calc(12.5 * var(--a)); color: #9AA7BD;
    }
    .fa-rules li svg {
      width: calc(15 * var(--a)); height: calc(15 * var(--a)); flex: none;
      border: 1px solid rgba(255,255,255,.25); border-radius: 50%; padding: 2px;
    }
    .fa-rules li.ok { color: #6EE7B7; }
    .fa-rules li.ok svg { border-color: rgba(52,211,153,.6); }

    .fa-rules.three { grid-template-columns: 1fr 1fr 1fr; }
    .fa-must { margin: calc(18 * var(--a)) 0 0; font-size: calc(14 * var(--a)); font-weight: 700; color: #fff; }

    /* Deux champs cote a cote. */
    .fa-grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 0 calc(18 * var(--a)); }

    /* ── Colonne laterale « Pourquoi creer un compte ? » ── */
    .fa-side h3 { margin: calc(16 * var(--a)) 0 calc(18 * var(--a)); font-size: calc(17 * var(--a)); font-weight: 800; color: #fff; text-align: center; }
    .fa-side-art { display: flex; justify-content: center; }
    .fa-side-art svg { width: calc(150 * var(--a)); height: calc(150 * var(--a)); }
    .fa-benefit { display: flex; gap: calc(12 * var(--a)); margin-bottom: calc(16 * var(--a)); text-align: left; }
    .fa-benefit-ic {
      width: calc(38 * var(--a)); height: calc(38 * var(--a)); border-radius: calc(10 * var(--a)); flex: none;
      background: rgba(139,92,246,.12); border: 1px solid rgba(167,139,250,.3);
      display: grid; place-items: center;
    }
    .fa-benefit-ic svg { width: calc(19 * var(--a)); height: calc(19 * var(--a)); }
    .fa-benefit h4 { margin: 0; font-size: calc(13.5 * var(--a)); font-weight: 800; color: #fff; }
    .fa-benefit p { margin: calc(3 * var(--a)) 0 0; font-size: calc(12 * var(--a)); line-height: calc(17 * var(--a)); color: #9AA7BD; }

    @media (max-width: 820px) {
      .fa-card { padding: calc(24 * var(--a)) calc(22 * var(--a)); }
      .fa-wide .fa-card { width: calc(654 * var(--a)); }
      .fa-split { grid-template-columns: 1fr; }
      .fa-col-side { border-left: 0; border-top: 1px solid rgba(255,255,255,.08); }
      .fa-grid2 { grid-template-columns: 1fr; }
      .fa-rules { grid-template-columns: 1fr; }
    }
  `]
})
export class FranceAuthComponent {
  /** Carte large (1006 px de maquette) pour l'inscription. */
  @Input() wide = false;
  /** Mini-arguments sous le logo du pied (capture « compte pret »). */
  @Input() perks = false;
}
