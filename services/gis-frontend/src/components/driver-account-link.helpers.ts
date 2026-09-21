/**
 * Écran Utilisateurs — choix de la fiche chauffeur reliée à un compte chauffeur.
 * Fonctions pures, testées dans driver-account-link.helpers.spec.ts.
 *
 * Pourquoi un sélecteur : sans lui, le serveur retrouvait la fiche par e-mail
 * seulement (ou par le lien « Créer son compte » de l'écran Chauffeurs). Une fiche
 * sans e-mail — champ facultatif — était ratée et une seconde fiche « Ali Ben Salah »
 * naissait, sans véhicule ni tournées, pendant que la fiche d'origine restait sans
 * compte (envoi de tournée refusé, DRIVER_NO_APP_ACCOUNT).
 *
 * Contrat serveur (UsersController / DriverAccountRules, 21/09/2026) :
 * - POST /api/users et PUT /api/users/{id} acceptent driverId (int | null), lu
 *   seulement si isDriverAccount: true ; null = « créer ou retrouver par e-mail ».
 * - 404 « Fiche chauffeur introuvable dans votre société. »
 * - 409 « Cette fiche chauffeur est déjà reliée à un autre compte : … »
 * - 409 « Ce compte est déjà relié à une autre fiche chauffeur. » (un compte déjà
 *   relié ne change pas de fiche : choisir une autre fiche pour lui est refusé)
 * - GET /api/drivers renvoie pour chaque fiche userId (compte relié) et accountStatus.
 */

export interface DriverRecord {
  id: number;
  firstName?: string | null;
  lastName?: string | null;
  email?: string | null;
  status?: string | null;
  /** Compte relié à la fiche (null = aucun compte). */
  userId?: number | null;
  accountStatus?: string | null;
}

/** Libellé de l'option par défaut : aucune fiche désignée, le serveur choisit. */
export const AUTO_DRIVER_LINK_LABEL = 'Créer ou retrouver par e-mail automatiquement';

/** Même identifiant, que le serveur l'ait rendu en nombre ou en chaîne. */
function sameId(a: number | string | null | undefined, b: number | string | null | undefined): boolean {
  return a != null && b != null && String(a) === String(b);
}

/**
 * Fiches proposées : celles de la société SANS compte, plus celle déjà reliée à CE
 * compte (modification). Une fiche reliée à un autre compte n'est jamais proposée :
 * le serveur la refuserait (409), et la relier volerait l'accès d'un autre chauffeur.
 * Triées par nom, pour qu'une flotte de cent chauffeurs reste lisible.
 */
export function linkableDriverRecords(
  drivers: DriverRecord[] | null | undefined, accountId: number | string | null | undefined
): DriverRecord[] {
  return (drivers ?? [])
    .filter(d => d && (d.userId == null || sameId(d.userId, accountId)))
    .sort((a, b) => driverRecordName(a).localeCompare(driverRecordName(b), 'fr', { sensitivity: 'base' }));
}

/** Fiche déjà reliée à ce compte, ou null (compte ordinaire, fiche supprimée…). */
export function driverRecordLinkedTo(
  drivers: DriverRecord[] | null | undefined, accountId: number | string | null | undefined
): number | null {
  if (accountId == null) return null;
  const fiche = (drivers ?? []).find(d => d && sameId(d.userId, accountId));
  return fiche ? fiche.id : null;
}

function driverRecordName(d: DriverRecord): string {
  return `${d.firstName ?? ''} ${d.lastName ?? ''}`.trim() || `Fiche n° ${d.id}`;
}

/**
 * Libellé d'une option : nom, e-mail s'il existe (deux homonymes restent
 * distinguables), et mention de la fiche déjà reliée à ce compte ou inactive.
 */
export function driverRecordLabel(d: DriverRecord, accountId: number | string | null | undefined): string {
  let label = driverRecordName(d);
  if (d.email) label += ` — ${d.email}`;
  if (sameId(d.userId, accountId)) label += ' (reliée à ce compte)';
  else if (d.status && d.status.toLowerCase() !== 'active') label += ' (fiche inactive)';
  return label;
}

/**
 * Refus du serveur qui porte sur la fiche à relier (404 fiche introuvable, 409 fiche
 * ou compte déjà relié) : son message est affiché sous le sélecteur, là où l'admin
 * doit corriger son choix, et pas seulement dans un toast qui disparaît. Les autres
 * 404/409 (« Cet email est déjà utilisé », compte introuvable) ne parlent pas de la
 * fiche : les afficher sous le sélecteur désignerait le mauvais champ — null, le
 * toast suffit.
 */
export function driverLinkErrorMessage(err: any): string | null {
  const status = err?.status;
  if (status !== 404 && status !== 409) return null;
  const message = typeof err?.error?.message === 'string' ? err.error.message.trim() : '';
  return /fiche/i.test(message) ? message : null;
}
