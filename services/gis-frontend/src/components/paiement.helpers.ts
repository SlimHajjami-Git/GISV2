import { OnlinePaymentResult, OnlinePaymentStatus } from '../services/api.service';

/**
 * Paiement par carte (ClicToPay, migration 067) : règles d'écran, sans Angular pour être testées.
 *
 * Le serveur fait foi sur tout (montant, résultat) ; l'écran ne décide que de deux choses :
 * suivre ou non la page de paiement renvoyée, et comment présenter un résultat vérifié.
 */

/**
 * La page de paiement doit être chez ClicToPay, en HTTPS. Le serveur le contrôle déjà ; l'écran
 * le recontrôle avant de quitter Calypso, une redirection ailleurs serait un hameçonnage parfait.
 */
export function isClicToPayPage(url: string | null | undefined): boolean {
  if (!url) return false;
  try {
    const u = new URL(url);
    const host = u.hostname.toLowerCase();
    return u.protocol === 'https:' && (host === 'clictopay.com' || host.endsWith('.clictopay.com'));
  } catch {
    return false;
  }
}

/** Gabarit de la page ClicToPay (paramètre pageView du manuel). */
export function pageViewFor(viewportWidth: number): 'DESKTOP' | 'MOBILE' {
  return viewportWidth > 0 && viewportWidth < 768 ? 'MOBILE' : 'DESKTOP';
}

/** Ton de l'écran de retour. */
export type PaymentTone = 'success' | 'pending' | 'warning' | 'error';

export interface PaymentView {
  tone: PaymentTone;
  title: string;
  /** L'écran doit-il revérifier tout seul dans un instant ? */
  retry: boolean;
  /** Proposer de relancer un paiement (refus, expiration, erreur). */
  canPayAgain: boolean;
}

/** Ce que l'écran de retour montre pour un résultat vérifié. */
export function paymentView(result: Pick<OnlinePaymentResult, 'status' | 'orderConfirmed'>): PaymentView {
  switch (result.status) {
    case 'paid':
      return result.orderConfirmed
        ? { tone: 'success', title: 'Paiement accepté', retry: false, canPayAgain: false }
        : { tone: 'warning', title: 'Paiement reçu', retry: false, canPayAgain: false };
    case 'failed':
      return { tone: 'error', title: 'Paiement refusé', retry: false, canPayAgain: true };
    case 'expired':
      return { tone: 'error', title: 'Paiement expiré', retry: false, canPayAgain: true };
    case 'error':
      return { tone: 'error', title: 'Paiement non abouti', retry: false, canPayAgain: true };
    case 'anomaly':
      return { tone: 'warning', title: 'Vérification nécessaire', retry: false, canPayAgain: false };
    default:
      return { tone: 'pending', title: 'Confirmation en cours', retry: true, canPayAgain: false };
  }
}

/** Nombre de vérifications automatiques avant de laisser la main au client. */
export const AUTO_VERIFY_ATTEMPTS = 4;

/** Revérifier automatiquement ? (paiement pas encore confirmé par la banque, essais restants) */
export function shouldAutoVerify(status: OnlinePaymentStatus | null, attempts: number): boolean {
  return status === 'pending' && attempts < AUTO_VERIFY_ATTEMPTS;
}

/** Libellé court d'une tentative (liste des paiements). */
export function paymentStatusLabel(status: OnlinePaymentStatus | string): string {
  switch (status) {
    case 'paid': return 'Payé';
    case 'pending': return 'En attente';
    case 'failed': return 'Refusé';
    case 'expired': return 'Expiré';
    case 'error': return 'Erreur';
    case 'anomaly': return 'À vérifier';
    default: return status;
  }
}

/** Message d'une erreur HTTP de l'API de paiement, en français. */
export function paymentErrorMessage(err: any, fallback: string): string {
  if (err?.status === 0) return 'Connexion impossible. Vérifiez votre connexion internet et réessayez.';
  return err?.error?.message || err?.error?.error || fallback;
}
