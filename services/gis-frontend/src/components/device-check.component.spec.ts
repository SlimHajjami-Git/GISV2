import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { DeviceCheckComponent, messageErreurDiagnostic } from './device-check.component';
import { authInterceptor } from '../services/auth.interceptor';
import { AuthGuard } from '../guards/auth.guard';
import { routes } from '../app.routes';

/**
 * Diagnostic boîtier (/device-check) — fermeture du 23/09/2026.
 *
 * L'API /api/devicecheck était la SEULE route sans [Authorize] : sans jeton, elle
 * rendait position, contact, compteur et carburant de n'importe quel véhicule de
 * n'importe quelle société, à partir d'une plaque lue dans la rue. Elle exige
 * désormais un compte connecté. Côté écran, trois choses devaient suivre :
 *   • la route Angular gardée par AuthGuard ;
 *   • l'intercepteur qui RETIRAIT le jeton de tout appel « /devicecheck/ » ;
 *   • un appel par HttpClient (donc par l'intercepteur, qui rafraîchit le jeton)
 *     au lieu d'un window.fetch nu, et un 401 qui renvoie à la connexion.
 */
describe('DeviceCheckComponent — diagnostic réservé aux comptes connectés', () => {
  let component: DeviceCheckComponent;
  let http: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [DeviceCheckComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    component = TestBed.createComponent(DeviceCheckComponent).componentInstance;
    (component as any).cdr = { detectChanges: () => {} };
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    jest.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  /** Jeton loin de son expiration : l'intercepteur ne tente pas de rafraîchissement proactif. */
  function jetonValide(): string {
    const charge = btoa(JSON.stringify({ sub: '58', exp: Math.floor(Date.now() / 1000) + 3600 }));
    return `x.${charge}.y`;
  }

  it('la route /device-check est gardée par AuthGuard', () => {
    const route = routes.find(r => r.path === 'device-check');
    expect(route).toBeDefined();
    expect(route!.canActivate).toContain(AuthGuard);
  });

  it("l'appel passe par HttpClient ET porte le jeton (l'intercepteur ne l'en retire plus)", () => {
    const jeton = jetonValide();
    localStorage.setItem('auth_token', jeton);
    component.query = '  123 TU 4567 ';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/lookup'));
    expect(requete.request.params.get('q')).toBe('123 TU 4567');
    expect(requete.request.headers.get('Authorization')).toBe(`Bearer ${jeton}`);

    requete.flush({ found: true, hasGps: true, connected: true });
    expect(component.result).toEqual({ found: true, hasGps: true, connected: true });
    expect(component.loading).toBe(false);
    expect(component.error).toBe('');
  });

  it('401 sans session : message clair et renvoi vers la connexion, jamais un échec muet', () => {
    component.query = 'MAT-A';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/lookup'))
      .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(component.error).toBe(messageErreurDiagnostic(401));
    expect(component.loading).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/device-check' } });
  });

  it('403 : message explicite, pas de redirection', () => {
    localStorage.setItem('auth_token', jetonValide());
    component.query = 'MAT-A';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/lookup'))
      .flush({ message: 'Interdit' }, { status: 403, statusText: 'Forbidden' });

    expect(component.error).toBe(messageErreurDiagnostic(403));
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('une recherche vide ne part pas', () => {
    component.query = '   ';
    component.search();
    http.expectNone(() => true);
    expect(component.loading).toBe(false);
  });

  it('chaque statut a un message lisible', () => {
    expect(messageErreurDiagnostic(401)).toContain('Reconnectez-vous');
    expect(messageErreurDiagnostic(403)).toContain("n'a pas accès");
    expect(messageErreurDiagnostic(0)).toContain('Serveur injoignable');
    expect(messageErreurDiagnostic(500)).toContain('erreur 500');
  });
});
