import { of, throwError } from 'rxjs';
import { SubscriptionComponent } from './subscription.component';

/**
 * Paiement par carte (ClicToPay, migration 067) depuis l'écran Abonnement : l'écran ne fait
 * qu'envoyer le client sur la page ClicToPay renvoyée par le serveur ; le montant vient de la
 * commande, jamais de l'écran.
 */
describe('Écran Abonnement — payer par carte', () => {
  const plan = { id: 4, code: 'plan-basique', isActive: true, pricePerVehicle: false,
                 monthlyPrice: 0, quarterlyPrice: 0, semiannualPrice: 336, yearlyPrice: 504 };
  const enAttente = { id: 9, subscriptionTypeId: 4, planName: 'Calypso GPA', planCode: 'plan-basique',
                      billingCycle: 'semiannual', amount: 336, status: 'pending' } as any;
  const page = 'https://test.clictopay.com/merchants/test/payment_fr.html?mdOrder=abc';

  const ecran = (pay: jest.Mock = jest.fn((): any => of({ paymentId: 1, reference: 'CLY-9-x', formUrl: page, alreadyPaid: false, testMode: true }))) => {
    const api: any = {
      createSubscriptionOrder: jest.fn(() => of({ id: 12 })),
      getMySubscriptionOrders: jest.fn(() => of([])),
      paySubscriptionOrder: pay
    };
    const router = { navigate: jest.fn() };
    const c = new SubscriptionComponent(api, { getCurrentUserSync: () => null } as any,
      { detectChanges: () => {} } as any, router as any);
    const redirect = jest.spyOn(c, 'redirectTo').mockImplementation(() => {});
    c.orders = [enAttente];
    c.cardPayment = { enabled: true, provider: 'clictopay', testMode: true };
    return { c, api, router, redirect };
  };

  it('le bouton n\'existe que si le serveur a ouvert le paiement par carte', () => {
    const { c } = ecran();
    expect(c.cardPaymentEnabled).toBe(true);
    c.cardPayment = { enabled: false, provider: 'clictopay', testMode: false };
    expect(c.cardPaymentEnabled).toBe(false);
    c.cardPayment = null;
    expect(c.cardPaymentEnabled).toBe(false);
  });

  it('payer la commande en attente envoie sur la page ClicToPay, sans montant', () => {
    const { c, api, redirect } = ecran();
    c.payPending();
    expect(api.paySubscriptionOrder).toHaveBeenCalledWith(9, expect.stringMatching(/^(DESKTOP|MOBILE)$/));
    expect(redirect).toHaveBeenCalledWith(page);
  });

  it('depuis le panneau d\'achat : commande (qui remplace celle en attente) PUIS paiement de la nouvelle', () => {
    const { c, api, redirect } = ecran();
    c.openPurchase(plan, 'yearly');
    c.payNow();
    expect(api.createSubscriptionOrder).toHaveBeenCalledWith(4, 'yearly', 9);
    expect(api.paySubscriptionOrder).toHaveBeenCalledWith(12, expect.any(String));
    expect(redirect).toHaveBeenCalledWith(page);
  });

  it('une adresse de paiement hors de ClicToPay n\'est jamais suivie', () => {
    const { c, redirect } = ecran(jest.fn(() => of({ paymentId: 1, reference: 'r', formUrl: 'https://pirate.example/payer', alreadyPaid: false, testMode: false })));
    c.payPending();
    expect(redirect).not.toHaveBeenCalled();
    expect(c.payError).toContain('adresse inattendue');
    expect(c.busy).toBe(false);
  });

  it('déjà payée dans un autre onglet : écran de résultat, pas une seconde page de paiement', () => {
    const { c, router, redirect } = ecran(jest.fn(() => of({ paymentId: 1, reference: 'CLY-9-x', formUrl: null, alreadyPaid: true, testMode: false })));
    c.payPending();
    expect(redirect).not.toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/paiement/retour'], { queryParams: { ref: 'CLY-9-x' } });
  });

  it('refus du serveur : le motif est affiché et l\'écran redevient utilisable', () => {
    const { c, redirect } = ecran(jest.fn(() => throwError(() => ({ status: 400, error: { message: 'Le paiement par carte n\'accepte pas la devise DZD.' } }))));
    c.payPending();
    expect(redirect).not.toHaveBeenCalled();
    expect(c.payError).toContain('DZD');
    expect(c.busy).toBe(false);
    expect(c.paying).toBe(false);
  });
});
