import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { DeviceCheckComponent, messageErreurDiagnostic } from './device-check.component';
import { authInterceptor } from '../services/auth.interceptor';
import { routes } from '../app.routes';

/**
 * Diagnostic boîtier (/device-check) — deux passes à connaître ensemble.
 *
 * 23/09/2026, FERMETURE. L'API /api/devicecheck était la SEULE route sans [Authorize] :
 * sans jeton, elle rendait position, contact, compteur et carburant de n'importe quel
 * véhicule de n'importe quelle société, à partir d'une PLAQUE lue dans la rue.
 *
 * 28/09/2026, RÉOUVERTURE MESURÉE (décision de Slim). Les installateurs travaillent sur
 * le terrain sans compte, et leur imposer une connexion n'avait pas de sens. L'écran a
 * donc deux modes, et c'est le mode qui protège, plus le garde de route :
 *   • sans session → /devicecheck/status, IMEI strict, réponse réduite à « enregistré /
 *     remonte des trames ». Aucune position, aucune plaque, aucune société ;
 *   • avec session → /devicecheck/lookup, réponse complète, bornée à la société de
 *     l'appelant et à sa portée véhicule.
 * Ce fichier fige les deux chemins : c'est le seul endroit qui empêche de « simplifier »
 * l'écran en appelant lookup sans jeton, ce qui rouvrirait le trou mot pour mot.
 */
describe('DeviceCheckComponent — public réduit, complet pour les comptes connectés', () => {
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

  it("la route /device-check est PUBLIQUE : le cloisonnement est côté API, pas dans un garde", () => {
    const route = routes.find(r => r.path === 'device-check');
    expect(route).toBeDefined();
    // Remettre un AuthGuard ici n'ajouterait aucune sécurité — la route publique de l'API
    // ne rend rien de confidentiel — et priverait le terrain de l'outil.
    expect(route!.canActivate).toBeUndefined();
  });

  // ───────── Avec session : la route complète, inchangée ─────────

  it("connecté : appelle lookup avec la saisie telle quelle ET porte le jeton", () => {
    const jeton = jetonValide();
    localStorage.setItem('auth_token', jeton);
    component.query = '  123 TU 4567 ';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/lookup'));
    expect(requete.request.params.get('q')).toBe('123 TU 4567');
    expect(requete.request.headers.get('Authorization')).toBe(`Bearer ${jeton}`);

    requete.flush({ found: true, hasGps: true, connected: true });
    expect(component.modePublic).toBe(false);
    expect(component.result).toEqual({ found: true, hasGps: true, connected: true });
    expect(component.loading).toBe(false);
    expect(component.error).toBe('');
  });

  it('connecté, 401 : message clair et renvoi vers la connexion, jamais un échec muet', () => {
    localStorage.setItem('auth_token', jetonValide());
    component.query = 'MAT-A';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/lookup'))
      .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(component.error).toBe(messageErreurDiagnostic(401));
    expect(router.navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/device-check' } });
  });

  it('connecté, 403 : message explicite, pas de redirection', () => {
    localStorage.setItem('auth_token', jetonValide());
    component.query = 'MAT-A';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/lookup'))
      .flush({ message: 'Interdit' }, { status: 403, statusText: 'Forbidden' });

    expect(component.error).toBe(messageErreurDiagnostic(403));
    expect(router.navigate).not.toHaveBeenCalled();
  });

  // ───────── Sans session : la route publique réduite ─────────

  it("sans session : appelle status avec l'IMEI, et JAMAIS lookup", () => {
    component.query = ' 351234567890123 ';

    component.search();

    http.expectNone(r => r.url.endsWith('/devicecheck/lookup'));
    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/status'));
    expect(requete.request.params.get('q')).toBe('351234567890123');
    expect(requete.request.headers.has('Authorization')).toBe(false);

    requete.flush({ found: true, reporting: true, imei: '***0123', minutesSinceLastFrame: 3 });
    expect(component.modePublic).toBe(true);
    expect(component.result.reporting).toBe(true);
    expect(component.error).toBe('');
  });

  it("sans session : les espaces et tirets de l'étiquette sont retirés de l'IMEI", () => {
    component.query = '35 1234-5678 90123';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/status'));
    expect(requete.request.params.get('q')).toBe('351234567890123');
    requete.flush({ found: false });
  });

  // ───────── Le matricule, clé réelle des techniciens (29/09/2026) ─────────

  it("sans session : un MATRICULE alphanumérique part tel quel, sans être charcuté", () => {
    component.query = ' NR08G1075 ';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/status'));
    // Le nettoyage ne vise que les IMEI recopiés avec des espaces : un matricule
    // alphanumérique doit arriver intact au serveur.
    expect(requete.request.params.get('q')).toBe('NR08G1075');
    requete.flush({ found: true, reporting: true, imei: '***********1075' });
    expect(component.result.reporting).toBe(true);
  });

  it('connecté : une PLAQUE garde ses espaces, qui sont signifiants en base', () => {
    localStorage.setItem('auth_token', jetonValide());
    component.query = '  233 TU 5102 ';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/lookup'));
    expect(requete.request.params.get('q')).toBe('233 TU 5102');
    requete.flush({ found: true, hasGps: true, connected: true });
  });

  it("connecté : un IMEI recopié AVEC des espaces est nettoyé lui aussi", () => {
    localStorage.setItem('auth_token', jetonValide());
    component.query = '8601 4107 6687 244';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/lookup'));
    // Mesuré le 28/09 sur la production : avec les espaces, la route authentifiée
    // répondait « introuvable » même à un compte qui voit toute la plate-forme.
    expect(requete.request.params.get('q')).toBe('860141076687244');
    requete.flush({ found: true, hasGps: true, connected: true });
  });

  it("connecté : une plaque ENTIÈREMENT NUMÉRIQUE garde ses espaces", () => {
    localStorage.setItem('auth_token', jetonValide());
    component.query = '123 4567';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/lookup'));
    // Cette forme de plaque n'existe pas en Tunisie mais ailleurs si, et le produit a
    // déjà un déploiement algérien : la nettoyer collerait les chiffres et ne trouverait
    // plus rien. Sur le chemin authentifié on ne nettoie donc que les IMEI de 15 chiffres.
    expect(requete.request.params.get('q')).toBe('123 4567');
    requete.flush({ found: false });
  });

  it("sans session : un matricule mal recopié avec un espace est rattrapé", () => {
    component.query = 'NR08 G1075';

    component.search();

    const requete = http.expectOne(r => r.url.endsWith('/devicecheck/status'));
    // Sans session la plaque n'est jamais une clé valide : on peut nettoyer largement.
    expect(requete.request.params.get('q')).toBe('NR08G1075');
    requete.flush({ found: true, reporting: true });
  });

  it("sans session : un matricule porté par deux boîtiers est signalé, pas tranché", () => {
    component.query = 'NR08G0885';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/status')).flush({
      found: true,
      ambiguous: true,
      message: 'Plusieurs boîtiers portent ce matricule. Cherchez par IMEI pour lever le doute.'
    });

    expect(component.result.ambiguous).toBe(true);
    // Ce n'est pas une erreur : c'est une réponse légitime, à afficher telle quelle.
    expect(component.error).toBe('');
  });

  it('sans session, 400 : le message du serveur est relayé tel quel', () => {
    component.query = '111 TU 1';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/status'))
      .flush({ error: "Saisissez l'IMEI du boîtier : 15 chiffres, sans espace." },
             { status: 400, statusText: 'Bad Request' });

    expect(component.error).toContain('15 chiffres');
    expect(router.navigate).not.toHaveBeenCalled();
  });

  it("sans session, 401 : aucun renvoi vers la connexion — il n'y a pas de session à retrouver", () => {
    component.query = '351234567890123';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/status'))
      .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(router.navigate).not.toHaveBeenCalled();
    expect(component.error).not.toContain('Reconnectez-vous');
  });

  it('sans session, 429 : le plafond se dit en clair', () => {
    component.query = '351234567890123';

    component.search();

    http.expectOne(r => r.url.endsWith('/devicecheck/status'))
      .flush({ message: 'trop' }, { status: 429, statusText: 'Too Many Requests' });

    expect(component.error).toContain('Trop de recherches');
  });

  // ───────── Communs ─────────

  it('une recherche vide ne part pas', () => {
    component.query = '   ';
    component.search();
    http.expectNone(() => true);
    expect(component.loading).toBe(false);
  });

  it('chaque statut a un message lisible, et 401 dépend du mode', () => {
    expect(messageErreurDiagnostic(401)).toContain('Reconnectez-vous');
    expect(messageErreurDiagnostic(401, true)).not.toContain('Reconnectez-vous');
    expect(messageErreurDiagnostic(403)).toContain("n'a pas accès");
    expect(messageErreurDiagnostic(0)).toContain('Serveur injoignable');
    expect(messageErreurDiagnostic(429)).toContain('Trop de recherches');
    expect(messageErreurDiagnostic(400, true, 'Message du serveur')).toBe('Message du serveur');
    expect(messageErreurDiagnostic(400, true, '   ')).toContain('15 chiffres');
    expect(messageErreurDiagnostic(500)).toContain('erreur 500');
  });
});
