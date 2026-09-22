/**
 * Crédit IA MENSUEL du scan de factures (demande de Slim du 22/09/2026) : le compteur
 * « 12/20 ce mois » devient une barre de progression d'un crédit en JETONS, rechargé le
 * 1er de chaque mois. Un scan coûte en moyenne 3 000 jetons (mesure TN, de 1 953 à 3 957 :
 * une photo floue déclenche une seconde lecture) — compter des scans ne disait rien de ce
 * que la société consomme réellement.
 *
 * Fonctions pures, partagées par la brique « Scanner une facture » (cinq écrans) et par
 * la fiche société de l'admin : mêmes seuils de couleur et mêmes libellés partout.
 * Le serveur (GET /api/costs/scan-quota, InvoiceScanCredit côté API) fait foi ; rien
 * n'est recalculé ici sinon la présentation.
 */

/** Crédit IA du mois d'une société, tel que le renvoie l'API. */
export interface CreditIa {
  /** false = fonction désactivée pour la société (budget 0). */
  enabled: boolean;
  budgetTokens: number;
  usedTokens: number;
  remainingTokens: number;
  /** 0-100, arrondi vers le bas par le serveur : 100 si et seulement s'il ne reste rien. */
  percentUsed: number;
  scansThisMonth: number;
  /** Jetons restants / 3 000, arrondi vers le bas. */
  estimatedScansLeft: number;
  /** Prochaine recharge (ISO, 1er du mois suivant à minuit UTC). */
  resetsAt?: string;
}

/** Coût moyen d'un scan, pour les équivalences « ≈ N scans » (même valeur que l'API). */
export const JETONS_PAR_SCAN = 3000;

/** Niveau de couleur de la barre : vert < 70 %, orange 70-89 %, rouge ≥ 90 %. */
export type NiveauCredit = 'ok' | 'alerte' | 'critique';

function entier(v: unknown): number | null {
  const n = typeof v === 'string' && v.trim() !== '' ? Number(v) : v;
  return typeof n === 'number' && Number.isFinite(n) ? Math.floor(n) : null;
}

/**
 * Lit la réponse du serveur. `null` quand elle n'a pas la forme du crédit — en
 * particulier l'ANCIENNE réponse { used, limit, remaining } d'une API pas encore
 * redéployée : sans barre, le bouton reste utilisable et le serveur tranche, plutôt
 * que d'afficher « NaN % » ou de griser le bouton à tort.
 */
export function lireCreditIa(brut: unknown): CreditIa | null {
  if (!brut || typeof brut !== 'object') return null;
  const x = brut as Record<string, unknown>;
  const budget = entier(x['budgetTokens']);
  const used = entier(x['usedTokens']);
  if (budget === null || used === null) return null;

  const budgetPos = Math.max(0, budget);
  const usedPos = Math.max(0, used);
  const remaining = entier(x['remainingTokens']) ?? Math.max(0, budgetPos - usedPos);
  const percent = entier(x['percentUsed'])
    ?? (budgetPos === 0 ? 100 : Math.floor((usedPos * 100) / budgetPos));
  const resetsAt = typeof x['resetsAt'] === 'string' ? (x['resetsAt'] as string) : undefined;

  return {
    enabled: typeof x['enabled'] === 'boolean' ? (x['enabled'] as boolean) : budgetPos > 0,
    budgetTokens: budgetPos,
    usedTokens: usedPos,
    remainingTokens: Math.max(0, remaining),
    percentUsed: Math.min(100, Math.max(0, percent)),
    scansThisMonth: Math.max(0, entier(x['scansThisMonth']) ?? 0),
    estimatedScansLeft: Math.max(0, entier(x['estimatedScansLeft']) ?? Math.floor(Math.max(0, remaining) / JETONS_PAR_SCAN)),
    ...(resetsAt ? { resetsAt } : {})
  };
}

/** Plus rien à consommer ce mois-ci, ou fonction fermée. */
export function creditEpuise(c: CreditIa): boolean {
  return !c.enabled || c.remainingTokens <= 0;
}

/**
 * La date de recharge est passée : ce crédit décrit un mois RÉVOLU. Le serveur l'a déjà
 * remis à zéro, mais l'écran — resté ouvert — ne le sait pas tant qu'il ne l'a pas relu.
 */
export function creditPerime(c: CreditIa, maintenant: number): boolean {
  if (!c.resetsAt) return false;
  const recharge = Date.parse(c.resetsAt);
  return Number.isFinite(recharge) && maintenant >= recharge;
}

/**
 * Le bouton se grise : fonction fermée pour la société, ou crédit épuisé TANT QUE la date
 * de recharge n'est pas passée. Sans cette réserve, un écran ouvert le 30 restait grisé le
 * 1er après la recharge : aucun clic possible, donc aucun appel pour relire le crédit, et
 * l'utilisateur devait recharger la page. Une fonction fermée, elle, ne rouvre pas au
 * changement de mois (c'est un réglage de l'admin, pas un compteur).
 */
export function creditBloque(c: CreditIa, maintenant: number): boolean {
  if (!c.enabled) return true;
  return c.remainingTokens <= 0 && !creditPerime(c, maintenant);
}

/** Plus long délai qu'accepte setTimeout (≈ 24,8 jours) : au-delà, il se déclenche AUSSITÔT. */
export const DELAI_MAX_MINUTERIE = 2_147_483_647;

/** Marge après minuit UTC avant de relire : l'horloge du poste peut avancer sur celle du serveur. */
export const MARGE_APRES_RECHARGE_MS = 30_000;

/**
 * Délai avant de relire le crédit à sa date de recharge (barre remise à 0 % et bouton
 * dégrisé sans recharger la page), ou `null` s'il n'y a rien à attendre : pas de crédit,
 * fonction fermée, date absente ou déjà passée. Borné à DELAI_MAX_MINUTERIE : un crédit
 * épuisé le 3 d'un mois de 31 jours aurait sinon déclenché une relecture immédiate, en
 * boucle.
 */
export function delaiAvantRecharge(c: CreditIa | null, maintenant: number): number | null {
  if (!c || !c.enabled || !c.resetsAt) return null;
  const recharge = Date.parse(c.resetsAt);
  if (!Number.isFinite(recharge) || maintenant >= recharge) return null;
  return Math.min(recharge - maintenant + MARGE_APRES_RECHARGE_MS, DELAI_MAX_MINUTERIE);
}

export function niveauCredit(pourcentage: number): NiveauCredit {
  if (pourcentage >= 90) return 'critique';
  if (pourcentage >= 70) return 'alerte';
  return 'ok';
}

/** « 25 200 » : milliers séparés par une espace, indépendamment de la locale du navigateur. */
export function formatJetons(n: number): string {
  const v = Math.max(0, Math.floor(Number.isFinite(n) ? n : 0));
  return String(v).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
}

/** « le 1er octobre » : jour de la prochaine recharge, lu en UTC comme le serveur la fixe. */
export function libelleRecharge(resetsAt?: string): string {
  if (!resetsAt) return 'le 1er du mois prochain';
  const d = new Date(resetsAt);
  if (isNaN(d.getTime())) return 'le 1er du mois prochain';
  const mois = d.toLocaleDateString('fr-FR', { month: 'long', timeZone: 'UTC' });
  return 'le ' + (d.getUTCDate() === 1 ? '1er' : String(d.getUTCDate())) + ' ' + mois;
}

/** « environ 11 scans restants » / « environ 1 scan restant » / « moins d'un scan restant ». */
export function libelleScansRestants(c: CreditIa): string {
  const n = c.estimatedScansLeft;
  if (n <= 0) return c.remainingTokens > 0 ? "moins d'un scan restant" : 'aucun scan restant';
  return `environ ${n} scan${n > 1 ? 's' : ''} restant${n > 1 ? 's' : ''}`;
}

/**
 * Infobulle de la barre (et du bouton) : ce que veut dire la barre, et quand elle repart.
 * `maintenant` fourni et date de recharge passée : la barre montre un mois révolu — on le
 * dit, plutôt qu'annoncer une recharge « le 1er octobre » le 2 octobre.
 */
export function infobulleCredit(c: CreditIa, maintenant?: number): string {
  if (!c.enabled) return "Le scan de factures IA n'est pas activé pour votre société.";
  const recharge = libelleRecharge(c.resetsAt);
  if (maintenant !== undefined && creditPerime(c, maintenant)) {
    return `Le crédit IA s'est rechargé ${recharge} : vous pouvez de nouveau scanner.`;
  }
  if (c.remainingTokens <= 0) {
    return `Crédit IA du mois épuisé (100 %) — il se recharge ${recharge}. Votre administrateur peut l'augmenter.`;
  }
  return `${c.percentUsed} % du crédit IA gratuit du mois utilisé `
    + `(${formatJetons(c.usedTokens)} / ${formatJetons(c.budgetTokens)} jetons, ${libelleScansRestants(c)}). `
    + `Se recharge ${recharge}.`;
}
