/**
 * Crédit IA MENSUEL de la société (demandes de Slim du 22/09/2026) : le compteur
 * « 12/20 ce mois » du scan devient une barre de progression d'un crédit en JETONS,
 * rechargé le 1er de chaque mois — puis « et bien sûr le quota inclut l'utilisation de
 * l'IA » : ce crédit couvre TOUTE l'IA de la société (scans de factures, assistant IA,
 * rapports IA, explications de consommation, récits d'accident). Un scan coûte en moyenne
 * 3 000 jetons (mesure TN, de 1 953 à 3 957), une réponse de l'assistant ≈ 2 400.
 *
 * Fonctions pures, partagées par la brique « Scanner une facture » (cinq écrans), l'assistant
 * IA, le rapport IA flotte et la fiche société de l'admin : mêmes seuils de couleur et mêmes
 * libellés partout. Le serveur (GET /api/ai-credit, AiCredit côté API) fait foi ; rien n'est
 * recalculé ici sinon la présentation.
 */

/** Crédit IA du mois d'une société, tel que le renvoie l'API. */
export interface CreditIa {
  /** false = IA désactivée pour la société (budget 0). */
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
  /** Jetons du mois par fonction (invoice_scan, assistant_chat, fleet_report…) ; absent
   *  d'une API antérieure. */
  byFeature?: Record<string, number>;
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
  const byFeature = lireVentilation(x['byFeature']);

  return {
    enabled: typeof x['enabled'] === 'boolean' ? (x['enabled'] as boolean) : budgetPos > 0,
    budgetTokens: budgetPos,
    usedTokens: usedPos,
    remainingTokens: Math.max(0, remaining),
    percentUsed: Math.min(100, Math.max(0, percent)),
    scansThisMonth: Math.max(0, entier(x['scansThisMonth']) ?? 0),
    estimatedScansLeft: Math.max(0, entier(x['estimatedScansLeft']) ?? Math.floor(Math.max(0, remaining) / JETONS_PAR_SCAN)),
    ...(resetsAt ? { resetsAt } : {}),
    ...(byFeature ? { byFeature } : {})
  };
}

/** Ventilation { fonction: jetons } : entiers positifs seulement, null si illisible. */
function lireVentilation(brut: unknown): Record<string, number> | null {
  if (!brut || typeof brut !== 'object' || Array.isArray(brut)) return null;
  const ventilation: Record<string, number> = {};
  for (const [cle, valeur] of Object.entries(brut as Record<string, unknown>)) {
    const n = entier(valeur);
    if (n !== null) ventilation[cle] = Math.max(0, n);
  }
  return ventilation;
}

/**
 * Crédit joint à une réponse d'IA réussie (« credit » ; « quota » pour le scan d'avant le
 * 22/09/2026), ou null si le serveur ne l'a pas renvoyé.
 */
export function creditDepuisReponse(res: unknown): CreditIa | null {
  if (!res || typeof res !== 'object') return null;
  const x = res as Record<string, unknown>;
  return lireCreditIa(x['credit'] ?? x['quota']);
}

/** Codes des refus de crédit renvoyés par l'API (403 / 429). */
export type CodeRefusCreditIa = 'AI_CREDIT_DISABLED' | 'AI_CREDIT_EXHAUSTED';

/** Refus d'un appel d'IA faute de crédit : message du serveur à afficher, crédit du moment. */
export interface RefusCreditIa {
  code: CodeRefusCreditIa;
  message: string;
  credit: CreditIa | null;
}

/**
 * Lit une erreur HTTP : refus de crédit IA (403 AI_CREDIT_DISABLED, 429 AI_CREDIT_EXHAUSTED)
 * ou null pour toute autre erreur. Le message est CELUI DU SERVEUR (« Crédit IA du mois
 * épuisé (100 %). Il se recharge le 01/10/2026 ; … ») ; le crédit joint permet de passer la
 * barre à 100 % et de griser les boutons d'une page ouverte avant l'épuisement.
 */
export function lireRefusCreditIa(err: unknown): RefusCreditIa | null {
  const corps = (err as { error?: unknown } | null)?.error;
  if (!corps || typeof corps !== 'object') return null;
  const x = corps as Record<string, unknown>;
  const code = x['code'];
  if (code !== 'AI_CREDIT_DISABLED' && code !== 'AI_CREDIT_EXHAUSTED') return null;
  const credit = lireCreditIa(x['credit']);
  const message = typeof x['message'] === 'string' && x['message'].trim() !== ''
    ? (x['message'] as string)
    : (code === 'AI_CREDIT_DISABLED' ? IA_DESACTIVEE : 'Crédit IA du mois épuisé (100 %).');
  return { code, message, credit };
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

/** Message affiché quand l'IA est coupée pour la société (même texte que le serveur). */
export const IA_DESACTIVEE = "Les fonctions d'IA ne sont pas activées pour votre société.";

/**
 * Le bouton se grise : IA fermée pour la société, ou crédit épuisé TANT QUE la date
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

/** Options de l'infobulle. */
export interface OptionsInfobulle {
  /** À côté du bouton de scan SEULEMENT : ajoute « environ N scans restants ». */
  scans?: boolean;
}

/**
 * Infobulle de la barre (et des boutons d'IA) : ce que veut dire la barre, et quand elle
 * repart. Texte GÉNÉRIQUE — le crédit couvre toute l'IA : « 42 % du crédit IA gratuit du
 * mois utilisé (25 200 / 60 000 jetons). Se recharge le 1er octobre. » ; « environ N scans
 * restants » n'est ajouté qu'à côté du bouton de scan (`options.scans`).
 * `maintenant` fourni et date de recharge passée : la barre montre un mois révolu — on le
 * dit, plutôt qu'annoncer une recharge « le 1er octobre » le 2 octobre.
 */
export function infobulleCredit(c: CreditIa, maintenant?: number, options: OptionsInfobulle = {}): string {
  if (!c.enabled) return IA_DESACTIVEE;
  const recharge = libelleRecharge(c.resetsAt);
  if (maintenant !== undefined && creditPerime(c, maintenant)) {
    return `Le crédit IA s'est rechargé ${recharge} : vous pouvez de nouveau ${options.scans ? 'scanner' : "utiliser l'IA"}.`;
  }
  if (c.remainingTokens <= 0) {
    return `Crédit IA du mois épuisé (100 %) — il se recharge ${recharge}. Votre administrateur peut l'augmenter.`;
  }
  const scans = options.scans ? `, ${libelleScansRestants(c)}` : '';
  return `${c.percentUsed} % du crédit IA gratuit du mois utilisé `
    + `(${formatJetons(c.usedTokens)} / ${formatJetons(c.budgetTokens)} jetons${scans}). `
    + `Se recharge ${recharge}.`;
}

/**
 * Message affiché À CÔTÉ d'un bouton d'IA grisé (assistant, rapport flotte) : pourquoi il
 * est grisé et jusqu'à quand. '' quand rien ne bloque.
 */
export function messageCreditBloque(c: CreditIa | null, maintenant: number): string {
  if (!c || !creditBloque(c, maintenant)) return '';
  if (!c.enabled) return IA_DESACTIVEE;
  return `Crédit IA du mois épuisé (100 %) — il se recharge ${libelleRecharge(c.resetsAt)}. Votre administrateur peut l'augmenter.`;
}

/** Libellés des fonctions d'IA, dans l'ordre de la ventilation (clés de l'API). */
export const LIBELLES_FONCTIONS_IA: Readonly<Record<string, string>> = {
  invoice_scan: 'Scans de factures',
  assistant_chat: 'Assistant IA',
  vehicle_compare: 'Comparaisons de véhicules',
  vehicle_report: 'Rapports diagnostic véhicule',
  fleet_report: 'Rapports IA flotte',
  fleet_report_ask: 'Questions sur le rapport flotte',
  consumption_explain: 'Explications de consommation',
  accident_narrative: "Récits d'accident"
};

/** Une ligne de la ventilation du mois (fiche société de l'admin). */
export interface LigneVentilation {
  cle: string;
  libelle: string;
  jetons: number;
  /** Part de la consommation du mois, 0-100 arrondi au plus proche. */
  part: number;
}

/**
 * Ventilation du mois par fonction, des plus gros consommateurs aux plus petits, fonctions
 * à 0 écartées. Une fonction inconnue de l'écran (API plus récente) garde sa clé comme
 * libellé plutôt que de disparaître.
 */
export function ventilationCredit(c: CreditIa | null): LigneVentilation[] {
  const v = c?.byFeature;
  if (!v) return [];
  const total = Object.values(v).reduce((s, n) => s + Math.max(0, n), 0);
  const ordre = Object.keys(LIBELLES_FONCTIONS_IA);
  return Object.entries(v)
    .filter(([, jetons]) => jetons > 0)
    .map(([cle, jetons]) => ({
      cle,
      libelle: LIBELLES_FONCTIONS_IA[cle] ?? cle,
      jetons,
      part: total > 0 ? Math.round((jetons * 100) / total) : 0
    }))
    .sort((a, b) => b.jetons - a.jetons || ordre.indexOf(a.cle) - ordre.indexOf(b.cle));
}
