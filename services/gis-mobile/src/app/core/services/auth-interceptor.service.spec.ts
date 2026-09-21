import { TestBed } from '@angular/core/testing';
import { HTTP_INTERCEPTORS, HttpClient, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { Observable, firstValueFrom, from } from 'rxjs';
import { AuthInterceptor, DRIVER_APP_ONLY_CODE } from './auth-interceptor.service';
import { AuthService } from './auth.service';

/** AuthService piloté par le test : jeton courant, rafraîchissement en vol, type de compte. */
class FakeAuth {
  token: string | null = 'T1';
  expiringSoon = false;
  driver = false;
  pendingRefresh: Promise<any> | null = null;
  refreshCalls = 0;
  /** Ce que fait un rafraîchissement (par défaut : échec passager, jeton inchangé). */
  refreshImpl: () => Promise<any> = async () => null;
  logout = jasmine.createSpy('logout');

  getToken() { return this.token; }
  isTokenExpiringSoon() { return this.expiringSoon; }
  isDriver() { return this.driver; }
  refreshAccessToken(): Observable<any> {
    this.refreshCalls++;
    return from(this.refreshImpl());
  }
}

describe('AuthInterceptor', () => {
  let auth: FakeAuth;
  let http: HttpClient;
  let backend: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    auth = new FakeAuth();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptorsFromDi()),
        provideHttpClientTesting(),
        { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true },
        { provide: AuthService, useValue: auth }
      ]
    });
    http = TestBed.inject(HttpClient);
    backend = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => backend.verify());

  const settle = () => new Promise(r => setTimeout(r, 5));

  it('une requête émise PENDANT un rafraîchissement l\'attend, puis part avec le nouveau jeton', async () => {
    let release!: () => void;
    auth.pendingRefresh = new Promise<void>(r => { release = r; }).then(() => { auth.token = 'T2'; auth.pendingRefresh = null; });

    const result = firstValueFrom(http.get('/api/driver-app/tours'));
    await settle();
    expect(backend.match('/api/driver-app/tours').length).toBe(0);   // retenue, pas partie avec T1

    release();
    await settle();
    const req = backend.expectOne('/api/driver-app/tours');
    expect(req.request.headers.get('Authorization')).toBe('Bearer T2');
    req.flush([]);
    await result;
  });

  it('401 alors qu\'un autre appel a déjà remplacé le jeton : rejouée avec le nouveau, sans nouveau rafraîchissement', async () => {
    const result = firstValueFrom(http.post('/api/driver-app/tours/7/waypoints/11/arrive', {}));
    await settle();
    const first = backend.expectOne('/api/driver-app/tours/7/waypoints/11/arrive');
    expect(first.request.headers.get('Authorization')).toBe('Bearer T1');
    auth.token = 'T2';
    first.flush({}, { status: 401, statusText: 'Unauthorized' });
    await settle();

    const retry = backend.expectOne('/api/driver-app/tours/7/waypoints/11/arrive');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer T2');
    retry.flush({ tourStatus: 'in_progress' });
    expect(await result).toEqual({ tourStatus: 'in_progress' });
    expect(auth.refreshCalls).toBe(0);
  });

  it('401 puis rafraîchissement réussi : un seul nouvel essai, avec le nouveau jeton', async () => {
    auth.refreshImpl = async () => { auth.token = 'T2'; return { token: 'T2' }; };
    const result = firstValueFrom(http.get('/api/driver-app/me'));
    await settle();
    backend.expectOne('/api/driver-app/me').flush({}, { status: 401, statusText: 'Unauthorized' });
    await settle();
    const retry = backend.expectOne('/api/driver-app/me');
    expect(retry.request.headers.get('Authorization')).toBe('Bearer T2');
    retry.flush({ driverId: 1 });
    expect(await result).toEqual({ driverId: 1 });
    expect(auth.refreshCalls).toBe(1);
  });

  it('401 et rafraîchissement impossible (réseau, 5xx) : l\'erreur d\'origine remonte, AUCUNE déconnexion', async () => {
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    const result = firstValueFrom(http.get('/api/driver-app/me')).catch(e => e);
    await settle();
    backend.expectOne('/api/driver-app/me').flush({}, { status: 401, statusText: 'Unauthorized' });

    const err = await result;
    expect(err.status).toBe(401);
    expect(auth.logout).not.toHaveBeenCalled();
    expect(navigate).not.toHaveBeenCalled();
    backend.expectNone('/api/driver-app/me');
  });

  it('jeton sur le point d\'expirer : rafraîchi AVANT l\'envoi (via le rafraîchissement partagé)', async () => {
    auth.expiringSoon = true;
    auth.refreshImpl = async () => { auth.token = 'T2'; auth.expiringSoon = false; return { token: 'T2' }; };
    const result = firstValueFrom(http.get('/api/driver-app/me'));
    await settle();
    const req = backend.expectOne('/api/driver-app/me');
    expect(req.request.headers.get('Authorization')).toBe('Bearer T2');
    req.flush({});
    await result;
    expect(auth.refreshCalls).toBe(1);
  });

  it('403 DRIVER_APP_ONLY alors que la session est déjà « chauffeur » : retour à « Mes tournées »', async () => {
    auth.driver = true;
    const nav = spyOn(router, 'navigateByUrl').and.resolveTo(true);
    const result = firstValueFrom(http.get('/api/vehicles')).catch(e => e);
    await settle();
    backend.expectOne('/api/vehicles').flush({ code: DRIVER_APP_ONLY_CODE }, { status: 403, statusText: 'Forbidden' });
    expect((await result).status).toBe(403);
    expect(nav).toHaveBeenCalledWith('/driver/tours', { replaceUrl: true });
    expect(auth.refreshCalls).toBe(0);
  });

  it('les appels d\'authentification passent sans jeton ni attente', async () => {
    auth.pendingRefresh = new Promise(() => { /* jamais résolu */ });
    const result = firstValueFrom(http.post('/api/auth/refresh', {}));
    const req = backend.expectOne('/api/auth/refresh');
    expect(req.request.headers.has('Authorization')).toBeFalse();
    req.flush({});
    await result;
  });
});
