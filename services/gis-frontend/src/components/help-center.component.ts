import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { AppLayoutComponent } from './shared/app-layout.component';
import { HelpService } from '../services/help.service';
import { HelpArticle } from '../services/help-content.model';

/**
 * Ecran "Aide" (/aide) : recherche par mot-cle dans les articles, filtree sur
 * les modules souscrits par le client.
 *
 * Le texte n'est pas ici mais dans services/help-content.ts : un seul endroit
 * a corriger quand un ecran change.
 */
@Component({
  selector: 'app-help-center',
  standalone: true,
  imports: [CommonModule, FormsModule, AppLayoutComponent],
  template: `
    <app-layout>
    <div class="aide">
      <header class="aide-entete">
        <div>
          <h1>Centre d'aide</h1>
          <p>Cherchez un mot : « plein », « rapport », « kilométrage », « zone »…</p>
        </div>
        <div class="aide-boutons">
          <!-- GPA : plus de visite guidee ; le bouton rejoue les premiers pas
               (Vehicules, Chauffeurs, Entretien programmable), pour un nouvel
               utilisateur qui les a (Karim, 25/09/2026). -->
          @if (!offreGpa) {
            <button type="button" class="btn-visite" (click)="rejouerLaVisite()">
              Revoir la visite guidée
            </button>
          } @else if (aDesPremiersPas) {
            <button type="button" class="btn-visite" (click)="rejouerLaVisite()">
              Revoir les premiers pas
            </button>
          }
          <!-- Seulement s'il existe un guide d'ecran pour ce client (nouvel
               utilisateur, bonne offre) : ailleurs le bouton ne ferait rien. -->
          @if (ecransAvecGuide.length) {
            <button type="button" class="btn-visite" (click)="revoirLesGuidesDesEcrans()">
              {{ guidesRemis
                   ? (ecransAvecGuide.length > 1 ? "C'est fait : ouvrez les écrans " : "C'est fait : ouvrez l'écran ") + ecransAvecGuide.join(', ')
                   : 'Revoir les guides des écrans' }}
            </button>
          }
        </div>
      </header>

      <div class="aide-recherche">
        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>
        </svg>
        <input type="search" [ngModel]="question()" (ngModelChange)="question.set($event)"
               placeholder="Rechercher dans l'aide" aria-label="Rechercher dans l'aide" autofocus>
        @if (question()) {
          <button type="button" class="effacer" (click)="question.set('')" aria-label="Effacer">×</button>
        }
      </div>

      <div class="aide-corps">
        <!-- Liste des resultats -->
        <nav class="aide-liste">
          @if (resultats().length === 0) {
            <p class="aide-vide">
              Aucun article ne correspond à « {{ question() }} ».<br>
              Essayez un mot plus simple, ou contactez votre administrateur.
            </p>
          }
          @for (article of resultats(); track article.id) {
            <button type="button" class="aide-item" [class.actif]="article.id === articleActif()?.id"
                    (click)="ouvrir(article)">
              <span class="item-titre">{{ article.titre }}</span>
              <span class="item-resume">{{ article.resume }}</span>
            </button>
          }
        </nav>

        <!-- Article -->
        <article class="aide-article">
          @if (articleActif(); as a) {
            <h2>{{ a.titre }}</h2>
            <p class="article-resume">{{ a.resume }}</p>

            @if (help.aUneVideo(a)) {
              <video class="article-video" [src]="a.video!.url" controls preload="none"></video>
            } @else if (a.video) {
              <p class="article-video-attente">
                🎬 Une courte vidéo « {{ a.video.titre }} » sera ajoutée ici prochainement.
              </p>
            }

            @if (a.etapes?.length) {
              <h3>Marche à suivre</h3>
              <ol class="article-etapes">
                @for (etape of a.etapes; track etape) { <li>{{ etape }}</li> }
              </ol>
            }

            <!-- Captures d'ecran. Une image manquante se masque d'elle-meme
                 (onerror) : l'article reste lisible pendant qu'on les produit. -->
            @for (capture of a.captures || []; track capture.fichier) {
              <figure class="article-capture">
                <img [src]="'assets/aide/' + capture.fichier" [alt]="capture.legende"
                     loading="lazy" (error)="masquerImage($event)">
                <figcaption>{{ capture.legende }}</figcaption>
              </figure>
            }

            @for (paragraphe of a.paragraphes || []; track paragraphe) {
              <p class="article-paragraphe">{{ paragraphe }}</p>
            }

            @if (a.aRetenir) {
              <aside class="article-retenir">
                <strong>À retenir</strong>
                <span>{{ a.aRetenir }}</span>
              </aside>
            }
          } @else {
            <div class="article-accueil">
              <h2>Que cherchez-vous ?</h2>
              <p>Choisissez un article à gauche, ou tapez un mot dans la recherche.</p>
            </div>
          }
        </article>
      </div>
    </div>
    </app-layout>
  `,
  styles: [`
    .aide { padding: 20px 24px 40px; max-width: 1180px; margin: 0 auto; }

    .aide-entete { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; flex-wrap: wrap; }
    .aide-entete h1 { margin: 0 0 4px; font-size: 22px; font-weight: 650; }
    .aide-entete p { margin: 0; color: #64748b; font-size: 13.5px; }
    .btn-visite {
      font: inherit; font-size: 13px; padding: 8px 14px; border-radius: 8px;
      border: 1px solid #cbd5e1; background: transparent; color: #475569; cursor: pointer;
    }
    .btn-visite:hover { border-color: #2563eb; color: #2563eb; }
    .aide-boutons { display: flex; gap: 8px; flex-wrap: wrap; justify-content: flex-end; }

    .aide-recherche {
      display: flex; align-items: center; gap: 10px; margin: 18px 0 20px;
      border: 1px solid #e2e8f0; border-radius: 10px; padding: 0 12px; background: #fff;
      color: #94a3b8;
    }
    .aide-recherche:focus-within { border-color: #2563eb; box-shadow: 0 0 0 3px rgba(37,99,235,.12); }
    .aide-recherche input {
      flex: 1; border: 0; outline: 0; background: transparent; font: inherit;
      font-size: 14.5px; padding: 12px 0; color: inherit;
    }
    .aide-recherche .effacer { border: 0; background: none; font-size: 20px; cursor: pointer; color: #94a3b8; line-height: 1; }
    :host-context([data-theme="dark"]) .aide-recherche { background: #1e293b; border-color: #334155; }

    .aide-corps { display: grid; grid-template-columns: 320px 1fr; gap: 20px; align-items: start; }

    .aide-liste { display: flex; flex-direction: column; gap: 6px; max-height: 70vh; overflow-y: auto; }
    .aide-item {
      text-align: left; display: flex; flex-direction: column; gap: 3px; cursor: pointer;
      border: 1px solid transparent; border-radius: 9px; padding: 11px 13px; background: transparent; font: inherit;
    }
    .aide-item:hover { background: #f1f5f9; }
    :host-context([data-theme="dark"]) .aide-item:hover { background: #1e293b; }
    .aide-item.actif { background: #eff6ff; border-color: #bfdbfe; }
    :host-context([data-theme="dark"]) .aide-item.actif { background: #1e3a5f; border-color: #1d4ed8; }
    .item-titre { font-size: 14px; font-weight: 600; }
    .item-resume { font-size: 12.5px; color: #64748b; line-height: 1.45; }
    .aide-vide { color: #64748b; font-size: 13.5px; line-height: 1.6; padding: 12px 4px; }

    .aide-article {
      background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; padding: 24px 26px; min-height: 320px;
    }
    :host-context([data-theme="dark"]) .aide-article { background: #1e293b; border-color: #334155; }
    .aide-article h2 { margin: 0 0 6px; font-size: 19px; font-weight: 650; }
    .aide-article h3 { margin: 22px 0 8px; font-size: 14px; text-transform: uppercase; letter-spacing: .05em; color: #2563eb; }
    .article-resume { margin: 0 0 18px; color: #64748b; font-size: 14px; }
    .article-etapes { margin: 0; padding-left: 20px; display: flex; flex-direction: column; gap: 9px; }
    .article-etapes li { font-size: 14px; line-height: 1.6; }
    .article-paragraphe { font-size: 14px; line-height: 1.65; margin: 14px 0 0; }

    .article-capture { margin: 18px 0 0; }
    .article-capture img {
      width: 100%; display: block; border: 1px solid #e2e8f0; border-radius: 10px;
      background: #f8fafc;
    }
    :host-context([data-theme="dark"]) .article-capture img { border-color: #334155; background: #0f172a; }
    .article-capture figcaption { margin-top: 7px; font-size: 12.5px; color: #64748b; line-height: 1.5; }

    .article-video { width: 100%; border-radius: 10px; margin: 8px 0 4px; background: #0f172a; }
    .article-video-attente {
      margin: 8px 0 4px; padding: 12px 14px; border: 1px dashed #cbd5e1; border-radius: 10px;
      color: #64748b; font-size: 13px;
    }

    .article-retenir {
      display: flex; flex-direction: column; gap: 4px; margin-top: 22px;
      background: #fffbeb; border-left: 3px solid #f59e0b; border-radius: 0 8px 8px 0; padding: 12px 15px;
    }
    :host-context([data-theme="dark"]) .article-retenir { background: #2d2410; }
    .article-retenir strong { font-size: 12px; text-transform: uppercase; letter-spacing: .05em; color: #b45309; }
    .article-retenir span { font-size: 13.5px; line-height: 1.6; }

    .article-accueil { color: #64748b; }
    .article-accueil h2 { color: inherit; }

    @media (max-width: 900px) {
      .aide-corps { grid-template-columns: 1fr; }
      .aide-liste { max-height: none; }
    }
  `]
})
export class HelpCenterComponent implements OnInit {
  help = inject(HelpService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  question = signal('');
  articleActif = signal<HelpArticle | null>(null);

  resultats = computed(() => this.help.rechercher(this.question()));

  ngOnInit(): void {
    // /aide?article=ajouter-vehicule permet de pointer un article precis,
    // depuis un lien d'e-mail ou un bouton contextuel d'un ecran.
    const demande = this.route.snapshot.queryParamMap.get('article');
    if (demande) {
      const article = this.help.articleParId(demande);
      if (article) { this.articleActif.set(article); }
    }
  }

  ouvrir(article: HelpArticle): void {
    this.articleActif.set(article);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { article: article.id },
      replaceUrl: true
    });
  }

  rejouerLaVisite(): void {
    this.help.reinitialiserGuide();
  }

  /**
   * Ecrans qui ont un guide pour ce client, lus une fois. Pendant le pilote :
   * « Vehicules » en GPA, rien en GPS — le bouton n'apparait alors pas.
   */
  ecransAvecGuide = this.help.ecransAvecGuide();
  offreGpa = this.help.offreGpa();
  aDesPremiersPas = this.help.premiersPasDuClient().length > 0;
  guidesRemis = false;

  /** Chaque guide d'ecran reviendra a la prochaine ouverture de son ecran. */
  revoirLesGuidesDesEcrans(): void {
    this.help.reinitialiserEcrans();
    this.guidesRemis = true;
  }

  /** Capture pas encore produite : on retire l'image plutot qu'afficher un cadre casse. */
  masquerImage(evenement: Event): void {
    const figure = (evenement.target as HTMLElement)?.closest('figure');
    if (figure) { figure.remove(); }
  }
}
