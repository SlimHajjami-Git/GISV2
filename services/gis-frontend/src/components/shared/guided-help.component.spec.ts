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
 * Tutoriel pas à pas de l'écran Véhicules (Karim, 24/09/2026) : « quand il rentre
 * dans Véhicule, on lui dit de cliquer sur Nouveau véhicule, puis de renseigner le
 * nom, la plaque, la marque, le modèle […] puis de cliquer sur Ajouter ». Le client
 * remplit les champs au fur et à mesure. Nouvel utilisateur = première connexion.
 *
 * L'écran de test reproduit la fiche « Nouveau véhicule » : ses quatre champs, la
 * liste « -- Sélectionner -- » (valeur "null") et un « Ajouter » qui ferme la fiche
 * quand l'enregistrement réussit.
 */
@Component({ standalone: true, template: `
  @if (admin) { <button data-guide="vehicules-nouveau" (click)="ouverte = true">Nouveau véhicule</button> }
  @if (ouverte) {
    <form>
      <input data-guide="vehicule-nom">
      <input data-guide="vehicule-plaque">
      <select data-guide="vehicule-marque"><option value="null">-- Sélectionner --</option><option value="3">Renault</option></select>
      <select data-guide="vehicule-modele"><option value="null">-- Sélectionner --</option><option value="7">Clio</option></select>
      <button type="button" data-guide="vehicule-ajouter" (click)="enregistrer()">Ajouter</button>
    </form>
  }` })
class EcranVehicules {
  static admin = true;
  static refus = false;
  admin = EcranVehicules.admin;
  ouverte = false;
  /** Enregistrement réussi : la fiche se ferme. Refusé (alerte du serveur) : elle reste ouverte. */
  enregistrer(): void { if (!EcranVehicules.refus) { this.ouverte = false; } }
}

@Component({ standalone: true, template: `<p>Tableau de bord</p>` })
class EcranTableauDeBord {}

describe('Tutoriel pas à pas — un nouvel administrateur ouvre l\'écran Véhicules (GPA)', () => {
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
  const rafraichir = async (n = 4) => {
    harness.fixture.detectChanges();
    await images(n);
    harness.fixture.detectChanges();
    guide.detectChanges();
  };
  const ouvrir = async (url: string) => { await harness.navigateByUrl(url); await rafraichir(); };
  const el = (cible: string) => document.querySelector('[data-guide="' + cible + '"]') as HTMLInputElement;
  const bulle = () => guide.nativeElement.querySelector('.guide-bulle') as HTMLElement | null;
  const bouton = (libelle: string) => Array.from(guide.nativeElement.querySelectorAll('.guide-bulle button') as NodeListOf<HTMLButtonElement>)
    .find(b => b.textContent!.trim() === libelle);
  const etape = () => guide.componentInstance.etape?.id;

  /** Geste du client : clic sur l'élément encadré. */
  const cliquer = async (cible: string) => { el(cible).click(); await rafraichir(); };
  /** Geste du client : il remplit le champ encadré (texte ou liste). */
  const remplir = async (cible: string, valeur: string) => {
    const champ = el(cible);
    champ.value = valeur;
    champ.dispatchEvent(new Event('input', { bubbles: true }));
    champ.dispatchEvent(new Event('change', { bubbles: true }));
    await rafraichir(3);
  };
  const suivant = async () => { bouton('Suivant')!.click(); await rafraichir(); };
  /** Les quatre champs remplis : le tutoriel attend le clic sur « Ajouter ». */
  const jusquAAjouter = async () => {
    await cliquer('vehicules-nouveau');
    await remplir('vehicule-nom', 'Camion principal'); await suivant();
    await remplir('vehicule-plaque', 'AB-123-CD'); await suivant();
    await remplir('vehicule-marque', '3'); await suivant();
    await remplir('vehicule-modele', '7'); await suivant();
  };

  beforeEach(async () => {
    localStorage.clear();
    EcranVehicules.admin = true;
    EcranVehicules.refus = false;
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

  it('première bulle : « Cliquez sur Nouveau véhicule », le bouton reste cliquable, pas de « Suivant »', async () => {
    await ouvrir('/vehicles');

    expect(guide.componentInstance.mode).toBe('ecran');
    expect(etape()).toBe('tuto-vehicule-nouveau');
    expect(bulle()?.textContent).toContain('Écran Véhicules · Étape 1 sur 6');
    expect(bulle()?.textContent).toContain('Cliquez sur « Nouveau véhicule »');
    expect(bouton('Suivant')).toBeUndefined();
    // Le voile entoure le bouton en quatre bandes au lieu de le couvrir.
    expect(guide.nativeElement.querySelectorAll('.guide-voile.bande').length).toBe(4);
    expect(guide.nativeElement.querySelector('.guide-voile:not(.bande)')).toBeNull();
  });

  it('le clic du client sur « Nouveau véhicule » ouvre la fiche et passe au nom, curseur dans le champ', async () => {
    await ouvrir('/vehicles');
    await cliquer('vehicules-nouveau');

    expect(etape()).toBe('tuto-vehicule-nom');
    expect(document.activeElement).toBe(el('vehicule-nom'));
    // Pas de retour vers un bouton déjà cliqué : la fiche est ouverte.
    expect(bouton('Précédent')).toBeUndefined();
  });

  it('« Suivant » attend que le champ soit rempli ; Entrée vaut « Suivant » et n\'envoie pas le formulaire', async () => {
    await ouvrir('/vehicles');
    await cliquer('vehicules-nouveau');
    expect(bouton('Suivant')!.disabled).toBe(true);

    await remplir('vehicule-nom', 'Camion principal');
    expect(bouton('Suivant')!.disabled).toBe(false);

    const entree = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    el('vehicule-nom').dispatchEvent(entree);
    await rafraichir();
    expect(entree.defaultPrevented).toBe(true);
    expect(etape()).toBe('tuto-vehicule-plaque');
  });

  it('marque et modèle absents de la liste (ou liste vide) : « Suivant » reste actif, le client continue', async () => {
    // Karim, 24/09/2026 : « le même problème avec Modèle (il est vide), fais-le
    // passer pour continuer ». 8 des 27 marques de production n'ont aucun modèle.
    await ouvrir('/vehicles');
    await cliquer('vehicules-nouveau');
    await remplir('vehicule-nom', 'Camion principal'); await suivant();
    await remplir('vehicule-plaque', 'AB-123-CD'); await suivant();
    expect(etape()).toBe('tuto-vehicule-marque');

    await remplir('vehicule-marque', 'null');          // rien choisi
    expect(bouton('Suivant')!.disabled).toBe(false);
    await suivant();
    expect(etape()).toBe('tuto-vehicule-modele');

    await remplir('vehicule-modele', 'null');          // liste vide ou modèle absent
    const entree = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true });
    el('vehicule-modele').dispatchEvent(entree);       // Entrée passe aussi
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-ajouter');
  });

  it('nom et plaque restent obligatoires : « Suivant » grisé tant qu\'ils sont vides', async () => {
    await ouvrir('/vehicles');
    await cliquer('vehicules-nouveau');
    expect(bouton('Suivant')!.disabled).toBe(true);
    await remplir('vehicule-nom', 'Camion principal'); await suivant();
    expect(bouton('Suivant')!.disabled).toBe(true);
  });

  it('parcours complet : le véhicule est ajouté, le tutoriel se termine et ne revient plus', async () => {
    await ouvrir('/vehicles');
    await jusquAAjouter();

    expect(etape()).toBe('tuto-vehicule-ajouter');
    expect(bulle()?.textContent).toContain('Cliquez sur « Ajouter »');
    expect(bouton('Terminer')).toBeUndefined();   // c'est le clic sur « Ajouter » qui termine
    expect(bouton('Précédent')).toBeDefined();    // on peut revenir corriger le modèle

    await cliquer('vehicule-ajouter');            // enregistrement réussi : la fiche se ferme
    await rafraichir();
    expect(guide.componentInstance.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('enregistrement refusé par le serveur : la fiche reste ouverte, le tutoriel reste sur « Ajouter »', async () => {
    EcranVehicules.refus = true;
    await ouvrir('/vehicles');
    await jusquAAjouter();

    await cliquer('vehicule-ajouter');
    await rafraichir();
    expect(guide.componentInstance.actif).toBe(true);
    expect(etape()).toBe('tuto-vehicule-ajouter');
  });

  it('non-administrateur : pas de tutoriel (il n\'a pas le bouton « Nouveau véhicule »)', async () => {
    compte = { ...compte, isCompanyAdmin: false };
    EcranVehicules.admin = false;
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('« Passer » : il ne revient plus, même en rouvrant l\'écran', async () => {
    await ouvrir('/vehicles');
    guide.componentInstance.passer();
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
    expect(etape()).toBe('tuto-vehicule-nouveau');
  });

  it('le drapeau de première connexion est retenu : le rafraîchissement du jeton ne le fait pas perdre', async () => {
    await ouvrir('/dashboard');            // la connexion dépose le client ici
    compte = { id: 'u-nouveau', companyName: 'Transports Martin', isCompanyAdmin: true }; // jeton rafraîchi
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(true);
  });

  it('un client déjà installé (pas une première connexion) ne le voit pas', async () => {
    compte = { id: 'u-ancien', firstLogin: false, companyName: 'Transports Martin', isCompanyAdmin: true };
    help.fermerGuide(true);
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('dérogation provisoire : la société « Belive GPA » le voit en local, première connexion ou non', async () => {
    compte = { id: 'u-karim', firstLogin: false, companyName: 'Belive GPA', isCompanyAdmin: true };
    help.fermerGuide(true);
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(true);
  });

  it('offre GPS : pas de tutoriel Véhicules pendant le pilote', async () => {
    modulesGps = true;
    abonnementGps = true;
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it("société GPS, utilisateur sans accès à la carte : c'est l'abonnement qui compte, pas de tutoriel GPA", async () => {
    abonnementGps = true;
    modulesGps = false;
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('tant que la visite de première connexion doit être proposée, elle passe avant', async () => {
    help.reinitialiserGuide();
    help.fermerGuide(false);                 // Échap : non vue
    (help as any).dejaProposee.clear();      // nouvelle session : rien encore proposé
    expect(help.doitProposerLeGuide()).toBe(true);
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('la visite de première connexion demandée pendant le tutoriel prend la main', async () => {
    await ouvrir('/vehicles');
    expect(guide.componentInstance.mode).toBe('ecran');
    help.reinitialiserGuide();
    guide.detectChanges();
    expect(guide.componentInstance.mode).toBe('parcours');
    expect(guide.componentInstance.actif).toBe(true);
  });

  it('« Terminer » de la visite dépose sur Véhicules sans enchaîner le tutoriel ; il vient au prochain accès', async () => {
    help.reinitialiserGuide();
    guide.detectChanges();
    const c = guide.componentInstance;
    c.index = c.etapes.length - 1;           // dernière étape : « Recevez vos alertes par e-mail »
    expect(c.etapes[c.index].routeApresFin).toBe('/vehicles');
    c.suivant();                             // « Terminer »
    await rafraichir();
    expect(TestBed.inject(Router).url).toBe('/vehicles');
    expect(c.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(c.actif).toBe(true);
    expect(c.mode).toBe('ecran');
  });
});
