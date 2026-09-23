/**
 * Textes du module Sinistres qui dependent du pays du client (23/09/2026).
 *
 * Les ecrans du module affichaient des libelles et des exemples tunisiens
 * (« Gouvernorat », « Tunis, Sfax… », « +216 … », « STAR, COMAR, AMI… ») a tous
 * les clients, y compris francais. La cle qui distingue le pays est la DEVISE
 * du compte, deja lue par ces ecrans pour afficher « Cout estime (EUR) » : un
 * compte en euros voit les textes francais, tout autre compte garde les textes
 * d'origine — aucun changement pour les clients tunisiens, marocains ou
 * algeriens. Le pays de la societe (`societes.country`) n'est pas utilise :
 * il n'arrive pas au front, et il est faux sur au moins une societe.
 *
 * Les valeurs, c'est Karim qui les a choisies (« Departement » / « Ville »,
 * exemples « Rhone, Bouches-du-Rhone… »).
 */
export interface TextesSinistres {
  /** Division administrative : « Gouvernorat » en Tunisie, « Departement » en France. */
  division: string;
  /** Localite : « Commune » en Tunisie, « Ville » en France. */
  localite: string;
  exempleDivision: string;
  exempleLocalite: string;
  exempleTelephone: string;
  exemplePlaque: string;
  exempleAssureurs: string;
  /**
   * Synthese du rapport : « … le 12/09/2026[avantLocalite]Lyon[apresLocalite][avantDivision]Rhone[apresDivision]. »
   * En francais on evite « dans le departement du/des/de la » — l'article
   * change selon le departement — au profit de « a Lyon (Rhone) ».
   */
  synthAvantLocalite: string;
  synthApresLocalite: string;
  synthAvantDivision: string;
  synthApresDivision: string;
}

/** Textes d'origine, inchanges : ce que voient aujourd'hui tous les clients hors euro. */
export const TEXTES_SINISTRES_TUNISIE: TextesSinistres = {
  division: 'Gouvernorat',
  localite: 'Commune',
  exempleDivision: 'Tunis, Sfax, ...',
  exempleLocalite: 'Le Bardo, ...',
  exempleTelephone: '+216 ...',
  exemplePlaque: '123 TU 4567',
  exempleAssureurs: 'STAR, COMAR, AMI...',
  synthAvantLocalite: ', sur la commune de ',
  synthApresLocalite: '',
  synthAvantDivision: ', dans le gouvernorat de ',
  synthApresDivision: ''
};

export const TEXTES_SINISTRES_FRANCE: TextesSinistres = {
  division: 'Département',
  localite: 'Ville',
  exempleDivision: 'Rhône, Bouches-du-Rhône, ...',
  exempleLocalite: 'Lyon, Marseille, ...',
  exempleTelephone: '+33 6 ...',
  exemplePlaque: 'AB-123-CD',
  exempleAssureurs: 'AXA, MAIF, Allianz...',
  synthAvantLocalite: ', à ',
  synthApresLocalite: '',
  synthAvantDivision: ' (',
  synthApresDivision: ')'
};

/** Jeu de textes pour la devise du compte. Inconnue ou absente : textes d'origine. */
export function textesSinistres(devise: string | null | undefined): TextesSinistres {
  return devise === 'EUR' ? TEXTES_SINISTRES_FRANCE : TEXTES_SINISTRES_TUNISIE;
}
