/**
 * Console admin (Monitoring), demandes de Karim du 05/11/2026 :
 * - les véhicules de toutes les sociétés arrivent DÉCOCHÉS (la carte part vide) ;
 * - les sociétés arrivent REPLIÉES (seule la ligne de la société est visible).
 *
 * Seuls les véhicules et sociétés jamais vus sont concernés : au rafraîchissement de la
 * liste, ce que l'administrateur a coché ou déplié le reste ; un nouveau venu arrive
 * décoché ou replié.
 */
export function decocherNouveauxVehicules(
  vehicules: { id?: number | string | null }[],
  dejaVus: Set<string>,
  masques: Set<string>
): void {
  for (const v of vehicules) {
    const id = v?.id?.toString();
    if (!id || dejaVus.has(id)) continue;
    dejaVus.add(id);
    masques.add(id);
  }
}

/** Les sociétés jamais vues arrivent repliées ; celles que l'administrateur a dépliées le restent. */
export function replierNouvellesSocietes(
  cles: string[],
  dejaVues: Set<string>,
  repliees: Set<string>
): void {
  for (const cle of cles) {
    if (!cle || dejaVues.has(cle)) continue;
    dejaVues.add(cle);
    repliees.add(cle);
  }
}
