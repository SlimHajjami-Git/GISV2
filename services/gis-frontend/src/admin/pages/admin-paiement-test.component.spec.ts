import { of, throwError } from 'rxjs';
import { AdminPaiementTestComponent } from './admin-paiement-test.component';

/**
 * Console « Paiement ClicToPay » : lancer un paiement de test, revenir, faire VÉRIFIER par le
 * serveur. L'écran ne suit qu'une page ClicToPay en HTTPS et ne décide rien sur le retour seul.
 */
describe('Console admin — paiement ClicToPay de test', () => {
  beforeEach(() => jest.useFakeTimers());
  afterEach(() => jest.useRealTimers());

  const status = { configured: true, testEnvironment: true, host: 'test.clictopay.com', clientCheckoutEnabled: false,
                   pilotCompanyIds: null, mobilePageView: false, minAmount: 0.1, maxAmount: 1000 };
  const paye = { reference: 'CLYT-0-abc', status: 'paid', message: 'Paiement de test accepté et vérifié auprès de ClicToPay.',
                 orderId: null, amount: 1.5, currency: 'TND', cardPanMasked: '450921**1119', approvalCode: '826279',
                 paidAt: '2026-11-06T10:00:00Z', orderConfirmed: false, subscriptionExpiresAt: null, testMode: true };

  const ecran = (params: Record<string, string> = {}, over: any = {}) => {
    const admin: any = {
      getClicToPayStatus: jest.fn(() => of(status)),
      getAdminPayments: jest.fn(() => of([])),
      startClicToPayTest: jest.fn((): any => of({ paymentId: 1, reference: 'CLYT-0-abc',
        formUrl: 'https://test.clictopay.com/epg/merchants/CLICTOPAY/payment.html?mdOrder=x', alreadyPaid: false, testMode: true })),
      verifyAdminPayment: jest.fn((): any => of(paye)),
      ...over
    };
    const route: any = { snapshot: { queryParamMap: { get: (k: string) => params[k] ?? null } } };
    const router: any = { navigate: jest.fn() };
    const c = new AdminPaiementTestComponent(admin, route, router, { detectChanges: () => {} } as any);
    const redirect = jest.spyOn(c, 'redirectTo').mockImplementation(() => {});
    return { c, admin, redirect };
  };

  it('au retour de ClicToPay (?ref=…), fait vérifier la tentative par le serveur', () => {
    const { c, admin } = ecran({ ref: 'CLYT-0-abc', echec: '1' });
    c.ngOnInit();
    expect(admin.verifyAdminPayment).toHaveBeenCalledWith('CLYT-0-abc');
    expect(c.result?.status).toBe('paid');
    expect(c.view?.tone).toBe('success');
  });

  it('lancer un test envoie montant et devise, puis part sur la page ClicToPay', () => {
    const { c, admin, redirect } = ecran();
    c.amount = 1.5;
    c.currency = 'TND';
    c.startTest();
    expect(admin.startClicToPayTest).toHaveBeenCalledWith(1.5, 'TND');
    expect(redirect).toHaveBeenCalledWith('https://test.clictopay.com/epg/merchants/CLICTOPAY/payment.html?mdOrder=x');
  });

  it('une page hors de ClicToPay n\'est jamais suivie', () => {
    const { c, redirect } = ecran({}, {
      startClicToPayTest: jest.fn(() => of({ paymentId: 1, reference: 'r', formUrl: 'https://pirate.example/', alreadyPaid: false, testMode: true }))
    });
    c.startTest();
    expect(redirect).not.toHaveBeenCalled();
    expect(c.startError).toContain('inattendue');
    expect(c.starting).toBe(false);
  });

  it('refus du serveur (montant hors bornes) : le motif est affiché', () => {
    const { c, redirect } = ecran({}, {
      startClicToPayTest: jest.fn(() => throwError(() => ({ status: 400, error: { message: 'Montant de test entre 0.1 et 1000 TND.' } })))
    });
    c.amount = 5000;
    c.startTest();
    expect(redirect).not.toHaveBeenCalled();
    expect(c.startError).toContain('entre 0.1 et 1000');
  });

  it('montant vide : rien n\'est envoyé', () => {
    const { c, admin } = ecran();
    c.amount = 0;
    c.startTest();
    expect(admin.startClicToPayTest).not.toHaveBeenCalled();
  });

  it('banque pas encore décidée : revérifie quelques fois puis s\'arrête', () => {
    const verify = jest.fn((): any => of({ ...paye, status: 'pending', approvalCode: null }));
    const { c } = ecran({ ref: 'CLYT-0-abc' }, { verifyAdminPayment: verify });
    c.ngOnInit();
    jest.advanceTimersByTime(3000 * 10);
    expect(verify).toHaveBeenCalledTimes(4);
  });

  it('montants affichés en millimes pour le dinar', () => {
    const { c } = ecran();
    expect(c.money(1.5, 'TND').replace(/\s/g, ' ')).toBe('1,500 TND');
    expect(c.money(3, 'EUR').replace(/\s/g, ' ')).toBe('3,00 EUR');
  });
});
