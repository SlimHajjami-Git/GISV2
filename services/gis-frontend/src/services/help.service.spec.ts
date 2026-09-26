import { TestBed } from '@angular/core/testing';
import { HelpService } from './help.service';
import { AuthService } from './auth.service';
import { PermissionService } from './permission.service';

/**
 * Aide integree (21/09/2026) : la recherche est la piece qui peut echouer en
 * silence — le client tape sans accent, avec ses propres mots, et ne doit
 * jamais tomber sur un article d'un module que sa societe n'a pas souscrit.
 */
describe('HelpService', () => {
  let service: HelpService;
  let modulesAutorises: string[];
  /** Rapports fermés à ce client (tous ouverts par défaut). */
  let rapportsFermes: string[];
  /** Compte connecté vu par l'aide (type de société compris). */
  let compte: any;

  beforeEach(() => {
    rapportsFermes = [];
    compte = { id: 'u1', companyType: 'transport' };
    // Abonnement complet par defaut : les tests de filtrage restreignent
    // ensuite cette liste au cas par cas.
    modulesAutorises = [
      'dashboard', 'vehicles', 'monitoring', 'playback', 'reports',
      'maintenance', 'carburant', 'geofences', 'documents', 'users',
      'costs', 'expenses', 'settings', 'employees', 'tours',
      'fleet_management', 'suppliers', 'accidents'
    ];

    TestBed.configureTestingModule({
      providers: [
        HelpService,
        { provide: AuthService, useValue: { getCurrentUserSync: () => compte } },
        { provide: PermissionService, useValue: {
          hasModuleAccess: (m: string) => modulesAutorises.includes(m),
          abonnementComprend: (m: string) => modulesAutorises.includes(m),
          hasReportAccess: (r: string) => !rapportsFermes.includes(r)
        } }
      ]
    });

    service = TestBed.inject(HelpService);
    localStorage.clear();
  });

  /** Nouvelle ouverture de l'application : nouvelle instance du service, même localStorage. */
  const nouvelleSession = (id = 'u1'): HelpService => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        HelpService,
        { provide: AuthService, useValue: { getCurrentUserSync: () => ({ id }) } },
        { provide: PermissionService, useValue: { hasModuleAccess: () => true, abonnementComprend: () => true, hasReportAccess: () => true } }
      ]
    });
    return TestBed.inject(HelpService);
  };

  describe('recherche par mot-cle', () => {
    it('trouve sans accent ce qui est ecrit avec accent', () => {
      // Le client tape « kilometrage », l'article s'appelle « Corriger un kilométrage erroné ».
      const resultats = service.rechercher('kilometrage');
      expect(resultats.some(a => a.id === 'corriger-kilometrage')).toBe(true);
    });

    it('trouve malgre la casse et la ponctuation', () => {
      expect(service.rechercher('PLEIN').some(a => a.id === 'saisir-plein')).toBe(true);
      expect(service.rechercher('mot de passe !').some(a => a.id === 'se-connecter')).toBe(true);
    });

    it('trouve par le vocabulaire du client, pas seulement par le notre', () => {
      // « immatriculation » n'est pas dans le titre : c'est un mot-cle.
      expect(service.rechercher('immatriculation').some(a => a.id === 'ajouter-vehicule')).toBe(true);
      // « ou est » : la question que se pose vraiment l'utilisateur.
      expect(service.rechercher('ou est').some(a => a.id === 'suivre-en-direct')).toBe(true);
    });

    it('exige TOUS les mots tapes, sinon la recherche ramene la moitie de l’aide', () => {
      const resultats = service.rechercher('ajouter vehicule');
      expect(resultats.some(a => a.id === 'ajouter-vehicule')).toBe(true);
      // « saisir un plein » ne parle pas d'ajout de vehicule : il doit sortir du lot.
      expect(resultats.some(a => a.id === 'saisir-plein')).toBe(false);
    });

    it('classe le titre avant une simple mention dans le corps du texte', () => {
      const resultats = service.rechercher('rapport');
      expect(resultats[0].id).toBe('premier-rapport');
    });

    it('ne renvoie rien sur un mot absent, plutot que n’importe quoi', () => {
      expect(service.rechercher('zzzinconnu')).toEqual([]);
    });

    it('recherche vide : tout le contenu autorise', () => {
      expect(service.rechercher('').length).toBe(service.articlesVisibles().length);
    });

    it('repond a une QUESTION, pas seulement a des mots-cles', () => {
      // Constate le 21/09/2026 dans l'application : sans filtrage des mots
      // vides, « mon » et « comment » devaient eux aussi etre trouves et la
      // recherche ne rendait rien.
      expect(service.rechercher('ou est mon camion').some(a => a.id === 'suivre-en-direct')).toBe(true);
      expect(service.rechercher('comment je saisis un plein').some(a => a.id === 'saisir-plein')).toBe(true);
      expect(service.rechercher('je veux ajouter un vehicule').some(a => a.id === 'ajouter-vehicule')).toBe(true);
    });

    it('encaisse les conjugaisons et les pluriels', () => {
      expect(service.rechercher('saisis facture').some(a => a.id === 'scanner-facture')).toBe(true);
      expect(service.rechercher('vehicules').some(a => a.id === 'ajouter-vehicule')).toBe(true);
      expect(service.rechercher('reparations').some(a => a.id === 'saisir-reparation')).toBe(true);
    });

    it('une question faite QUE de mots vides rend tout, pas rien', () => {
      expect(service.rechercher('comment faire ?').length).toBe(service.articlesVisibles().length);
    });
  });

  describe('filtrage selon l’abonnement', () => {
    it('cache les articles d’un module non souscrit', () => {
      modulesAutorises = ['vehicles', 'reports'];
      expect(service.rechercher('zone').some(a => a.id === 'creer-geofence')).toBe(false);
      expect(service.articlesVisibles().some(a => a.id === 'saisir-plein')).toBe(false);
    });

    it('garde toujours les articles generaux (connexion, navigation)', () => {
      modulesAutorises = [];
      const visibles = service.articlesVisibles();
      expect(visibles.some(a => a.id === 'se-connecter')).toBe(true);
      expect(visibles.every(a => a.module === 'general')).toBe(true);
    });

    it('saute les etapes de visite guidee liees a un module absent (offre GPS)', () => {
      modulesAutorises = ['monitoring', 'vehicles'];
      const etapes = service.etapesGuide();
      expect(etapes.some(e => e.id === 'voir-la-carte')).toBe(true);
      expect(etapes.some(e => e.id === 'ajouter-chauffeurs')).toBe(false);
      expect(etapes.some(e => e.id === 'echeances')).toBe(false);
    });

    // Karim, 25/09/2026 : en GPA, les premiers pas remplacent la visite guidee.
    it('offre GPA : plus de visite guidee, ni proposee ni jouee', () => {
      modulesAutorises = ['vehicles', 'employees', 'maintenance', 'documents', 'users'];
      expect(service.etapesGuide()).toEqual([]);
      expect(service.doitProposerLeGuide()).toBe(false);   // client deja installe : pas de conseil non plus
    });
  });

  describe('memoire de la visite guidee', () => {
    it('proposee a la premiere connexion, plus jamais ensuite', () => {
      expect(service.doitProposerLeGuide()).toBe(true);
      service.fermerGuide(true);
      expect(service.doitProposerLeGuide()).toBe(false);
    });

    it('« Passer » vaut definitivement non : on n’insiste pas au rechargement', () => {
      service.fermerGuide(true);
      const autreInstance = TestBed.inject(HelpService);
      expect(autreInstance.doitProposerLeGuide()).toBe(false);
    });

    it('l’etat est garde par utilisateur : le collegue suivant a sa propre visite', () => {
      service.fermerGuide(true);
      expect(service.doitProposerLeGuide()).toBe(false);

      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          HelpService,
          { provide: AuthService, useValue: { getCurrentUserSync: () => ({ id: 'u2' }) } },
          { provide: PermissionService, useValue: { hasModuleAccess: () => true, abonnementComprend: () => true } }
        ]
      });
      expect(TestBed.inject(HelpService).doitProposerLeGuide()).toBe(true);
    });

    it('« Revoir la visite guidee » la represente', () => {
      service.fermerGuide(true);
      const ouvertures: boolean[] = [];
      service.guideOuvert$.subscribe(o => ouvertures.push(o));
      service.reinitialiserGuide();
      // Ouverte tout de suite…
      expect(ouvertures[ouvertures.length - 1]).toBe(true);
      // …et, si le client la quitte par Echap, reproposee a la prochaine ouverture
      // de l'application : le « Passer » d'avant est bien efface.
      service.fermerGuide(false);
      expect(nouvelleSession().doitProposerLeGuide()).toBe(true);
    });

    // Relecture du 22/09/2026 : <app-layout> est recreee a CHAQUE page et propose
    // la visite dans son ngOnInit. Echap puis un clic dans le menu, et la visite
    // repartait de l'etape 1 en ramenant de force au tableau de bord.
    it('Echap : pas de nouvelle proposition a la page suivante, mais a la prochaine session', () => {
      expect(service.doitProposerLeGuide()).toBe(true);
      service.proposerLeGuide();
      service.fermerGuide(false); // Echap
      expect(service.doitProposerLeGuide()).toBe(false);

      const ouvertures: boolean[] = [];
      service.guideOuvert$.subscribe(o => ouvertures.push(o));
      service.proposerLeGuide(); // ngOnInit de l'app-layout de la page suivante
      expect(ouvertures).toEqual([false]);

      expect(nouvelleSession().doitProposerLeGuide()).toBe(true);
    });

    it('une visite en cours n\'est pas reproposee par la page suivante', () => {
      service.proposerLeGuide();
      expect(service.doitProposerLeGuide()).toBe(false);
    });

    it('« Passer » tient pour la session meme si localStorage refuse l\'ecriture', () => {
      const setItem = jest.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
        throw new Error('QuotaExceededError');
      });
      try {
        service.proposerLeGuide();
        service.fermerGuide(true);
        expect(service.doitProposerLeGuide()).toBe(false);
      } finally {
        setItem.mockRestore();
      }
    });

    it('un collegue sur le meme poste garde sa propre proposition dans la session', () => {
      service.proposerLeGuide();
      compte = { id: 'u2' };
      expect(service.doitProposerLeGuide()).toBe(true);
    });
  });

  describe('filtrage plus fin que le module', () => {
    it('un paragraphe lie a un rapport ferme disparait, de l\'article comme de la recherche', () => {
      rapportsFermes = ['trips', 'stops', 'daily', 'speed', 'speed_infraction', 'driving_behavior'];
      const article = service.articleParId('choisir-rapport')!;
      const texte = (article.paragraphes || []).join(' ');
      expect(texte).not.toContain('Rapport de trajets');
      expect(texte).not.toContain('Infractions vitesse');
      expect(texte).toContain('Réparations véhicules');
      expect(service.rechercher('vitesse').some(a => a.id === 'choisir-rapport')).toBe(false);
    });

    it('le meme paragraphe revient des que le rapport est ouvert', () => {
      const texte = (service.articleParId('choisir-rapport')!.paragraphes || []).join(' ');
      expect(texte).toContain('Rapport de trajets');
      expect(service.rechercher('vitesse').some(a => a.id === 'choisir-rapport')).toBe(true);
    });

    it('« Emprunts » n\'est montre qu\'aux societes de location', () => {
      compte = { id: 'u1', companyType: 'transport' };
      expect(service.articleParId('emprunter-vehicule')).toBeUndefined();
      expect(service.rechercher('emprunt')).toEqual([]);
      compte = { id: 'u1', companyType: 'location' };
      expect(service.articleParId('emprunter-vehicule')).toBeDefined();
    });
  });

  describe('videos', () => {
    it('pas de lecteur tant que la capsule n’est pas tournee (url vide)', () => {
      const article = service.articleParId('ajouter-vehicule')!;
      expect(article.video).toBeDefined();
      expect(service.aUneVideo(article)).toBe(false);
    });

    it('lecteur des que l’url est renseignee', () => {
      expect(service.aUneVideo({
        id: 'x', titre: 'x', module: 'general', motsCles: [], resume: '',
        video: { titre: 'demo', url: '/uploads/aide/demo.mp4' }
      })).toBe(true);
    });
  });

  // Guides des ecrans (24/09/2026) : l’etat de l aide etait REMPLACE a chaque
  // ecriture. Terminer la visite guidee aurait efface les guides d’ecran deja vus.
  describe('guides des ecrans : l’etat se complete, il ne s’ecrase plus', () => {
    const etat = (id: string) => JSON.parse(localStorage.getItem('calypso_aide_v1') || '{}')[id] || {};

    it('terminer puis revoir la visite guidee garde les guides d’ecran vus', () => {
      const s = nouvelleSession('u9');
      s.marquerEcranVu('ecran-vehicules-gpa');
      s.fermerGuide(true);
      expect(etat('u9').ecransVus).toEqual(['ecran-vehicules-gpa']);
      expect(etat('u9').guideTermine).toBe(true);

      s.reinitialiserGuide();
      expect(etat('u9').ecransVus).toEqual(['ecran-vehicules-gpa']);
      expect(etat('u9').guideTermine).toBe(false);
    });

    it('« Revoir les guides des ecrans » les remet tous, sans toucher a la visite guidee', () => {
      const s = nouvelleSession('u9');
      s.fermerGuide(true);
      s.marquerEcranVu('ecran-vehicules-gpa');
      s.reinitialiserEcrans();
      expect(etat('u9').ecransVus).toEqual([]);
      expect(etat('u9').guideTermine).toBe(true);
    });
  });

  // Karim, 26/09/2026 : « sur le compte Belive GPA, prends comme si c'est ma première
  // connexion à chaque fois que je clique sur Revoir les premiers pas ».
  describe('« Revoir les premiers pas » en local sur « Belive GPA » : une première connexion', () => {
    const etat = (id: string) => JSON.parse(localStorage.getItem('calypso_aide_v1') || '{}')[id] || {};
    const offreGpa = () => { modulesAutorises = ['vehicles', 'employees', 'maintenance', 'documents', 'users', 'accidents']; };

    it('conseil, premiers pas et guides de TOUS les écrans reviennent', () => {
      offreGpa();
      compte = { id: 'u-karim', companyName: 'Belive GPA', isCompanyAdmin: true };
      service.marquerConseilVu();
      service.marquerEcranVu('tuto-vehicules-gpa');
      service.marquerEcranVu('tuto-reparations-gpa');
      service.arreterPremiersPas();

      service.reinitialiserGuide();

      expect(etat('u-karim').ecransVus).toEqual([]);
      expect(service.conseilAMontrer()).toBe(true);
      expect(service.visiteEcranAProposer('/reparations')?.id).toBe('tuto-reparations-gpa');
    });

    it('une autre société : seuls le conseil et les premiers pas reviennent', () => {
      offreGpa();
      compte = { id: 'u-autre', firstLogin: true, companyName: 'Transports Martin', isCompanyAdmin: true };
      service.marquerEcranVu('tuto-vehicules-gpa');
      service.marquerEcranVu('tuto-reparations-gpa');

      service.reinitialiserGuide();

      expect(etat('u-autre').ecransVus).toEqual(['tuto-reparations-gpa']);
    });

    it('en production (isDevMode faux), rien de tout cela', () => {
      offreGpa();
      compte = { id: 'u-karim', companyName: 'Belive GPA', isCompanyAdmin: true };
      jest.spyOn(service, 'enDeveloppement').mockReturnValue(false);
      service.marquerEcranVu('tuto-reparations-gpa');

      service.reinitialiserGuide();

      expect(etat('u-karim').ecransVus).toEqual(['tuto-reparations-gpa']);
    });
  });
});
