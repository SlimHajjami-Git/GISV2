import { ModuleKey } from './permission.service';

/**
 * Aide integree Calypso - modele de contenu.
 *
 * Tout le texte vu par le client vit dans help-content.ts : un seul fichier a
 * corriger quand un ecran change, jamais du texte eparpille dans les composants.
 *
 * `module` sert au filtrage : un client qui n'a pas souscrit les geofences ne
 * doit pas trouver d'article "Creer une zone" dans la recherche. On reutilise
 * les memes cles que le menu (PermissionService.hasModuleAccess), plus
 * 'general' pour ce qui est visible par tout le monde (connexion, profil...).
 */
export type HelpModule = ModuleKey | 'general';

/**
 * Capsule video d'un article. `url` reste vide tant que la video n'est pas
 * tournee : l'article s'affiche alors normalement, sans lecteur (voir
 * HelpService.aUneVideo). Rien a re-developper le jour ou les videos arrivent.
 */
export interface HelpVideo {
  titre: string;
  url: string;
  dureeSecondes?: number;
}

/**
 * Capture d'ecran illustrant un article.
 *
 * Le fichier vit dans `src/assets/aide/`. Tant qu'il n'existe pas, l'image
 * est simplement masquee : l'article reste lisible, rien ne casse (voir le
 * gestionnaire d'erreur dans HelpCenterComponent).
 */
export interface HelpCapture {
  /** Nom du fichier, par exemple 'ajouter-vehicule.png'. */
  fichier: string;
  /** Legende : ce que le lecteur doit regarder sur l'image. */
  legende: string;
}

export interface HelpArticle {
  /** Identifiant stable : sert d'ancre dans l'URL (/aide?article=ajouter-vehicule). */
  id: string;
  titre: string;
  module: HelpModule;
  /**
   * Mots que le client tapera sans forcement connaitre notre vocabulaire :
   * "immatriculation" pour matricule, "carte" pour suivi, "facture" pour depense.
   * C'est ce qui fait la difference entre une recherche qui trouve et une qui echoue.
   */
  motsCles: string[];
  resume: string;
  /** Marche a suivre numerotee, affichee en liste ordonnee. */
  etapes?: string[];
  /** Explications libres, un paragraphe par entree. */
  paragraphes?: string[];
  /** Encadre "A retenir" en fin d'article. */
  aRetenir?: string;
  /** Captures d'ecran, affichees entre la marche a suivre et les explications. */
  captures?: HelpCapture[];
  video?: HelpVideo;
}

/** Regroupement affiche dans le sommaire de l'ecran Aide. */
export interface HelpSection {
  titre: string;
  articles: HelpArticle[];
}

/** Une bulle de la visite guidee de premiere connexion. */
export interface GuideEtape {
  id: string;
  titre: string;
  texte: string;
  /**
   * Valeur de l'attribut data-guide pose sur l'element vise dans l'application.
   * Si l'element est absent (module non souscrit, ecran non ouvert), l'etape est
   * sautee automatiquement : la visite ne doit jamais pointer dans le vide.
   */
  cible: string;
  /** Module requis pour que l'etape ait un sens. */
  module?: HelpModule;
  /** Page sur laquelle l'etape doit etre jouee. */
  route?: string;
}
