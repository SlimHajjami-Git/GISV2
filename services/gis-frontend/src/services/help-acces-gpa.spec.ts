import { TestBed } from '@angular/core/testing';
import { HelpService } from './help.service';
import { PermissionService } from './permission.service';
import { AuthService } from './auth.service';

/**
 * Regle posee par Karim le 21/09/2026 : un client dont l'abonnement ne contient
 * que la GPA ne doit PAS voir l'aide des fonctionnalites GPS (geofencing, suivi
 * en direct, playback) — meme s'il est administrateur de sa societe.
 *
 * Ce test branche le VRAI PermissionService (pas un mock) sur l'aide, pour
 * prouver que l'abonnement prime sur le statut d'administrateur.
 */
describe('Aide — un abonnement GPA ne montre jamais les articles GPS', () => {

  function aideAvecCompte(compte: any): HelpService {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        HelpService,
        PermissionService,
        { provide: AuthService, useValue: { getCurrentUserSync: () => compte } }
      ]
    });
    return TestBed.inject(HelpService);
  }

  /**
   * Abonnement "gestion de parc seule", aligne sur le VRAI plan-basique (seed de
   * Program.cs, migration FixNoGpsPlanDefinition, 037 et 040) : pas de suivi GPS,
   * pas de geofences, pas de tournees, et le module Rapports ouvert mais les
   * rapports GPS fermes un a un. Le mock precedent ouvrait les tournees et ne
   * portait aucun drapeau report_* : il laissait passer les articles qui
   * envoyaient ce client chercher un « Rapport de trajets » (relecture du 22/09/2026).
   */
  const abonnementGpa = {
    gpsTracking: false,
    moduleDashboard: true, moduleMonitoring: false, moduleGeofences: false,
    moduleVehicles: true, moduleEmployees: true, moduleMaintenance: true,
    moduleCosts: true, moduleReports: true, moduleFuel: true,
    moduleDocuments: true, moduleUsers: true, moduleSettings: true,
    moduleSuppliers: true, moduleAccidents: true, moduleFleetManagement: false,
    moduleTours: false,
    reportTrips: false, reportFuel: false, reportSpeed: false, reportStops: false,
    reportMileage: false, reportCosts: true, reportMaintenance: true,
    reportDaily: false, reportMonthly: false, reportMileagePeriod: false,
    reportSpeedInfraction: false, reportDrivingBehavior: false, reportMonthlyCosts: true
  };

  /** Rapports GPS : fermes au plan-basique, ils ne doivent etre cites nulle part. */
  const RAPPORTS_GPS = [
    'Rapport de trajets', 'Rapport des arrêts', 'Rapport journalier', 'Rapport de vitesse',
    'Infractions vitesse', 'Comportement conduite', 'Kilométrage par période',
    'Rapport kilométrique', 'Carburant réel vs GPS', 'Estimation coûts carburant',
    '« Consommation carburant »'
  ];

  /** Tout le texte qu'un article montre au client. */
  const texteDe = (a: any): string =>
    [a.titre, a.resume, ...(a.etapes || []), ...(a.paragraphes || []), a.aRetenir || ''].join(' ');

  const ARTICLES_GPS = ['suivre-en-direct', 'partager-position', 'rejouer-trajet', 'creer-geofence', 'remorquages'];

  it('ADMIN de societe en GPA : aucun article GPS, malgre son statut d’administrateur', () => {
    const aide = aideAvecCompte({
      id: 'u-admin-gpa', isSystemAdmin: false, isCompanyAdmin: true,
      subscriptionFeatures: abonnementGpa, userPermissions: null
    });

    const visibles = aide.articlesVisibles().map(a => a.id);
    for (const interdit of ARTICLES_GPS) {
      expect(visibles).not.toContain(interdit);
    }
    // Et il garde bien tout le reste de la gestion de parc.
    expect(visibles).toContain('ajouter-vehicule');
    expect(visibles).toContain('saisir-plein');
    expect(visibles).toContain('entretien-ou-reparation');
  });

  it('la recherche ne ramene rien sur "geofence" ni sur "suivi" pour ce meme admin', () => {
    const aide = aideAvecCompte({
      id: 'u-admin-gpa', isSystemAdmin: false, isCompanyAdmin: true,
      subscriptionFeatures: abonnementGpa, userPermissions: null
    });

    expect(aide.rechercher('geofence')).toEqual([]);
    expect(aide.rechercher('zone')).toEqual([]);
    expect(aide.rechercher('playback')).toEqual([]);
    expect(aide.articleParId('suivre-en-direct')).toBeUndefined();
  });

  it('la visite guidee saute l’etape "carte en direct" en GPA', () => {
    const aide = aideAvecCompte({
      id: 'u-admin-gpa', isSystemAdmin: false, isCompanyAdmin: true,
      subscriptionFeatures: abonnementGpa, userPermissions: null
    });

    const etapes = aide.etapesGuide().map(e => e.id);
    expect(etapes).not.toContain('voir-la-carte');
    // Parcours GPA fixe par Karim le 23/09/2026, dans cet ordre : vehicule,
    // chauffeurs, echeances, programme d'entretien, son affectation, puis les
    // alertes par e-mail (ajoutees le meme jour : « une etape tres importante »).
    expect(etapes).toEqual([
      'bienvenue', 'ajouter-vehicule', 'ajouter-chauffeurs', 'echeances', 'entretien-modele', 'entretien-affecter', 'alertes-email'
    ]);
  });

  it('offre GPS : pas d’ajout de vehicule (crees par l’equipe Belive), mais la carte et les zones', () => {
    // Parcours GPS fixe par Karim le 23/09/2026 : « presque la meme chose que
    // GPA », sans « Ajoutez votre premier vehicule ». Toutes les offres GPS
    // (Standard, Pro, Premium) ont le suivi, le geofencing et les tournees.
    const aide = aideAvecCompte({
      id: 'u-admin-gps', isSystemAdmin: false, isCompanyAdmin: true,
      subscriptionFeatures: { ...abonnementGpa, moduleMonitoring: true, moduleGeofences: true, moduleTours: true },
      userPermissions: null
    });

    expect(aide.etapesGuide().map(e => e.id)).toEqual([
      'bienvenue-gps', 'vehicules-en-place', 'ajouter-chauffeurs', 'voir-la-carte',
      'echeances', 'entretien-modele', 'entretien-affecter', 'alertes-email', 'premier-rapport'
    ]);
  });

  it('les alertes par e-mail sont avant-dernieres en GPS, dernieres en GPA — et jamais sans le droit Utilisateurs', () => {
    const gps = aideAvecCompte({ id: 'u-gps', isSystemAdmin: false, isCompanyAdmin: true, subscriptionFeatures: { ...abonnementGpa, moduleMonitoring: true, moduleGeofences: true, moduleTours: true }, userPermissions: null });
    const etapesGps = gps.etapesGuide().map(e => e.id);
    expect(etapesGps[etapesGps.length - 2]).toBe('alertes-email');
    const gpa = aideAvecCompte({ id: 'u-gpa', isSystemAdmin: false, isCompanyAdmin: true, subscriptionFeatures: abonnementGpa, userPermissions: null });
    const etapesGpa = gpa.etapesGuide().map(e => e.id);
    expect(etapesGpa[etapesGpa.length - 1]).toBe('alertes-email');
    // Un utilisateur simple sans le droit « Utilisateurs » ne peut pas ouvrir
    // l'ecran : l'etape est retiree, la visite se termine sur l'affectation.
    const sansDroit = aideAvecCompte({ id: 'u-sans', isSystemAdmin: false, isCompanyAdmin: false, subscriptionFeatures: abonnementGpa, userPermissions: { canVehicles: true, canEmployees: true, canDocuments: true, canMaintenance: true, canUsers: false } });
    expect(sansDroit.etapesGuide().map(e => e.id)).not.toContain('alertes-email');
  });

  it('« Terminer » depose le client sur Vehicules en GPA, sur Suivi en direct en GPS', () => {
    const gpa = aideAvecCompte({ id: 'u-gpa', isSystemAdmin: false, isCompanyAdmin: true, subscriptionFeatures: abonnementGpa, userPermissions: null });
    const gps = aideAvecCompte({ id: 'u-gps', isSystemAdmin: false, isCompanyAdmin: true, subscriptionFeatures: { ...abonnementGpa, moduleMonitoring: true, moduleGeofences: true, moduleTours: true }, userPermissions: null });
    const derniere = (etapes: ReturnType<typeof gpa.etapesGuide>) => etapes[etapes.length - 1];
    expect(derniere(gpa.etapesGuide()).routeApresFin).toBe('/vehicles');
    expect(derniere(gps.etapesGuide()).routeApresFin).toBe('/monitoring');
  });

  it('ADMIN en GPA : aucun article visible ne cite un rapport GPS ferme', () => {
    const aide = aideAvecCompte({
      id: 'u-admin-gpa', isSystemAdmin: false, isCompanyAdmin: true,
      subscriptionFeatures: abonnementGpa, userPermissions: null
    });

    for (const article of aide.articlesVisibles()) {
      for (const rapport of RAPPORTS_GPS) {
        expect({ article: article.id, cite: texteDe(article).includes(rapport) })
          .toEqual({ article: article.id, cite: false });
      }
    }
    // Les rapports qu'il A bien restent presentes.
    const choisir = texteDe(aide.articleParId('choisir-rapport')!);
    expect(choisir).toContain('Réparations véhicules');
    expect(choisir).toContain('Coûts maintenance');
    expect(texteDe(aide.articleParId('premier-rapport')!)).toContain('Réparations véhicules');
  });

  it('la recherche « trajet », « vitesse » ou « arret » ne l\'envoie vers aucun rapport ferme', () => {
    const aide = aideAvecCompte({
      id: 'u-admin-gpa', isSystemAdmin: false, isCompanyAdmin: true,
      subscriptionFeatures: abonnementGpa, userPermissions: null
    });

    for (const question of ['trajet', 'vitesse', 'arret']) {
      for (const article of aide.rechercher(question)) {
        for (const rapport of RAPPORTS_GPS) expect(texteDe(article)).not.toContain(rapport);
      }
      expect(aide.rechercher(question).map(a => a.id)).not.toContain('choisir-rapport');
      expect(aide.rechercher(question).map(a => a.id)).not.toContain('premier-rapport');
    }
  });

  it('abonnement GPS, mais droit « Rapport de trajets » retire a CET utilisateur : l\'aide ne le cite plus', () => {
    const aide = aideAvecCompte({
      id: 'u-compta', isSystemAdmin: false, isCompanyAdmin: false,
      subscriptionFeatures: { ...abonnementGpa, moduleMonitoring: true, gpsTracking: true,
        reportTrips: true, reportStops: true, reportSpeed: true },
      userPermissions: { canReports: true, canReportTrips: false, canReportStops: true, canReportSpeed: true }
    });

    const choisir = texteDe(aide.articleParId('choisir-rapport')!);
    expect(choisir).not.toContain('Rapport de trajets');
    expect(choisir).toContain('Rapport des arrêts');
    expect(choisir).toContain('Rapport de vitesse');
  });

  it('meme abonnement GPA, utilisateur simple : toujours aucun article GPS', () => {
    const aide = aideAvecCompte({
      id: 'u-simple-gpa', isSystemAdmin: false, isCompanyAdmin: false,
      subscriptionFeatures: abonnementGpa,
      userPermissions: { canMonitoring: true, canGeofences: true, canVehicles: true }
      // Droits utilisateur volontairement ouverts sur le GPS : l'abonnement doit primer.
    });

    const visibles = aide.articlesVisibles().map(a => a.id);
    for (const interdit of ARTICLES_GPS) {
      expect(visibles).not.toContain(interdit);
    }
  });

  it('abonnement GPS complet : les articles GPS reviennent', () => {
    const aide = aideAvecCompte({
      id: 'u-admin-gps', isSystemAdmin: false, isCompanyAdmin: true,
      subscriptionFeatures: { ...abonnementGpa, moduleMonitoring: true, moduleGeofences: true },
      userPermissions: null
    });

    const visibles = aide.articlesVisibles().map(a => a.id);
    expect(visibles).toContain('suivre-en-direct');
    expect(visibles).toContain('creer-geofence');
    expect(visibles).toContain('rejouer-trajet');
  });

  it('abonnement GPS mais droit Monitoring retire a CET utilisateur : pas d’articles de suivi', () => {
    const aide = aideAvecCompte({
      id: 'u-compta', isSystemAdmin: false, isCompanyAdmin: false,
      subscriptionFeatures: { ...abonnementGpa, moduleMonitoring: true, moduleGeofences: true },
      userPermissions: { canMonitoring: false, canGeofences: false, canCosts: true, canMaintenance: true }
    });

    const visibles = aide.articlesVisibles().map(a => a.id);
    expect(visibles).not.toContain('suivre-en-direct');
    expect(visibles).not.toContain('creer-geofence');
    // Le comptable garde ce qui le concerne.
    expect(visibles).toContain('entretien-ou-reparation');
  });

  it('un droit non renseigne vaut REFUS, pas autorisation', () => {
    // Regle du PermissionService : hors administrateur, un module absent de
    // userPermissions retombe sur le tableau de bord seul. A savoir quand on
    // cree un utilisateur : ne rien cocher ne donne pas "tout".
    const aide = aideAvecCompte({
      id: 'u-vierge', isSystemAdmin: false, isCompanyAdmin: false,
      subscriptionFeatures: { ...abonnementGpa, moduleMonitoring: true, moduleGeofences: true },
      userPermissions: {}
    });

    const modules = new Set(aide.articlesVisibles().map(a => a.module));
    expect([...modules].every(m => m === 'general' || m === 'dashboard')).toBe(true);
  });
});
