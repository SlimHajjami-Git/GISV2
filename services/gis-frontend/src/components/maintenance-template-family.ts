/**
 * « Même type d'entretien » entre deux modèles aux noms différents (campagne de tests GPA du
 * 06/10/2026, erreur n°17) : « Vidange + filtre à huile » et « Vidange Moteur » sur un même
 * véhicule faisaient deux échéances, et la vidange faite sur l'une laissait l'autre « en retard ».
 *
 * Même règle que le serveur (MaintenanceTemplateFamily.cs), qui refuse sans confirmation : le
 * premier mot significatif du nom, sans accents ni majuscules ni pluriel, en sautant les mots
 * génériques (« changement », « kit »…) et les nombres ; « filtre » garde le mot qui le suit.
 */
const MOTS_SANS_SENS = new Set([
  'changement', 'changment', 'remplacement', 'kit', 'entretien',
  'de', 'du', 'des', 'd', 'la', 'le', 'les', 'l', 'a', 'et', 'en', 'au', 'aux', 'pour'
]);

function mots(nom: string | null | undefined): string[] {
  if (!nom) return [];
  return nom.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase()
    .split(/[^\p{L}\p{N}]+/u)
    .filter(m => m.length > 0)
    .map(m => (m.length > 3 && m.endsWith('s') ? m.slice(0, -1) : m));
}

/** Famille du modèle, ou chaîne vide si le nom n'en donne aucune. */
export function familleEntretien(nom: string | null | undefined): string {
  const m = mots(nom).filter(x => !MOTS_SANS_SENS.has(x) && !/^\d+$/.test(x));
  if (m.length === 0) return '';
  return m[0] === 'filtre' && m.length > 1 ? `filtre ${m[1]}` : m[0];
}

/**
 * Phrases à confirmer avant d'affecter `choisis` à un véhicule qui porte déjà `dejaAffectes` :
 * un modèle choisi du même type qu'un entretien déjà affecté, ou que l'un des autres choisis.
 */
export function entretiensDuMemeType(dejaAffectes: string[], choisis: string[]): string[] {
  const phrases: string[] = [];
  choisis.forEach((nom, i) => {
    const f = familleEntretien(nom);
    if (!f) return;
    const existant = dejaAffectes.find(n => familleEntretien(n) === f);
    if (existant) {
      phrases.push(`« ${nom} » : ce véhicule a déjà « ${existant} », du même type.`);
      return;
    }
    const autre = choisis.slice(0, i).find(n => familleEntretien(n) === f);
    if (autre) phrases.push(`« ${autre} » et « ${nom} » sont du même type.`);
  });
  return phrases;
}
