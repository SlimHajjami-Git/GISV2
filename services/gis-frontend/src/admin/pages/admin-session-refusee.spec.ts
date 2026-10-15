import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { authInterceptor } from '../../services/auth.interceptor';
import { AdminService, ADMIN_ACCESS_DENIED, MESSAGE_ACCES_ADMIN_REFUSE } from '../services/admin.service';
import { AdminLoginComponent } from './admin-login.component';

/**
 * Espace /admin vide, sans un mot (constat de Karim du 15/10/2026) : une session
 * d'administration que le serveur refuse — compte supprimé, ou qui n'est pas administrateur
 * système — restait « connectée », chaque page avalait le 403 et n'affichait rien. Et la
 * connexion acceptait n'importe quel compte, qui tombait ensuite sur les mêmes pages vides.
 */
describe('Espace /admin — une session sans accès renvoie à la connexion avec l\'explication', () => {
  let http: HttpTestingController;
  let client: HttpClient;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    client = TestBed.inject(HttpClient);
    router = TestBed.inject(Router);
    jest.spyOn(router, 'navigate').mockResolvedValue(true);
    localStorage.setItem('admin_token', 'jeton-admin');
    localStorage.setItem('admin_user', JSON.stringify({ id: '15', email: 'ancien@compte.tn', role: 'admin' }));
    localStorage.setItem('auth_token', 'jeton-client');
  });

  afterEach(() => {
    http.verify();
    localStorage.clear();
  });

  it('un refus « administrateur système » efface la session admin et renvoie à la connexion', () => {
    client.get('/api/admin/company').subscribe({ error: () => {} });
    http.expectOne('/api/admin/company').flush(
      { error: 'admin_required', message: 'Accès réservé aux administrateurs système' },
      { status: 403, statusText: 'Forbidden' });

    expect(localStorage.getItem('admin_token')).toBeNull();
    expect(localStorage.getItem('admin_user')).toBeNull();
    expect(localStorage.getItem('auth_token')).toBe('jeton-client'); // la session client n'est pas touchée
    expect(router.navigate).toHaveBeenCalledWith(['/admin/login'], { queryParams: { raison: 'acces' } });
  });

  it('un refus métier sur une route admin (sans ce code) garde la session', () => {
    client.delete('/api/admin/users/12').subscribe({ error: () => {} });
    http.expectOne('/api/admin/users/12').flush(
      { message: 'Vous ne pouvez pas supprimer votre propre compte.' },
      { status: 403, statusText: 'Forbidden' });

    expect(localStorage.getItem('admin_token')).toBe('jeton-admin');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it('la connexion refuse un compte qui n\'est pas administrateur système, sans rien enregistrer', () => {
    localStorage.clear();
    const service = TestBed.inject(AdminService);
    let erreur: any;
    service.login('sophie.martin@belive-gpa.fr', 'x').subscribe({ error: e => (erreur = e) });
    http.expectOne(r => r.url.endsWith('/auth/login')).flush({
      token: 'jeton', refreshToken: 'r', user: { id: 18, email: 'sophie.martin@belive-gpa.fr', isSystemAdmin: false, roles: [] }
    });

    expect(erreur?.code).toBe(ADMIN_ACCESS_DENIED);
    expect(localStorage.getItem('admin_token')).toBeNull();
    expect(localStorage.getItem('admin_user')).toBeNull();
  });

  it('la connexion d\'un administrateur système passe comme avant', () => {
    localStorage.clear();
    const service = TestBed.inject(AdminService);
    let ok = false;
    service.login('admin@belive.tn', 'x').subscribe(() => (ok = true));
    http.expectOne(r => r.url.endsWith('/auth/login')).flush({
      token: 'jeton', refreshToken: 'r', user: { id: 12, email: 'admin@belive.tn', isSystemAdmin: true, roles: ['admin'] }
    });

    expect(ok).toBe(true);
    expect(localStorage.getItem('admin_token')).toBe('jeton');
  });
});

describe('Page de connexion admin — messages', () => {
  const cdr: any = { detectChanges() {} };
  const routeur: any = { navigate: jest.fn() };
  const route = (raison: string | null): any => ({ snapshot: { queryParamMap: { get: () => raison } } });

  it('renvoyé par l\'intercepteur : la page dit pourquoi', () => {
    const page = new AdminLoginComponent(routeur, { isAuthenticated: () => false } as any, cdr, route('acces'));
    expect(page.error).toBe(MESSAGE_ACCES_ADMIN_REFUSE);
  });

  it('un compte sans accès reçoit l\'explication, pas « mot de passe incorrect »', () => {
    const service: any = { isAuthenticated: () => false, login: () => throwError(() => ({ code: ADMIN_ACCESS_DENIED })) };
    const page = new AdminLoginComponent(routeur, service, cdr, route(null));
    page.email = 'sophie.martin@belive-gpa.fr';
    page.password = 'x';
    page.login();
    expect(page.error).toBe(MESSAGE_ACCES_ADMIN_REFUSE);
  });

  it('un mauvais mot de passe garde son message', () => {
    const service: any = { isAuthenticated: () => false, login: () => throwError(() => new Error('Invalid credentials')) };
    const page = new AdminLoginComponent(routeur, service, cdr, route(null));
    page.email = 'admin@belive.tn';
    page.password = 'faux';
    page.login();
    expect(page.error).toBe('E-mail ou mot de passe incorrect');
  });

  it('une connexion réussie mène au tableau de bord', () => {
    const service: any = { isAuthenticated: () => false, login: () => of({}) };
    const page = new AdminLoginComponent(routeur, service, cdr, route(null));
    page.email = 'admin@belive.tn';
    page.password = 'x';
    page.login();
    expect(routeur.navigate).toHaveBeenCalledWith(['/admin/dashboard']);
  });
});
