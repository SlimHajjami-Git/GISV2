import { TestBed } from '@angular/core/testing';
import { RegionService } from './region.service';

/**
 * Aiguillage de région — relecture du 10/10/2026.
 *
 * La vitrine /fr est passée sous WordPress : son en-tête n'est plus du code
 * Angular, donc plus personne ne pose la marque de session « le visiteur est
 * sur le parcours européen ». Sans remplaçant, un prospect français cliquant
 * « Essayer gratuitement » sur la vitrine recevait le formulaire d'inscription
 * tunisien, en dinars. Le référent prend le relais : la vitrine et
 * l'application partagent le domaine, et un référent de MÊME origine porte le
 * chemin complet.
 *
 * Ces tests fixent les deux moitiés de la règle : on fait confiance au référent
 * de notre domaine, à lui seul, et uniquement sous /fr.
 */
describe('RegionService — provenance de la vitrine', () => {
  function referent(valeur: string): void {
    Object.defineProperty(document, 'referrer', { value: valeur, configurable: true });
  }

  function service(): RegionService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [RegionService] });
    return TestBed.inject(RegionService);
  }

  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    referent('');
  });

  it('un visiteur venu de la vitrine est européen', () => {
    referent(window.location.origin + '/fr/tarifs/');
    expect(service().isEurope).toBe(true);
  });

  it('la racine de la vitrine compte aussi, avec ou sans barre finale', () => {
    referent(window.location.origin + '/fr');
    expect(service().isEurope).toBe(true);
    sessionStorage.clear();
    referent(window.location.origin + '/fr/');
    expect(service().isEurope).toBe(true);
  });

  it('la provenance est mémorisée pour la session, pour survivre au clic suivant', () => {
    referent(window.location.origin + '/fr/');
    expect(service().isEurope).toBe(true);
    // Deuxième page de l'application : le référent est désormais interne,
    // la session doit garder le parcours européen.
    referent(window.location.origin + '/inscription');
    expect(service().isEurope).toBe(true);
  });

  it('un référent d\'un AUTRE domaine ne décide de rien, même en /fr', () => {
    referent('https://exemple-tiers.test/fr/tarifs/');
    expect(service().isEurope).toBe(false);
  });

  it('un visiteur venu d\'ailleurs sur notre domaine reste sur l\'accueil habituel', () => {
    referent(window.location.origin + '/login');
    expect(service().isEurope).toBe(false);
  });

  it('sans référent, rien ne change : accès direct, favori, courriel', () => {
    referent('');
    expect(service().isEurope).toBe(false);
  });

  it('un chemin qui COMMENCE par fr sans en être ne compte pas', () => {
    referent(window.location.origin + '/france-autre');
    expect(service().isEurope).toBe(false);
  });

  it('le forçage explicite garde la priorité sur le référent', () => {
    localStorage.setItem('calypso_region_override', 'default');
    referent(window.location.origin + '/fr/');
    expect(service().isEurope).toBe(false);
  });
});
