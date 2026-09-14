/**
 * Règles d'affichage partagées par le tableau de bord (offre GPA) et l'écran
 * Rapports. Fonctions pures, testées dans dashboard-gpa.helpers.spec.ts.
 */

/**
 * Compte « sans boîtier » (offre Calypso GPA) : décidé au niveau de la SOCIÉTÉ,
 * par son abonnement (moduleMonitoring à false), comme accident-report et
 * alert-emails. Jamais par le code d'abonnement ('gpa' est une offre GPS), ni par
 * le droit « Monitoring » de l'utilisateur : un employé d'une société équipée à
 * qui l'on retire ce droit basculait sur le tableau de bord sans GPS (constat du
 * 14/09/2026). Abonnement inconnu : pas GPA.
 */
export function estSocieteSansGps(features: { moduleMonitoring?: boolean } | null | undefined): boolean {
  return features?.moduleMonitoring === false;
}

/**
 * Périmètre affiché d'un rapport de flotte : « Tout le parc » pour un
 * administrateur, sinon ses véhicules affectés — le serveur borne déjà les
 * chiffres à cette portée (VehicleScope), le libellé ne doit pas dire le contraire.
 */
export function libellePerimetreParc(estAdmin: boolean, nbVehicules: number): string {
  return estAdmin ? 'Tout le parc' : `Mes véhicules (${nbVehicules})`;
}

/**
 * Mois incomplet de l'évolution des coûts : « en cours » s'il s'agit du mois
 * calendaire actuel, « incomplet » sinon (premier mois d'une période qui commence
 * le 15, dernier mois d'une période passée qui s'arrête avant sa fin).
 */
export function libelleMoisIncomplet(year: number, month: number, today: Date = new Date()): string {
  return year === today.getFullYear() && month === today.getMonth() + 1 ? 'en cours' : 'incomplet';
}

/**
 * Sous-titre de la tuile « Coût d'achats » (coût complet du parc) : la part de la
 * PÉRIODE affichée, définition de la tuile « Achats véhicule » livrée le 11/09/2026.
 */
export function sousTitreAchats(
  acquisition: { periodCost?: number | null } | null | undefined,
  formatMontant: (v: number) => string
): string {
  if (!acquisition) return '';
  const periode = Number(acquisition.periodCost) || 0;
  return `dont ${formatMontant(periode)} sur la période`;
}

/** Un refus de droit (403) n'est pas une absence de données. */
export function estRefusDeDroit(err: unknown): boolean {
  return (err as { status?: number } | null)?.status === 403;
}
