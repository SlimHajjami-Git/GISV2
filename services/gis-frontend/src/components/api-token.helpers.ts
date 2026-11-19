/**
 * Jeton d'API à usage unique (migration 064) : logique pure de la section « Accès API » du profil.
 * Hors TestBed — le composant ne fait que l'afficher.
 */

export interface ApiTokenRow {
  id: number;
  tokenPrefix: string;
  createdAt: string;
  expiresAt: string;
  usedAt: string | null;
  status: 'pending' | 'used' | 'expired' | string;
}

/** Libellé français du statut d'un jeton de la liste. */
export function apiTokenStatusLabel(status: string): string {
  switch (status) {
    case 'pending': return 'En attente';
    case 'used': return 'Utilisé';
    case 'expired': return 'Expiré';
    default: return status;
  }
}

/** Secondes restantes avant l'expiration, jamais négatives. */
export function secondsUntil(expiresAt: string | Date, nowMs: number): number {
  const end = new Date(expiresAt).getTime();
  if (Number.isNaN(end)) return 0;
  return Math.max(0, Math.floor((end - nowMs) / 1000));
}

/** « 14 min 05 s », « 42 s » ou « expiré ». */
export function formatCountdown(seconds: number): string {
  if (seconds <= 0) return 'expiré';
  const min = Math.floor(seconds / 60);
  const sec = seconds % 60;
  return min > 0 ? `${min} min ${String(sec).padStart(2, '0')} s` : `${sec} s`;
}

/** Date au format AAAA-MM-JJ (le rapport d'arrêts raisonne au jour près). */
export function isoDay(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Exemple d'appel prêt à copier : arrêts de TOUS les véhicules du compte sur les 7 derniers jours.
 * L'adresse est celle du site ouvert, pas une valeur codée en dur (TN, DZ ou un poste de développement).
 */
export function curlExample(token: string, origin: string, today: Date = new Date()): string {
  const start = new Date(today);
  start.setDate(start.getDate() - 7);
  return `curl "${origin.replace(/\/+$/, '')}/api/reports/stops?startDate=${isoDay(start)}&endDate=${isoDay(today)}" \\\n  -H "X-Api-Token: ${token}"`;
}

/** Message affiché pour une erreur de génération (400 métier, 429 limiteur, autre). */
export function apiTokenErrorMessage(err: any, fallback = 'Impossible de générer le jeton.'): string {
  if (err?.status === 429) {
    return err?.error?.message || 'Trop de tentatives. Patientez une minute avant de réessayer.';
  }
  const apiMessage = err?.error?.message || err?.error?.Message || err?.error?.title;
  return apiMessage || fallback;
}

// ==================== Clés d'API (migration 069) ====================

/** Libellé français du statut d'une clé. */
export function apiKeyStatusLabel(status: string): string {
  switch (status) {
    case 'active': return 'Active';
    case 'revoked': return 'Révoquée';
    case 'expired': return 'Expirée';
    default: return status;
  }
}

/** Clés encore utilisables (le plafond de la société porte sur elles seules). */
export function activeKeyCount(keys: { status: string }[]): number {
  return keys.filter(k => k.status === 'active').length;
}

/**
 * Exemple d'utilisation d'une clé : l'échanger contre un jeton à usage unique, puis appeler l'API
 * avec ce jeton. L'adresse est celle du site ouvert.
 */
export function apiKeyExample(key: string, origin: string): string {
  const url = origin.replace(/\/+$/, '');
  return [
    '# 1. Obtenir un jeton à usage unique (valable 15 minutes)',
    `curl -X POST "${url}/api/api-tokens/echange" -H "X-Api-Key: ${key}"`,
    '',
    '# 2. Appeler l\'API avec le jeton reçu (champ "token" de la réponse)',
    `curl "${url}/api/reports/stops?startDate=AAAA-MM-JJ&endDate=AAAA-MM-JJ" -H "X-Api-Token: cly_..."`,
  ].join('\n');
}
