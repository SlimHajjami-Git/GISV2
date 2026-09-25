import { Component, OnInit, OnDestroy, HostListener, ChangeDetectorRef, ElementRef, NgZone, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { NavigationEnd, Router } from '@angular/router';
import { Subscription, filter } from 'rxjs';
import { HelpService } from '../../services/help.service';
import { AuthService } from '../../services/auth.service';
import { GuideEtape, VisiteEcran } from '../../services/help-content.model';
import { CONSEIL_PREMIERE_CONNEXION } from '../../services/help-content';

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
 *
 * Deux usages du meme moteur (cadre bleu + bulle) :
 *  - 'parcours' : la visite de premiere connexion, qui change de page ;
 *  - 'ecran'    : le guide de l'ecran qui vient de s'ouvrir, presente a un
 *                 nouvel utilisateur a chaque acces tant qu'il ne l'a ni passe
 *                 ni termine (Karim, 24/09/2026). Il ne change jamais de page.
 * Le parcours a toujours la main : aucun guide d'ecran ne demarre pendant lui.
 */
@Component({
  selector: 'app-guided-help',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (conseilOuvert) {
      <!-- Conseil de premiere connexion, au centre de l'ecran, juste avant la visite. -->
      <div class="guide-voile"></div>
      <div class="guide-conseil" role="dialog" aria-modal="true" aria-labelledby="guide-conseil-titre">
        <div class="conseil-icone" aria-hidden="true">
          <!-- Ampoule : c'est un conseil, pas une alerte. -->
          <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M9 18h6"/><path d="M10 22h4"/>
            <path d="M12 2a7 7 0 0 0-4 12.7c.6.5 1 1.3 1 2.1V18h6v-1.2c0-.8.4-1.6 1-2.1A7 7 0 0 0 12 2z"/>
          </svg>
        </div>
        <h3 id="guide-conseil-titre">{{ conseil.titre }}</h3>
        @for (p of conseil.paragraphes; track $index) {
          <p>@for (m of p; track $index) {@if (m.gras) {<strong>{{ m.texte }}</strong>} @else {{{ m.texte }}}}</p>
        }
        <div class="conseil-actions">
          <button type="button" class="conseil-bouton" (click)="commencerApresConseil()">{{ conseil.bouton }}</button>
        </div>
      </div>
    }
    @if (actif && etape) {
      <!-- Le voile ne ferme PAS la visite : un clic a cote ne doit pas supprimer
           definitivement un parcours que le client n'a jamais vu. Pour sortir,
           il y a "Passer" et la touche Echap.
           Guide d'ecran : rien n'est pose tant que sa cible n'est pas trouvee —
           le client n'a encore rien demande, un ecran assombri sans bulle le
           bloquerait pour rien. Le parcours, lui, garde voile et bulle pendant
           qu'il change de page : sans bulle, le client restait devant un ecran
           sombre sans aucun bouton (relecture du 24/09/2026). -->
      @if (cibleTrouvee || mode === 'parcours') {
      @if (etape.action && cibleTrouvee) {
        <!-- Etape a FAIRE : le voile entoure la cible en quatre bandes au lieu de
             la couvrir, pour que le client clique le bouton ou remplisse le champ
             encadre — et rien d'autre. -->
        @for (b of bandes; track $index) {
          <div class="guide-voile bande" [style.top.px]="b.top" [style.left.px]="b.left"
               [style.width.px]="b.width" [style.height.px]="b.height"></div>
        }
      } @else {
        <div class="guide-voile"></div>
      }
      @if (cibleTrouvee) {
        <div class="guide-halo" [style.top.px]="halo.top" [style.left.px]="halo.left"
             [style.width.px]="halo.width" [style.height.px]="halo.height"></div>
      }

      <div class="guide-bulle" [style.top.px]="bulle.top" [style.left.px]="bulle.left"
           [class.fleche-haut]="flecheEnHaut" role="dialog" aria-live="polite">
        <div class="guide-compteur">
          @if (mode === 'ecran' && visiteEcran) { {{ visiteEcran.titre }} · }Étape {{ index + 1 }} sur {{ etapes.length }}
        </div>
        <h3>{{ etape.titre }}</h3>
        <p>{{ texte }}</p>

        <div class="guide-points">
          @for (e of etapes; track e.id; let i = $index) {
            <span class="point" [class.vu]="i <= index"></span>
          }
        </div>

        <div class="guide-actions">
          <button type="button" class="lien" (click)="passer()">Passer</button>
          <span class="espace"></span>
          @if (aUnePrecedente()) {
            <button type="button" class="secondaire" (click)="precedent()">Précédent</button>
          }
          <!-- Etape « clic » ou « disparition » : c'est le geste du client qui fait
               avancer, pas de bouton. Etape « valeur » : Suivant attend le champ rempli. -->
          @if (!etape.action || etape.action === 'valeur') {
            <button type="button" class="principal" (click)="suivant()"
                    [disabled]="etape.action === 'valeur' && !etape.facultatif && !valeurSaisie">
              {{ index === etapes.length - 1 ? 'Terminer' : 'Suivant' }}
            </button>
          }
        </div>
      </div>
      }
    }
  `,
  styles: [`
    .guide-voile {
      position: fixed; inset: 0; background: rgba(15, 23, 42, 0.55);
      z-index: 10000; animation: guide-apparition .18s ease-out;
    }
    /* Bandes du voile autour d'une cible a manipuler : positionnees une a une. */
    .guide-voile.bande { inset: auto; animation: none; }
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
    /* Conseil de premiere connexion. Karim (25/09/2026) : « bon sur le plan
       ergonomique, avec une police tres claire » — texte en 16 px, presque noir sur
       blanc (contraste AAA), interligne large, colonne etroite pour une lecture
       facile, un seul bouton bien visible. Centree sans transform : l'animation
       d'apparition en utilise deja un. */
    .guide-conseil {
      position: fixed; z-index: 10002; inset: 0; margin: auto; height: fit-content;
      width: 520px; max-width: calc(100vw - 32px); box-sizing: border-box;
      background: #fff; color: #0f172a; border-radius: 16px; padding: 28px 30px 24px;
      box-shadow: 0 24px 60px rgba(15, 23, 42, .35);
      font-family: inherit; -webkit-font-smoothing: antialiased;
      animation: guide-apparition .18s ease-out;
    }
    :host-context([data-theme="dark"]) .guide-conseil { background: #1e293b; color: #f1f5f9; }
    .conseil-icone {
      width: 44px; height: 44px; border-radius: 50%; margin-bottom: 14px;
      display: flex; align-items: center; justify-content: center;
      background: #eff6ff; color: #2563eb;
    }
    :host-context([data-theme="dark"]) .conseil-icone { background: rgba(37, 99, 235, .18); color: #93c5fd; }
    .guide-conseil h3 { margin: 0 0 14px; font-size: 20px; line-height: 1.3; font-weight: 700; letter-spacing: -.01em; }
    .guide-conseil p { margin: 0 0 14px; font-size: 16px; line-height: 1.65; color: #1e293b; }
    .guide-conseil p strong { font-weight: 700; color: #0f172a; }
    :host-context([data-theme="dark"]) .guide-conseil p { color: #e2e8f0; }
    :host-context([data-theme="dark"]) .guide-conseil p strong { color: #fff; }
    .conseil-actions { display: flex; justify-content: flex-end; margin-top: 22px; }
    .conseil-bouton {
      font: inherit; font-size: 15px; font-weight: 600; cursor: pointer;
      padding: 11px 22px; border-radius: 10px; border: none;
      background: #2563eb; color: #fff;
    }
    .conseil-bouton:hover { background: #1d4ed8; }
    .conseil-bouton:focus-visible { outline: 3px solid #93c5fd; outline-offset: 2px; }
    @media (max-width: 640px) {
      .guide-conseil { padding: 22px 20px 18px; }
      .guide-conseil p { font-size: 15px; }
      .conseil-bouton { width: 100%; }
    }
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
    .guide-actions .principal:disabled { opacity: .45; cursor: not-allowed; background: #2563eb; }
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
  private hote = inject(ElementRef<HTMLElement>);

  actif = false;
  index = 0;
  etapes: GuideEtape[] = [];
  etape: GuideEtape | null = null;
  /** Texte de la bulle affichee. */
  texte = '';
  /** Selecteur suivi pour l'etape affichee (data-guide de sa cible). */
  private cibleSuivie = '';
  /** Etape « valeur » : le champ encadre est rempli, « Suivant » s'active. */
  valeurSaisie = false;
  /**
   * Voile d'une etape a faire, en quatre bandes autour du cadre (haut, bas,
   * gauche, droite) : la cible reste cliquable, le reste de l'ecran non.
   */
  bandes: { top: number; left: number; width: number; height: number }[] = [];
  /** Taille de fenetre du dernier placement : un redimensionnement recalcule les bandes. */
  private fenetre = { w: 0, h: 0 };
  /**
   * Page ou « Terminer » du parcours vient de deposer le client. Il sort d'une
   * visite de sept bulles : on ne lui en impose pas une deuxieme sur-le-champ, qui
   * repeterait en plus l'etape « Ajoutez votre premier vehicule ». Le guide de
   * l'ecran viendra au prochain acces (relecture du 24/09/2026).
   */
  private arriveeFinParcours: string | null = null;
  /** Hauteur reelle de la bulle, mesuree une fois affichee (les textes longs depassent 210 px). */
  private hauteurBulle = 210;
  /** Visite de premiere connexion, ou guide de l'ecran ouvert (visiteEcran). */
  mode: 'parcours' | 'ecran' = 'parcours';
  /** Conseil de premiere connexion affiche au centre, avant la premiere bulle de la visite. */
  conseilOuvert = false;
  readonly conseil = CONSEIL_PREMIERE_CONNEXION;
  visiteEcran: VisiteEcran | null = null;
  /** Cible de l'etape affichee trouvee : sans elle, ni cadre ni bulle. */
  cibleTrouvee = false;
  /**
   * Au moins une bulle a ete montree pendant ce guide d'ecran. Un guide dont
   * toutes les cibles manquaient se ferme seul : il n'est pas marque vu, le
   * client ne l'a jamais lu.
   */
  private etapeMontree = false;
  /** Etapes sautees faute de cible : « Precedent » les enjambe. */
  private sautees = new Set<number>();

  halo = { top: 0, left: 0, width: 0, height: 0 };
  bulle = { top: 0, left: 0 };
  flecheEnHaut = false;

  private abonnements: Subscription[] = [];
  /** Image demandee : recherche de la cible, puis suivi de sa position (suivre). */
  private attente?: number;
  /** Images consecutives ou la cible n'a pas bouge, et recentrage deja fait pour l'etape. */
  private imagesStables = 0;
  private recentree = false;
  /** Etape « disparition » : le client a clique la cible (« Ajouter ») pendant l'etape. */
  private cibleCliquee = false;
  /** Images consecutives sans cible pendant une etape a faire : la fiche a ete fermee. */
  private imagesSansCible = 0;
  /**
   * Ancetre qui fait defiler la cible (corps de la fiche) : une cible qui passe sous
   * son en-tete n'est plus visible, meme si elle reste dans la fenetre.
   */
  private conteneur: HTMLElement | null = null;
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
      if (ouvert) {
        // Une visite deja en cours n'est jamais relancee a l'etape 1 : c'est ce
        // retour force au debut qui bloquait le client sur la premiere bulle.
        // Un guide d'ecran, lui, cede la place (il n'est pas marque vu).
        if (!this.actif || this.mode === 'ecran') { this.arreter(); this.demarrer(); }
      } else if (this.mode === 'parcours') {
        this.arreter();
      }
      this.cdr.detectChanges();
    }));
    // Deconnexion (volontaire ou jeton expire) : la visite ne doit pas rester
    // posee sur l'ecran de connexion. Elle n'est pas marquee comme vue.
    this.abonnements.push(this.auth.getCurrentUser().subscribe(utilisateur => {
      if (utilisateur || (!this.actif && !this.conseilOuvert)) { return; }
      if (this.mode === 'parcours') { this.help.fermerGuide(false); }
      else { this.arreter(); this.cdr.detectChanges(); }
    }));
    this.abonnements.push(this.router.events
      .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
      .subscribe(e => this.auChangementDePage(e.urlAfterRedirects)));
    // Application rechargee directement sur un ecran : sa navigation peut etre
    // deja terminee quand ce composant s'abonne.
    if (this.router.navigated) { this.auChangementDePage(this.router.url); }

    // Tutoriel pas a pas : le geste du client fait avancer. Ecoute en phase de
    // CAPTURE : un bouton qui arrete la propagation (stopPropagation) ne doit pas
    // la cacher, et Entree doit etre intercepte avant d'atteindre le formulaire.
    const auClic = (e: MouseEvent) => this.auClic(e);
    const auClavier = (e: KeyboardEvent) => this.auClavier(e);
    const aLaSaisie = (e: Event) => this.aLaSaisie(e);
    document.addEventListener('click', auClic, true);
    document.addEventListener('keydown', auClavier, true);
    document.addEventListener('input', aLaSaisie, true);
    document.addEventListener('change', aLaSaisie, true);
    this.abonnements.push(new Subscription(() => {
      document.removeEventListener('click', auClic, true);
      document.removeEventListener('keydown', auClavier, true);
      document.removeEventListener('input', aLaSaisie, true);
      document.removeEventListener('change', aLaSaisie, true);
    }));
  }

  /**
   * Clic du client sur la cible. Etape « clic » : son propre clic suit son cours
   * (la fiche s'ouvre), puis on avance. Etape « disparition » : on retient qu'il a
   * bien clique « Ajouter » — seule une fiche fermee APRES ce clic vaut succes.
   */
  private auClic(e: MouseEvent): void {
    const action = this.etape?.action;
    if (!this.actif || !this.cibleTrouvee || (action !== 'clic' && action !== 'disparition')) { return; }
    const cible = this.chercher(this.cibleSuivie);
    if (!cible || !cible.contains(e.target as Node)) { return; }
    if (action === 'disparition') { this.cibleCliquee = true; return; }
    const generation = this.generation;
    setTimeout(() => {
      if (this.detruit || generation !== this.generation) { return; }
      this.suivant();
      this.cdr.detectChanges();
    });
  }

  /**
   * Etape « valeur » : Entree dans le champ encadre vaut « Suivant ». Laissee
   * passer, elle enverrait le formulaire de la fiche (ngSubmit) et creerait un
   * vehicule a moitie rempli au milieu du tutoriel.
   */
  private auClavier(e: KeyboardEvent): void {
    if (e.key !== 'Enter' || !this.actif || this.etape?.action !== 'valeur') { return; }
    const cible = this.chercher(this.cibleSuivie);
    if (!cible || !cible.contains(e.target as Node)) { return; }
    e.preventDefault();
    e.stopPropagation();
    this.valeurSaisie = this.champRempli(cible);
    if (this.valeurSaisie || this.etape.facultatif) { this.suivant(); }
    this.cdr.detectChanges();
  }

  /**
   * Etape « valeur » : « Suivant » suit la saisie des qu'elle a lieu. La boucle
   * d'images (suivre) le fait aussi, mais elle s'arrete quand l'onglet ne se
   * redessine plus (fenetre en arriere-plan) : une liste choisie laissait alors
   * « Suivant » grise (constate en verifiant l'ecran Entretien, 24/09/2026).
   */
  private aLaSaisie(e: Event): void {
    if (!this.actif || this.etape?.action !== 'valeur' || !this.cibleTrouvee) { return; }
    const cible = this.chercher(this.cibleSuivie);
    if (!cible || !cible.contains(e.target as Node)) { return; }
    const rempli = this.champRempli(cible);
    if (rempli !== this.valeurSaisie) { this.valeurSaisie = rempli; this.cdr.detectChanges(); }
  }

  /**
   * Champ rempli : texte non vide, ou liste positionnee sur un vrai choix. Une option
   * vide vaut "null" avec [value]="null" (fiche vehicule), mais "0: null" avec
   * [ngValue]="null" (« Aucun vehicule » de la fiche chauffeur) : les deux sont vides.
   */
  private champRempli(el: HTMLElement): boolean {
    // Cible qui regroupe plusieurs champs (intervalle d'entretien : km OU mois) :
    // remplie des que l'un d'eux l'est.
    if (!/^(INPUT|SELECT|TEXTAREA)$/.test(el.tagName)) {
      return Array.from(el.querySelectorAll('input, select, textarea')).some(c => this.champRempli(c as HTMLElement));
    }
    const v = (el as HTMLInputElement | HTMLSelectElement).value;
    // Un montant prerempli a 0 (prix d'une piece, montant d'une depense) n'est pas
    // une saisie : « Suivant » attend un vrai prix (Karim, 24/09/2026).
    if ((el as HTMLInputElement).type === 'number' && Number(v) === 0) { return false; }
    return typeof v === 'string' && v.trim() !== '' && v !== 'null' && !/^\d+:\s*null$/.test(v);
  }

  /** Champ ou le curseur est pose : la cible elle-meme, ou le premier champ du groupe qu'elle encadre. */
  private champAFocaliser(el: HTMLElement): HTMLElement {
    if (/^(INPUT|SELECT|TEXTAREA)$/.test(el.tagName)) { return el; }
    return (el.querySelector('input, select, textarea') as HTMLElement | null) || el;
  }

  /**
   * Chaque page ouverte : son guide, s'il y en a un pour ce nouvel utilisateur.
   * La visite de premiere connexion navigue elle-meme d'ecran en ecran : pendant
   * qu'elle tourne, aucun guide d'ecran ne s'intercale.
   */
  private auChangementDePage(url: string): void {
    // Ni pendant la visite de premiere connexion, ni pendant le conseil qui la precede.
    if (this.detruit || this.conseilOuvert || (this.actif && this.mode === 'parcours')) { return; }
    const arrivee = this.arriveeFinParcours;
    this.arriveeFinParcours = null;
    if (arrivee && url.split(/[?#]/)[0] === arrivee.split(/[?#]/)[0]) { return; }
    if (this.actif && this.mode === 'ecran') {
      if (this.visiteEcran && this.help.estSurSonEcran(this.visiteEcran, url)) { return; }
      // Retour arriere du navigateur pendant un guide : il appartenait a
      // l'ecran quitte, il s'efface sans etre marque vu.
      this.arreter();
    }
    const visite = this.help.visiteEcranAProposer(url);
    if (visite) { this.demarrerEcran(visite); }
    this.cdr.detectChanges();
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
   * Un guide d'ecran ferme par Echap revient au prochain acces a l'ecran.
   */
  @HostListener('window:keydown.escape') auEchap(): void {
    if (!this.actif && !this.conseilOuvert) { return; }
    const mode = this.conseilOuvert ? 'parcours' : this.mode;
    this.arreter();
    if (mode === 'parcours') { this.help.fermerGuide(false); }
    this.cdr.detectChanges();
  }

  /** Masque la visite (et le conseil qui la precede) et coupe toute recherche de cible. */
  private arreter(): void {
    this.conseilOuvert = false;
    this.actif = false;
    this.etape = null;
    this.cibleTrouvee = false;
    this.generation++;
    this.annulerAttente();
  }

  private annulerAttente(): void {
    if (this.attente) { cancelAnimationFrame(this.attente); this.attente = undefined; }
  }

  private demarrer(): void {
    // Nouvel utilisateur : le conseil « renseignez tous les champs » passe avant la
    // visite, une seule fois. Il est marque lu des qu'il s'affiche — ferme par Echap,
    // il ne revient pas non plus (Karim : « il ne revient ensuite plus jamais »).
    if (this.help.conseilAMontrer()) {
      this.help.marquerConseilVu();
      this.mode = 'parcours';
      this.conseilOuvert = true;
      setTimeout(() => (this.hote.nativeElement.querySelector('.conseil-bouton') as HTMLElement | null)?.focus());
      return;
    }
    this.lancer('parcours', null, this.help.etapesGuide());
  }

  /** « C'est compris, on commence » : le conseil laisse la place a la visite. */
  commencerApresConseil(): void {
    this.conseilOuvert = false;
    this.lancer('parcours', null, this.help.etapesGuide());
    this.cdr.detectChanges();
  }

  private demarrerEcran(visite: VisiteEcran): void {
    this.lancer('ecran', visite, visite.etapes);
  }

  private lancer(mode: 'parcours' | 'ecran', visite: VisiteEcran | null, etapes: GuideEtape[]): void {
    this.mode = mode;
    this.visiteEcran = visite;
    this.etapes = etapes;
    this.index = 0;
    this.etapeMontree = false;
    this.sautees.clear();
    this.actif = this.etapes.length > 0;
    if (this.actif) { this.allerA(0); }
  }

  suivant(): void {
    if (this.index >= this.etapes.length - 1) {
      // « Terminer » : on ferme, puis on depose le client la ou son parcours
      // se poursuit (Vehicules en GPA, Suivi en direct en GPS). « Passer », lui,
      // ne deplace pas : le client a voulu s'arreter la ou il est.
      const arrivee = this.mode === 'parcours' ? this.etapes[this.index]?.routeApresFin : undefined;
      this.terminer();
      if (arrivee) {
        this.arriveeFinParcours = arrivee;
        this.router.navigateByUrl(arrivee);
      }
      return;
    }
    this.allerA(this.index + 1);
  }

  /**
   * Etape montrable avant celle-ci, en enjambant celles sautees faute de cible ; -1 sinon.
   * Jamais vers un geste deja fait (bouton clique, fiche fermee) : il ne se rejoue
   * pas — revenir a « Cliquez sur Nouveau vehicule » avec la fiche deja ouverte
   * ne menerait nulle part.
   */
  private indexPrecedent(): number {
    let i = this.index - 1;
    while (i >= 0 && this.sautees.has(i)) { i--; }
    const action = i >= 0 ? this.etapes[i].action : undefined;
    return action === 'clic' || action === 'disparition' ? -1 : i;
  }

  /** « Precedent » n'apparait que s'il mene quelque part. */
  aUnePrecedente(): boolean { return this.indexPrecedent() >= 0; }

  precedent(): void {
    const i = this.indexPrecedent();
    if (i >= 0) { this.allerA(i); }
  }

  /** "Passer" et "Terminer" ont le meme effet : on ne represente plus la visite. */
  passer(): void { this.terminer(); }

  private terminer(): void {
    const mode = this.mode;
    const visite = this.visiteEcran;
    const montree = this.etapeMontree;
    this.arreter();
    if (mode === 'parcours') { this.help.fermerGuide(true); }
    else if (visite && montree) { this.help.marquerEcranVu(visite.id); }
    this.cdr.detectChanges();
  }

  private allerA(index: number): void {
    this.annulerAttente();
    const generation = ++this.generation;
    this.index = index;
    this.etape = this.etapes[index];
    this.texte = this.etape.texte;
    this.cibleTrouvee = false;
    this.valeurSaisie = false;
    this.cibleCliquee = false;
    this.imagesSansCible = 0;
    const etape = this.etape;
    // Premiere etape, ou ecran qui vient d'etre ouvert : la page peut encore se
    // peindre, on laisse ~3 s a la cible. Sur une page deja affichee, une cible
    // absente (bouton reserve a l'administrateur) l'est pour de bon : on ne fait
    // pas attendre le client plus d'une demi-seconde.
    const navigation = !!etape.route && !this.router.url.startsWith(etape.route);
    // Premier geste d'un tutoriel d'ecran : rien n'est affiche pendant l'attente,
    // on peut donc attendre ~6 s. L'ecran Echeances enchaine deux appels (vehicules,
    // puis echeances) avant d'afficher sa premiere ligne ; en developpement il
    // depassait les 3 s, et le tutoriel se refermait sans s'etre montre.
    // Champ suivant d'une meme fiche (une bulle par champ, Karim 24/09/2026) : s'il
    // n'est pas affiche (champs du leasing quand « Achat » est choisi), il ne le sera
    // pas — on le saute en ~10 images au lieu de 30, sinon les temps morts s'additionnent.
    const precedente = index > 0 ? this.etapes[index - 1] : undefined;
    const limite = this.mode === 'ecran' && index === 0 ? 360
      : navigation || index === 0 ? 180
      : this.mode === 'ecran' && precedente?.action === 'valeur' ? 10
      : 30;
    const aller = () => {
      if (this.detruit || generation !== this.generation) { return; }
      this.attendreCible(etape, 0, generation, limite);
    };

    // Certaines etapes vivent sur une autre page (ajout d'un vehicule, carte,
    // rapports) : on y navigue avant de chercher l'element.
    if (navigation) {
      this.router.navigateByUrl(etape.route!).then(aller);
    } else {
      aller();
    }
  }

  /**
   * Un ecran Angular n'est pas peint instantanement apres la navigation : on
   * laisse jusqu'a ~3 s a l'element pour apparaitre (`limite` images), puis on
   * saute l'etape plutot que de pointer une zone vide.
   */
  private attendreCible(etape: GuideEtape, essais: number, generation: number, limite = 180): void {
    this.attente = undefined;
    if (this.detruit || generation !== this.generation) { return; }
    const cible = this.chercher(etape.cible);

    if (cible) {
      this.cibleSuivie = etape.cible;
      this.sautees.delete(this.index);
      cible.scrollIntoView?.({ block: 'center', behavior: 'smooth' });
      this.placer(cible.getBoundingClientRect());
      if (etape.action === 'valeur') {
        // Champ deja rempli (retour par « Precedent ») : Suivant actif d'emblee.
        // Le curseur y est pose : le client tape sans avoir a cliquer.
        this.valeurSaisie = this.champRempli(cible);
        const champ = this.champAFocaliser(cible);
        champ.focus?.({ preventScroll: true });
        // 0 prerempli : selectionne, la frappe le remplace (sinon « 45 » donnait « 045 »).
        if ((champ as HTMLInputElement).type === 'number' && Number((champ as HTMLInputElement).value) === 0) {
          try { (champ as HTMLInputElement).select(); } catch { /* navigateur qui refuse select() sur un nombre */ }
        }
      }
      this.conteneur = this.conteneurDefilant(cible);
      this.cibleTrouvee = true;
      this.etapeMontree = true;
      this.cdr.detectChanges();
      this.imagesStables = 0;
      this.recentree = false;
      this.suivre(generation);
      return;
    }

    if (essais > limite) { this.sauter(); return; }
    this.attente = requestAnimationFrame(() => this.attendreCible(etape, essais + 1, generation, limite));
  }

  private chercher(selecteur: string): HTMLElement | null {
    return document.querySelector('[data-guide="' + selecteur + '"]') as HTMLElement | null;
  }

  /** Cible introuvable : on avance sans bloquer le client sur un ecran fige. */
  private sauter(): void {
    // Tutoriel d'ecran dont le premier geste est impossible — aucun vehicule, donc
    // aucune ligne d'echeance ni liste a remplir : il n'a pas lieu, SANS etre marque
    // vu. Il reviendra au prochain acces, quand l'ecran le permettra. Enchainer les
    // etapes suivantes le ferait surgir plus tard, en plein milieu d'une saisie.
    if (this.mode === 'ecran' && !this.etapeMontree) {
      this.arreter();
      this.cdr.detectChanges();
      return;
    }
    this.sautees.add(this.index);
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
  private suivre(generation: number): void {
    this.zone.runOutsideAngular(() => {
      this.attente = requestAnimationFrame(() => {
        this.attente = undefined;
        if (this.detruit || generation !== this.generation) { return; }
        // Hauteur reelle de la bulle : un texte long la fait depasser les 210 px
        // supposes, et posee au-dessus de sa cible elle la recouvrait.
        const h = (this.hote.nativeElement.querySelector('.guide-bulle') as HTMLElement | null)?.offsetHeight;
        if (h) { this.hauteurBulle = h; }
        const cible = this.chercher(this.cibleSuivie);
        // Etape « disparition » : le client a clique « Ajouter » et le bouton a quitte
        // l'ecran — la fiche s'est fermee sur un enregistrement reussi. L'etape est faite.
        if (!cible && this.etape?.action === 'disparition' && this.cibleCliquee) {
          this.zone.run(() => { this.suivant(); this.cdr.detectChanges(); });
          return;
        }
        // Cible d'une etape a faire disparue sans ce clic : la fiche a ete fermee
        // autrement (Tab jusqu'a « Annuler » puis Entree). Rien n'a ete enregistre :
        // le tutoriel s'arrete SANS etre marque vu, il reviendra au prochain acces.
        // Quelques images de patience, au cas ou la fiche se redessine.
        if (!cible && this.etape?.action) {
          if (++this.imagesSansCible >= 10) {
            this.zone.run(() => { this.arreter(); this.cdr.detectChanges(); });
            return;
          }
        } else {
          this.imagesSansCible = 0;
        }
        // Etape « valeur » : « Suivant » s'active des que le champ est rempli.
        if (cible && this.etape?.action === 'valeur') {
          const rempli = this.champRempli(cible);
          if (rempli !== this.valeurSaisie) { this.valeurSaisie = rempli; this.cdr.detectChanges(); }
        }
        const r = cible?.getBoundingClientRect();
        // Cible absente ou masquee un instant (liste en cours de rafraichissement) :
        // on garde le cadre ou il est plutot que de l'envoyer dans le coin de l'ecran.
        if (cible && r && (r.width > 0 || r.height > 0)) {
          if (this.placer(r)) {
            this.imagesStables = 0;
            this.cdr.detectChanges();
          } else if (++this.imagesStables === 15 && !this.recentree && this.masquee(r)) {
            // La page s'est tassee apres le premier defilement, ou la fiche a defile,
            // et la cible n'est plus visible : on la ramene, une seule fois par etape
            // pour ne pas lutter contre un client qui fait defiler lui-meme.
            this.recentree = true;
            cible.scrollIntoView?.({ block: 'center', behavior: 'smooth' });
          }
        }
        this.suivre(generation);
      });
    });
  }

  /**
   * Cible hors de vue : hors de la fenetre, ou hors de la zone visible de l'ancetre
   * qui la fait defiler — un champ de la fiche passe sous son en-tete reste dans la
   * fenetre, mais le client ne le voit plus (relecture du 24/09/2026).
   */
  private masquee(r: DOMRect): boolean {
    if (r.top < 0 || r.bottom > window.innerHeight) { return true; }
    const c = this.conteneur?.isConnected ? this.conteneur.getBoundingClientRect() : null;
    return !!c && (r.top < c.top || r.bottom > c.bottom);
  }

  /** Plus proche ancetre qui defile verticalement (corps d'une fiche), ou null. */
  private conteneurDefilant(el: HTMLElement): HTMLElement | null {
    for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) {
      if (/(auto|scroll)/.test(getComputedStyle(p).overflowY) && p.scrollHeight > p.clientHeight) { return p; }
    }
    return null;
  }

  /** Pose le cadre et la bulle sur le rectangle de la cible ; faux si rien n'a bouge. */
  private placer(r: DOMRect): boolean {
    const marge = 6;
    // Cible pleine largeur (barre des compteurs) : le cadre reste dans la fenetre,
    // sinon son bord gauche sortait de 6 px et disparaissait.
    const gauche = Math.max(2, r.left - marge);
    const droite = Math.min(window.innerWidth - 2, r.right + marge);
    const halo = {
      top: r.top - marge, left: gauche,
      width: Math.max(0, droite - gauche), height: r.height + marge * 2
    };

    const hauteurBulle = this.hauteurBulle;
    const placeEnDessous = window.innerHeight - r.bottom > hauteurBulle + 20;
    const bulle = {
      top: placeEnDessous ? r.bottom + 16 : Math.max(12, r.top - hauteurBulle - 16),
      left: Math.min(Math.max(12, r.left - 10), window.innerWidth - 342)
    };

    const proche = (a: number, b: number) => Math.abs(a - b) < 0.5;
    const W = window.innerWidth, H = window.innerHeight;
    if (proche(halo.top, this.halo.top) && proche(halo.left, this.halo.left)
        && proche(halo.width, this.halo.width) && proche(halo.height, this.halo.height)
        && proche(bulle.top, this.bulle.top) && proche(bulle.left, this.bulle.left)
        && placeEnDessous === this.flecheEnHaut && W === this.fenetre.w && H === this.fenetre.h) {
      return false;
    }
    this.halo = halo;
    this.bulle = bulle;
    this.flecheEnHaut = placeEnDessous;
    this.fenetre = { w: W, h: H };
    // Les quatre bandes du voile laissent le cadre a nu (etapes a faire).
    const haut = Math.min(H, Math.max(0, halo.top));
    const bas = Math.max(haut, Math.min(H, halo.top + halo.height));
    const g = Math.min(W, Math.max(0, halo.left));
    const d = Math.max(g, Math.min(W, halo.left + halo.width));
    this.bandes = [
      { top: 0, left: 0, width: W, height: haut },
      { top: bas, left: 0, width: W, height: H - bas },
      { top: haut, left: 0, width: g, height: bas - haut },
      { top: haut, left: d, width: W - d, height: bas - haut },
    ];
    return true;
  }
}
