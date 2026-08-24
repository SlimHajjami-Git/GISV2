import { Component, Input, ViewEncapsulation } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * En-tête commercial Calypso, partagé par le site France, l'inscription et
 * l'assistant public.
 *
 * <p><b>Pourquoi un composant et non trois copies.</b> Le même bandeau doit
 * apparaître sur trois écrans qui vivent dans des dossiers différents. Recopié,
 * il aurait divergé dès la première retouche — un lien ajouté ici, un logo
 * changé là — et le défaut ne se verrait que sur la page qu'on ne regarde
 * pas.</p>
 *
 * <p><b>Pourquoi ViewEncapsulation.None.</b> Comme la coque du site France :
 * la feuille doit habiller un balisage projeté dans des pages tierces. Chaque
 * sélecteur est donc préfixé par <code>.fr-hd</code> — et non <code>.fr</code>,
 * pour que poser cet en-tête sur une page claire n'entraîne pas avec lui les
 * règles de fond sombre de la coque, qui repeindraient la page entière.</p>
 */
@Component({
  selector: 'app-france-header',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  encapsulation: ViewEncapsulation.None,
  template: `
    <header class="fr-hd">
      <div class="fr-hd-bar">
        <a class="fr-hd-brand" routerLink="/fr" aria-label="Calypso, accueil">
          <img src="/assets/calypso-logo.svg" alt="Calypso" width="504" height="170">
        </a>
        <nav class="fr-hd-nav">
          <a class="fr-hd-hide" routerLink="/fr/fonctionnalites" routerLinkActive="on">Fonctionnalités</a>
          <a class="fr-hd-hide" routerLink="/fr/tarifs" routerLinkActive="on">Tarifs</a>
          <a class="fr-hd-hide" routerLink="/fr/calypso-auto" routerLinkActive="on">Calypso Auto</a>
          <a class="fr-hd-hide" routerLink="/fr/contact" routerLinkActive="on">Contact</a>
          @if (connected) {
            <span class="fr-hd-chip" [title]="userName">
              <span class="fr-hd-dot"></span>
              <span class="fr-hd-hide">Connecté{{ userName ? " · " + userName : "" }}</span>
            </span>
            <a class="fr-hd-cta" routerLink="/dashboard">Ouvrir Calypso</a>
          } @else {
            <a class="fr-hd-sep" routerLink="/login">Se connecter</a>
            <a class="fr-hd-cta" routerLink="/inscription">Essayer gratuitement</a>
          }
        </nav>
      </div>
    </header>
  `,
  styles: [`
    /* Tout est préfixé .fr-hd : ce bandeau se pose aussi sur des pages claires
       (inscription), où il ne doit rien repeindre d'autre que lui-même. */
    .fr-hd {
      position: sticky; top: 0; z-index: 60;
      background: rgba(5,7,15,.94);
      backdrop-filter: blur(14px);
      border-bottom: 1px solid rgba(255,255,255,.08);
      font-family: Manrope, system-ui, -apple-system, "Segoe UI", sans-serif;
    }
    .fr-hd *, .fr-hd *::before, .fr-hd *::after { box-sizing: border-box; }
    .fr-hd-bar {
      max-width: 1320px; margin: 0 auto; padding: 0 40px;
      display: flex; align-items: center; gap: 34px; height: 78px;
    }
    .fr-hd-brand { display: flex; align-items: center; text-decoration: none; flex-shrink: 0; }
    .fr-hd-brand img { height: 30px; width: auto; display: block; }
    .fr-hd-nav { display: flex; gap: 30px; margin-left: auto; align-items: center; }
    .fr-hd-nav a {
      color: #9AA7BD; text-decoration: none; font-size: 14.5px; font-weight: 600;
      white-space: nowrap; transition: color .18s ease;
    }
    .fr-hd-nav a:hover, .fr-hd-nav a.on, .fr-hd-sep { color: #fff; }
    .fr-hd-cta {
      display: inline-flex; align-items: center; justify-content: center;
      background: linear-gradient(135deg, #4F46E5 0%, #7C3AED 100%);
      color: #fff !important;
      font-size: 12px; font-weight: 700; letter-spacing: .06em; text-transform: uppercase;
      padding: 11px 18px; border-radius: 10px; text-decoration: none;
      box-shadow: 0 8px 26px rgba(99,72,235,.38);
      transition: transform .18s ease, box-shadow .18s ease;
    }
    .fr-hd-cta:hover { transform: translateY(-2px); box-shadow: 0 12px 34px rgba(99,72,235,.5); }
    /* Etat connecte : on ne propose pas « se connecter » a quelqu un qui l est
       deja, et le bouton mene a l application plutot qu a l inscription. */
    .fr-hd-chip {
      display: inline-flex; align-items: center; gap: 8px;
      padding: 7px 13px; border-radius: 99px;
      background: rgba(52,211,153,.12); border: 1px solid rgba(52,211,153,.32);
      color: #6EE7B7; font-size: 13px; font-weight: 600; white-space: nowrap;
    }
    .fr-hd-dot { width: 7px; height: 7px; border-radius: 50%; background: #34D399; flex: none; }
    .fr-hd a:focus-visible { outline: 2px solid #A78BFA; outline-offset: 3px; border-radius: 8px; }
    /* Le menu tient jusqu a 900px. Il disparaissait a 1080, si bien que sur un
       portable le bandeau se reduisait au logo et aux deux boutons — et ne
       ressemblait plus du tout a celui des pages du site. */
    @media (max-width: 1180px) { .fr-hd-nav { gap: 20px; } .fr-hd-bar { gap: 22px; } }
    @media (max-width: 900px) {
      .fr-hd-nav .fr-hd-hide { display: none; }
      .fr-hd-bar { gap: 16px; }
    }
    @media (max-width: 640px) { .fr-hd-bar { padding: 0 22px; height: 66px; } }
  `]
})
export class FranceHeaderComponent {
  /**
   * Vrai quand une session est ouverte. Sans cela, la page de l assistant
   * proposerait « Se connecter » a quelqu un deja connecte et perdrait son
   * acces direct a l application.
   */
  @Input() connected = false;

  /** Nom affiche a cote de la pastille, quand il est connu. */
  @Input() userName = "";
}
