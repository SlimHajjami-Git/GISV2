/**
 * Remise à zéro des données d'une société (fiche société admin) — textes de l'aperçu
 * et du résultat qui parlent des comptes chauffeurs. Fonctions pures, testées dans
 * company-reset.helpers.spec.ts.
 *
 * Constat (relecture du 21/09/2026, R7c) : l'écran promettait « Conservé : … ses
 * utilisateurs » alors que la remise à zéro supprime les fiches chauffeurs et désactive
 * les comptes chauffeurs qui leur étaient reliés (ils ne se connectent plus à
 * l'application). Le serveur rend désormais driverAccountsClosed : comptes qui seront
 * désactivés (aperçu, dryRun: true) ou qui l'ont été (exécution).
 */

export interface ResetDriverAccountsInfo {
  totalRows: number;
  /** Absent d'un serveur antérieur au 21/09/2026 : lu comme 0. */
  driverAccountsClosed?: number | null;
}

/** Nombre rendu par le serveur, 0 s'il est absent ou invalide. */
function reportedCount(r: ResetDriverAccountsInfo | null | undefined): number {
  const n = Number(r?.driverAccountsClosed);
  return Number.isInteger(n) && n > 0 ? n : 0;
}

/**
 * Comptes chauffeurs que l'aperçu annonce. Un aperçu sans aucune ligne à supprimer ne
 * compte pas : le bouton « Supprimer définitivement » y est verrouillé, rien ne sera
 * désactivé et l'annoncer serait faux.
 */
export function driverAccountsToClose(preview: ResetDriverAccountsInfo | null | undefined): number {
  return preview && preview.totalRows > 0 ? reportedCount(preview) : 0;
}

/**
 * Fragment « ses utilisateurs et leurs rôles » de la phrase « Conservé : … ». Tant que
 * l'aperçu n'est pas arrivé, on ne sait pas combien de comptes chauffeurs tomberont :
 * la phrase les exclut d'emblée plutôt que de promettre de les garder.
 */
export function keptUsersText(preview: ResetDriverAccountsInfo | null | undefined): string {
  if (!preview) return 'ses utilisateurs et leurs rôles (hors comptes chauffeurs, désactivés avec leurs fiches)';
  const n = driverAccountsToClose(preview);
  if (n === 0) return 'ses utilisateurs et leurs rôles';
  return n === 1
    ? 'ses utilisateurs et leurs rôles, sauf 1 compte chauffeur qui sera désactivé'
    : `ses utilisateurs et leurs rôles, sauf ${n} comptes chauffeurs qui seront désactivés`;
}

/** Avertissement de l'aperçu ; vide s'il n'y a aucun compte chauffeur à désactiver. */
export function driverAccountsPreviewText(preview: ResetDriverAccountsInfo | null | undefined): string {
  const n = driverAccountsToClose(preview);
  if (n === 0) return '';
  return n === 1
    ? '1 compte chauffeur sera désactivé : sa fiche part avec les données, il ne pourra plus se connecter à l\'application.'
    : `${n} comptes chauffeurs seront désactivés : leurs fiches partent avec les données, ils ne pourront plus se connecter à l'application.`;
}

/**
 * Bilan après exécution ; vide s'il n'y a eu aucun compte chauffeur désactivé. Le
 * nombre rendu fait foi même si aucune ligne n'a été supprimée (données parties entre
 * l'aperçu et l'exécution) : les comptes, eux, ont bien été désactivés.
 */
export function driverAccountsResultText(result: ResetDriverAccountsInfo | null | undefined): string {
  const n = reportedCount(result);
  if (n === 0) return '';
  return n === 1
    ? '1 compte chauffeur désactivé : sa fiche est partie avec les données.'
    : `${n} comptes chauffeurs désactivés : leurs fiches sont parties avec les données.`;
}
