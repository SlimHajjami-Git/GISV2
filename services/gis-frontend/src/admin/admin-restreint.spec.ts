import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { AdminService } from './services/admin.service';
import { AdminGuard } from './guards/admin.guard';
import { AdminLayoutComponent } from './components/admin-layout.component';

/**
 * Administrateur restreint (demande du 22/10/2026, compte d'Adel) : la console ne montre que
 * Sociétés, Users, Véhicules et Monitoring ; tout autre écran renvoie au premier autorisé.
 * Le serveur refuse le reste (AdminScreenAccess) ; ici, ce que voit la console.
 */
describe('Console d\'administration — administrateur restreint', () => {
  const ECRANS = ['clients', 'users', 'vehicles', 'monitoring'];
  let service: AdminService;
  let http: HttpTestingController;
  let router: Router;

  const connecter = (adminScreens: string[] | null) => {
    service.login('adel@exemple.tn', 'secret').subscribe();
    http.expectOne(r => r.url.endsWith('/auth/login')).flush({
      token: 'jeton', refreshToken: 'r',
      user: { id: 30, email: 'adel@exemple.tn', name: 'Adel', isSystemAdmin: true, roles: ['Administrateur restreint', 'system_admin'], adminScreens },
    });
  };
  const route = (path: string): any => ({ routeConfig: { path } });

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(AdminService);
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    jest.spyOn(router, 'navigate').mockResolvedValue(true);
  });

  afterEach(() => { http.verify(); localStorage.clear(); });

  it('la connexion retient les écrans autorisés', () => {
    connecter(ECRANS);
    expect(service.allowedScreens()).toEqual(ECRANS);
    expect(service.isRestricted()).toBe(true);
    expect(JSON.parse(localStorage.getItem('admin_user')!).screens).toEqual(ECRANS);
  });

  it('le menu ne montre que Sociétés, Users, Véhicules et Monitoring', () => {
    connecter(ECRANS);
    const menu = new AdminLayoutComponent(router, service);
    expect(menu.visibleNavItems.map(i => i.route)).toEqual(['clients', 'users', 'vehicles', 'monitoring']);
  });

  it('un écran non autorisé renvoie au premier écran autorisé ; la fiche société reste ouverte', () => {
    connecter(ECRANS);
    const garde = TestBed.runInInjectionContext(() => new AdminGuard(service, router));
    expect(garde.canActivate(route('subscriptions'), {} as any)).toBe(false);
    expect(router.navigate).toHaveBeenCalledWith(['/admin', 'clients']);
    expect(garde.canActivate(route('dashboard'), {} as any)).toBe(false);
    expect(garde.canActivate(route('clients/:id'), {} as any)).toBe(true);
    expect(garde.canActivate(route('monitoring'), {} as any)).toBe(true);
  });

  it('un administrateur sans restriction garde toute la console, comme avant', () => {
    connecter(null);
    expect(service.isRestricted()).toBe(false);
    const garde = TestBed.runInInjectionContext(() => new AdminGuard(service, router));
    expect(garde.canActivate(route('subscriptions'), {} as any)).toBe(true);
    expect(new AdminLayoutComponent(router, service).visibleNavItems.length).toBeGreaterThan(10);
  });
});
