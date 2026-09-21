import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Preferences } from '@capacitor/preferences';
import { firstValueFrom } from 'rxjs';
import { AuthService, AuthResponse, CALYPSO_CLIENT_HEADER } from './auth.service';

/** Jeton JWT factice : seul le champ exp est lu par l'application. */
function jwt(expiresInSec: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expiresInSec }));
  return `eyJhbGciOiJIUzI1NiJ9.${payload}.signature`;
}

function response(accountType?: string, token = jwt(3600)): AuthResponse {
  return {
    token,
    refreshToken: 'refresh-2',
    user: {
      id: 42, firstName: 'Sami', lastName: 'Ben Ali', email: 'sami@transporttest.tn',
      roleId: 3, roleName: 'Chauffeur', isCompanyAdmin: false, isSystemAdmin: false,
      companyId: 5, companyName: 'TransportTest', permissions: {},
      subscriptionFeatures: null, assignedVehicleIds: null, userPermissions: null,
      ...(accountType !== undefined ? { accountType } : {})
    }
  };
}

async function clearPrefs() {
  for (const key of ['auth_token', 'refresh_token', 'auth_user', 'server_url']) {
    await Preferences.remove({ key });
  }
}

async function storeSession(token: string, accountType: 'staff' | 'driver' = 'driver') {
  await Preferences.set({ key: 'auth_token', value: token });
  await Preferences.set({ key: 'refresh_token', value: 'refresh-1' });
  await Preferences.set({ key: 'auth_user', value: JSON.stringify({ id: '42', name: 'Sami', accountType }) });
}

describe('AuthService', () => {
  let http: HttpTestingController;

  async function create(): Promise<AuthService> {
    TestBed.configureTestingModule({
      providers: [AuthService, provideHttpClient(), provideHttpClientTesting()]
    });
    const service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
    await service.ready;
    return service;
  }

  /** Attend qu'une requête soit émise (restoreSession la lance après quelques promesses). */
  async function expectOneEventually(urlPart: string) {
    for (let i = 0; i < 50; i++) {
      const found = http.match(r => r.url.includes(urlPart));
      if (found.length) return found[0];
      await new Promise(r => setTimeout(r, 2));
    }
    return http.expectOne(r => r.url.includes(urlPart));
  }

  beforeEach(async () => { await clearPrefs(); });
  afterEach(async () => { http?.verify(); await clearPrefs(); });

  describe('login', () => {
    it('se déclare application mobile par l\'en-tête X-Calypso-Client ET par le champ client du corps', async () => {
      const service = await create();
      service.login('sami@transporttest.tn', 'secret').subscribe();

      const req = http.expectOne(r => r.url.endsWith('/auth/login'));
      expect(req.request.method).toBe('POST');
      expect(CALYPSO_CLIENT_HEADER).toBe('X-Calypso-Client');
      expect(req.request.headers.get('X-Calypso-Client')).toBe('mobile');
      expect(req.request.body).toEqual({ email: 'sami@transporttest.tn', password: 'secret', client: 'mobile' });
      req.flush(response('driver'));
    });

    it('lit et stocke accountType « driver » : isDriver() vrai', async () => {
      const service = await create();
      const done = new Promise<any>(resolve => service.login('a@b.tn', 'x').subscribe(resolve));
      http.expectOne(r => r.url.endsWith('/auth/login')).flush(response('driver'));
      const user = await done;

      expect(user.accountType).toBe('driver');
      expect(service.isDriver()).toBeTrue();
      const { value } = await Preferences.get({ key: 'auth_user' });
      expect(JSON.parse(value!).accountType).toBe('driver');
    });

    it('sans accountType (serveur ancien) ou « staff » : gestionnaire', async () => {
      const service = await create();
      const done = new Promise<any>(resolve => service.login('a@b.tn', 'x').subscribe(resolve));
      http.expectOne(r => r.url.endsWith('/auth/login')).flush(response(undefined));
      expect((await done).accountType).toBe('staff');
      expect(service.isDriver()).toBeFalse();
    });

    it('le refus « compte réservé à l\'application » remonte tel quel à l\'écran', async () => {
      const service = await create();
      const failed = new Promise<any>(resolve => service.login('a@b.tn', 'x').subscribe({ error: resolve }));
      http.expectOne(r => r.url.endsWith('/auth/login'))
        .flush({ message: 'Ce compte est réservé à l\'application mobile Calypso' }, { status: 400, statusText: 'Bad Request' });
      const err = await failed;
      expect(err.status).toBe(400);
      expect(err.error.message).toContain('application mobile');
    });
  });

  describe('session au démarrage', () => {
    it('relit accountType stocké ; une session ancienne sans ce champ est « staff »', async () => {
      await Preferences.set({ key: 'auth_token', value: jwt(3600) });
      await Preferences.set({ key: 'auth_user', value: JSON.stringify({ id: '1', name: 'Ancien' }) });
      const service = await create();
      expect(service.getCurrentUserSync()?.accountType).toBe('staff');
      expect(service.isDriver()).toBeFalse();
    });

    it('jeton valide : restoreSession() vrai sans appel réseau', async () => {
      await storeSession(jwt(3600));
      const service = await create();
      expect(await service.restoreSession()).toBeTrue();
      expect(service.isDriver()).toBeTrue();
    });

    it('jeton expiré + refresh token : rafraîchissement SILENCIEUX (avec l\'en-tête mobile)', async () => {
      await storeSession(jwt(-60));
      const service = await create();
      expect(service.isAuthenticated()).toBeFalse();

      const restored = service.restoreSession();
      const req = await expectOneEventually('/auth/refresh');
      expect(req.request.headers.get('X-Calypso-Client')).toBe('mobile');
      expect(req.request.body.refreshToken).toBe('refresh-1');
      const fresh = jwt(3600);
      req.flush(response('driver', fresh));

      expect(await restored).toBeTrue();
      expect(service.isAuthenticated()).toBeTrue();
      expect(service.getToken()).toBe(fresh);
      expect(service.isDriver()).toBeTrue();
    });

    it('rafraîchissement impossible faute de réseau : la session est gardée (zone blanche)', async () => {
      await storeSession(jwt(-60));
      const service = await create();

      const restored = service.restoreSession();
      (await expectOneEventually('/auth/refresh')).error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

      expect(await restored).toBeTrue();
      expect(service.getCurrentUserSync()).not.toBeNull();
      const { value } = await Preferences.get({ key: 'refresh_token' });
      expect(value).toBe('refresh-1');
    });

    it('refresh refusé par le serveur (révoqué, compte désactivé) : déconnexion', async () => {
      await storeSession(jwt(-60));
      const service = await create();

      const restored = service.restoreSession();
      (await expectOneEventually('/auth/refresh')).flush({ message: 'invalide' }, { status: 401, statusText: 'Unauthorized' });

      expect(await restored).toBeFalse();
      await new Promise(r => setTimeout(r, 5));
      expect(service.getCurrentUserSync()).toBeNull();
    });

    it('refresh refusé en 403 (compte désactivé) : déconnexion aussi', async () => {
      await storeSession(jwt(-60));
      const service = await create();

      const restored = service.restoreSession();
      (await expectOneEventually('/auth/refresh')).flush({ message: 'désactivé' }, { status: 403, statusText: 'Forbidden' });

      expect(await restored).toBeFalse();
      expect(service.getCurrentUserSync()).toBeNull();
    });

    for (const status of [502, 503, 504, 408, 429, 500]) {
      it(`refresh en échec ${status} (rollout de l'API, surcharge) : la session est GARDÉE`, async () => {
        await storeSession(jwt(-60));
        const service = await create();

        const restored = service.restoreSession();
        (await expectOneEventually('/auth/refresh')).flush({ message: 'indisponible' }, { status, statusText: 'Erreur' });

        expect(await restored).toBeTrue();
        expect(service.getCurrentUserSync()).not.toBeNull();
        expect(service.getToken()).not.toBeNull();
        const { value } = await Preferences.get({ key: 'refresh_token' });
        expect(value).toBe('refresh-1');
      });
    }

    it('aucune session : restoreSession() faux sans appel réseau', async () => {
      const service = await create();
      expect(await service.restoreSession()).toBeFalse();
    });
  });

  describe('un seul rafraîchissement en vol pour toute l\'application', () => {
    it('garde (restoreSession) + intercepteur en même temps : UN SEUL POST /auth/refresh, le même résultat', async () => {
      await storeSession(jwt(-60));
      const service = await create();

      const fromInterceptor = firstValueFrom(service.refreshAccessToken());
      const fromGuard = service.restoreSession();
      const fromSwitch = firstValueFrom(service.refreshAccessToken());
      await new Promise(r => setTimeout(r, 20));
      const reqs = http.match(r => r.url.includes('/auth/refresh'));
      // Un second POST avec le même jeton de rafraîchissement serait refusé (révoqué au
      // premier usage) et déconnecterait la session que le premier vient de restaurer.
      expect(reqs.length).toBe(1);
      const fresh = jwt(3600);
      reqs.forEach(req => req.flush(response('driver', fresh)));

      expect(await fromGuard).toBeTrue();
      expect((await fromInterceptor)?.token).toBe(fresh);
      expect((await fromSwitch)?.token).toBe(fresh);
      expect(service.getToken()).toBe(fresh);
    });

    it('l\'échec (401) d\'un jeton déjà remplacé par une nouvelle connexion ne déconnecte PAS', async () => {
      await storeSession(jwt(-60));
      const service = await create();

      const stale = firstValueFrom(service.refreshAccessToken());
      const refreshReq = await expectOneEventually('/auth/refresh');
      const loggedIn = new Promise<any>(resolve => service.login('a@b.tn', 'x').subscribe(resolve));
      const loginToken = jwt(3600);
      http.expectOne(r => r.url.endsWith('/auth/login')).flush(response('driver', loginToken));
      await loggedIn;

      refreshReq.flush({ message: 'Refresh token révoqué' }, { status: 401, statusText: 'Unauthorized' });
      expect(await stale).toBeNull();
      await new Promise(r => setTimeout(r, 5));
      expect(service.getToken()).toBe(loginToken);
      expect(service.getCurrentUserSync()).not.toBeNull();
    });

    it('pendingRefresh expose le rafraîchissement en vol, puis revient à null', async () => {
      await storeSession(jwt(-60));
      const service = await create();
      expect(service.pendingRefresh).toBeNull();
      const done = firstValueFrom(service.refreshAccessToken());
      expect(service.pendingRefresh).not.toBeNull();
      (await expectOneEventually('/auth/refresh')).flush(response('driver'));
      await done;
      await new Promise(r => setTimeout(r, 0));
      expect(service.pendingRefresh).toBeNull();
    });
  });
});
