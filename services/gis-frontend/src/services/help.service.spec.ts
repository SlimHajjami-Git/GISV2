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

  beforeEach(() => {
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
        { provide: AuthService, useValue: { getCurrentUserSync: () => ({ id: 'u1' }) } },
        { provide: PermissionService, useValue: { hasModuleAccess: (m: string) => modulesAutorises.includes(m) } }
      ]
    });

    service = TestBed.inject(HelpService);
    localStorage.clear();
  });

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

    it('saute les etapes de visite guidee liees a un module absent', () => {
      modulesAutorises = ['vehicles'];
      const etapes = service.etapesGuide();
      expect(etapes.some(e => e.id === 'ajouter-vehicule')).toBe(true);
      expect(etapes.some(e => e.id === 'voir-la-carte')).toBe(false);
      expect(etapes.some(e => e.id === 'premier-rapport')).toBe(false);
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
          { provide: PermissionService, useValue: { hasModuleAccess: () => true } }
        ]
      });
      expect(TestBed.inject(HelpService).doitProposerLeGuide()).toBe(true);
    });

    it('« Revoir la visite guidee » la represente', () => {
      service.fermerGuide(true);
      service.reinitialiserGuide();
      expect(service.doitProposerLeGuide()).toBe(true);
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
});
