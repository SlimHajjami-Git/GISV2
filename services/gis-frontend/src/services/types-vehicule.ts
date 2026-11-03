/**
 * Types de véhicule de la fiche (liste « Type ») et rattachement des types du catalogue.
 *
 * Le catalogue des modèles (vehicle_models) porte aussi des types anglais hors de cette
 * liste : van, hatchback, sedan, truck, pickup (93 modèles en production, 28/10/2026).
 * Choisir un tel modèle posait ce type tel quel : le champ Type paraissait vide, et la
 * liste des véhicules affichait « van » sans libellé ni icône. Ils sont ramenés au type
 * le plus proche de la liste.
 */
export const TYPES_VEHICULE = ['camion', 'citadine', 'suv', 'utilitaire', 'other'] as const;

const RATTACHEMENTS: Record<string, string> = {
  van: 'utilitaire',
  fourgon: 'utilitaire',
  camionnette: 'utilitaire',
  pickup: 'utilitaire',
  'pick-up': 'utilitaire',
  truck: 'camion',
  hatchback: 'citadine',
  sedan: 'citadine',
  berline: 'citadine',
};

/** Le type de la liste correspondant à `type` ; '' reste '' (non renseigné), l'inconnu devient « Autre ». */
export function typeDeLaListe(type: string | null | undefined): string {
  const t = (type ?? '').trim().toLowerCase();
  if (!t) return '';
  if ((TYPES_VEHICULE as readonly string[]).includes(t)) return t;
  return RATTACHEMENTS[t] ?? 'other';
}
