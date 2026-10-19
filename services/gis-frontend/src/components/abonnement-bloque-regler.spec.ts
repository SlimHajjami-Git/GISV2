import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { authInterceptor } from '../services/auth.interceptor';
import { SubscriptionStatusService } from '../services/subscription-status.service';
import { SubscriptionBlockedComponent } from './subscription-blocked.component';

/**
 * Préparation du paiement en ligne (19/10/2026, point 1) : l'écran de blocage disait
 * « contactez votre prestataire » même à l'administrateur d'une société en libre-service
 * dont l'abonnement (ou l'essai) était simplement arrivé à échéance. Il propose désormais
 * « Régler mon abonnement », et l'écran Abonnement n'est plus renvoyé vers le blocage.
 */
describe('Écran de blocage — « Régler mon abonnement »', () => {
  const ecran = (reason: string, compte: any): SubscriptionBlockedComponent => {
    const routeur: any = { navigate: jest.fn() };
    const c = new SubscriptionBlockedComponent(
      { snapshot: { queryParamMap: { get: () => reason } } } as any, routeur, {} as any,
      { getCurrentUserSync: () => compte } as any,
      { canManageOwnSubscription: () => !!compte?.isSystemAdmin || (!!compte?.selfServiceSubscription && !!compte?.isCompanyAdmin) } as any);
    c.ngOnInit();
    return c;
  };
  const adminLibreService = { selfServiceSubscription: true, isCompanyAdmin: true };
  const employeLibreService = { selfServiceSubscription: true, isCompanyAdmin: false };

  it('fin d\'abonnement, administrateur en libre-service : le bouton mène à l\'écran Abonnement', () => {
    const c = ecran('expired', adminLibreService);
    expect(c.peutRegler).toBe(true);
    c.regler();
    expect((c as any).router.navigate).toHaveBeenCalledWith(['/subscription']);
  });

  it('suspension ou résiliation par Belive : pas de bouton, on contacte', () => {
    expect(ecran('suspended', adminLibreService).peutRegler).toBe(false);
    expect(ecran('cancelled', adminLibreService).peutRegler).toBe(false);
  });

  it('employé d\'une société en libre-service : pas de bouton, il s\'adresse à son administrateur', () => {
    const c = ecran('expired', employeLibreService);
    expect(c.peutRegler).toBe(false);
    expect(c.demanderAdministrateur).toBe(true);
  });

  it('client installé par Belive (offre GPS) : pas de bouton', () => {
    expect(ecran('expired', { selfServiceSubscription: false, isCompanyAdmin: true }).peutRegler).toBe(false);
  });
});

describe('Société bloquée sur l\'écran Abonnement : elle n\'est pas renvoyée au blocage', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let router: Router;
  let url: string;

  beforeEach(() => {
    localStorage.clear();
    // Jeton loin de son expiration : l'intercepteur ne tente pas de rafraîchissement.
    localStorage.setItem('auth_token', 'x.' + btoa(JSON.stringify({ sub: '18', exp: Math.floor(Date.now() / 1000) + 3600 })) + '.y');
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    router = TestBed.inject(Router);
    jest.spyOn(router, 'navigate').mockResolvedValue(true);
    jest.spyOn(router, 'url', 'get').mockImplementation(() => url);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  const refusAbonnement = () => http.expectOne('/api/notifications').flush(
    { error: 'subscription_expired', message: 'Abonnement expiré' }, { status: 403, statusText: 'Forbidden' });

  it('un appel refusé pour abonnement sur l\'écran Abonnement ne renvoie pas au blocage', () => {
    url = '/subscription';
    client.get('/api/notifications').subscribe({ error: () => {} });
    refusAbonnement();
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('ailleurs, le même refus renvoie toujours au blocage', () => {
    url = '/dashboard';
    client.get('/api/notifications').subscribe({ error: () => {} });
    refusAbonnement();
    expect(router.navigate).toHaveBeenCalledWith(['/abonnement-suspendu'], { queryParams: { reason: 'expired' } });
  });

  it('la surveillance du statut laisse l\'écran Abonnement ouvert', () => {
    const statut = TestBed.inject(SubscriptionStatusService);
    url = '/subscription';
    statut.refresh();
    http.expectOne(r => r.url.endsWith('/subscriptions/banner')).flush({ level: 'blocked', reason: 'expired', expiresAt: null, daysRemaining: -10, graceDaysLeft: 0 });
    expect(router.navigate).not.toHaveBeenCalled();

    url = '/dashboard';
    statut.refresh();
    http.expectOne(r => r.url.endsWith('/subscriptions/banner')).flush({ level: 'blocked', reason: 'expired', expiresAt: null, daysRemaining: -10, graceDaysLeft: 0 });
    expect(router.navigate).toHaveBeenCalledWith(['/abonnement-suspendu'], { queryParams: { reason: 'expired' } });
  });
});
