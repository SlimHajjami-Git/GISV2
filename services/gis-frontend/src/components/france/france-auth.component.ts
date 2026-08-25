import { Component, Input, ViewEncapsulation } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FranceHeaderComponent } from './france-header.component';

/**
 * Habillage commun aux écrans d'authentification du parcours européen :
 * connexion, création de compte, mot de passe oublié, réinitialisation.
 *
 * <p>Les quatre écrans validés partagent exactement la même mise en page —
 * arcs décoratifs, pastille ronde, titre à dégradé partiel, carte sombre,
 * bouton dégradé, séparateur « ou », bouton secondaire. Le construire une fois
 * évite que les quatre divergent à la première retouche.</p>
 *
 * <p><b>Préfixe .fr-auth, et pas .fr.</b> Comme l'en-tête : la feuille est
 * servie sans encapsulation pour habiller un contenu projeté, et doit rester
 * incapable de repeindre autre chose. Ces écrans vivent hors du site France —
 * la feuille de la coque n'y est pas chargée, d'où cette feuille autonome.</p>
 */
@Component({
  selector: 'app-france-auth',
  standalone: true,
  imports: [RouterLink, FranceHeaderComponent],
  encapsulation: ViewEncapsulation.None,
  template: `
    <div class="fr-auth">
      <app-france-header></app-france-header>

      <main class="fa-main">
        <div class="fa-arcs" aria-hidden="true">
          <span class="fa-arc left"></span>
          <span class="fa-arc right"></span>
        </div>
        <div class="fa-inner" [class.wide]="wide">
          <ng-content></ng-content>
        </div>
      </main>

      <footer class="fa-foot">
        <div class="fa-foot-grid">
          <div class="fa-foot-brand">
            <img src="/assets/calypso-logo.svg" alt="Calypso">
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
            <h4>Calypso</h4>
            <ul>
              <li><a routerLink="/fr/contact">Contact</a></li>
              <li><a routerLink="/login">Se connecter</a></li>
              <li><a routerLink="/inscription">Essayer gratuitement</a></li>
            </ul>
          </div>
          <div>
            <h4>Légal</h4>
            <ul>
              <li><a routerLink="/fr/confidentialite">Confidentialité</a></li>
              <li><a routerLink="/fr/mentions-legales">Mentions légales</a></li>
              <li><a routerLink="/fr/rgpd">RGPD</a></li>
              <li><a routerLink="/fr/cookies">Cookies</a></li>
            </ul>
          </div>
          <!-- Le bloc « Données sécurisées » de la maquette est conservé : c'est
               une assertion vérifiable et non une promesse de conformité, que le
               document maître interdirait. -->
          <div class="fa-secure">
            <span class="fa-secure-ic">
              <svg viewBox="0 0 24 24" fill="none" stroke="#A78BFA" stroke-width="1.7" aria-hidden="true"><path d="M12 3l7.5 3v6c0 4.4-3.1 7.6-7.5 9-4.4-1.4-7.5-4.6-7.5-9V6z"/><path d="m9 12 2 2 4-4.5"/></svg>
            </span>
            <div>
              <h5>Données sécurisées</h5>
              <p>Vos données sont hébergées en toute sécurité.</p>
            </div>
          </div>
        </div>
        <div class="fa-foot-bottom">© {{ year }} Calypso. Tous droits réservés.</div>
      </footer>
    </div>
  `,
  styles: [`
    .fr-auth {
      --fa-bg:     #05070F;
      --fa-bg-2:   #080D1B;
      --fa-surface:rgba(255,255,255,.035);
      --fa-line:   rgba(255,255,255,.09);
      --fa-line-2: rgba(255,255,255,.16);
      --fa-txt:    #FFFFFF;
      --fa-soft:   #9AA7BD;
      --fa-faint:  #6B7A94;
      --fa-violet: #8B5CF6;
      --fa-violet2:#A78BFA;
      --fa-blue:   #60A5FA;
      --fa-grad:   linear-gradient(135deg, #4F46E5 0%, #7C3AED 100%);
      --fa-grad-t: linear-gradient(90deg, #7C7CF8 0%, #A78BFA 100%);
      background: var(--fa-bg); color: var(--fa-txt);
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
      min-height: 100vh; display: flex; flex-direction: column;
    }
    .fr-auth *, .fr-auth *::before, .fr-auth *::after { box-sizing: border-box; }

    .fr-auth .fa-main { position: relative; flex: 1; padding: 62px 24px 88px; overflow: hidden; }
    .fr-auth .fa-inner { position: relative; z-index: 1; max-width: 700px; margin: 0 auto; }

    /* Arcs decoratifs des maquettes : deux halos circulaires en bordure. */
    .fr-auth .fa-arcs { position: absolute; inset: 0; pointer-events: none; }
    .fr-auth .fa-arc {
      position: absolute; top: 50%; width: 620px; height: 620px; margin-top: -310px;
      border-radius: 50%; border: 1px dashed rgba(139,92,246,.18);
      background: radial-gradient(circle at 50% 50%, rgba(59,130,246,.10), transparent 62%);
    }
    .fr-auth .fa-arc.left { left: -330px; }
    .fr-auth .fa-arc.right { right: -330px; border-color: rgba(96,165,250,.16); }
    @media (max-width: 900px) { .fr-auth .fa-arc { display: none; } }

    /* ---------- en-tete d ecran ---------- */
    .fr-auth .fa-badge {
      width: 78px; height: 78px; border-radius: 50%; margin: 0 auto 22px;
      display: grid; place-items: center;
      border: 1.5px solid rgba(167,139,250,.55);
      background: rgba(124,58,237,.10);
      box-shadow: 0 0 40px rgba(124,58,237,.30);
    }
    .fr-auth .fa-badge svg { width: 34px; height: 34px; }
    .fr-auth .fa-title {
      font-size: clamp(30px, 4.4vw, 46px); font-weight: 800; letter-spacing: -.035em;
      line-height: 1.1; margin: 0 0 14px; text-align: center; color: #fff;
    }
    .fr-auth .fa-title em {
      font-style: normal;
      background: var(--fa-grad-t); -webkit-background-clip: text; background-clip: text; color: transparent;
    }
    .fr-auth .fa-sub {
      text-align: center; color: var(--fa-soft); font-size: 16px;
      margin: 0 auto 34px; max-width: 56ch; line-height: 1.6;
    }

    /* ---------- carte ---------- */
    .fr-auth .fa-card {
      background: var(--fa-surface); border: 1px solid var(--fa-line);
      border-radius: 18px; padding: 34px 36px;
    }
    .fr-auth .fa-card-head { display: flex; gap: 15px; align-items: flex-start; margin-bottom: 24px; }
    .fr-auth .fa-card-ic {
      width: 52px; height: 52px; border-radius: 50%; flex: none; display: grid; place-items: center;
      border: 1.5px solid rgba(167,139,250,.45); background: rgba(124,58,237,.10);
    }
    .fr-auth .fa-card-ic svg { width: 23px; height: 23px; }
    .fr-auth .fa-card-head h2 { font-size: 17px; font-weight: 700; margin: 0 0 4px; color: #fff; }
    .fr-auth .fa-card-head p { margin: 0; font-size: 13.5px; color: var(--fa-soft); }

    /* ---------- champs ---------- */
    .fr-auth label {
      display: block; font-size: 13.5px; font-weight: 700; color: #fff; margin: 0 0 8px;
    }
    .fr-auth label .req, .fr-auth .req { color: #F87171; margin-left: 3px; font-weight: 700; }
    .fr-auth .fa-field { position: relative; margin-bottom: 20px; }
    .fr-auth .fa-field > svg.pre {
      position: absolute; left: 15px; top: 50%; margin-top: -9px;
      width: 18px; height: 18px; pointer-events: none;
    }
    .fr-auth input, .fr-auth select, .fr-auth textarea {
      width: 100%; padding: 14px 16px 14px 46px; font-family: inherit; font-size: 15px;
      color: #fff; background: rgba(0,0,0,.34);
      border: 1px solid var(--fa-line-2); border-radius: 11px;
      transition: border-color .16s ease;
    }
    .fr-auth input.plain { padding-left: 16px; }
    .fr-auth input::placeholder, .fr-auth textarea::placeholder { color: var(--fa-faint); }
    .fr-auth input:focus, .fr-auth select:focus, .fr-auth textarea:focus {
      outline: none; border-color: var(--fa-violet);
    }
    .fr-auth select { appearance: none; -webkit-appearance: none; padding-right: 42px; cursor: pointer; }
    .fr-auth select option { background: #0C1225; color: #fff; }
    .fr-auth .fa-chev {
      position: absolute; right: 17px; top: 50%; margin-top: -5px;
      width: 8px; height: 8px; border-right: 2px solid var(--fa-soft);
      border-bottom: 2px solid var(--fa-soft); transform: rotate(45deg); pointer-events: none;
    }
    .fr-auth .fa-eye {
      position: absolute; right: 12px; top: 50%; margin-top: -16px;
      width: 32px; height: 32px; border: 0; background: none; cursor: pointer;
      color: var(--fa-soft); display: grid; place-items: center; padding: 0;
    }
    .fr-auth .fa-eye svg { width: 19px; height: 19px; }
    .fr-auth .fa-hint { display: block; margin: -12px 0 20px; font-size: 12.5px; color: var(--fa-faint); }

    /* ---------- boutons ---------- */
    .fr-auth .fa-btn {
      display: flex; align-items: center; justify-content: center; gap: 10px;
      width: 100%; padding: 15px 24px; border-radius: 11px; border: 1.5px solid transparent;
      font-family: inherit; font-size: 15px; font-weight: 700; cursor: pointer;
      text-decoration: none; transition: transform .16s ease, box-shadow .16s ease, border-color .16s ease;
    }
    .fr-auth .fa-btn svg { width: 18px; height: 18px; flex: none; }
    .fr-auth .fa-btn.grad { background: var(--fa-grad); color: #fff; box-shadow: 0 10px 28px rgba(99,72,235,.35); }
    .fr-auth .fa-btn.grad:hover:not(:disabled) { transform: translateY(-1px); box-shadow: 0 14px 34px rgba(99,72,235,.48); }
    .fr-auth .fa-btn.line { background: transparent; color: var(--fa-violet2); border-color: var(--fa-line-2); }
    .fr-auth .fa-btn.line:hover { border-color: rgba(167,139,250,.5); }
    .fr-auth .fa-btn:disabled { opacity: .5; cursor: not-allowed; }

    /* ---------- separateur ---------- */
    .fr-auth .fa-or {
      display: flex; align-items: center; gap: 16px; margin: 20px 0;
      color: var(--fa-faint); font-size: 13px;
    }
    .fr-auth .fa-or::before, .fr-auth .fa-or::after {
      content: ""; flex: 1; height: 1px; background: var(--fa-line);
    }

    .fr-auth .fa-row { display: flex; align-items: center; justify-content: space-between; gap: 14px; margin-bottom: 22px; }
    .fr-auth .fa-check { display: inline-flex; align-items: center; gap: 9px; font-size: 14px; color: var(--fa-soft); cursor: pointer; }
    .fr-auth .fa-check input { width: 16px; height: 16px; padding: 0; accent-color: var(--fa-violet); cursor: pointer; }
    .fr-auth .fa-link { color: var(--fa-violet2); font-size: 14px; text-decoration: underline; }
    .fr-auth .fa-link:hover { color: #fff; }
    .fr-auth .fa-foot-note { margin: 18px 0 0; text-align: center; font-size: 14px; color: var(--fa-soft); }
    .fr-auth .fa-error {
      background: rgba(239,68,68,.10); border: 1px solid rgba(248,113,113,.35);
      color: #FCA5A5; border-radius: 11px; padding: 12px 15px; font-size: 14px; margin-bottom: 18px;
    }
    .fr-auth .fa-ok {
      background: rgba(52,211,153,.10); border: 1px solid rgba(52,211,153,.35);
      color: #6EE7B7; border-radius: 11px; padding: 12px 15px; font-size: 14px; margin-bottom: 18px;
    }
    .fr-auth a:focus-visible, .fr-auth button:focus-visible, .fr-auth input:focus-visible {
      outline: 2px solid var(--fa-violet2); outline-offset: 3px;
    }

    /* ---------- carte large a deux colonnes (creation de compte) ------- */
    /* Le conteneur passe a 1160px : la carte de la capture porte le
       formulaire ET le panneau des benefices cote a cote. */
    .fr-auth .fa-inner.wide { max-width: 1160px; }
    .fr-auth .fa-split { display: grid; grid-template-columns: 1.45fr 1fr; }
    .fr-auth .fa-split > .fa-col-form { padding: 34px 38px; }
    .fr-auth .fa-split > .fa-col-side {
      padding: 34px 38px; border-left: 1px solid var(--fa-line);
      background: rgba(255,255,255,.02);
    }
    @media (max-width: 940px) {
      .fr-auth .fa-split { grid-template-columns: 1fr; }
      .fr-auth .fa-split > .fa-col-side { border-left: 0; border-top: 1px solid var(--fa-line); }
    }
    .fr-auth .fa-grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 0 20px; }
    @media (max-width: 620px) { .fr-auth .fa-grid2 { grid-template-columns: 1fr; } }

    /* Panneau lateral : illustration puis quatre benefices separes de filets. */
    .fr-auth .fa-side-art { display: grid; place-items: center; margin-bottom: 26px; }
    .fr-auth .fa-side-art svg { width: 132px; height: 132px; }
    .fr-auth .fa-side h3 { font-size: 19px; font-weight: 800; margin: 0 0 22px; text-align: center; color: #fff; }
    .fr-auth .fa-benefit { display: flex; gap: 14px; align-items: flex-start; padding: 18px 0; }
    .fr-auth .fa-benefit + .fa-benefit { border-top: 1px solid var(--fa-line); }
    .fr-auth .fa-benefit-ic {
      width: 42px; height: 42px; border-radius: 11px; flex: none; display: grid; place-items: center;
      background: rgba(124,58,237,.14);
    }
    .fr-auth .fa-benefit-ic svg { width: 20px; height: 20px; }
    .fr-auth .fa-benefit h4 { font-size: 14.5px; font-weight: 700; margin: 0 0 5px; color: #fff; }
    .fr-auth .fa-benefit p { margin: 0; font-size: 13.5px; color: var(--fa-soft); line-height: 1.55; }

    /* Regles de mot de passe : elles passent au vert PENDANT la saisie. */
    .fr-auth .fa-rules { list-style: none; margin: -8px 0 20px; padding: 0; display: grid; grid-template-columns: 1fr 1fr; gap: 9px 20px; }
    .fr-auth .fa-rules li { display: flex; align-items: center; gap: 8px; font-size: 12.5px; color: var(--fa-faint); transition: color .16s ease; }
    .fr-auth .fa-rules svg { width: 14px; height: 14px; flex: none; }
    .fr-auth .fa-rules li.ok { color: #6EE7B7; }
    @media (max-width: 620px) { .fr-auth .fa-rules { grid-template-columns: 1fr; } }

    /* ---------- pied de page ---------- */
    .fr-auth .fa-foot { background: var(--fa-bg-2); border-top: 1px solid var(--fa-line); padding: 52px 40px 28px; }
    .fr-auth .fa-foot-grid {
      max-width: 1320px; margin: 0 auto 34px;
      display: grid; grid-template-columns: 1.5fr 1fr 1fr 1fr 1.4fr; gap: 34px;
    }
    .fr-auth .fa-foot-brand img { height: 30px; width: auto; display: block; }
    .fr-auth .fa-foot h4 {
      color: #fff; font-size: 12.5px; font-weight: 800; letter-spacing: .12em;
      text-transform: uppercase; margin: 0 0 14px;
    }
    .fr-auth .fa-foot ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 10px; }
    .fr-auth .fa-foot a { color: var(--fa-soft); text-decoration: none; font-size: 14.5px; }
    .fr-auth .fa-foot a:hover { color: #fff; }
    .fr-auth .fa-secure {
      display: flex; gap: 15px; align-items: flex-start;
      background: var(--fa-surface); border: 1px solid var(--fa-line);
      border-radius: 14px; padding: 20px 22px;
    }
    .fr-auth .fa-secure-ic {
      width: 46px; height: 46px; border-radius: 50%; flex: none; display: grid; place-items: center;
      background: rgba(124,58,237,.14);
    }
    .fr-auth .fa-secure-ic svg { width: 22px; height: 22px; }
    .fr-auth .fa-secure h5 { font-size: 14.5px; font-weight: 700; margin: 0 0 5px; color: #fff; }
    .fr-auth .fa-secure p { margin: 0; font-size: 13.5px; color: var(--fa-soft); line-height: 1.5; }
    .fr-auth .fa-foot-bottom {
      max-width: 1320px; margin: 0 auto; padding-top: 22px;
      border-top: 1px solid var(--fa-line); font-size: 13.5px; color: var(--fa-faint);
    }
    @media (max-width: 1000px) { .fr-auth .fa-foot-grid { grid-template-columns: 1fr 1fr; } }
    @media (max-width: 640px) {
      .fr-auth .fa-foot { padding: 40px 22px 24px; }
      .fr-auth .fa-card { padding: 26px 22px; }
    }
  `]
})
export class FranceAuthComponent {
  /** Elargit le conteneur pour les ecrans a deux colonnes (creation de compte). */
  @Input() wide = false;

  /** Année du bas de page. Calculée, jamais figée dans le gabarit. */
  readonly year = new Date().getFullYear();
}
