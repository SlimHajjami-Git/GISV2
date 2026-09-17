/**
 * Analyse de la saisie du quota mensuel de scans de factures IA (fiche société admin).
 *
 * Deux pièges réels, corrigés ici après la recette du 17/09/2026 (« ceci ne marche
 * pas » — aucune société n'avait jamais de limite enregistrée en prod) :
 *
 * 1. L'input est `type="number"` : `[(ngModel)]` fournit un NOMBRE (ou `null` quand
 *    le champ est vidé), pas une chaîne. L'ancien code faisait
 *    `(this.scanQuotaInput || '').trim()` → `TypeError: trim is not a function` dès
 *    que l'administrateur avait tapé une valeur. En zoneless, l'exception dans le
 *    gestionnaire de clic ne s'affichait nulle part : le bouton semblait mort.
 * 2. `0` est falsy : `(0 || '')` devenait `''`, donc « 0 = désactiver » était
 *    silencieusement transformé en « vide = défaut plateforme ».
 *
 * Ce helper accepte indifféremment nombre, chaîne, `null` ou `undefined`.
 */
export const SCAN_QUOTA_MAX = 100_000;

export type ScanQuotaParse =
  | { ok: true; limit: number | null }
  | { ok: false; message: string };

export const SCAN_QUOTA_INVALID_MESSAGE =
  `Valeur invalide (0 à ${SCAN_QUOTA_MAX}, ou vide pour le défaut).`;

/** `null` = défaut plateforme ; `0` = désactivé ; sinon un entier entre 1 et SCAN_QUOTA_MAX. */
export function parseScanQuotaInput(raw: unknown): ScanQuotaParse {
  if (raw === null || raw === undefined) return { ok: true, limit: null };

  const text = String(raw).trim();
  if (text === '') return { ok: true, limit: null };

  // Number('') vaut 0 et Number(' 12 ') vaut 12 : on a déjà écarté le vide, et on
  // refuse tout ce qui n'est pas un nombre fini (lettres, 'Infinity', '1e999'…).
  const value = Number(text);
  if (!Number.isFinite(value)) return { ok: false, message: SCAN_QUOTA_INVALID_MESSAGE };

  const limit = Math.floor(value);
  if (limit < 0 || limit > SCAN_QUOTA_MAX) return { ok: false, message: SCAN_QUOTA_INVALID_MESSAGE };

  return { ok: true, limit };
}
