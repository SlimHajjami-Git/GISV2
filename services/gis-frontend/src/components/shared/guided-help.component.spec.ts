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

// Tutoriels « tous les champs » (24/09/2026) : des parcours de 14 à 21 bulles.
jest.setTimeout(20000);

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
        { provide: PermissionService, useValue: { hasModuleAccess: () => true, abonnementComprend: () => true, hasReportAccess: () => true } }
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
      <!-- Le reste de la fiche, prérempli comme la vraie (resetForm) ; le leasing n'est pas affiché en « Achat ». -->
      <input type="number" data-guide="vehicule-annee" value="2026">
      <select data-guide="vehicule-type"><option value="citadine" selected>Citadine</option></select>
      <select data-guide="vehicule-statut"><option value="available" selected>Disponible</option></select>
      <input type="number" data-guide="vehicule-compteur" value="0">
      <input data-guide="vehicule-couleur">
      <select data-guide="vehicule-carburant"><option value="" selected>-- Sélectionner --</option><option value="diesel">Diesel</option></select>
      <input type="number" data-guide="vehicule-reservoir">
      <input type="date" data-guide="vehicule-mise-en-circulation">
      <select data-guide="vehicule-acquisition" (change)="acquisition = $any($event.target).value">
        <option value="purchase" selected>Achat</option><option value="leasing">Crédit</option>
      </select>
      <input type="date" data-guide="vehicule-date-achat">
      <input type="number" data-guide="vehicule-prix-achat">
      @if (acquisition === 'leasing') {
        <input type="number" data-guide="vehicule-traite">
        <input type="number" data-guide="vehicule-duree-leasing">
        <input type="date" data-guide="vehicule-debut-leasing">
        <select data-guide="vehicule-jour-paiement"><option value="0: null">— Choisir —</option><option value="1: 5">5</option></select>
      }
      <button type="button" data-guide="vehicule-ajouter" (click)="enregistrer()">Ajouter</button>
      <button type="button" class="annuler" (click)="ouverte = false">Annuler</button>
    </form>
  }` })
class EcranVehicules {
  static admin = true;
  static refus = false;
  admin = EcranVehicules.admin;
  acquisition = 'purchase';
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
  /**
   * Une bulle par champ (Karim, 24/09/2026) : les champs absents de l'écran de test sont
   * sautés par le moteur ; on avance jusqu'à la bulle voulue.
   */
  const jusqua = async (id: string, cliquerSuivant = false) => {
    for (let i = 0; i < (cliquerSuivant ? 80 : 25) && etape() !== id; i++) {
      const b = bouton('Suivant');
      if (cliquerSuivant && b && !b.disabled) { b.click(); }
      await rafraichir(cliquerSuivant ? 4 : 14);
    }
  };
  /** Les quatre champs remplis : le tutoriel attend le clic sur « Ajouter ». */
  const jusquAAjouter = async () => {
    await cliquer('vehicules-nouveau');
    await remplir('vehicule-nom', 'Camion principal'); await suivant();
    await remplir('vehicule-plaque', 'AB-123-CD'); await suivant();
    await remplir('vehicule-marque', '3'); await suivant();
    await remplir('vehicule-modele', '7'); await suivant();
    await jusqua('tuto-vehicule-compteur', true);
    await remplir('vehicule-compteur', '85000');        // 0 prérempli = vide : obligatoire
    await jusqua('tuto-vehicule-carburant', true);
    await remplir('vehicule-carburant', 'diesel');      // obligatoire, rien de pré-choisi
    await jusqua('tuto-vehicule-ajouter', true);
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
    help.marquerConseilVu();                 // le conseil a ses propres tests
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
  });

  afterEach(() => guide.destroy());

  it('première bulle : « Cliquez sur Nouveau véhicule », le bouton reste cliquable, pas de « Suivant »', async () => {
    await ouvrir('/vehicles');

    expect(guide.componentInstance.mode).toBe('ecran');
    expect(etape()).toBe('tuto-vehicule-nouveau');
    expect(bulle()?.textContent).toContain('Écran Véhicules · Étape 1 sur 21');
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
    expect(etape()).toBe('tuto-vehicule-annee');        // puis tous les autres champs
    await jusqua('tuto-vehicule-compteur', true);
    expect(bouton('Suivant')!.disabled).toBe(true);     // compteur obligatoire, 0 ne compte pas
    await remplir('vehicule-compteur', '85000');
    await jusqua('tuto-vehicule-carburant', true);
    await remplir('vehicule-carburant', 'diesel');
    await jusqua('tuto-vehicule-ajouter', true);
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

  /** Jusqu'à la bulle « Couleur » (facultative) ; la suivante est « Type de carburant ». */
  const jusquACouleur = async () => {
    await cliquer('vehicules-nouveau');
    await remplir('vehicule-nom', 'Camion principal'); await suivant();
    await remplir('vehicule-plaque', 'AB-123-CD'); await suivant();
    await jusqua('tuto-vehicule-compteur', true);
    await remplir('vehicule-compteur', '85000');
    await jusqua('tuto-vehicule-couleur', true);
  };
  /** Couleur passée, carburant choisi : la bulle suivante est « Réservoir ». */
  const apresCarburant = async () => {
    await jusquACouleur();
    await suivant();
    await remplir('vehicule-carburant', 'diesel');
    await suivant();
  };
  const clic = (b: HTMLButtonElement, detail: number) =>
    b.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true, detail }));
  const entree = (champ: HTMLElement, repeat = false) => {
    const ev = new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true, repeat });
    champ.dispatchEvent(ev);
    return ev;
  };
  const pause = (ms: number) => new Promise(fin => setTimeout(fin, ms));

  // Karim, 25/09/2026 : « "type de carburant" doit être un champ obligatoire », avec
  // « Sélectionner » en première ligne comme « Marque ».
  it('« Type de carburant » : rien de pré-choisi, « Suivant » grisé tant qu\'il n\'est pas choisi', async () => {
    await ouvrir('/vehicles');
    await jusquACouleur();
    await suivant();                                   // couleur : facultative
    expect(etape()).toBe('tuto-vehicule-carburant');
    expect(bulle()?.textContent).toContain('Choisissez son « Type de carburant » dans la liste.');
    expect(bulle()?.textContent).not.toContain('facultatif');
    expect(el('vehicule-carburant').value).toBe('');
    expect(bouton('Suivant')!.disabled).toBe(true);
    await remplir('vehicule-carburant', 'diesel');
    expect(bouton('Suivant')!.disabled).toBe(false);
  });

  it('double-clic sur « Suivant » : la bulle suivante, pré-remplie (« Année »), n\'est pas sautée', async () => {
    await ouvrir('/vehicles');
    await cliquer('vehicules-nouveau');
    await remplir('vehicule-nom', 'Camion principal'); await suivant();
    await remplir('vehicule-plaque', 'AB-123-CD'); await suivant();
    await suivant();                                   // marque : facultative
    expect(etape()).toBe('tuto-vehicule-modele');
    const b = bouton('Suivant')!;
    clic(b, 1);
    clic(b, 2);                                        // second clic du double-clic
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-annee');
  });

  it('double-clic sur « Précédent » : un seul pas en arrière', async () => {
    await ouvrir('/vehicles');
    await cliquer('vehicules-nouveau');
    await remplir('vehicule-nom', 'Camion principal'); await suivant();
    await remplir('vehicule-plaque', 'AB-123-CD'); await suivant();
    await jusqua('tuto-vehicule-type', true);
    const b = bouton('Précédent')!;
    clic(b, 1);
    clic(b, 2);
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-annee');
  });

  // Relecture du 25/09/2026 : sur Mac, Entrée sur une liste envoie le formulaire qui a
  // un bouton submit (fiche Chauffeur) — elle est retenue, liste comprise.
  it('Entrée sur la liste encadrée : retenue ; elle n\'avance que quand un carburant est choisi', async () => {
    await ouvrir('/vehicles');
    await jusquACouleur();
    await suivant();
    expect(etape()).toBe('tuto-vehicule-carburant');
    const vide = entree(el('vehicule-carburant'));
    await rafraichir();
    expect(vide.defaultPrevented).toBe(true);
    expect(etape()).toBe('tuto-vehicule-carburant');   // obligatoire, rien de choisi
    await remplir('vehicule-carburant', 'diesel');
    entree(el('vehicule-carburant'));
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-reservoir');
  });

  it('Entrée dans un autre champ que celui encadré : retenue, sans faire avancer', async () => {
    await ouvrir('/vehicles');
    await jusquACouleur();
    await suivant();
    const ailleurs = entree(el('vehicule-nom'));
    await rafraichir();
    expect(ailleurs.defaultPrevented).toBe(true);      // la fiche n'est pas envoyée
    expect(etape()).toBe('tuto-vehicule-carburant');
  });

  it('deux Entrée rapprochées, ou la touche maintenue, n\'avancent que d\'une bulle', async () => {
    await ouvrir('/vehicles');
    await apresCarburant();
    expect(etape()).toBe('tuto-vehicule-reservoir');
    entree(el('vehicule-reservoir'));                  // « Réservoir » (facultatif) : Suivant
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-mise-en-circulation');
    expect(document.activeElement).toBe(el('vehicule-mise-en-circulation'));
    entree(el('vehicule-mise-en-circulation'));        // second appui, dans la foulée
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-mise-en-circulation');
    await pause(GuidedHelpComponent.PAUSE_CLAVIER_MS + 50);
    entree(el('vehicule-mise-en-circulation'), true);  // touche maintenue : répétition
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-mise-en-circulation');
    entree(el('vehicule-mise-en-circulation'));        // nouvel appui, plus tard : il avance
    await rafraichir();
    expect(etape()).toBe('tuto-vehicule-acquisition');
  });

  // Contre-vérification du 25/09/2026 : pendant que la bulle suivante cherche sa cible,
  // le curseur est encore dans le champ précédent, déjà rempli.
  it('Entrée dans le champ précédent pendant que la bulle suivante cherche sa cible : sans effet', async () => {
    await ouvrir('/vehicles');
    await apresCarburant();
    await jusqua('tuto-vehicule-prix-achat', true);
    await remplir('vehicule-prix-achat', '20000');
    bouton('Suivant')!.click();                       // « Traite » : absente en « Achat »
    expect(etape()).toBe('tuto-vehicule-traite');
    expect(guide.componentInstance.cibleTrouvee).toBe(false);
    const acquisition = el('vehicule-acquisition') as unknown as HTMLSelectElement;
    acquisition.value = 'leasing';                    // la traite va apparaître
    acquisition.dispatchEvent(new Event('change', { bubbles: true }));
    const ev = entree(el('vehicule-prix-achat'));
    await rafraichir();
    expect(ev.defaultPrevented).toBe(true);
    expect(etape()).toBe('tuto-vehicule-traite');
  });

  it('« Ajouter » refusé puis fiche fermée : ce n\'est pas un enregistrement, le tutoriel reviendra', async () => {
    await ouvrir('/vehicles');
    await jusquAAjouter();
    EcranVehicules.refus = true;                      // alerte : la fiche reste ouverte
    await cliquer('vehicule-ajouter');
    expect(etape()).toBe('tuto-vehicule-ajouter');
    document.body.dispatchEvent(new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }));
    (document.querySelector('.annuler') as HTMLElement).click();   // Tab jusqu'à « Annuler », Entrée
    await rafraichir(14);
    expect(guide.componentInstance.actif).toBe(false);
    EcranVehicules.refus = false;
    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(true);   // pas marqué vu
    expect(etape()).toBe('tuto-vehicule-nouveau');
  });

  // Karim, 25/09/2026 : avec « Crédit », « il y aura d'autres champs à remplir et c'est
  // très important ». Le montant y devient l'« Apport » (ligne « Apport » de
  // l'échéancier côté serveur) : facultatif, un crédit peut ne pas en avoir.
  it('« Crédit » : apport facultatif ; traite, durée, date de début et jour de paiement obligatoires', async () => {
    await ouvrir('/vehicles');
    await apresCarburant();
    await jusqua('tuto-vehicule-acquisition', true);
    await remplir('vehicule-acquisition', 'leasing');
    await suivant();
    expect(etape()).toBe('tuto-vehicule-date-achat');
    await suivant();                                   // date d'achat : facultative
    expect(etape()).toBe('tuto-vehicule-prix-achat');
    expect(bulle()?.textContent).toContain('« Apport »');
    expect(bouton('Suivant')!.disabled).toBe(false);  // sans apport, on continue
    await suivant();
    for (const [id, cible, valeur] of [
      ['tuto-vehicule-traite', 'vehicule-traite', '1250'],
      ['tuto-vehicule-duree-leasing', 'vehicule-duree-leasing', '36'],
      ['tuto-vehicule-debut-leasing', 'vehicule-debut-leasing', '2026-10-01'],
      ['tuto-vehicule-jour-paiement', 'vehicule-jour-paiement', '1: 5'],
    ]) {
      expect(etape()).toBe(id);
      expect(bulle()?.textContent).not.toContain('facultatif');
      expect(bouton('Suivant')!.disabled).toBe(true);  // vide : on ne passe pas
      await remplir(cible, valeur);
      expect(bouton('Suivant')!.disabled).toBe(false);
      await suivant();
    }
    expect(etape()).toBe('tuto-vehicule-ajouter');
  });

  it('« Achat » : le prix reste facultatif et les bulles du crédit sont enjambées', async () => {
    await ouvrir('/vehicles');
    await apresCarburant();
    await jusqua('tuto-vehicule-prix-achat', true);
    expect(bouton('Suivant')!.disabled).toBe(false);
    await suivant();
    await jusqua('tuto-vehicule-ajouter');
    expect(etape()).toBe('tuto-vehicule-ajouter');
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

  it('fiche fermée sans cliquer « Ajouter » (Tab puis Entrée sur Annuler) : le tutoriel s\x27arrête sans être marqué vu', async () => {
    await ouvrir('/vehicles');
    await jusquAAjouter();
    (document.querySelector('.annuler') as HTMLElement).click();   // rien d'enregistré
    await rafraichir(14);
    expect(guide.componentInstance.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(true);           // il revient
  });

  it('fiche fermée pendant la saisie du nom : le tutoriel s\x27arrête au lieu de rester sur un voile vide', async () => {
    await ouvrir('/vehicles');
    await cliquer('vehicules-nouveau');
    expect(etape()).toBe('tuto-vehicule-nom');
    (document.querySelector('.annuler') as HTMLElement).click();
    await rafraichir(14);
    expect(guide.componentInstance.actif).toBe(false);
    expect(guide.nativeElement.querySelectorAll('.guide-voile').length).toBe(0);
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
    help.marquerConseilVu();                 // son conseil est déjà lu
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

  it('tant que le conseil de première connexion doit être montré, il passe avant', async () => {
    compte = { id: 'u-autre', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
    expect(help.doitProposerLeGuide()).toBe(true);
    await ouvrir('/vehicles');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('GPA : plus de visite guidée ; relancée depuis l\'Aide, ce sont les premiers pas, à la première bulle de Véhicules', async () => {
    await ouvrir('/vehicles');
    guide.componentInstance.passer();
    expect(help.etapesGuide()).toEqual([]);
    jest.spyOn(help, 'enDeveloppement').mockReturnValue(false);  // production : pas de conseil
    help.reinitialiserGuide();                                   // « Revoir les premiers pas »
    await rafraichir();
    expect(guide.componentInstance.mode).toBe('ecran');
    expect(etape()).toBe('tuto-vehicule-nouveau');
  });
});

/**
 * Tutoriel pas à pas de l'écran Chauffeurs (GPA), sur le modèle validé de Véhicules :
 * « Nouveau chauffeur », prénom, nom, date d'expiration du permis et véhicule
 * (facultatifs), puis « Créer le chauffeur ». L'écran de test reproduit la fiche
 * (employee-popup) : la liste « Aucun véhicule » est en [ngValue]="null", sa valeur
 * vaut donc "0: null".
 */
@Component({ standalone: true, template: `
  <button data-guide="chauffeurs-nouveau" (click)="ouverte = true">Nouveau chauffeur</button>
  @if (ouverte) {
    <form>
      <input data-guide="chauffeur-prenom">
      <input data-guide="chauffeur-nom">
      <input type="date" data-guide="chauffeur-permis-expiration">
      <select data-guide="chauffeur-vehicule"><option value="0: null">Aucun véhicule</option><option value="1: 5">Clio (AB-123-CD)</option></select>
      <button type="button" data-guide="chauffeur-creer" (click)="ouverte = false">Créer le chauffeur</button>
    </form>
  }` })
class EcranChauffeurs { ouverte = false; }

describe('Tutoriel pas à pas — écran Chauffeurs (GPA)', () => {
  let compte: any;
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  let help: HelpService;

  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const rafraichir = async (n = 4) => {
    harness.fixture.detectChanges(); await images(n); harness.fixture.detectChanges(); guide.detectChanges();
  };
  const ouvrir = async (url: string) => { await harness.navigateByUrl(url); await rafraichir(); };
  const el = (cible: string) => document.querySelector('[data-guide="' + cible + '"]') as HTMLInputElement;
  const bouton = (libelle: string) => Array.from(guide.nativeElement.querySelectorAll('.guide-bulle button') as NodeListOf<HTMLButtonElement>)
    .find(b => b.textContent!.trim() === libelle);
  const etape = () => guide.componentInstance.etape?.id;
  const cliquer = async (cible: string) => { el(cible).click(); await rafraichir(); };
  const remplir = async (cible: string, valeur: string) => {
    const champ = el(cible);
    champ.value = valeur;
    champ.dispatchEvent(new Event('input', { bubbles: true }));
    champ.dispatchEvent(new Event('change', { bubbles: true }));
    await rafraichir(3);
  };
  const suivant = async () => { bouton('Suivant')!.click(); await rafraichir(); };
  /**
   * Une bulle par champ (Karim, 24/09/2026) : les champs absents de l'écran de test sont
   * sautés par le moteur ; on avance jusqu'à la bulle voulue.
   */
  const jusqua = async (id: string) => {
    for (let i = 0; i < 40 && etape() !== id; i++) { await rafraichir(14); }
  };

  beforeEach(async () => {
    localStorage.clear();
    // Utilisateur NON administrateur : « Nouveau chauffeur » lui est ouvert.
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: false };
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: EcranTableauDeBord },
          { path: 'drivers', component: EcranChauffeurs },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring',
          abonnementComprend: (m: string) => m !== 'monitoring',
          hasReportAccess: () => true
        } }
      ]
    });
    help = TestBed.inject(HelpService);
    help.fermerGuide(true);
    help.marquerConseilVu();                 // le conseil passe avant, il a ses propres tests
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
  });

  afterEach(() => guide.destroy());

  it('un non-administrateur le reçoit, et le parcours complet crée le chauffeur puis ne revient plus', async () => {
    await ouvrir('/drivers');
    expect(guide.componentInstance.mode).toBe('ecran');
    expect(etape()).toBe('tuto-chauffeur-nouveau');
    expect(guide.nativeElement.querySelector('.guide-compteur')?.textContent).toContain('Écran Chauffeurs · Étape 1 sur 14');

    await cliquer('chauffeurs-nouveau');
    expect(etape()).toBe('tuto-chauffeur-prenom');
    await remplir('chauffeur-prenom', 'Jean'); await suivant();
    await remplir('chauffeur-nom', 'Dupont'); await suivant();
    await jusqua('tuto-chauffeur-permis');
    expect(etape()).toBe('tuto-chauffeur-permis');
    // Obligatoire (Karim, 24/09/2026 : « la Date d'expiration est importante »).
    expect(bouton('Suivant')!.disabled).toBe(true);
    await remplir('chauffeur-permis-expiration', '2028-05-31');
    await suivant();
    await jusqua('tuto-chauffeur-vehicule');
    expect(etape()).toBe('tuto-chauffeur-vehicule');
    await suivant();                                   // « Aucun véhicule »
    expect(etape()).toBe('tuto-chauffeur-creer');
    expect(bouton('Terminer')).toBeUndefined();

    await cliquer('chauffeur-creer');                  // enregistré : la fiche se ferme
    await rafraichir();
    expect(guide.componentInstance.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/drivers');
    expect(guide.componentInstance.actif).toBe(false);
  });

  it('prénom et nom obligatoires ; « Aucun véhicule » (valeur "0: null") compte comme vide', async () => {
    await ouvrir('/drivers');
    await cliquer('chauffeurs-nouveau');
    expect(bouton('Suivant')!.disabled).toBe(true);
    await remplir('chauffeur-prenom', 'Jean'); await suivant();
    expect(bouton('Suivant')!.disabled).toBe(true);
    await remplir('chauffeur-nom', 'Dupont'); await suivant();
    await jusqua('tuto-chauffeur-permis');
    await remplir('chauffeur-permis-expiration', '2028-05-31'); await suivant();
    await jusqua('tuto-chauffeur-vehicule');
    expect(etape()).toBe('tuto-chauffeur-vehicule');
    expect(guide.componentInstance.valeurSaisie).toBe(false);   // « Aucun véhicule » = rien choisi
    await remplir('chauffeur-vehicule', '1: 5');
    expect(guide.componentInstance.valeurSaisie).toBe(true);
  });
});

/**
 * Tutoriel des alertes par e-mail (Karim, 24/09/2026 : « il faut ajouter alertes par
 * mail »). Écran Utilisateurs, onglet « Alertes par email ». Particularité : le bouton
 * « Ajouter une adresse » DISPARAÎT dès qu'on le clique (le formulaire le remplace).
 */
@Component({ standalone: true, template: `
  <button data-guide="alertes-email-onglet" (click)="onglet = true">Alertes par email</button>
  @if (onglet) {
    @if (!formulaire) { <button data-guide="alerte-ajouter" (click)="formulaire = true">Ajouter une adresse</button> }
    @if (formulaire) {
      <input type="email" data-guide="alerte-adresse">
      <select data-guide="alerte-type"><option value="" disabled selected>Choisir un type…</option><option value="assurance">Assurance</option></select>
      <button data-guide="alerte-enregistrer" (click)="formulaire = false">Enregistrer</button>
    }
  }` })
class EcranUtilisateurs { onglet = false; formulaire = false; }

describe('Tutoriel pas à pas — alertes par e-mail (GPA)', () => {
  let compte: any;
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;

  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const rafraichir = async (n = 4) => {
    harness.fixture.detectChanges(); await images(n); harness.fixture.detectChanges(); guide.detectChanges();
  };
  const ouvrir = async (url: string) => { await harness.navigateByUrl(url); await rafraichir(); };
  const el = (cible: string) => document.querySelector('[data-guide="' + cible + '"]') as HTMLInputElement;
  const bouton = (libelle: string) => Array.from(guide.nativeElement.querySelectorAll('.guide-bulle button') as NodeListOf<HTMLButtonElement>)
    .find(b => b.textContent!.trim() === libelle);
  const etape = () => guide.componentInstance.etape?.id;
  const cliquer = async (cible: string) => { el(cible).click(); await rafraichir(); };
  const remplir = async (cible: string, valeur: string) => {
    const champ = el(cible);
    champ.value = valeur;
    champ.dispatchEvent(new Event('input', { bubbles: true }));
    champ.dispatchEvent(new Event('change', { bubbles: true }));
    await rafraichir(3);
  };

  beforeEach(async () => {
    localStorage.clear();
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true, email: 'gerant@transports-martin.fr' };
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: EcranTableauDeBord },
          { path: 'users', component: EcranUtilisateurs },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring',
          abonnementComprend: (m: string) => m !== 'monitoring',
          hasReportAccess: () => true
        } }
      ]
    });
    TestBed.inject(HelpService).fermerGuide(true);
    TestBed.inject(HelpService).marquerConseilVu();   // le conseil passe avant, il a ses propres tests
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
  });

  afterEach(() => guide.destroy());

  it('onglet, « Ajouter une adresse » (qui disparaît au clic), adresse, type, « Enregistrer » : l\'adresse est inscrite', async () => {
    await ouvrir('/users');
    expect(etape()).toBe('tuto-alerte-onglet');
    expect(guide.nativeElement.querySelector('.guide-compteur')?.textContent).toContain('Alertes par e-mail · Étape 1 sur 5');

    await cliquer('alertes-email-onglet');
    expect(etape()).toBe('tuto-alerte-ajouter');

    await cliquer('alerte-ajouter');                  // le bouton disparaît : ce n'est pas un abandon
    await rafraichir(14);
    expect(guide.componentInstance.actif).toBe(true);
    expect(etape()).toBe('tuto-alerte-adresse');

    await remplir('alerte-adresse', 'gerant@transports-martin.fr');
    bouton('Suivant')!.click(); await rafraichir();
    expect(etape()).toBe('tuto-alerte-type');
    expect(bouton('Suivant')!.disabled).toBe(true);   // « Choisir un type… » ne compte pas
    await remplir('alerte-type', 'assurance');
    expect(bouton('Suivant')!.disabled).toBe(false);
    bouton('Suivant')!.click(); await rafraichir();

    expect(etape()).toBe('tuto-alerte-enregistrer');
    await cliquer('alerte-enregistrer');
    await rafraichir();
    expect(guide.componentInstance.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/users');
    expect(guide.componentInstance.actif).toBe(false);
  });
});

/**
 * Tutoriel de l'écran Échéances : pas de bouton de création, les lignes existent
 * d'office (trois par véhicule). Crayon « Modifier l'échéance » de la première ligne,
 * date, « Enregistrer ». L'écran s'ouvre en /documents (menu) ou /echeances (visite).
 * Sans véhicule, aucune ligne : le tutoriel ne se montre pas et revient plus tard.
 */
@Component({ standalone: true, template: `
  @for (d of lignes; track d; let i = $index) {
    <div class="ligne">{{ d }}
      <button (click)="ouverte = true" [attr.data-guide]="i === 0 ? 'echeances-modifier' : null">✎</button>
    </div>
  }
  @if (ouverte) {
    <input type="date" data-guide="echeance-date">
    <button data-guide="echeance-enregistrer" (click)="ouverte = false">Enregistrer</button>
  }` })
class EcranEcheances {
  static lignes = ['Assurance', 'Visite technique', 'Vignette'];
  lignes = EcranEcheances.lignes;
  ouverte = false;
}

describe('Tutoriel pas à pas — écran Échéances (GPA)', () => {
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  const compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };

  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const rafraichir = async (n = 4) => {
    harness.fixture.detectChanges(); await images(n); harness.fixture.detectChanges(); guide.detectChanges();
  };
  const ouvrir = async (url: string, n = 4) => { await harness.navigateByUrl(url); await rafraichir(n); };
  const el = (cible: string) => document.querySelector('[data-guide="' + cible + '"]') as HTMLInputElement;
  const etape = () => guide.componentInstance.etape?.id;

  beforeEach(async () => {
    localStorage.clear();
    EcranEcheances.lignes = ['Assurance', 'Visite technique', 'Vignette'];
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: EcranTableauDeBord },
          { path: 'documents', component: EcranEcheances },
          { path: 'echeances', component: EcranEcheances },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring',
          abonnementComprend: (m: string) => m !== 'monitoring',
          hasReportAccess: () => true
        } }
      ]
    });
    TestBed.inject(HelpService).fermerGuide(true);
    TestBed.inject(HelpService).marquerConseilVu();   // le conseil passe avant, il a ses propres tests
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
  });

  afterEach(() => guide.destroy());

  it('depuis le menu (/documents) : crayon de la première ligne, date, « Enregistrer »', async () => {
    await ouvrir('/documents');
    expect(etape()).toBe('tuto-echeance-modifier');
    expect(guide.nativeElement.querySelector('.guide-compteur')?.textContent).toContain('Écran Échéances · Étape 1 sur 3');

    el('echeances-modifier').click(); await rafraichir();
    expect(etape()).toBe('tuto-echeance-date');
    const date = el('echeance-date');
    date.value = '2027-08-15';
    date.dispatchEvent(new Event('input', { bubbles: true }));
    await rafraichir(3);
    guide.componentInstance.suivant(); await rafraichir();
    expect(etape()).toBe('tuto-echeance-enregistrer');

    el('echeance-enregistrer').click(); await rafraichir(); await rafraichir();
    expect(guide.componentInstance.actif).toBe(false);
    await ouvrir('/dashboard');
    await ouvrir('/documents');
    expect(guide.componentInstance.actif).toBe(false);          // vu : il ne revient plus
  });

  it('depuis la visite (/echeances) : même tutoriel', async () => {
    await ouvrir('/echeances');
    expect(etape()).toBe('tuto-echeance-modifier');
  });

  it('client sans véhicule (aucune ligne) : rien ne s\'affiche, et il revient quand les lignes existent', async () => {
    EcranEcheances.lignes = [];
    await ouvrir('/documents', 380);                             // ~6 s d'attente du crayon
    expect(guide.componentInstance.actif).toBe(false);
    expect(guide.nativeElement.querySelector('.guide-voile')).toBeNull();

    EcranEcheances.lignes = ['Assurance', 'Visite technique', 'Vignette'];
    await ouvrir('/dashboard');
    await ouvrir('/documents');
    expect(etape()).toBe('tuto-echeance-modifier');              // pas marqué vu
  }, 20000);
});

/**
 * Tutoriel de l'écran Entretien programmable : créer un programme (nom, catégorie,
 * intervalle en km OU en mois), l'enregistrer, puis l'affecter à un véhicule (choisir
 * le véhicule, cocher le programme, « Ajouter »). L'intervalle est une ligne de deux
 * champs : l'un ou l'autre suffit.
 */
@Component({ standalone: true, template: `
  <button data-guide="entretiens-affecter" (click)="affectation = true">Affecter</button>
  <button data-guide="entretiens-nouveau-modele" (click)="fiche = true">Nouveau modele</button>
  @if (fiche) {
    <input data-guide="entretien-modele-nom" #nom>
    <select data-guide="entretien-modele-categorie"><option value="">Choisir</option><option value="Moteur">Moteur</option></select>
    <div data-guide="entretien-modele-intervalle"><input type="number" class="km"><input type="number" class="mois"></div>
    <button data-guide="entretien-modele-enregistrer" (click)="cree = $any(nom).value; fiche = false">Enregistrer</button>
  }
  @if (affectation) {
    <select data-guide="entretien-affecter-vehicule" (change)="vehicule = $any($event.target).value">
      <option value="">Choisir un vehicule...</option><option value="7">Clio - AB-123-CD</option>
    </select>
    @if (vehicule) {
      @for (t of ['Vidange existante', cree]; track t) {
        <div class="tpl-item" [attr.data-guide]="t === cree ? 'entretien-affecter-modele' : null" (click)="coche = t">{{ t }}</div>
      }
    }
    <button data-guide="entretien-affecter-ajouter" (click)="affectation = false">Ajouter</button>
  }` })
class EcranEntretiens { fiche = false; affectation = false; vehicule = ''; cree = ''; coche = ''; }

describe('Tutoriel pas à pas — écran Entretien programmable (GPA)', () => {
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  const compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };

  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const rafraichir = async (n = 4) => {
    harness.fixture.detectChanges(); await images(n); harness.fixture.detectChanges(); guide.detectChanges();
  };
  const ouvrir = async (url: string) => { await harness.navigateByUrl(url); await rafraichir(); };
  const el = (sel: string) => document.querySelector(sel.startsWith('.') ? sel : '[data-guide="' + sel + '"]') as HTMLInputElement;
  const bouton = (libelle: string) => Array.from(guide.nativeElement.querySelectorAll('.guide-bulle button') as NodeListOf<HTMLButtonElement>)
    .find(b => b.textContent!.trim() === libelle);
  const etape = () => guide.componentInstance.etape?.id;
  const cliquer = async (cible: string) => { el(cible).click(); await rafraichir(); };
  const remplir = async (sel: string, valeur: string) => {
    const champ = el(sel);
    champ.value = valeur;
    champ.dispatchEvent(new Event('input', { bubbles: true }));
    champ.dispatchEvent(new Event('change', { bubbles: true }));
    await rafraichir(3);
  };
  const suivant = async () => { bouton('Suivant')!.click(); await rafraichir(); };
  /**
   * Une bulle par champ (Karim, 24/09/2026) : les champs absents de l'écran de test sont
   * sautés par le moteur ; on avance jusqu'à la bulle voulue.
   */
  const jusqua = async (id: string) => {
    for (let i = 0; i < 40 && etape() !== id; i++) { await rafraichir(14); }
  };

  beforeEach(async () => {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: EcranTableauDeBord },
          { path: 'entretien-programmable', component: EcranEntretiens },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring',
          abonnementComprend: (m: string) => m !== 'monitoring',
          hasReportAccess: () => true
        } }
      ]
    });
    TestBed.inject(HelpService).fermerGuide(true);
    TestBed.inject(HelpService).marquerConseilVu();   // le conseil passe avant, il a ses propres tests
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
  });

  afterEach(() => guide.destroy());

  it('programme créé (intervalle en mois seulement) puis affecté à un véhicule, en 17 bulles', async () => {
    await ouvrir('/entretien-programmable');
    expect(guide.nativeElement.querySelector('.guide-compteur')?.textContent).toContain('Écran Entretien programmable · Étape 1 sur 17');

    await cliquer('entretiens-nouveau-modele');
    await remplir('entretien-modele-nom', 'Révision annuelle'); await suivant();
    await jusqua('tuto-entretien-categorie');
    expect(bouton('Suivant')!.disabled).toBe(true);              // « Choisir » ne compte pas
    await remplir('entretien-modele-categorie', 'Moteur'); await suivant();
    await jusqua('tuto-entretien-intervalle');

    expect(etape()).toBe('tuto-entretien-intervalle');
    expect(document.activeElement).toBe(el('.km'));              // curseur dans le premier des deux champs
    expect(bouton('Suivant')!.disabled).toBe(true);
    await remplir('.mois', '12');                                // les mois seuls suffisent
    expect(bouton('Suivant')!.disabled).toBe(false);
    await suivant();
    await jusqua('tuto-entretien-enregistrer');

    expect(etape()).toBe('tuto-entretien-enregistrer');
    await cliquer('entretien-modele-enregistrer'); await rafraichir();
    expect(etape()).toBe('tuto-entretien-affecter');

    await cliquer('entretiens-affecter');
    await remplir('entretien-affecter-vehicule', '7'); await suivant();
    expect(etape()).toBe('tuto-entretien-cocher');
    // Le cadre vise le programme CRÉÉ à l'étape précédente, pas le premier de la liste
    // (Karim, 24/09/2026).
    expect(el('entretien-affecter-modele').textContent).toBe('Révision annuelle');
    await cliquer('entretien-affecter-modele');
    expect(harness.routeDebugElement!.componentInstance.coche).toBe('Révision annuelle');
    expect(etape()).toBe('tuto-entretien-ajouter');
    await cliquer('entretien-affecter-ajouter'); await rafraichir();
    expect(guide.componentInstance.actif).toBe(false);

    await ouvrir('/dashboard');
    await ouvrir('/entretien-programmable');
    expect(guide.componentInstance.actif).toBe(false);
  });
});

describe('Tutoriels — un montant laissé à 0 n\'est pas une saisie', () => {
  // Karim, 24/09/2026 : le tutoriel Réparations fait saisir le coût de la pièce,
  // prérempli à 0 ; « Suivant » doit attendre un vrai prix.
  it('champ numérique : 0 = vide, 45 = rempli ; un groupe suit la même règle', () => {
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { getCurrentUserSync: () => null, getCurrentUser: () => new BehaviorSubject<any>(null).asObservable() } },
        { provide: PermissionService, useValue: { hasModuleAccess: () => true, abonnementComprend: () => false, hasReportAccess: () => true } }
      ]
    });
    const c = TestBed.createComponent(GuidedHelpComponent).componentInstance as any;
    const prix = document.createElement('input');
    prix.type = 'number';
    prix.value = '0';
    expect(c.champRempli(prix)).toBe(false);
    prix.value = '45';
    expect(c.champRempli(prix)).toBe(true);
    const texte = document.createElement('input');
    texte.value = '0';                                  // un texte « 0 » reste une saisie
    expect(c.champRempli(texte)).toBe(true);
  });
});

/**
 * Conseil de première connexion (Karim, 25/09/2026) : une fenêtre au centre,
 * « une seule fois, à la première connexion, juste avant la visite guidée. Il ne
 * revient ensuite plus jamais. »
 */
describe('Conseil de première connexion', () => {
  let compte: any;
  let abonnementGps: boolean;
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  let help: HelpService;

  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const attendre = async () => {
    for (let i = 0; i < 5; i++) { await harness.fixture.whenStable(); harness.fixture.detectChanges(); guide.detectChanges(); }
    await images(4);
    harness.fixture.detectChanges();
    guide.detectChanges();
  };
  const fenetre = () => guide.nativeElement.querySelector('.guide-conseil') as HTMLElement | null;
  /**
   * « C'est compris, on commence ». Image par image : whenStable attendrait la fin de la
   * recherche de la cible, qui ne peut aboutir sans détection de changements.
   */
  const commencer = async () => {
    (fenetre()!.querySelector('.conseil-bouton') as HTMLElement).click();
    for (let i = 0; i < 3; i++) { harness.fixture.detectChanges(); await images(4); guide.detectChanges(); }
  };

  const monter = async () => {
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: PageTableauDeBord },
          { path: 'vehicles', component: EcranVehicules },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring' || abonnementGps,
          abonnementComprend: (m: string) => m !== 'monitoring' || abonnementGps,
          hasReportAccess: () => true
        } }
      ]
    });
    help = TestBed.inject(HelpService);
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');   // la page propose le guide
    await attendre();
  };

  beforeEach(() => {
    localStorage.clear();
    abonnementGps = false;
    EcranVehicules.admin = true;
    EcranVehicules.refus = false;
  });
  afterEach(() => guide.destroy());

  it('GPA, nouvel utilisateur : le conseil d\'abord ; son bouton ouvre Véhicules et son guide, sans visite guidée', async () => {
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
    await monter();

    expect(guide.componentInstance.conseilOuvert).toBe(true);
    expect(guide.componentInstance.actif).toBe(false);          // pas encore de bulle
    expect(fenetre()?.textContent).toContain('Un conseil avant de commencer');
    expect(fenetre()?.textContent).toContain('à l\'écran comme dans vos rapports');

    await commencer();
    expect(fenetre()).toBeNull();
    expect(TestBed.inject(Router).url).toBe('/vehicles');
    expect(guide.componentInstance.mode).toBe('ecran');
    expect(guide.componentInstance.etape?.id).toBe('tuto-vehicule-nouveau');
  });

  it('GPS : le bouton du conseil lance la visite guidée, qui reste en place', async () => {
    abonnementGps = true;
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
    await monter();
    expect(guide.componentInstance.conseilOuvert).toBe(true);

    await commencer();
    expect(guide.componentInstance.mode).toBe('parcours');
    expect(guide.componentInstance.etape?.id).toBe('bienvenue-gps');
  });

  it('en production, il ne revient plus jamais, même quand les premiers pas sont relancés depuis l\'Aide', async () => {
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
    await monter();
    jest.spyOn(help, 'enDeveloppement').mockReturnValue(false);  // image de production
    await commencer();
    guide.componentInstance.passer();

    help.reinitialiserGuide();                                   // « Revoir les premiers pas »
    await attendre();
    expect(guide.componentInstance.conseilOuvert).toBe(false);
    expect(guide.componentInstance.etape?.id).toBe('tuto-vehicule-nouveau');
  });

  it('en local (développement), « Revoir les premiers pas » le remontre, pour que Karim puisse le revoir', async () => {
    compte = { id: 'u-karim', firstLogin: false, companyName: 'Belive GPA', isCompanyAdmin: true };
    await monter();
    await commencer();
    guide.componentInstance.passer();

    help.reinitialiserGuide();
    await attendre();
    expect(guide.componentInstance.conseilOuvert).toBe(true);
  });

  it('fermé par Échap : il ne revient pas non plus (lu dès son affichage)', async () => {
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
    await monter();
    guide.componentInstance.auEchap();
    expect(guide.componentInstance.conseilOuvert).toBe(false);
    expect(help.conseilAMontrer()).toBe(false);
  });

  it('GPA, client déjà installé (pas une première connexion) : ni conseil, ni visite', async () => {
    compte = { id: 'u-ancien', firstLogin: false, companyName: 'Transports Martin', isCompanyAdmin: true };
    await monter();
    expect(guide.componentInstance.conseilOuvert).toBe(false);
    expect(guide.componentInstance.actif).toBe(false);
    expect(help.doitProposerLeGuide()).toBe(false);
  });

  it('GPS, client déjà installé : pas de conseil, sa visite guidée reste proposée', async () => {
    abonnementGps = true;
    compte = { id: 'u-ancien', firstLogin: false, companyName: 'Transports Martin', isCompanyAdmin: true };
    await monter();
    expect(guide.componentInstance.conseilOuvert).toBe(false);
    expect(guide.componentInstance.etape?.id).toBe('bienvenue-gps');
  });
});

/**
 * Premiers pas de l'offre GPA (Karim, 25/09/2026) : « quand il clique sur "Commencer",
 * on le ramène directement sur le premier écran "véhicules", après "chauffeurs", après
 * "programme d'entretien" » — et plus de visite guidée. Le bouton du conseil ouvre
 * Véhicules ; chaque guide terminé ouvre l'écran suivant, après une courte pause.
 */
describe('Premiers pas (GPA) — Véhicules, puis Chauffeurs, puis Entretien programmable', () => {
  let compte: any;
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  let help: HelpService;

  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const rafraichir = async (n = 4) => {
    harness.fixture.detectChanges(); await images(n); harness.fixture.detectChanges(); guide.detectChanges();
  };
  const url = () => TestBed.inject(Router).url;
  const guideOuvert = () => guide.componentInstance.actif ? guide.componentInstance.visiteEcran?.id : undefined;
  const pause = (ms: number) => new Promise(fin => setTimeout(fin, ms));

  const monter = async () => {
    harness = await RouterTestingHarness.create('/dashboard');   // la page propose le conseil
    await rafraichir();
  };
  /** Le client lit le conseil et clique « C'est compris, on commence ». */
  const commencer = async () => {
    (guide.nativeElement.querySelector('.conseil-bouton') as HTMLElement).click();
    await rafraichir();
    await rafraichir();
  };
  const premiereBulle = async () => {
    for (let i = 0; i < 40 && !guide.componentInstance.cibleTrouvee; i++) { await rafraichir(); }
  };
  /**
   * Première bulle affichée, puis le guide mené à son terme. Raccourci : la dernière
   * bulle, puis sa fin — chaque tutoriel a ses propres tests, geste par geste.
   */
  const finirLeGuide = async () => {
    await premiereBulle();
    const c = guide.componentInstance;
    c.index = c.etapes.length - 1;
    c.suivant();
    await rafraichir();
  };
  /** La pause d'enchaînement, puis l'écran suivant et son guide. */
  const enchainement = async () => { await pause(250); await rafraichir(); await rafraichir(); };

  beforeEach(async () => {
    localStorage.clear();
    EcranVehicules.admin = true;
    EcranVehicules.refus = false;
    compte = { id: 'u-nouveau', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: PageTableauDeBord },
          { path: 'vehicles', component: EcranVehicules },
          { path: 'drivers', component: EcranChauffeurs },
          { path: 'entretien-programmable', component: EcranEntretiens },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring',
          abonnementComprend: (m: string) => m !== 'monitoring',
          hasReportAccess: () => true
        } }
      ]
    });
    help = TestBed.inject(HelpService);
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.componentInstance.delaiEnchainement = 200;
    guide.detectChanges();
  });

  afterEach(() => guide.destroy());

  it('le conseil ouvre Véhicules ; chaque guide terminé ouvre le suivant, jusqu\'au programme d\'entretien', async () => {
    await monter();
    expect(guide.componentInstance.conseilOuvert).toBe(true);
    await commencer();
    expect(url()).toBe('/vehicles');
    expect(guideOuvert()).toBe('tuto-vehicules-gpa');

    await finirLeGuide();
    expect(url()).toBe('/vehicles');                 // il voit d'abord son véhicule dans la liste
    await enchainement();
    expect(url()).toBe('/drivers');
    expect(guideOuvert()).toBe('tuto-chauffeurs-gpa');

    await finirLeGuide();
    await enchainement();
    expect(url()).toBe('/entretien-programmable');
    expect(guideOuvert()).toBe('tuto-entretiens-gpa');

    await finirLeGuide();
    await enchainement();
    expect(url()).toBe('/entretien-programmable');   // fin des premiers pas : il reste là
    expect(guide.componentInstance.actif).toBe(false);
    expect(help.premiersPasEnCours()).toBe(false);
  });

  it('non-administrateur : Véhicules lui est fermé, les premiers pas commencent à Chauffeurs', async () => {
    compte = { ...compte, isCompanyAdmin: false };
    EcranVehicules.admin = false;
    await monter();
    await commencer();
    expect(url()).toBe('/drivers');
    expect(guideOuvert()).toBe('tuto-chauffeurs-gpa');
  });

  it('un écran dont le guide est déjà fait est enjambé', async () => {
    help.marquerEcranVu('tuto-chauffeurs-gpa');
    await monter();
    await commencer();
    await finirLeGuide();
    await enchainement();
    expect(url()).toBe('/entretien-programmable');
    expect(guideOuvert()).toBe('tuto-entretiens-gpa');
  });

  it('« Passer » arrête l\'accompagnement : le client reste où il est, le guide Chauffeurs l\'attend à son écran', async () => {
    await monter();
    await commencer();
    await premiereBulle();
    guide.componentInstance.passer();
    await enchainement();
    expect(url()).toBe('/vehicles');
    expect(help.premiersPasEnCours()).toBe(false);

    await harness.navigateByUrl('/drivers');
    await rafraichir();
    expect(guideOuvert()).toBe('tuto-chauffeurs-gpa');
  });

  it('Échap arrête aussi l\'accompagnement', async () => {
    await monter();
    await commencer();
    await premiereBulle();
    guide.componentInstance.auEchap();
    await enchainement();
    expect(url()).toBe('/vehicles');
    expect(help.premiersPasEnCours()).toBe(false);
  });

  it('le client quitte l\'écran pendant la pause : on ne l\'emmène pas ailleurs', async () => {
    await monter();
    await commencer();
    await finirLeGuide();
    await harness.navigateByUrl('/dashboard');
    await enchainement();
    expect(url()).toBe('/dashboard');
  });

  it('retenus pour la prochaine ouverture de l\'application (page rechargée en cours de route)', async () => {
    await monter();
    await commencer();
    (help as any).premiersPasEnMemoire.clear();      // seule reste la trace dans localStorage
    expect(help.premiersPasEnCours()).toBe(true);
  });
});

/**
 * Karim, 26/09/2026 : « pourquoi quand je rentre dans "réparation" le tuto ne s'affiche
 * pas ? ». Le tutoriel Réparations n'avait pas de test d'ouverture.
 */
@Component({ standalone: true, template: `<button data-guide="reparations-nouvelle">Nouvelle réparation</button>` })
class EcranReparations {}

describe('Tutoriel pas à pas — écran Réparations (GPA) : il démarre à l\'ouverture', () => {
  let compte: any;
  let guide: ComponentFixture<GuidedHelpComponent>;
  let harness: RouterTestingHarness;
  let help: HelpService;
  const images = (n: number) => new Promise<void>(fin => {
    let i = 0;
    const image = () => { if (++i >= n) { fin(); } else { requestAnimationFrame(image); } };
    requestAnimationFrame(image);
  });
  const ouvrir = async (url: string) => {
    await harness.navigateByUrl(url);
    for (let i = 0; i < 3; i++) { harness.fixture.detectChanges(); await images(4); guide.detectChanges(); }
  };

  beforeEach(async () => {
    localStorage.clear();
    compte = { id: 'u-karim', firstLogin: false, companyName: 'Belive GPA', isCompanyAdmin: true };
    TestBed.configureTestingModule({
      imports: [GuidedHelpComponent],
      providers: [
        provideRouter([
          { path: 'dashboard', component: EcranTableauDeBord },
          { path: 'reparations', component: EcranReparations },
          { path: 'repairs', component: EcranReparations },
        ]),
        { provide: AuthService, useValue: {
          getCurrentUserSync: () => compte,
          getCurrentUser: () => new BehaviorSubject<any>(compte).asObservable()
        } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => m !== 'monitoring',
          abonnementComprend: (m: string) => m !== 'monitoring',
          hasReportAccess: () => true
        } }
      ]
    });
    help = TestBed.inject(HelpService);
    help.marquerConseilVu();                 // le conseil passe avant, il a ses propres tests
    guide = TestBed.createComponent(GuidedHelpComponent);
    guide.detectChanges();
    harness = await RouterTestingHarness.create('/dashboard');
  });

  afterEach(() => guide.destroy());

  it('« Belive GPA » en local ouvre Réparations (menu : /reparations) : première bulle « Nouvelle réparation »', async () => {
    await ouvrir('/reparations');
    expect(guide.componentInstance.actif).toBe(true);
    expect(guide.componentInstance.visiteEcran?.id).toBe('tuto-reparations-gpa');
    expect(guide.componentInstance.etape?.id).toBe('tuto-reparation-nouvelle');
  });

  it('déjà vu : il ne revient pas ; « Revoir les premiers pas » (Belive GPA, local) le remet', async () => {
    help.marquerEcranVu('tuto-reparations-gpa');
    await ouvrir('/reparations');
    expect(guide.componentInstance.actif).toBe(false);

    jest.spyOn(help, 'ouvrirGuide').mockImplementation(() => {});   // sans lancer les premiers pas
    help.reinitialiserGuide();
    help.marquerConseilVu();
    await ouvrir('/dashboard');
    await ouvrir('/reparations');
    expect(guide.componentInstance.visiteEcran?.id).toBe('tuto-reparations-gpa');
  });

  it('conseil de première connexion encore à lire : il passe avant le tutoriel', async () => {
    compte = { ...compte, id: 'u-neuf' };    // conseil jamais lu
    await ouvrir('/reparations');
    expect(guide.componentInstance.actif).toBe(false);
    expect(help.doitProposerLeGuide()).toBe(true);
  });
});
