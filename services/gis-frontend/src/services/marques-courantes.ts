/**
 * Marques les plus courantes dans les flottes de nos clients (Tunisie, Algérie,
 * France) : en tête de la liste « Marque » de la fiche véhicule, par ordre
 * alphabétique, avant toutes les autres, elles aussi par ordre alphabétique
 * (Karim, 25/09/2026). Le catalogue compte plus de cent marques (migration 053).
 */
export const MARQUES_COURANTES: readonly string[] = [
  'AUDI', 'BMW', 'CHERY', 'CHEVROLET', 'CITROËN', 'DACIA', 'FIAT', 'FORD', 'GEELY',
  'HYUNDAI', 'ISUZU', 'IVECO', 'KIA', 'MAN', 'MERCEDES-BENZ', 'MG', 'MITSUBISHI',
  'NISSAN', 'OPEL', 'PEUGEOT', 'RENAULT', 'SCANIA', 'SEAT', 'SKODA', 'SUZUKI',
  'TOYOTA', 'VOLKSWAGEN', 'VOLVO'
];

/** Nom comparable : sans casse, sans accents, sans espaces ni ponctuation (« Citroën » = « CITROEN »). */
export function cleMarque(nom: string | null | undefined): string {
  return (nom || '').toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/[^a-z0-9]/g, '');
}

const COURANTES = new Set(MARQUES_COURANTES.map(cleMarque));

/** Les marques courantes d'abord, puis les autres ; chaque groupe par ordre alphabétique. */
export function rangerMarques<T extends { name: string }>(marques: readonly T[]): { courantes: T[]; autres: T[] } {
  const alpha = (a: T, b: T) => a.name.localeCompare(b.name, 'fr', { sensitivity: 'base', numeric: true });
  const triees = [...marques].sort(alpha);
  return {
    courantes: triees.filter(m => COURANTES.has(cleMarque(m.name))),
    autres: triees.filter(m => !COURANTES.has(cleMarque(m.name))),
  };
}
