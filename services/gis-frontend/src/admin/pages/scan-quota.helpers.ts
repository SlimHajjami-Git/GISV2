import { CreditIa, formatJetons, JETONS_PAR_SCAN } from '../../components/shared/credit-ia.helpers';

/**
 * Crédit IA MENSUEL de la société, en jetons (fiche société admin). Depuis le 22/09/2026
 * l'administrateur ne règle plus un nombre de scans mais un crédit de jetons, commun à
 * TOUTE l'IA de la société — scans, assistant, rapports IA (vide = défaut plateforme
 * 60 000 ≈ 20 scans, 0 = IA désactivée, maximum 10 000 000).
 *
 * Pièges réels, corrigés ici après la recette du 17/09/2026 (« ceci ne marche
 * pas » — aucune société n'avait jamais de limite enregistrée en prod), toujours valables :
 *
 * 1. Avec un input `type="number"`, `[(ngModel)]` fournit un NOMBRE (ou `null` quand
 *    le champ est vidé), pas une chaîne. L'ancien code faisait
 *    `(this.scanQuotaInput || '').trim()` → `TypeError: trim is not a function` dès
 *    que l'administrateur avait tapé une valeur. En zoneless, l'exception dans le
 *    gestionnaire de clic ne s'affichait nulle part : le bouton semblait mort.
 * 2. `0` est falsy : `(0 || '')` devenait `''`, donc « 0 = désactiver » était
 *    silencieusement transformé en « vide = défaut plateforme ».
 * 3. (22/09/2026) Des jetons se comptent en dizaines de milliers et l'écran les écrit
 *    « 60 000 ». Sur un `type="number"`, le navigateur ne transmet JAMAIS le texte tapé :
 *    « 150 000 » (Firefox) devient `null`, donc « défaut » enregistré sans erreur, et
 *    « 150.000 » (Chrome) devient 150 jetons. Le champ est donc `type="text"
 *    inputmode="numeric"` : la chaîne brute arrive ici, les espaces sont tolérées, et le
 *    point ou la virgule — séparateur de milliers ou décimale, on ne peut pas savoir —
 *    est REFUSÉ avec un message plutôt que deviné.
 *
 * Ce helper accepte indifféremment nombre, chaîne, `null` ou `undefined`.
 */
export const SCAN_CREDIT_MAX = 10_000_000;

/** Crédit par défaut d'une société sans réglage (même valeur que l'API). */
export const SCAN_CREDIT_DEFAULT = 60_000;

export type ScanCreditParse =
  | { ok: true; tokens: number | null }
  | { ok: false; message: string };

export const SCAN_CREDIT_INVALID_MESSAGE =
  'Valeur invalide (0 à 10 000 000 jetons, ou vide pour le défaut).';

/** « 150.000 » ou « 150,000 » : milliers ou décimales ? Refusé plutôt que deviné (piège 3). */
export const SCAN_CREDIT_SEPARATOR_MESSAGE =
  'Nombre entier de jetons, sans point ni virgule (ex. 150000 ou 150 000).';

/** `null` = défaut plateforme ; `0` = désactivé ; sinon un entier entre 1 et SCAN_CREDIT_MAX. */
export function parseScanCreditInput(raw: unknown): ScanCreditParse {
  if (raw === null || raw === undefined) return { ok: true, tokens: null };

  // Un NOMBRE (affectation par le code) n'a pas d'ambiguïté de séparateur : décimales
  // tronquées vers le bas, comme avant.
  if (typeof raw === 'number') {
    if (!Number.isFinite(raw)) return { ok: false, message: SCAN_CREDIT_INVALID_MESSAGE };
    const n = Math.floor(raw);
    return n < 0 || n > SCAN_CREDIT_MAX ? { ok: false, message: SCAN_CREDIT_INVALID_MESSAGE } : { ok: true, tokens: n };
  }

  // Espaces ordinaires, insécables et fines insécables : « 60 000 » collé depuis l'écran.
  const text = String(raw).replace(/[\s  ]/g, '');
  if (text === '') return { ok: true, tokens: null };

  // Point ou virgule : séparateur de milliers ou décimale, impossible à trancher — le
  // deviner a déjà enregistré 150 jetons pour « 150.000 » (piège 3).
  if (/[.,]/.test(text)) return { ok: false, message: SCAN_CREDIT_SEPARATOR_MESSAGE };

  // Chiffres seulement : ni signe, ni lettres, ni « Infinity » ou « 1e5 » que Number()
  // accepterait.
  if (!/^\d+$/.test(text)) return { ok: false, message: SCAN_CREDIT_INVALID_MESSAGE };

  const tokens = Number(text);
  if (tokens > SCAN_CREDIT_MAX) return { ok: false, message: SCAN_CREDIT_INVALID_MESSAGE };

  return { ok: true, tokens };
}

/** « ≈ 20 scans par mois » sous le champ : ce que représente la saisie, en scans moyens. */
export function equivalenceScans(tokens: number | null): string {
  const budget = tokens ?? SCAN_CREDIT_DEFAULT;
  if (budget <= 0) return 'IA désactivée pour cette société (scans, assistant, rapports IA)';
  const scans = Math.floor(budget / JETONS_PAR_SCAN);
  const libelle = scans < 1 ? "moins d'un scan" : `${scans} scan${scans > 1 ? 's' : ''}`;
  return `≈ ${libelle} par mois` + (tokens === null ? ` (défaut : ${formatJetons(SCAN_CREDIT_DEFAULT)} jetons)` : '');
}

/** Libellé sous le champ selon la saisie en cours (équivalence, ou motif du refus). */
export function aideSaisieCredit(raw: unknown): string {
  const parsed = parseScanCreditInput(raw);
  return parsed.ok ? equivalenceScans(parsed.tokens) : parsed.message;
}

/**
 * Crédit du mois tel que la fiche société (GET /api/admin/societes/{id}) le décrit, pour
 * la barre. `null` si l'API ne renvoie pas encore ces champs (pas de barre plutôt qu'une
 * barre fausse).
 */
export function creditDepuisFiche(s: unknown): CreditIa | null {
  if (!s || typeof s !== 'object') return null;
  const x = s as Record<string, unknown>;
  const budget = x['invoiceScanBudgetTokens'];
  const used = x['invoiceScanUsedTokens'];
  if (typeof budget !== 'number' || typeof used !== 'number') return null;

  const remaining = Math.max(0, budget - used);
  const percent = typeof x['invoiceScanPercentUsed'] === 'number'
    ? (x['invoiceScanPercentUsed'] as number)
    : (budget <= 0 ? 100 : Math.floor((used * 100) / budget));
  const resetsAt = typeof x['invoiceScanResetsAt'] === 'string' ? (x['invoiceScanResetsAt'] as string) : undefined;
  const byFeature = ventilationDepuisFiche(x['aiCreditByFeature']);

  return {
    enabled: budget > 0,
    budgetTokens: budget,
    usedTokens: used,
    remainingTokens: remaining,
    percentUsed: Math.min(100, Math.max(0, percent)),
    scansThisMonth: typeof x['invoiceScanUsedThisMonth'] === 'number' ? (x['invoiceScanUsedThisMonth'] as number) : 0,
    estimatedScansLeft: Math.floor(remaining / JETONS_PAR_SCAN),
    ...(resetsAt ? { resetsAt } : {}),
    ...(byFeature ? { byFeature } : {})
  };
}

/** Ventilation { fonction: jetons } de la fiche (GET) ou du réglage (PUT « byFeature »). */
function ventilationDepuisFiche(brut: unknown): Record<string, number> | null {
  if (!brut || typeof brut !== 'object' || Array.isArray(brut)) return null;
  const v: Record<string, number> = {};
  for (const [cle, jetons] of Object.entries(brut as Record<string, unknown>)) {
    if (typeof jetons === 'number' && Number.isFinite(jetons)) v[cle] = Math.max(0, Math.floor(jetons));
  }
  return v;
}

/**
 * Valeur initiale du champ : le crédit réglé, sinon — société encore sur l'ANCIEN quota en
 * scans — le budget converti (ex. 50 scans → 150 000), pour qu'un « Enregistrer » sans
 * retouche garde le même budget au lieu de retomber au défaut. Vide = défaut plateforme.
 */
export function saisieInitialeCredit(s: unknown): string {
  if (!s || typeof s !== 'object') return '';
  const x = s as Record<string, unknown>;
  const tokens = x['invoiceScanMonthlyTokens'];
  if (typeof tokens === 'number') return String(tokens);
  const legacy = x['invoiceScanMonthlyLimit'];
  const budget = x['invoiceScanBudgetTokens'];
  if (typeof legacy === 'number' && typeof budget === 'number') return String(budget);
  return '';
}
