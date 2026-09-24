import { Component, OnInit, OnDestroy, HostListener, ChangeDetectorRef, NgZone, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { Subscription } from 'rxjs';
import { HelpService } from '../../services/help.service';
import { AuthService } from '../../services/auth.service';
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
 *
 * Monte UNE SEULE FOIS, dans le composant racine (main.ts), jamais dans
 * <app-layout> : chaque page porte sa propre app-layout, et la visite, qui
 * change de page d'une etape a l'autre, etait detruite a chaque « Suivant ».
 * La nouvelle instance repartait de l'etape 1 et renvoyait au tableau de bord,
 * pendant que l'ancienne, detruite, continuait de naviguer (relecture du
 * 22/09/2026). Monte a la racine, le composant survit aussi a la deconnexion :
 * il se ferme donc de lui-meme quand l'utilisateur n'est plus connecte.
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
    /* Pas de transition sur le cadre : il suit sa cible image par image (voir
       suivre()), une transition de .2s le ferait trainer derriere le bouton
       pendant chaque defilement. */
    .guide-halo {
      position: fixed; z-index: 10001; pointer-events: none;
      border-radius: 10px; border: 2px solid #2563eb;
      box-shadow: 0 0 0 4px rgba(37, 99, 235, .25);
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
  private auth = inject(AuthService);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);
  private zone = inject(NgZone);

  actif = false;
  index = 0;
  etapes: GuideEtape[] = [];
  etape: GuideEtape | null = null;

  halo = { top: 0, left: 0, width: 0, height: 0 };
  bulle = { top: 0, left: 0 };
  flecheEnHaut = false;

  private abonnements: Subscription[] = [];
  /** Image demandee : recherche de la cible, puis suivi de sa position (suivre). */
  private attente?: number;
  /** Images consecutives ou la cible n'a pas bouge, et recentrage deja fait pour l'etape. */
  private imagesStables = 0;
  private recentree = false;
  /** Pose dans ngOnDestroy : plus aucune navigation ni boucle d'attente ensuite. */
  private detruit = false;
  /**
   * Numero de l'etape en cours de recherche. Une navigation terminee ou une
   * boucle d'attente lancee pour une etape PRECEDENTE (« Suivant » clique deux
   * fois, visite fermee pendant la navigation) ne doit plus rien faire.
   */
  private generation = 0;

  ngOnInit(): void {
    this.abonnements.push(this.help.guideOuvert$.subscribe(ouvert => {
      // Une visite deja en cours n'est jamais relancee a l'etape 1 : c'est ce
      // retour force au debut qui bloquait le client sur la premiere bulle.
      if (ouvert) { if (!this.actif) { this.demarrer(); } } else { this.arreter(); }
      this.cdr.detectChanges();
    }));
    // Deconnexion (volontaire ou jeton expire) : la visite ne doit pas rester
    // posee sur l'ecran de connexion. Elle n'est pas marquee comme vue.
    this.abonnements.push(this.auth.getCurrentUser().subscribe(utilisateur => {
      if (!utilisateur && this.actif) { this.help.fermerGuide(false); }
    }));
  }

  ngOnDestroy(): void {
    this.detruit = true;
    this.abonnements.forEach(a => a.unsubscribe());
    this.annulerAttente();
  }

  /**
   * Echap ferme la visite pour l'instant, mais ne la marque PAS comme vue :
   * elle sera reproposee au prochain chargement de l'application (pas a la page
   * suivante, voir HelpService.dejaProposee). Seuls "Passer" et "Terminer", qui
   * sont des gestes deliberes, valent definitivement non.
   */
  @HostListener('window:keydown.escape') auEchap(): void {
    if (!this.actif) { return; }
    this.arreter();
    this.help.fermerGuide(false);
    this.cdr.detectChanges();
  }

  /** Masque la visite et coupe toute recherche de cible encore en cours. */
  private arreter(): void {
    this.actif = false;
    this.etape = null;
    this.generation++;
    this.annulerAttente();
  }

  private annulerAttente(): void {
    if (this.attente) { cancelAnimationFrame(this.attente); this.attente = undefined; }
  }

  private demarrer(): void {
    this.etapes = this.help.etapesGuide();
    this.index = 0;
    this.actif = this.etapes.length > 0;
    if (this.actif) { this.allerA(0); }
  }

  suivant(): void {
    if (this.index >= this.etapes.length - 1) {
      // « Terminer » : on ferme, puis on depose le client la ou son parcours
      // se poursuit (Vehicules en GPA, Suivi en direct en GPS). « Passer », lui,
      // ne deplace pas : le client a voulu s'arreter la ou il est.
      const arrivee = this.etapes[this.index]?.routeApresFin;
      this.terminer();
      if (arrivee) { this.router.navigateByUrl(arrivee); }
      return;
    }
    this.allerA(this.index + 1);
  }

  precedent(): void { if (this.index > 0) { this.allerA(this.index - 1); } }

  /** "Passer" et "Terminer" ont le meme effet : on ne represente plus la visite. */
  passer(): void { this.terminer(); }

  private terminer(): void {
    this.arreter();
    this.help.fermerGuide(true);
    this.cdr.detectChanges();
  }

  private allerA(index: number): void {
    this.annulerAttente();
    const generation = ++this.generation;
    this.index = index;
    this.etape = this.etapes[index];
    const etape = this.etape;
    const aller = () => {
      if (this.detruit || generation !== this.generation) { return; }
      this.attendreCible(etape, 0, generation);
    };

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
  private attendreCible(etape: GuideEtape, essais: number, generation: number): void {
    this.attente = undefined;
    if (this.detruit || generation !== this.generation) { return; }
    const cible = document.querySelector('[data-guide="' + etape.cible + '"]') as HTMLElement | null;

    if (cible) {
      cible.scrollIntoView?.({ block: 'center', behavior: 'smooth' });
      this.placer(cible.getBoundingClientRect());
      this.cdr.detectChanges();
      this.imagesStables = 0;
      this.recentree = false;
      this.suivre(etape, generation);
      return;
    }

    if (essais > 180) { this.sauter(); return; }
    this.attente = requestAnimationFrame(() => this.attendreCible(etape, essais + 1, generation));
  }

  /** Cible introuvable : on avance sans bloquer le client sur un ecran fige. */
  private sauter(): void {
    if (this.index >= this.etapes.length - 1) { this.terminer(); }
    else { this.allerA(this.index + 1); }
  }

  /**
   * Garde le cadre et la bulle sur la cible tant que l'etape est affichee.
   *
   * Le cadre est en position fixe, calcule depuis getBoundingClientRect. Une
   * mesure unique, prise a l'instant ou la cible apparait, ne tenait pas : la
   * page bouge encore apres — defilement doux lance juste au-dessus, tableau
   * qui se remplit, ancienne page pas encore retiree — et le cadre restait la
   * ou le bouton ETAIT (Karim, 24/09/2026 : « Nouveau chauffeur » et
   * « Nouveau modele » encadres plus bas, dans la colonne Actions du tableau,
   * en GPA comme en GPS). On relit donc la position a chaque image et on ne
   * redessine que si elle a change ; cela couvre aussi le redimensionnement de
   * la fenetre et la molette de l'utilisateur.
   *
   * La boucle tourne hors de la zone Angular : dedans, chaque image relancerait
   * la detection de changements de TOUTE l'application, 60 fois par seconde.
   */
  private suivre(etape: GuideEtape, generation: number): void {
    this.zone.runOutsideAngular(() => {
      this.attente = requestAnimationFrame(() => {
        this.attente = undefined;
        if (this.detruit || generation !== this.generation) { return; }
        const cible = document.querySelector('[data-guide="' + etape.cible + '"]') as HTMLElement | null;
        const r = cible?.getBoundingClientRect();
        // Cible absente ou masquee un instant (liste en cours de rafraichissement) :
        // on garde le cadre ou il est plutot que de l'envoyer dans le coin de l'ecran.
        if (cible && r && (r.width > 0 || r.height > 0)) {
          if (this.placer(r)) {
            this.imagesStables = 0;
            this.cdr.detectChanges();
          } else if (++this.imagesStables === 15 && !this.recentree
                     && (r.top < 0 || r.bottom > window.innerHeight)) {
            // La page s'est tassee apres le premier defilement et a emporte le
            // bouton hors de l'ecran : on le ramene, une seule fois par etape
            // pour ne pas lutter contre un client qui fait defiler lui-meme.
            this.recentree = true;
            cible.scrollIntoView?.({ block: 'center', behavior: 'smooth' });
          }
        }
        this.suivre(etape, generation);
      });
    });
  }

  /** Pose le cadre et la bulle sur le rectangle de la cible ; faux si rien n'a bouge. */
  private placer(r: DOMRect): boolean {
    const marge = 6;
    const halo = {
      top: r.top - marge, left: r.left - marge,
      width: r.width + marge * 2, height: r.height + marge * 2
    };

    const hauteurBulle = 210;
    const placeEnDessous = window.innerHeight - r.bottom > hauteurBulle + 20;
    const bulle = {
      top: placeEnDessous ? r.bottom + 16 : Math.max(12, r.top - hauteurBulle - 16),
      left: Math.min(Math.max(12, r.left - 10), window.innerWidth - 342)
    };

    const proche = (a: number, b: number) => Math.abs(a - b) < 0.5;
    if (proche(halo.top, this.halo.top) && proche(halo.left, this.halo.left)
        && proche(halo.width, this.halo.width) && proche(halo.height, this.halo.height)
        && proche(bulle.top, this.bulle.top) && proche(bulle.left, this.bulle.left)
        && placeEnDessous === this.flecheEnHaut) {
      return false;
    }
    this.halo = halo;
    this.bulle = bulle;
    this.flecheEnHaut = placeEnDessous;
    return true;
  }
}
