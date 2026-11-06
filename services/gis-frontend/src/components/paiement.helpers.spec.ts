import {
  AUTO_VERIFY_ATTEMPTS, isClicToPayPage, pageViewFor, paymentErrorMessage, paymentStatusLabel,
  paymentView, shouldAutoVerify
} from './paiement.helpers';

describe('paiement par carte (ClicToPay) — règles d\'écran', () => {
  describe('isClicToPayPage', () => {
    it('accepte la page de test et la page de production en HTTPS', () => {
      expect(isClicToPayPage('https://test.clictopay.com/merchants/test/payment_fr.html?mdOrder=1')).toBe(true);
      expect(isClicToPayPage('https://ipay.clictopay.com/payment/merchants/x/payment_fr.html')).toBe(true);
    });

    it('refuse tout ce qui n\'est pas ClicToPay en HTTPS', () => {
      expect(isClicToPayPage('http://test.clictopay.com/payment_fr.html')).toBe(false);
      expect(isClicToPayPage('https://clictopay.com.pirate.example/payer')).toBe(false);
      expect(isClicToPayPage('https://pirate.example/?r=clictopay.com')).toBe(false);
      expect(isClicToPayPage('javascript:alert(1)')).toBe(false);
      expect(isClicToPayPage('')).toBe(false);
      expect(isClicToPayPage(null)).toBe(false);
    });
  });

  it('choisit le gabarit mobile sur un petit écran', () => {
    expect(pageViewFor(375)).toBe('MOBILE');
    expect(pageViewFor(1280)).toBe('DESKTOP');
    expect(pageViewFor(0)).toBe('DESKTOP');
  });

  describe('paymentView', () => {
    it('payé et commande validée : succès', () => {
      expect(paymentView({ status: 'paid', orderConfirmed: true })).toEqual(
        { tone: 'success', title: 'Paiement accepté', retry: false, canPayAgain: false });
    });

    it('payé mais commande non validée : reçu, l\'équipe finalise', () => {
      const v = paymentView({ status: 'paid', orderConfirmed: false });
      expect(v.tone).toBe('warning');
      expect(v.canPayAgain).toBe(false);
    });

    it('refus, expiration, erreur : proposer de payer à nouveau', () => {
      for (const status of ['failed', 'expired', 'error'] as const) {
        const v = paymentView({ status, orderConfirmed: false });
        expect(v.tone).toBe('error');
        expect(v.canPayAgain).toBe(true);
      }
    });

    it('anomalie : jamais « payer à nouveau » (il a peut-être déjà payé)', () => {
      expect(paymentView({ status: 'anomaly', orderConfirmed: false }).canPayAgain).toBe(false);
    });

    it('en attente : revérifier', () => {
      expect(paymentView({ status: 'pending', orderConfirmed: false }).retry).toBe(true);
    });
  });

  it('revérifie automatiquement un nombre limité de fois', () => {
    expect(shouldAutoVerify('pending', 0)).toBe(true);
    expect(shouldAutoVerify('pending', AUTO_VERIFY_ATTEMPTS)).toBe(false);
    expect(shouldAutoVerify('paid', 0)).toBe(false);
    expect(shouldAutoVerify(null, 0)).toBe(false);
  });

  it('libellés des tentatives', () => {
    expect(paymentStatusLabel('paid')).toBe('Payé');
    expect(paymentStatusLabel('anomaly')).toBe('À vérifier');
    expect(paymentStatusLabel('autre')).toBe('autre');
  });

  it('message d\'erreur de l\'API, sinon repli', () => {
    expect(paymentErrorMessage({ error: { message: 'Commande introuvable.' } }, 'x')).toBe('Commande introuvable.');
    expect(paymentErrorMessage({ status: 0 }, 'x')).toContain('Connexion impossible');
    expect(paymentErrorMessage({}, 'repli')).toBe('repli');
  });
});
