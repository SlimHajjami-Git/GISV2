/**
 * Fenêtre pendant laquelle un sinistre DÉTECTÉ peut être recréé par la détection
 * (AccidentDetectionService relit les trames jusqu'à 25 minutes en arrière, plus une
 * marge). Même valeur que le serveur : DeleteAccidentEventCommandHandler refuse (409)
 * la suppression d'un tel dossier. « Fausse alerte » garde la ligne au statut
 * « dismissed », que la détection reconnaît : c'est le bon geste pendant cette fenêtre.
 */
export const FENETRE_REDETECTION_MINUTES = 30;

/**
 * Règle unique de la liste et de la fiche des sinistres : un dossier automatique dont
 * l'incident date de moins de 30 minutes serait recréé s'il était supprimé.
 */
export function peutEtreRedetecte(
  origin: string | null | undefined,
  incidentAt: string | Date | null | undefined,
  now: number = Date.now()
): boolean {
  if (!incidentAt || origin === 'manual') return false;
  const t = new Date(incidentAt).getTime();
  if (isNaN(t)) return false;
  const minutes = (now - t) / 60000;
  return minutes >= 0 && minutes < FENETRE_REDETECTION_MINUTES;
}
