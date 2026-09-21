import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { DriverGuard, StaffGuard, homeUrlFor } from './driver.guard';
import { AuthGuard } from './auth.guard';
import { AuthService } from '../services/auth.service';

describe('Gardes de l\'espace chauffeur', () => {
  let isDriver: boolean;
  let restore: boolean;
  /** Ce que le rafraîchissement du jeton apprend du serveur (compte converti entre-temps). */
  let driverAfterRestore: boolean | null;
  let router: Router;

  beforeEach(() => {
    isDriver = false;
    restore = true;
    driverAfterRestore = null;
    const auth = {
      ready: Promise.resolve(),
      isDriver: () => isDriver,
      restoreSession: async () => {
        await new Promise(r => setTimeout(r, 1));   // le rafraîchissement prend du temps
        if (driverAfterRestore !== null) isDriver = driverAfterRestore;
        return restore;
      }
    };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }]
    });
    router = TestBed.inject(Router);
  });

  const url = (r: boolean | UrlTree) => (r instanceof UrlTree ? router.serializeUrl(r) : r);

  it('DriverGuard : laisse passer un chauffeur', async () => {
    isDriver = true;
    expect(await TestBed.inject(DriverGuard).canActivate()).toBeTrue();
  });

  it('DriverGuard : renvoie un gestionnaire vers ses onglets', async () => {
    isDriver = false;
    expect(url(await TestBed.inject(DriverGuard).canActivate())).toBe('/tabs/dashboard');
  });

  it('StaffGuard : un chauffeur ne peut pas ouvrir /tabs/* → /driver/tours', async () => {
    isDriver = true;
    expect(url(await TestBed.inject(StaffGuard).canActivate())).toBe('/driver/tours');
  });

  it('StaffGuard : laisse passer un gestionnaire', async () => {
    isDriver = false;
    expect(await TestBed.inject(StaffGuard).canActivate()).toBeTrue();
  });

  it('StaffGuard attend la session restaurée : un jeton expiré dont le rafraîchissement apporte « driver » mène à ses tournées', async () => {
    isDriver = false;                 // session stockée : gestionnaire
    driverAfterRestore = true;        // converti chauffeur côté serveur hier
    expect(url(await TestBed.inject(StaffGuard).canActivate())).toBe('/driver/tours');
  });

  it('DriverGuard attend la session restaurée : un chauffeur repassé gestionnaire retourne aux onglets', async () => {
    isDriver = true;
    driverAfterRestore = false;
    expect(url(await TestBed.inject(DriverGuard).canActivate())).toBe('/tabs/dashboard');
  });

  it('homeUrlFor : page d\'accueil selon le compte', () => {
    expect(homeUrlFor(true)).toBe('/driver/tours');
    expect(homeUrlFor(false)).toBe('/tabs/dashboard');
  });

  it('AuthGuard : session restaurée (rafraîchissement silencieux compris) → passe', async () => {
    restore = true;
    const nav = spyOn(router, 'navigate').and.resolveTo(true);
    expect(await TestBed.inject(AuthGuard).canActivate({} as any, { url: '/driver/tours/3' } as any)).toBeTrue();
    expect(nav).not.toHaveBeenCalled();
  });

  it('AuthGuard : session perdue → connexion, avec la cible en returnUrl', async () => {
    restore = false;
    const nav = spyOn(router, 'navigate').and.resolveTo(true);
    expect(await TestBed.inject(AuthGuard).canActivate({} as any, { url: '/driver/tours/3' } as any)).toBeFalse();
    expect(nav).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/driver/tours/3' } });
  });
});
