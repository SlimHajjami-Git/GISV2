import { of, throwError } from 'rxjs';
import { PaymentReturnComponent } from './payment-return.component';

/**
 * Retour de ClicToPay : arriver sur /paiement/retour ne prouve rien. L'écran demande la
 * vérification au serveur et n'affiche que son résultat ; « echec=1 » n'est jamais lu.
 */
describe('Écran de retour de paiement', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  const resultat = (over: any = {}) => ({
    reference: 'CLY-9-x', status: 'paid', message: 'Paiement accepté. Votre abonnement est activé.',
    orderId: 9, amount: 336, currency: 'TND', cardPanMasked: '450921**1119', approvalCode: '365067',
    paidAt: '2026-11-06T10:00:00Z', orderConfirmed: true, subscriptionExpiresAt: '2027-11-06T10:00:00Z',
    testMode: true, ...over
  });

  const ecran = (params: Record<string, string>, verify: jest.Mock) => {
    const route: any = { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } };
    const router: any = { navigate: jest.fn() };
    const api: any = { verifyOnlinePayment: verify };
    const c = new PaymentReturnComponent(route, router, api, { detectChanges: () => {} } as any);
    return { c, router, api };
  };

  it('vérifie la référence auprès du serveur et affiche le succès', () => {
    const { c, api } = ecran({ ref: 'CLY-9-x' }, jest.fn(() => of(resultat())));
    c.ngOnInit();
    expect(api.verifyOnlinePayment).toHaveBeenCalledWith('CLY-9-x');
    expect(c.state).toBe('done');
    expect(c.view?.tone).toBe('success');
    expect(c.testMode).toBe(true);
  });

  it('« echec=1 » ne décide rien : c\'est le statut vérifié qui compte', () => {
    const { c } = ecran({ ref: 'CLY-9-x', echec: '1' }, jest.fn(() => of(resultat())));
    c.ngOnInit();
    expect(c.view?.tone).toBe('success');
  });

  it('banque pas encore décidée : quelques revérifications automatiques, puis la main au client', () => {
    const verify = jest.fn(() => of(resultat({ status: 'pending', orderConfirmed: false })));
    const { c } = ecran({ ref: 'CLY-9-x' }, verify);
    c.ngOnInit();
    expect(c.state).toBe('verifying');
    jest.advanceTimersByTime(3000 * 10);
    expect(verify).toHaveBeenCalledTimes(4);
    expect(c.state).toBe('done');
    expect(c.view?.tone).toBe('pending');
  });

  it('refus : proposer de payer à nouveau', () => {
    const { c } = ecran({ ref: 'CLY-9-x' }, jest.fn(() => of(resultat({ status: 'failed', orderConfirmed: false }))));
    c.ngOnInit();
    expect(c.view?.canPayAgain).toBe(true);
    expect(c.view?.tone).toBe('error');
  });

  it('sans référence : rien n\'est appelé', () => {
    const verify = jest.fn();
    const { c } = ecran({}, verify);
    c.ngOnInit();
    expect(verify).not.toHaveBeenCalled();
    expect(c.state).toBe('missing');
  });

  it('référence inconnue : le message du serveur', () => {
    const { c } = ecran({ ref: 'X' }, jest.fn(() => throwError(() => ({ status: 400, error: { message: 'Paiement introuvable.' } }))));
    c.ngOnInit();
    expect(c.state).toBe('missing');
    expect(c.errorMessage).toBe('Paiement introuvable.');
  });
});
