import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { AuthService, DRIVER_WEB_LOGIN_REFUSED } from './auth.service';
import { UserPreferencesService } from './user-preferences.service';

/**
 * Compte chauffeur (21/09/2026) : réservé à l'application mobile Calypso. Le serveur
 * refuse déjà la connexion sur le site (400 + message) ; le service garde une ceinture
 * côté client. Dans les deux cas rien ne doit être stocké, et l'erreur doit porter le
 * message qui dit où se connecter — sinon l'écran affichait « mot de passe incorrect ».
 */
describe('AuthService — compte chauffeur refusé sur le site', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  const reponseChauffeur = {
    token: 'jeton-chauffeur',
    refreshToken: 'rafraichissement-chauffeur',
    user: {
      id: 42, firstName: 'Karim', lastName: 'Chauffeur', email: 'karim@transporttest.tn',
      roleId: 2, roleName: 'Utilisateur', isCompanyAdmin: false, isSystemAdmin: false,
      companyId: 7, companyName: 'TransportTest', permissions: {},
      subscriptionFeatures: null, assignedVehicleIds: null, userPermissions: null,
      accountType: 'driver'
    }
  };

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('refuse un login dont user.accountType vaut driver, sans rien stocker', () => {
    let utilisateur: any = 'non appelé';
    let erreur: any = null;
    service.login('karim@transporttest.tn', 'Secret@2026').subscribe({
      next: u => { utilisateur = u; },
      error: e => { erreur = e; }
    });

    httpMock.expectOne('/api/auth/login').flush(reponseChauffeur);

    expect(utilisateur).toBe('non appelé');
    expect(erreur).not.toBeNull();
    expect(erreur.driverAccount).toBe(true);
    expect(erreur.error.message).toBe(DRIVER_WEB_LOGIN_REFUSED);
    expect(localStorage.getItem('auth_token')).toBeNull();
    expect(localStorage.getItem('refresh_token')).toBeNull();
    expect(localStorage.getItem('auth_user')).toBeNull();
    expect(service.getCurrentUserSync()).toBeNull();
    expect(service.isAuthenticated()).toBe(false);
  });

  it('relaie le refus 400 du serveur avec son message (et non « mot de passe incorrect »)', () => {
    let utilisateur: any = 'non appelé';
    let erreur: any = null;
    service.login('karim@transporttest.tn', 'Secret@2026').subscribe({
      next: u => { utilisateur = u; },
      error: e => { erreur = e; }
    });

    httpMock.expectOne('/api/auth/login').flush(
      { message: DRIVER_WEB_LOGIN_REFUSED },
      { status: 400, statusText: 'Bad Request' }
    );

    expect(utilisateur).toBe('non appelé');
    expect(erreur.driverAccount).toBe(true);
    expect(erreur.error.message).toBe(DRIVER_WEB_LOGIN_REFUSED);
    expect(localStorage.getItem('auth_token')).toBeNull();
  });

  it('un mot de passe faux (400 ordinaire) reste réduit à null, comme avant', () => {
    let utilisateur: any = 'non appelé';
    let erreur: any = null;
    service.login('amel@transporttest.tn', 'faux').subscribe({
      next: u => { utilisateur = u; },
      error: e => { erreur = e; }
    });

    httpMock.expectOne('/api/auth/login').flush(
      { message: 'Email ou mot de passe incorrect' },
      { status: 400, statusText: 'Bad Request' }
    );

    expect(utilisateur).toBeNull();
    expect(erreur).toBeNull();
  });

  it('le refus dit quelle version de l’application installer, et le texte du serveur est reconnu', () => {
    // Texte EXACT de LoginClients.DriverWebLoginRefused (API) : c'est ce que reçoivent le
    // site et l'application 1.1.1, qui ne se déclare pas « mobile ».
    const texteServeur = "Ce compte est réservé à l'application mobile Calypso, version 1.2 ou plus récente : "
      + "installez-la ou mettez-la à jour sur votre téléphone pour vous connecter.";
    expect(DRIVER_WEB_LOGIN_REFUSED).toBe(texteServeur);

    let erreur: any = null;
    service.login('karim@transporttest.tn', 'Secret@2026').subscribe({ error: e => { erreur = e; } });
    httpMock.expectOne('/api/auth/login').flush({ message: texteServeur }, { status: 400, statusText: 'Bad Request' });

    expect(erreur.driverAccount).toBe(true);
    expect(erreur.error.message).toContain('version 1.2 ou plus récente');
  });

  it('un compte ordinaire ouvre sa session avec accountType, relu tel quel au démarrage (F5)', () => {
    service.login('amel@transporttest.tn', 'Secret@2026').subscribe();
    httpMock.expectOne('/api/auth/login').flush({
      ...reponseChauffeur,
      user: { ...reponseChauffeur.user, id: 10, firstName: 'Amel', lastName: 'Gestion', email: 'amel@transporttest.tn', accountType: 'staff' }
    });

    expect(service.getCurrentUserSync()?.accountType).toBe('staff');
    expect(JSON.parse(localStorage.getItem('auth_user')!).accountType).toBe('staff');

    // Nouvelle instance = rechargement de la page : le drapeau ne doit pas disparaître.
    const rechargee = new AuthService(TestBed.inject(HttpClient), TestBed.inject(UserPreferencesService));
    expect(rechargee.getCurrentUserSync()?.accountType).toBe('staff');
  });
});
