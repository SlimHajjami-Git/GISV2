import { Component, OnInit, OnDestroy, HostListener, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { HelpService } from '../../services/help.service';
import { GuideEtape } from '../../services/help-content.model';

/**
 * Visite guidee de premiere connexion (menu Vehicules -> Ajouter un vehicule
 * -> carte temps reel -> premier rapport).
 *
 * Attention au vocabulaire : dans Calypso, "tournee"/"tour" designe une tournee
 * de livraison (module metier tours). Cette fonctionnalite s'appelle donc
 * "visite guidee" / guided-help, jamais "tour".
 *
 * Le composant ne connait aucun ecran : il vise des elements portant un
 * attribut data-guide="...". Si la cible n'existe pas, l'etape est sautee.
 */
@Component({
  selector: 'app-guided-help',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (actif && etape) {
      <!-- Le voile ne ferme PAS la visite : un clic a cote ne doit pas supprimer
           definitivement un parcours que le client n'a jamais vu. Pour sortir,
           il y a "Passer" et la touche Echap. -->
      <div class="guide-voile"></div>
      <div class="guide-halo" [style.top.px]="halo.top" [style.left.px]="halo.left"
           [style.width.px]="halo.width" [style.height.px]="halo.height"></div>

      <div class="guide-bulle" [style.top.px]="bulle.top" [style.left.px]="bulle.left"
           [class.fleche-haut]="flecheEnHaut" role="dialog" aria-live="polite">
        <div class="guide-compteur">Étape {{ index + 1 }} sur {{ etapes.length }}</div>
        <h3>{{ etape.titre }}</h3>
        <p>{{ etape.texte }}</p>

        <div class="guide-points">
          @for (e of etapes; track e.id; let i = $index) {
            <span class="point" [class.vu]="i <= index"></span>
          }
        </div>

        <div class="guide-actions">
          <button type="button" class="lien" (click)="passer()">Passer</button>
          <span class="espace"></span>
          @if (index > 0) {
            <button type="button" class="secondaire" (click)="precedent()">Précédent</button>
          }
          <button type="button" class="principal" (click)="suivant()">
            {{ index === etapes.length - 1 ? 'Terminer' : 'Suivant' }}
          </button>
        </div>
      </div>
    }
  `,
  styles: [`
    .guide-voile {
      position: fixed; inset: 0; background: rgba(15, 23, 42, 0.55);
      z-index: 10000; animation: guide-apparition .18s ease-out;
    }
    .guide-halo {
      position: fixed; z-index: 10001; pointer-events: none;
      border-radius: 10px; border: 2px solid #2563eb;
      box-shadow: 0 0 0 4px rgba(37, 99, 235, .25);
      transition: top .2s ease, left .2s ease, width .2s ease, height .2s ease;
    }
    .guide-bulle {
      position: fixed; z-index: 10002; width: 330px; max-width: calc(100vw - 32px);
      background: #fff; color: #0f172a; border-radius: 12px; padding: 18px 18px 14px;
      box-shadow: 0 18px 40px rgba(15, 23, 42, .28);
      animation: guide-apparition .18s ease-out;
    }
    :host-context([data-theme="dark"]) .guide-bulle { background: #1e293b; color: #e2e8f0; }
    .guide-bulle h3 { margin: 6px 0 8px; font-size: 16px; font-weight: 650; }
    .guide-bulle p { margin: 0 0 14px; font-size: 13.5px; line-height: 1.55; color: #475569; }
    :host-context([data-theme="dark"]) .guide-bulle p { color: #cbd5e1; }
    .guide-compteur { font-size: 11px; text-transform: uppercase; letter-spacing: .06em; color: #2563eb; font-weight: 600; }

    .guide-points { display: flex; gap: 5px; margin-bottom: 12px; }
    .point { width: 16px; height: 3px; border-radius: 2px; background: #e2e8f0; }
    .point.vu { background: #2563eb; }

    .guide-actions { display: flex; align-items: center; gap: 8px; }
    .guide-actions .espace { flex: 1; }
    .guide-actions button { font: inherit; font-size: 13px; border-radius: 7px; cursor: pointer; padding: 7px 14px; border: 1px solid transparent; }
    .guide-actions .principal { background: #2563eb; color: #fff; font-weight: 600; }
    .guide-actions .principal:hover { background: #1d4ed8; }
    .guide-actions .secondaire { background: transparent; border-color: #cbd5e1; color: #475569; }
    .guide-actions .lien { background: none; color: #94a3b8; padding: 7px 4px; text-decoration: underline; }

    .guide-bulle::before {
      content: ''; position: absolute; left: 28px; border: 8px solid transparent;
      bottom: -16px; border-top-color: #fff;
    }
    .guide-bulle.fleche-haut::before { bottom: auto; top: -16px; border-top-color: transparent; border-bottom-color: #fff; }
    :host-context([data-theme="dark"]) .guide-bulle::before { border-top-color: #1e293b; }
    :host-context([data-theme="dark"]) .guide-bulle.fleche-haut::before { border-top-color: transparent; border-bottom-color: #1e293b; }

    @keyframes guide-apparition { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: none; } }

    @media (max-width: 640px) {
      .guide-bulle { width: calc(100vw - 32px); left: 16px !important; }
    }
  `]
})
export class GuidedHelpComponent implements OnInit, OnDestroy {
  private help = inject(HelpService);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);

  actif = false;
  index = 0;
  etapes: GuideEtape[] = [];
  etape: GuideEtape | null = null;

  halo = { top: 0, left: 0, width: 0, height: 0 };
  bulle = { top: 0, left: 0 };
  flecheEnHaut = false;

  private abonnement?: Subscription;
  private attente?: number;

  ngOnInit(): void {
    this.abonnement = this.help.guideOuvert$.subscribe(ouvert => {
      if (ouvert) { this.demarrer(); } else { this.actif = false; }
      this.cdr.detectChanges();
    });
  }

  ngOnDestroy(): void {
    this.abonnement?.unsubscribe();
    if (this.attente) { cancelAnimationFrame(this.attente); }
  }

  @HostListener('window:resize') auRedimensionnement(): void { if (this.actif) { this.placer(); } }
  /**
   * Echap ferme la visite pour l'instant, mais ne la marque PAS comme vue :
   * elle sera reproposee a la prochaine connexion. Seuls "Passer" et "Terminer",
   * qui sont des gestes deliberes, valent definitivement non.
   */
  @HostListener('window:keydown.escape') auEchap(): void {
    if (!this.actif) { return; }
    this.actif = false;
    this.etape = null;
    this.help.fermerGuide(false);
    this.cdr.detectChanges();
  }

  private demarrer(): void {
    this.etapes = this.help.etapesGuide();
    this.index = 0;
    this.actif = this.etapes.length > 0;
    if (this.actif) { this.allerA(0); }
  }

  suivant(): void {
    if (this.index >= this.etapes.length - 1) { this.terminer(); return; }
    this.allerA(this.index + 1);
  }

  precedent(): void { if (this.index > 0) { this.allerA(this.index - 1); } }

  /** "Passer" et "Terminer" ont le meme effet : on ne represente plus la visite. */
  passer(): void { this.terminer(); }

  private terminer(): void {
    this.actif = false;
    this.etape = null;
    this.help.fermerGuide(true);
    this.cdr.detectChanges();
  }

  private allerA(index: number): void {
    this.index = index;
    this.etape = this.etapes[index];
    const etape = this.etape;
    const aller = () => this.attendreCible(etape, 0);

    // Certaines etapes vivent sur une autre page (ajout d'un vehicule, carte,
    // rapports) : on y navigue avant de chercher l'element.
    if (etape.route && !this.router.url.startsWith(etape.route)) {
      this.router.navigateByUrl(etape.route).then(aller);
    } else {
      aller();
    }
  }

  /**
   * Un ecran Angular n'est pas peint instantanement apres la navigation : on
   * laisse jusqu'a ~3 s a l'element pour apparaitre, puis on saute l'etape
   * plutot que de pointer une zone vide.
   */
  private attendreCible(etape: GuideEtape, essais: number): void {
    const cible = document.querySelector('[data-guide="' + etape.cible + '"]') as HTMLElement | null;

    if (cible) {
      cible.scrollIntoView({ block: 'center', behavior: 'smooth' });
      this.placer(cible);
      this.cdr.detectChanges();
      return;
    }

    if (essais > 180) { this.sauter(); return; }
    this.attente = requestAnimationFrame(() => this.attendreCible(etape, essais + 1));
  }

  /** Cible introuvable : on avance sans bloquer le client sur un ecran fige. */
  private sauter(): void {
    if (this.index >= this.etapes.length - 1) { this.terminer(); }
    else { this.allerA(this.index + 1); }
  }

  private placer(element?: HTMLElement | null): void {
    const cible = element || (this.etape
      ? document.querySelector('[data-guide="' + this.etape.cible + '"]') as HTMLElement | null
      : null);
    if (!cible) { return; }

    const r = cible.getBoundingClientRect();
    const marge = 6;
    this.halo = {
      top: r.top - marge, left: r.left - marge,
      width: r.width + marge * 2, height: r.height + marge * 2
    };

    const hauteurBulle = 210;
    const placeEnDessous = window.innerHeight - r.bottom > hauteurBulle + 20;
    this.flecheEnHaut = placeEnDessous;

    const top = placeEnDessous ? r.bottom + 16 : Math.max(12, r.top - hauteurBulle - 16);
    const left = Math.min(Math.max(12, r.left - 10), window.innerWidth - 342);
    this.bulle = { top, left };
  }
}
