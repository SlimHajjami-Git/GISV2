import { of } from 'rxjs';
import { SubscriptionComponent } from './subscription.component';

/**
 * Préparation du paiement en ligne (19/10/2026, point 3) : une commande en attente
 * grisait tous les boutons « Choisir cette formule » (« Commande en cours… ») ; pour
 * changer de formule il fallait d'abord l'annuler. Choisir une autre formule la
 * remplace désormais ; seule la formule déjà commandée reste grisée.
 */
describe('Écran Abonnement — changer de formule remplace la commande en attente', () => {
  const plan = { id: 4, code: 'plan-basique', isActive: true, pricePerVehicle: false,
                 monthlyPrice: 0, quarterlyPrice: 0, semiannualPrice: 336, yearlyPrice: 504 };
  const enAttente = { id: 9, subscriptionTypeId: 4, planName: 'Calypso GPA', planCode: 'plan-basique',
                      billingCycle: 'semiannual', amount: 336, status: 'pending' } as any;

  const ecran = () => {
    const api: any = { createSubscriptionOrder: jest.fn(() => of({})), getMySubscriptionOrders: jest.fn(() => of([])) };
    const c = new SubscriptionComponent(api, { getCurrentUserSync: () => null } as any, { detectChanges: () => {} } as any);
    c.orders = [enAttente];
    return { c, api };
  };

  it('seule la formule déjà commandée est grisée, les autres restent commandables', () => {
    const { c } = ecran();
    expect(c.isPendingChoice(plan, 'semiannual')).toBe(true);
    expect(c.isPendingChoice(plan, 'yearly')).toBe(false);
  });

  it('commander une autre formule envoie la commande à remplacer', () => {
    const { c, api } = ecran();
    c.openPurchase(plan, 'yearly');
    expect(c.selectionIsPending).toBe(false);
    c.confirmPurchase();
    expect(api.createSubscriptionOrder).toHaveBeenCalledWith(4, 'yearly', 9);
  });

  it('sans commande en attente, rien à remplacer', () => {
    const { c, api } = ecran();
    c.orders = [];
    c.openPurchase(plan, 'yearly');
    c.confirmPurchase();
    expect(api.createSubscriptionOrder).toHaveBeenCalledWith(4, 'yearly', undefined);
  });

  it('en haut de page, le prochain règlement est celui de la commande en attente', () => {
    const { c } = ecran();
    c.plans = [plan];
    c.currentPlan = plan;
    c.nextPaymentAmount = 336;
    c.billingCycle = 'semiannual';
    c.orders = [{ ...enAttente, billingCycle: 'yearly', amount: 504 }];
    expect(c.headerAmount).toBe(504);
    expect(c.headerCycleLabel).toBe('facturation annuelle');
    expect(c.headerFollowsChoice).toBe(true);
    expect(c.headerChoiceNote).toContain('votre commande en attente');

    // Panneau ouvert sur une autre formule : le haut reste celui de la commande en
    // attente, comme la carte orange ; c'est le panneau qui annonce le remplacement.
    c.openPurchase(plan, 'semiannual');
    expect(c.headerAmount).toBe(504);
    expect(c.headerCycleLabel).toBe('facturation annuelle');
  });

  it('sans commande en attente, le haut suit la formule choisie, comme avant', () => {
    const { c } = ecran();
    c.orders = [];
    c.nextPaymentAmount = 504;
    c.billingCycle = 'yearly';
    c.openPurchase(plan, 'semiannual');
    expect(c.headerAmount).toBe(336);
    expect(c.headerFollowsChoice).toBe(true);
  });

  it('formule choisie depuis une carte : pas de second choix « Semestriel / Annuel » dans le panneau', () => {
    const { c } = ecran();
    c.openPurchase(plan, 'yearly');
    expect(c.purchaseCycleFromCard).toBe(true);
    // Grille générale (bouton sans cycle) : le choix du cycle reste proposé.
    c.openPurchase(plan);
    expect(c.purchaseCycleFromCard).toBe(false);
  });

  it('sans commande en attente, le haut garde le prochain règlement connu', () => {
    const { c } = ecran();
    c.orders = [];
    c.nextPaymentAmount = 336;
    c.billingCycle = 'semiannual';
    expect(c.headerAmount).toBe(336);
    expect(c.headerFollowsChoice).toBe(false);
  });

  it('le parc a changé depuis la commande : la même formule peut être recommandée au bon montant', () => {
    const { c } = ecran();
    c.orders = [{ ...enAttente, amount: 300 }];
    expect(c.isPendingChoice(plan, 'semiannual')).toBe(false);
  });
});
