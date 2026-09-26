import { Injectable, inject, isDevMode } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { AuthService } from './auth.service';
import { PermissionService, ModuleKey } from './permission.service';
import { HelpArticle, HelpModule, GuideEtape, VisiteEcran } from './help-content.model';
import { ARTICLES_AIDE, ETAPES_GUIDE, VISITES_ECRANS, PREMIERS_PAS_GPA } from './help-content';

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
  /**
   * Premiere connexion du compte vue dans ce navigateur : c'est un nouvel
   * utilisateur, les guides des ecrans lui sont presentes. Retenu ici parce que
   * le drapeau de la connexion disparait au premier rafraichissement du jeton.
   */
  nouvelUtilisateur?: boolean;
  /** Guides d'ecran passes ou termines (VisiteEcran.id). */
  ecransVus?: string[];
  /** Conseil de premiere connexion deja lu : il ne revient plus jamais. */
  conseilVu?: boolean;
  /**
   * Premiers pas en cours (offre GPA) : chaque guide termine emmene a l'ecran
   * suivant. Retenu ici pour survivre a un rafraichissement de la page.
   */
  premiersPas?: boolean;
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

  /**
   * Etapes pertinentes pour ce client : on saute les modules non souscrits. Aucune
   * en GPA : les premiers pas y remplacent la visite (Karim, 25/09/2026).
   */
  etapesGuide(): GuideEtape[] {
    if (this.offreGpa()) return [];
    return ETAPES_GUIDE.filter(e => this.pourCetteOffre(e) && this.pourCeProfil(e));
  }

  /** Etape ou tutoriel reserve a l'administrateur : meme regle que le bouton vise (vehicles.component.ts, isAdmin). */
  private pourCeProfil(e: { adminSeulement?: boolean }): boolean {
    if (!e.adminSeulement) return true;
    const u = this.auth.getCurrentUserSync();
    return !!u?.isCompanyAdmin || !!u?.isSystemAdmin;
  }

  /** Module requis present, module exclu (`sauf`) absent : vaut pour une etape comme pour un guide d'ecran. */
  private pourCetteOffre(e: { module?: HelpModule; sauf?: HelpModule }): boolean {
    return (!e.module || this.moduleAutorise(e.module))
      && (!e.sauf || !this.moduleAutorise(e.sauf));
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
    if (this.dejaProposee.has(cle)) return false;
    // GPA : plus de visite (Karim, 25/09/2026). Reste le conseil de premiere
    // connexion, dont le bouton lance les premiers pas.
    if (this.offreGpa()) return this.conseilAMontrer();
    return !this.termineeEnMemoire.has(cle) && !this.lireEtat().guideTermine;
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
      this.modifierEtat({ guideTermine: true, dateFin: new Date().toISOString() });
    }
  }

  /**
   * Permet au client de refaire la visite depuis l'Aide. Les guides d'ecran vus le
   * restent — sauf, en GPA, ceux des premiers pas, que ce bouton rejoue.
   */
  reinitialiserGuide(): void {
    const cle = this.cleUtilisateur();
    this.termineeEnMemoire.delete(cle);
    const modification: Partial<EtatAide> = { guideTermine: false, dateFin: undefined };
    if (this.offreGpa()) {
      PREMIERS_PAS_GPA.forEach(id => this.ecransVusEnMemoire.delete(cle + '|' + id));
      modification.ecransVus = (this.lireEtat().ecransVus || []).filter(id => !PREMIERS_PAS_GPA.includes(id));
    }
    // EN DEVELOPPEMENT SEULEMENT (isDevMode, faux dans l'image de production) : le
    // conseil de premiere connexion revient aussi, pour que Karim puisse le revoir
    // en local (25/09/2026). Chez un client, il reste affiche une seule fois.
    if (this.enDeveloppement()) {
      this.conseilVuEnMemoire.delete(cle);
      modification.conseilVu = false;
    }
    // PROVISOIRE, meme derogation que derogationPilote (dev + « Belive GPA ») : Karim
    // teste « comme si c'etait ma premiere connexion a chaque fois » (26/09/2026) —
    // les guides de TOUS les ecrans reviennent, et les premiers pas repartent de zero.
    if (this.derogationPilote()) {
      const prefixe = cle + '|';
      this.ecransVusEnMemoire.forEach(c => { if (c.startsWith(prefixe)) this.ecransVusEnMemoire.delete(c); });
      this.premiersPasEnMemoire.delete(cle);
      modification.ecransVus = [];
      modification.premiersPas = false;
    }
    this.modifierEtat(modification);
    this.ouvrirGuide();
  }

  // ------------------------------------------- Conseil de premiere connexion

  private conseilVuEnMemoire = new Set<string>();

  /**
   * Le conseil « renseignez tous les champs » precede la visite une seule fois, pour
   * un nouvel utilisateur (premiere connexion). Lu, il ne revient plus jamais —
   * pas meme si la visite est relancee depuis l'Aide (Karim, 25/09/2026).
   */
  conseilAMontrer(): boolean {
    return this.estNouvelUtilisateur()
      && !this.conseilVuEnMemoire.has(this.cleUtilisateur())
      && !this.lireEtat().conseilVu;
  }

  marquerConseilVu(): void {
    this.conseilVuEnMemoire.add(this.cleUtilisateur());
    this.modifierEtat({ conseilVu: true });
  }

  // ------------------------------------------------------- Guides des ecrans

  /** Memes filets que termineeEnMemoire, quand localStorage refuse d'ecrire. */
  private nouveauxEnMemoire = new Set<string>();
  private ecransVusEnMemoire = new Set<string>();

  /**
   * Nouvel utilisateur = premiere connexion (Karim, 24/09/2026). Le drapeau vient
   * de la reponse de /auth/login ; on le retient des qu'on le voit, car le
   * rafraichissement du jeton reecrit l'utilisateur sans lui. Un client deja
   * installe (last_login_at rempli en base) n'est donc jamais concerne.
   */
  estNouvelUtilisateur(): boolean {
    const cle = this.cleUtilisateur();
    if (this.auth.getCurrentUserSync()?.firstLogin && !this.nouveauxEnMemoire.has(cle)) {
      this.nouveauxEnMemoire.add(cle);
      this.modifierEtat({ nouvelUtilisateur: true });
    }
    return this.nouveauxEnMemoire.has(cle)
      || !!this.lireEtat().nouvelUtilisateur
      || this.derogationPilote();
  }

  /**
   * PROVISOIRE (Karim, 24/09/2026) : « applique-le provisoirement sur le compte de
   * la societe Belive GPA en local, meme si ce n'est pas un nouvel utilisateur ».
   * isDevMode() est faux dans la compilation de production (Dockerfile.prod,
   * --configuration=production) : la derogation ne peut pas s'activer sur un
   * serveur, ou une autre societe porterait-elle ce nom. A retirer quand le guide
   * sera generalise.
   */
  /** isDevMode(), isole pour que les tests puissent jouer le comportement de production. */
  enDeveloppement(): boolean {
    return isDevMode();
  }

  private derogationPilote(): boolean {
    return this.enDeveloppement() && this.auth.getCurrentUserSync()?.companyName === 'Belive GPA';
  }

  /**
   * Guide a presenter sur cette page, ou null. A chaque acces tant qu'il n'a ni
   * ete passe ni termine ; jamais tant que la visite de premiere connexion doit
   * encore etre proposee — elle passe avant, et les deux ne se superposent pas.
   */
  visiteEcranAProposer(url: string): VisiteEcran | null {
    // En premier : le drapeau de premiere connexion est retenu meme sur une page
    // sans guide (le tableau de bord, ou la connexion depose le client).
    const nouveau = this.estNouvelUtilisateur();
    const visite = VISITES_ECRANS.find(v => this.estSurSonEcran(v, url));
    if (!visite || !nouveau) return null;
    const pourLui = this.pourCeClient(visite);
    if (!pourLui || this.ecranVu(visite.id) || this.doitProposerLeGuide()) return null;
    return pourLui;
  }

  /** Le guide tel que ce client le recoit (etapes de son offre et de son profil), ou null s'il n'en a pas. */
  private pourCeClient(v: VisiteEcran): VisiteEcran | null {
    if (!this.guidePourCetteOffre(v)) return null;
    const etapes = v.etapes.filter(e => this.pourCetteOffre(e) && this.pourCeProfil(e));
    return etapes.length ? { ...v, etapes } : null;
  }

  /**
   * `module` suit les droits de l'utilisateur, comme pour une etape ; `sauf`
   * reconnait l'offre, donc l'ABONNEMENT. Avec les droits, un utilisateur d'une
   * societe GPS sans acces a la carte recevait le guide GPA (relecture du 24/09/2026).
   */
  private guidePourCetteOffre(v: VisiteEcran): boolean {
    return (!v.module || this.moduleAutorise(v.module))
      && (!v.sauf || v.sauf === 'general' || !this.permissions.abonnementComprend(v.sauf as ModuleKey))
      && this.pourCeProfil(v);
  }

  /**
   * Ecrans qui ont un guide pour ce client, par leur nom (« Vehicules »). Vide
   * s'il n'est pas un nouvel utilisateur : le centre d'aide cache alors son bouton.
   */
  ecransAvecGuide(): string[] {
    if (!this.estNouvelUtilisateur()) return [];
    return VISITES_ECRANS
      .filter(v => this.guidePourCetteOffre(v))
      .map(v => v.titre.replace(/^Écran\s+/, ''));
  }

  /** Ce chemin (parametres ignores) est-il celui de l'ecran de ce tutoriel, ou l'un de ses autres chemins ? */
  estSurSonEcran(v: VisiteEcran, url: string): boolean {
    const chemin = (url || '').split(/[?#]/)[0];
    return chemin === v.route || (v.autresRoutes || []).includes(chemin);
  }

  private ecranVu(id: string): boolean {
    return this.ecransVusEnMemoire.has(this.cleUtilisateur() + '|' + id)
      || (this.lireEtat().ecransVus || []).includes(id);
  }

  /** « Passer » ou « Terminer » sur le guide d'un ecran : il ne revient plus. */
  marquerEcranVu(id: string): void {
    this.ecransVusEnMemoire.add(this.cleUtilisateur() + '|' + id);
    const vus = this.lireEtat().ecransVus || [];
    if (!vus.includes(id)) this.modifierEtat({ ecransVus: [...vus, id] });
  }

  /** Bouton du centre d'aide : chaque guide d'ecran reviendra au prochain acces a son ecran. */
  reinitialiserEcrans(): void {
    const prefixe = this.cleUtilisateur() + '|';
    this.ecransVusEnMemoire.forEach(c => { if (c.startsWith(prefixe)) this.ecransVusEnMemoire.delete(c); });
    this.modifierEtat({ ecransVus: [] });
  }

  // ------------------------------------------------ Premiers pas (offre GPA)

  /**
   * Offre GPA (gestion de parc sans boitier) : l'abonnement ne comprend pas la carte
   * en direct. L'abonnement, pas les droits : un utilisateur d'une societe GPS sans
   * acces a la carte reste un client GPS.
   */
  offreGpa(): boolean {
    return !this.permissions.abonnementComprend('monitoring');
  }

  /** Meme filet que termineeEnMemoire ; la valeur ecrite en dernier fait foi. */
  private premiersPasEnMemoire = new Map<string, boolean>();

  /**
   * Ecrans des premiers pas que ce nouvel utilisateur peut faire, dans l'ordre, vus
   * ou non. Enjambe ceux qu'il n'a pas : Vehicules est reserve a l'administrateur.
   */
  premiersPasDuClient(): VisiteEcran[] {
    if (!this.offreGpa() || !this.estNouvelUtilisateur()) return [];
    return PREMIERS_PAS_GPA
      .map(id => VISITES_ECRANS.find(v => v.id === id))
      .filter((v): v is VisiteEcran => !!v && !!this.pourCeClient(v));
  }

  /**
   * « C'est compris, on commence » : rend l'ecran ou emmener le client — le premier
   * qu'il n'a pas encore fait —, ou null s'il n'en reste aucun.
   */
  commencerPremiersPas(): string | null {
    const premier = this.premiersPasDuClient().find(v => !this.ecranVu(v.id));
    if (premier) { this.retenirPremiersPas(true); }
    return premier ? premier.route : null;
  }

  /**
   * Guide d'un ecran termine : l'ecran suivant des premiers pas, ou null. Quand il
   * n'en reste plus, les premiers pas sont finis.
   */
  suiteDesPremiersPas(idTermine: string): string | null {
    if (!this.premiersPasEnCours() || !PREMIERS_PAS_GPA.includes(idTermine)) return null;
    const suivant = this.premiersPasDuClient().find(v => !this.ecranVu(v.id));
    if (!suivant) { this.arreterPremiersPas(); return null; }
    return suivant.route;
  }

  premiersPasEnCours(): boolean {
    const cle = this.cleUtilisateur();
    return this.premiersPasEnMemoire.has(cle) ? !!this.premiersPasEnMemoire.get(cle) : !!this.lireEtat().premiersPas;
  }

  /** « Passer » ou Echap : le client n'est plus emmene d'un ecran a l'autre. Chaque guide reste a son ecran. */
  arreterPremiersPas(): void {
    this.retenirPremiersPas(false);
  }

  private retenirPremiersPas(enCours: boolean): void {
    this.premiersPasEnMemoire.set(this.cleUtilisateur(), enCours);
    this.modifierEtat({ premiersPas: enCours });
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

  /**
   * Fusionne dans l'etat de l'utilisateur. Il le REMPLACAIT : terminer la visite
   * guidee aurait efface les guides d'ecran deja vus, et inversement.
   */
  private modifierEtat(modification: Partial<EtatAide>): void {
    try {
      const brut = localStorage.getItem(CLE_ETAT);
      const parTout = brut ? (JSON.parse(brut) as Record<string, EtatAide>) : {};
      const cle = this.cleUtilisateur();
      parTout[cle] = { ...(parTout[cle] || {}), ...modification };
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
