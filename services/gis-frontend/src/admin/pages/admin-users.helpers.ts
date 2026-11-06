/**
 * Écran Users de la console admin : la liste est triée par « Dernière connexion », de la
 * plus récente à la plus ancienne (demande de Karim, 05/11/2026). Les comptes qui ne se
 * sont jamais connectés (« Jamais ») viennent en dernier ; à égalité, ordre alphabétique.
 */
export function trierParDerniereConnexion<T extends { name?: string; lastLoginAt?: Date | string | null }>(users: T[]): T[] {
  const instant = (u: T): number => {
    const t = u.lastLoginAt ? new Date(u.lastLoginAt).getTime() : NaN;
    return Number.isNaN(t) ? -Infinity : t;
  };
  return [...users].sort((a, b) => {
    const ecart = instant(b) - instant(a);
    if (ecart > 0) return 1;
    if (ecart < 0) return -1;
    return (a.name || '').localeCompare(b.name || '', 'fr', { sensitivity: 'base' });
  });
}
