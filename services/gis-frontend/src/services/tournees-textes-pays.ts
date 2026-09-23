/**
 * Textes du module Tournees qui dependent du pays du client (23/09/2026).
 *
 * Meme regle que sinistres-textes-pays.ts, dans l'autre sens : le formulaire
 * proposait « Ex: Livraison Lyon - Marseille » a tous les clients, alors que le
 * GPS — et donc les tournees — vise d'abord le marche tunisien. La cle reste la
 * DEVISE du compte : un compte en euros garde l'exemple francais, tout autre
 * compte voit un exemple tunisien. Demande de Karim (« fais le »).
 */
export interface TextesTournees {
  /** Exemple affiche dans le champ « Nom » d'une tournee. */
  exempleNom: string;
}

export const TEXTES_TOURNEES_TUNISIE: TextesTournees = {
  exempleNom: 'Ex: Livraison Tunis - Sfax'
};

/** Texte d'origine du formulaire, conserve pour les comptes en euros. */
export const TEXTES_TOURNEES_FRANCE: TextesTournees = {
  exempleNom: 'Ex: Livraison Lyon - Marseille'
};

/** Jeu de textes pour la devise du compte. Inconnue ou absente : tunisien. */
export function textesTournees(devise: string | null | undefined): TextesTournees {
  return devise === 'EUR' ? TEXTES_TOURNEES_FRANCE : TEXTES_TOURNEES_TUNISIE;
}
