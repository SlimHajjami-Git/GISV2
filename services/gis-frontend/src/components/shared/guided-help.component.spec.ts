import { Component, OnInit, inject } from '@angular/core';
import { TestBed, ComponentFixture } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { BehaviorSubject } from 'rxjs';
import { GuidedHelpComponent } from './guided-help.component';
import { HelpService } from '../../services/help.service';
import { AuthService } from '../../services/auth.service';
import { PermissionService } from '../../services/permission.service';

/**
 * Visite guidée — relecture du 22/09/2026.
 *
 * Chaque page porte sa propre <app-layout>, qui PROPOSE la visite dans son
 * ngOnInit. La visite, qui change de page d'une étape à l'autre, était montée
 * dans cette app-layout : « Suivant » détruisait l'instance, la nouvelle
 * repartait de l'étape 1 et renvoyait au tableau de bord. Les étapes 2 à 4
 * n'étaient jamais montrées, et l'instance détruite continuait de naviguer.
 *
 * Les pages de test reproduisent la proposition de l'app-layout d'avant, sans
 * délai : c'est le cas le plus dur.
 */
declare const require: (m: string) => any;
declare const __dirname: string;

@Component({ standalone: true, template: `<button data-guide="menu-flotte">Exploitation</button>` })
class PageTableauDeBord implements OnInit {
  private help = inject(HelpService);
  ngOnInit(): void { if (this.help.doitProposerLeGuide()) { this.help.ouvrirGuide(); } }
}

@Component({ standalone: true, template: `<div data-guide="vehicules-liste">Liste</div><button data-guide="vehicules-nouveau">Nouveau véhicule</button>` })
class PageVehicules implements OnInit {
  private help = inject(HelpService);
  ngOnInit(): void { if (this.help.doitProposerLeGuide()) { this.help.ouvrirGuide(); } }
}

@Component({ standalone: true, template: `<div data-guide="suivi-liste">Liste</div>` })
class PageSuivi {}

@Component({ standalone: true, template: `<div data-guide="rapports-type">Type de rapport</div>` })
class PageRapports {}

// Parcours GPA du 23/09/2026 : chauffeurs, échéances, puis les deux étapes de
// l'écran Entretiens (créer un modèle, l'affecter) — deux cibles sur une même page.
@Component({ standalone: true, template: `<button data-guide="chauffeurs-nouveau">Nouveau chauffeur</button>` })
class PageChauffeurs {}

@Component({ standalone: true, template: `<div data-guide="echeances-compteurs">Compteurs</div>` })
class PageEcheances {}

@Component({ standalone: true, template: `<button data-guide="entretiens-affecter">Affecter</button><button data-guide="entretiens-nouveau-modele">Nouveau modele</button>` })
class PageEntretiens {}

// Alertes par e-mail (Karim, 23/09/2026) : l'onglet de « Gestion des Utilisateurs ».
@Component({ standalone: true, template: `<button data-guide="alertes-email-onglet">Alertes par email</button>` })
class PageUtilisateurs {}
describe('Visite guidée — elle avance d\'une page à l\'autre', () => {
  let utilisateur: BehaviorSubject<any>;
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  let help: HelpService;

  const attendre = async () => {
    for (let i = 0; i < 5; i++) {
      await harness.fixture.whenStable();
      harness.fixture.detectChanges();
      guide.detectChanges();
    }
  };

  beforeEach(async () => {
    localStorage.clear();
    utilisateur = new BehaviorSubject<any>({ id: 'u-guide' });
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: PageTableauDeBord },
          { path: 'vehicles', component: PageVehicules },
          { path: 'monitoring', component: PageSuivi },
          { path: 'reports', component: PageRapports },
          { path: 'drivers', component: PageChauffeurs },
          { path: 'echeances', component: PageEcheances },
          { path: 'entretien-programmable', component: PageEntretiens },
          { path: 'users', component: PageUtilisateurs },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => utilisateur.value,
          getCurrentUser: () => utilisateur.asObservable()
        } },
        { provide: PermissionService, useValue: { hasModuleAccess: () => true, hasReportAccess: () => true } }
      ]
    });
    help = TestBed.inject(HelpService);

    // Monté UNE fois, hors des pages, comme dans main.ts.
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
    await attendre();
  });

  afterEach(() => {
    guide.destroy();
    jest.restoreAllMocks();
  });

  // Tous les modules sont ouverts ici (hasModuleAccess => true) : c'est le
  // parcours GPS qui est joué — sans « Ajoutez votre premier véhicule ».
  it('s\'ouvre sur l\'étape 1 au tableau de bord', () => {
    expect(guide.componentInstance.actif).toBe(true);
    expect(guide.componentInstance.etape?.id).toBe('bienvenue-gps');
  });

  it('« Suivant » mène à l\'étape 2 sur /vehicles — la nouvelle page ne relance pas la visite', async () => {
    guide.componentInstance.suivant();
    await attendre();

    expect(TestBed.inject(Router).url).toBe('/vehicles');
    expect(guide.componentInstance.actif).toBe(true);
    expect(guide.componentInstance.index).toBe(1);
    expect(guide.componentInstance.etape?.id).toBe('vehicules-en-place');
  });

  it('va au bout des neuf étapes (offre GPS complète), puis ne se repropose plus', async () => {
    // Ordre voulu par Karim (23/09/2026) pour le GPS : véhicules en place,
    // chauffeurs, carte, échéances, programme d'entretien, affectation,
    // alertes par e-mail, puis le premier rapport.
    // Deux étapes de suite sur /entretien-programmable : la seconde ne doit pas
    // renaviguer ni se perdre.
    for (const attendue of ['vehicules-en-place', 'ajouter-chauffeurs', 'voir-la-carte', 'echeances', 'entretien-modele', 'entretien-affecter', 'alertes-email', 'premier-rapport']) {
      guide.componentInstance.suivant();
      await attendre();
      expect(guide.componentInstance.etape?.id).toBe(attendue);
    }
    guide.componentInstance.suivant(); // « Terminer »
    await attendre();
    expect(guide.componentInstance.actif).toBe(false);
    expect(help.doitProposerLeGuide()).toBe(false);
    // Fin du parcours GPS : le client est depose sur « Suivi en direct » pour
    // voir ses vehicules (Karim, 23/09/2026).
    expect(TestBed.inject(Router).url).toBe('/monitoring');
  });

  it('Échap puis changement de page : la visite ne revient pas et ne ramène pas au tableau de bord', async () => {
    guide.componentInstance.auEchap();
    await harness.navigateByUrl('/vehicles');
    await attendre();

    expect(guide.componentInstance.actif).toBe(false);
    expect(TestBed.inject(Router).url).toBe('/vehicles');
  });

  it('une instance détruite pendant la navigation ne cherche plus rien et ne navigue plus', async () => {
    const recherche = jest.spyOn(document, 'querySelector');
    const naviguer = jest.spyOn(TestBed.inject(Router), 'navigateByUrl');

    guide.componentInstance.suivant(); // navigation vers /vehicles lancée
    guide.destroy();                   // la page de l'ancienne instance disparaît
    recherche.mockClear();
    await harness.fixture.whenStable();
    await new Promise(r => setTimeout(r, 50));

    const ciblesCherchees = recherche.mock.calls.filter(([s]) => String(s).includes('data-guide'));
    expect(ciblesCherchees).toEqual([]);
    expect(naviguer).toHaveBeenCalledTimes(1);
  });

  // Karim, 24/09/2026 : « Nouveau chauffeur » et « Nouveau modele » encadrés
  // plus bas, dans la colonne Actions. Le cadre était calculé UNE fois, à
  // l'apparition du bouton, puis la page bougeait encore (défilement doux,
  // tableau qui se remplit) : il restait là où le bouton ÉTAIT.
  describe('le cadre bleu reste sur sa cible', () => {
    const images = (n: number) => new Promise<void>(fin => {
      let i = 0;
      const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
      requestAnimationFrame(image);
    });
    const rectangle = (top: number) => ({
      top, left: 900, width: 150, height: 36, bottom: top + 36, right: 1050, x: 900, y: top, toJSON() {}
    }) as DOMRect;
    const halo = () => guide.nativeElement.querySelector('.guide-halo') as HTMLElement;

    it('il suit le bouton quand la page bouge après la première mesure', async () => {
      const bouton = document.querySelector('[data-guide="menu-flotte"]') as HTMLElement;
      let top = 400;
      jest.spyOn(bouton, 'getBoundingClientRect').mockImplementation(() => rectangle(top));

      await images(3);
      expect(halo().style.top).toBe('394px');

      top = 60; // la page s'est tassée : le bouton est remonté
      await images(3);
      expect(halo().style.top).toBe('54px');
      expect(halo().style.left).toBe('894px');
    });

    it('un bouton resté hors de l\'écran est ramené une seule fois', async () => {
      const bouton = document.querySelector('[data-guide="menu-flotte"]') as HTMLElement;
      jest.spyOn(bouton, 'getBoundingClientRect').mockImplementation(() => rectangle(-200));
      const ramener = jest.fn();
      bouton.scrollIntoView = ramener;

      await images(45);
      expect(ramener).toHaveBeenCalledTimes(1);
    });
  });

  it('se ferme à la déconnexion, sans être marquée comme vue', async () => {
    utilisateur.next(null);
    guide.detectChanges();
    expect(guide.componentInstance.actif).toBe(false);
    utilisateur.next({ id: 'u-guide' });
    expect(help.doitProposerLeGuide()).toBe(false); // déjà montrée dans cette session
  });
});

describe('Visite guidée — montage', () => {
  const fs = require('fs');
  const path = require('path');
  const lire = (fichier: string): string => fs.readFileSync(path.join(__dirname, fichier), 'utf8')
    .replace(/<!--[\s\S]*?-->/g, '');

  it('est montée une seule fois, dans le composant racine (main.ts)', () => {
    expect(lire('../../main.ts')).toContain('<app-guided-help></app-guided-help>');
  });

  it('n\'est plus montée dans <app-layout>, recréée à chaque page', () => {
    expect(lire('app-layout.component.ts')).not.toContain('<app-guided-help');
  });

  it('app-layout annule sa proposition différée quand la page est quittée', () => {
    const layout = lire('app-layout.component.ts');
    expect(layout).toContain('clearTimeout(this.minuteurGuide)');
    expect(layout).toContain('this.help.proposerLeGuide()');
  });
});

/**
 * Guides des écrans (Karim, 24/09/2026) : « pour un nouvel utilisateur, on ajoute le
 * tuto de la page à chaque fois qu'il accède à cette page » — nouvel utilisateur =
 * première connexion. Pilote : l'écran Véhicules de l'offre GPA.
 */
@Component({ standalone: true, template: `
  <div data-guide="vehicules-recherche">Recherche</div>
  @if (admin) { <button data-guide="vehicules-nouveau">Nouveau véhicule</button> }
  <div data-guide="vehicules-compteurs">Compteurs</div>
  <div data-guide="vehicules-liste">
    @if (lignes) { <div data-guide="vehicules-ligne">Commercial 01</div> } @else { <div data-guide="vehicules-vide">Aucun véhicule trouvé</div> }
  </div>` })
class EcranVehicules {
  static admin = true; static lignes = true;
  admin = EcranVehicules.admin; lignes = EcranVehicules.lignes;
}

@Component({ standalone: true, template: `<p>Tableau de bord</p>` })
class EcranTableauDeBord {}

describe('Guides des écrans — un nouvel utilisateur ouvre l\'écran Véhicules (GPA)', () => {
  let compte: any;
  let modulesGps: boolean;
  let abonnementGps: boolean;
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  let help: HelpService;

  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const ouvrir = async (url: string, n = 4) => {
    await harness.navigateByUrl(url);
    await images(n);
    harness.fixture.detectChanges();
    guide.detectChanges();
  };
  const bulle = () => guide.nativeElement.querySelector('.guide-bulle') as HTMLElement | null;

  beforeEach(async () => {
    localStorage.clear();
    EcranVehicules.admin = true;
    EcranVehicules.lignes = true;
    modulesGps = false;
    abonnementGps = false;
    // Première connexion : le drapeau vient de la réponse de /auth/login.
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: EcranTableauDeBord },
          { path: 'vehicles', component: EcranVehicules },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring' || modulesGps,
          abonnementComprend: (m: string) => m !== 'monitoring' || abonnementGps,
          hasReportAccess: () => true
        } }
      ]
    });
    help = TestBed.inject(HelpService);
    // La visite de première connexion est déjà faite : elle passerait avant.
    help.fermerGuide(true);
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
  });

  afterEach(() => guide.destroy());

  it('le guide de l\'écran s\'ouvre à l\'arrivée sur Véhicules, avec ses quatre bulles', async () => {
    await ouvrir('/vehicles');

    const c = guide.componentInstance;
    expect(c.actif).toBe(true);
    expect(c.mode).toBe('ecran');
    expect(c.etapes.map(e => e.cible))
      .toEqual(['vehicules-nouveau', 'vehicules-compteurs', 'vehicules-ligne', 'vehicules-recherche']);
    expect(bulle()?.textContent).toContain('Écran Véhicules');
    expect(bulle()?.textContent).toContain('Ajoutez vos véhicules');
  });

  it('« Passer » : il ne revient plus, même en rouvrant l\'écran', async () => {
    await ouvrir('/vehicles');
    guide.componentInstance.passer();

    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('« Terminer » au bout des bulles : il ne revient plus non plus', async () => {
    await ouvrir('/vehicles');
    for (let i = 0; i < 4; i++) { guide.componentInstance.suivant(); await images(3); }
    expect(guide.componentInstance.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('Échap le ferme pour cette fois : il revient au prochain accès à l\'écran', async () => {
    await ouvrir('/vehicles');
    guide.componentInstance.auEchap();
    expect(guide.componentInstance.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(true);
    expect(guide.componentInstance.mode).toBe('ecran');
  });

  it('le drapeau de première connexion est retenu : le rafraîchissement du jeton ne le fait pas perdre', async () => {
    await ouvrir('/dashboard');            // la connexion dépose le client ici
    compte = { id: 'u-nouveau', companyName: 'Transports Martin' }; // jeton rafraîchi : plus de firstLogin
    await ouvrir('/vehicles');
    expect(guide.componentInstance.mode).toBe('ecran');
    expect(guide.componentInstance.actif).toBe(true);
  });

  it('un client déjà installé (pas une première connexion) ne le voit pas', async () => {
    compte = { id: 'u-ancien', firstLogin: false, companyName: 'Transports Martin' };
    help.fermerGuide(true); // sa visite de première connexion est faite : seul le guide d'écran est en jeu
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('dérogation provisoire : la société « Belive GPA » le voit en local, première connexion ou non', async () => {
    compte = { id: 'u-karim', firstLogin: false, companyName: 'Belive GPA' };
    help.fermerGuide(true); // sa visite de première connexion est faite : seul le guide d'écran est en jeu
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(true);
  });

  it('offre GPS : pas de guide Véhicules pendant le pilote', async () => {
    modulesGps = true;
    abonnementGps = true;
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('tant que la visite de première connexion doit être proposée, elle passe avant', async () => {
    help.reinitialiserGuide();              // la visite repart…
    help.fermerGuide(false);                // …mais Échap : non vue, et pas reproposée avant rechargement
    const neuf = TestBed.inject(HelpService);
    expect(neuf.doitProposerLeGuide()).toBe(false);
    // Nouvelle session : rien n'a encore été proposé, la visite n'est pas terminée.
    (neuf as any).dejaProposee.clear();
    expect(neuf.doitProposerLeGuide()).toBe(true);
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('la visite de première connexion demandée pendant un guide d\'écran prend la main', async () => {
    await ouvrir('/vehicles');
    expect(guide.componentInstance.mode).toBe('ecran');
    help.reinitialiserGuide();
    guide.detectChanges();
    expect(guide.componentInstance.mode).toBe('parcours');
    expect(guide.componentInstance.actif).toBe(true);
  });

  it("utilisateur non administrateur : la bulle « Nouveau véhicule » est retirée d'emblée — « Étape 1 sur 3 », sans attente", async () => {
    // Relecture du 24/09/2026 : sautée faute de cible, elle faisait attendre ~3 s
    // puis affichait « Étape 2 sur 4 » en première bulle.
    compte = { ...compte, isCompanyAdmin: false };
    EcranVehicules.admin = false;
    await ouvrir('/vehicles');
    const c = guide.componentInstance;
    expect(c.etapes.map(e => e.cible)).toEqual(['vehicules-compteurs', 'vehicules-ligne', 'vehicules-recherche']);
    expect(c.etape?.cible).toBe('vehicules-compteurs');
    expect(bulle()?.textContent).toContain('Étape 1 sur 3');
    expect(c.aUnePrecedente()).toBe(false);
  });

  it('liste vide (nouveau client) : la bulle 3 vise « Aucun véhicule trouvé » avec son propre texte', async () => {
    EcranVehicules.lignes = false;
    await ouvrir('/vehicles');
    guide.componentInstance.suivant(); await images(3);
    guide.componentInstance.suivant(); await images(3);
    guide.detectChanges();
    expect(guide.componentInstance.etape?.id).toBe('vehicules-liste-gpa');
    expect(bulle()?.textContent).toContain("Vos véhicules s'afficheront ici");
    expect(bulle()?.textContent).not.toContain('Chaque ligne donne la plaque');
  });

  it('avec des véhicules : la bulle 3 vise la première ligne et décrit les lignes', async () => {
    await ouvrir('/vehicles');
    guide.componentInstance.suivant(); await images(3);
    guide.componentInstance.suivant(); await images(3);
    guide.detectChanges();
    expect(bulle()?.textContent).toContain('Chaque ligne donne la plaque');
  });

  it('« Terminer » du parcours dépose sur Véhicules sans enchaîner le guide ; il vient au prochain accès', async () => {
    help.reinitialiserGuide();                  // parcours GPA relancé
    guide.detectChanges();
    const c = guide.componentInstance;
    expect(c.mode).toBe('parcours');
    c.index = c.etapes.length - 1;              // dernière étape : « Recevez vos alertes par e-mail »
    expect(c.etapes[c.index].routeApresFin).toBe('/vehicles');
    c.suivant();                                // « Terminer »
    await images(4);
    expect(TestBed.inject(Router).url).toBe('/vehicles');
    expect(c.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(c.actif).toBe(true);
    expect(c.mode).toBe('ecran');
  });

  it("société GPS, utilisateur sans accès à la carte : c'est l'abonnement qui compte, pas de guide GPA", async () => {
    abonnementGps = true;                       // la société a l'offre GPS…
    modulesGps = false;                         // …mais cet utilisateur n'a pas le droit « Suivi »
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });
});
