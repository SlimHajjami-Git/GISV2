import { Injectable, inject } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from './auth.service';
import { PermissionService, ModuleKey } from './permission.service';
import { HelpArticle, HelpModule, GuideEtape } from './help-content.model';
import { ARTICLES_AIDE, ETAPES_GUIDE } from './help-content';

/** Cle localStorage : on versionne pour pouvoir rejouer la visite apres une refonte. */
const CLE_ETAT = 'calypso_aide_v1';

/**
 * Mots vides du francais courant.
 *
 * Le client ne tape pas des mots-cles, il pose une question : « ou est mon
 * camion », « comment je saisis une facture ». Sans ce filtre, « mon » et
 * « comment » doivent eux aussi etre trouves, et la recherche ne rend rien.
 * Verifie le 21/09/2026 sur l'application : « ou est mon camion » ramenait 0.
 */
const MOTS_VIDES = new Set([
  'le', 'la', 'les', 'un', 'une', 'des', 'du', 'de', 'au', 'aux',
  'mon', 'ma', 'mes', 'son', 'sa', 'ses', 'notre', 'nos', 'votre', 'vos',
  'ce', 'cet', 'cette', 'ces', 'et', 'ou', 'est', 'sont', 'a', 'en', 'dans',
  'sur', 'pour', 'par', 'avec', 'sans', 'que', 'qui', 'quoi', 'comment',
  'pourquoi', 'quand', 'je', 'tu', 'il', 'elle', 'on', 'nous', 'vous',
  'faire', 'fait', 'peux', 'peut', 'dois', 'doit', 'veux', 'veut', 'y'
]);

interface EtatAide {
  /** La visite guidee a ete terminee OU passee volontairement. */
  guideTermine?: boolean;
  dateFin?: string;
}

@Injectable({ providedIn: 'root' })
export class HelpService {
  private auth = inject(AuthService);
  private permissions = inject(PermissionService);

  /** Ouverture de la visite guidee, pilotee depuis le bouton "?" de la barre du haut. */
  private guideOuvertSource = new BehaviorSubject<boolean>(false);
  guideOuvert$ = this.guideOuvertSource.asObservable();

  // ---------------------------------------------------------------- Recherche

  /**
   * Normalise pour comparer : minuscules, accents retires, ponctuation en espaces.
   * Sans ca, "geofence" ne trouve pas "Geofence" et "depenses" ne trouve pas
   * "depenses" saisi "Depenses" - le client tape rarement les accents.
   */
  private normaliser(texte: string): string {
    return (texte || '')
      .toLowerCase()
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')
      .replace(/[^a-z0-9]+/g, ' ')
      .trim();
  }

  /**
   * Articles auxquels ce client a droit : module de l'abonnement et du profil,
   * type de societe, et rapports reellement ouverts. Les paragraphes lies a un
   * rapport ferme sont retires de la copie rendue : la recherche ne les trouve
   * donc pas non plus.
   */
  articlesVisibles(): HelpArticle[] {
    return ARTICLES_AIDE
      .filter(a => this.moduleAutorise(a.module) && this.typeSocieteAutorise(a))
      .map(a => this.adapter(a));
  }

  private moduleAutorise(module: HelpModule): boolean {
    if (module === 'general') return true;
    return this.permissions.hasModuleAccess(module as ModuleKey);
  }

  private typeSocieteAutorise(article: HelpArticle): boolean {
    if (!article.typeSociete) return true;
    return this.auth.getCurrentUserSync()?.companyType === article.typeSociete;
  }

  /** Au moins un des rapports cites est-il ouvert (abonnement ET droits de l'utilisateur) ? */
  private unRapportOuvert(rapports: string[]): boolean {
    return rapports.some(r => this.permissions.hasReportAccess(r));
  }

  /** Copie de l'article ou ne restent que les paragraphes conditionnels qui le concernent. */
  private adapter(article: HelpArticle): HelpArticle {
    const { paragraphesConditionnels, ...reste } = article;
    if (!paragraphesConditionnels?.length) return article;
    const retenus = paragraphesConditionnels
      .filter(p => !p.rapports?.length || this.unRapportOuvert(p.rapports))
      .map(p => p.texte);
    return { ...reste, paragraphes: [...(article.paragraphes || []), ...retenus] };
  }

  /**
   * Recherche par mot-cle. Tous les mots tapes doivent etre trouves (ET), sinon
   * une recherche a deux mots ramene la moitie de l'aide et le client se perd.
   * Le score privilegie le titre, puis les mots-cles, puis le corps du texte.
   */
  rechercher(question: string): HelpArticle[] {
    const tousLesMots = this.normaliser(question).split(' ').filter(m => m.length > 1);
    const articles = this.articlesVisibles();

    // On retire les mots vides, sauf si la question n'est faite que de ceux-la
    // (« comment faire ? ») : dans ce cas on rend tout plutot que rien.
    const mots = tousLesMots.filter(m => !MOTS_VIDES.has(m));
    if (!mots.length) return articles;

    const resultats: { article: HelpArticle; score: number }[] = [];

    for (const article of articles) {
      const titre = this.normaliser(article.titre);
      const cles = this.normaliser(article.motsCles.join(' '));
      const corps = this.normaliser([
        article.resume,
        ...(article.etapes || []),
        ...(article.paragraphes || []),
        article.aRetenir || ''
      ].join(' '));

      let score = 0;
      let tousTrouves = true;

      for (const mot of mots) {
        let scoreMot = 0;
        if (this.contient(titre, mot)) scoreMot += 10;
        if (this.contient(cles, mot)) scoreMot += 5;
        if (this.contient(corps, mot)) scoreMot += 1;
        if (scoreMot === 0) { tousTrouves = false; break; }
        score += scoreMot;
      }

      if (tousTrouves) resultats.push({ article, score });
    }

    return resultats.sort((a, b) => b.score - a.score).map(r => r.article);
  }

  /**
   * Racine approximative d'un mot, pour encaisser les conjugaisons et les
   * pluriels sans embarquer de bibliotheque : « saisis » doit trouver
   * « saisir », « vehicules » doit trouver « vehicule ». On coupe les deux
   * dernieres lettres, sans jamais descendre sous 5 caracteres pour eviter
   * les rapprochements hasardeux.
   */
  private racine(mot: string): string {
    return mot.length >= 6 ? mot.slice(0, Math.max(5, mot.length - 2)) : mot;
  }

  /** Le texte contient-il ce mot, a la conjugaison pres ? */
  private contient(texte: string, mot: string): boolean {
    return texte.includes(mot) || texte.includes(this.racine(mot));
  }

  articleParId(id: string): HelpArticle | undefined {
    return this.articlesVisibles().find(a => a.id === id);
  }

  /** Une video n'est affichee que si elle a reellement ete tournee. */
  aUneVideo(article: HelpArticle): boolean {
    return !!article.video && !!article.video.url && article.video.url.trim().length > 0;
  }

  // ------------------------------------------------------------ Visite guidee

  /** Etapes pertinentes pour ce client : on saute les modules non souscrits. */
  etapesGuide(): GuideEtape[] {
    return ETAPES_GUIDE.filter(e =>
      (!e.module || this.moduleAutorise(e.module))
      && (!e.sauf || !this.moduleAutorise(e.sauf)));
  }

  /**
   * Utilisateurs a qui la visite a deja ete montree depuis le chargement de
   * l'application. <app-layout> est recreee a CHAQUE page et propose la visite
   * dans son ngOnInit : sans cette memoire, chaque changement de page la
   * reproposait tant qu'elle n'etait ni terminee ni passee. Echap, puis un clic
   * sur « Vehicules », et la visite repartait de l'etape 1 en ramenant de force
   * au tableau de bord (relecture du 22/09/2026).
   */
  private dejaProposee = new Set<string>();

  /**
   * « Passer » et « Terminer » retenus aussi en memoire : quand localStorage
   * refuse l'ecriture (quota plein, stockage bloque), le non du client valait
   * pour rien et la visite revenait a chaque page.
   */
  private termineeEnMemoire = new Set<string>();

  /**
   * Faut-il proposer la visite ? Oui tant que l'utilisateur ne l'a ni terminee
   * ni passee, et au plus une fois par ouverture de l'application. L'etat est
   * garde par utilisateur : sur un poste partage, le collegue suivant a droit a
   * sa propre visite.
   */
  doitProposerLeGuide(): boolean {
    const cle = this.cleUtilisateur();
    return !this.dejaProposee.has(cle)
      && !this.termineeEnMemoire.has(cle)
      && !this.lireEtat().guideTermine;
  }

  /** Proposition automatique de premiere connexion : sans effet si elle n'a plus lieu d'etre. */
  proposerLeGuide(): void {
    if (this.doitProposerLeGuide()) this.ouvrirGuide();
  }

  ouvrirGuide(): void {
    this.dejaProposee.add(this.cleUtilisateur());
    this.guideOuvertSource.next(true);
  }

  /** Appele a la fin de la visite comme sur "Passer" : dans les deux cas on n'insiste plus. */
  fermerGuide(termine: boolean): void {
    this.guideOuvertSource.next(false);
    if (termine) {
      this.termineeEnMemoire.add(this.cleUtilisateur());
      this.ecrireEtat({ guideTermine: true, dateFin: new Date().toISOString() });
    }
  }

  /** Permet au client de refaire la visite depuis le bouton "?". */
  reinitialiserGuide(): void {
    this.termineeEnMemoire.delete(this.cleUtilisateur());
    this.ecrireEtat({});
    this.ouvrirGuide();
  }

  // ------------------------------------------------------------------- Etat
  // localStorage volontairement, pas de colonne en base : aucune migration a
  // jouer sur la production pour livrer l'aide. Contrepartie assumee : la
  // visite peut se reproposer sur un nouveau navigateur.

  private lireEtat(): EtatAide {
    try {
      const brut = localStorage.getItem(CLE_ETAT);
      if (!brut) return {};
      const parTout = JSON.parse(brut) as Record<string, EtatAide>;
      return parTout[this.cleUtilisateur()] || {};
    } catch {
      return {};
    }
  }

  private ecrireEtat(etat: EtatAide): void {
    try {
      const brut = localStorage.getItem(CLE_ETAT);
      const parTout = brut ? (JSON.parse(brut) as Record<string, EtatAide>) : {};
      parTout[this.cleUtilisateur()] = etat;
      localStorage.setItem(CLE_ETAT, JSON.stringify(parTout));
    } catch {
      // Navigation privee ou stockage plein : l'aide reste utilisable. Le
      // choix du client tient pour la session (termineeEnMemoire) ; la visite
      // se representera au prochain chargement de l'application. Pas de quoi planter.
    }
  }

  private cleUtilisateur(): string {
    return this.auth.getCurrentUserSync()?.id || 'anonyme';
  }
}
