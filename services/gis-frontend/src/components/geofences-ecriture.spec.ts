import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { GeofencesComponent, corpsBasculeActif, messageErreurZone } from './geofences.component';
import { ApiService } from '../services/api.service';
import { ToastService } from '../services/toast.service';

/**
 * Écran Géozones — écritures d'un gestionnaire de zones (23/09/2026).
 *
 * Règle serveur du jour : un compte qui a la permission « Géofences » (7 Opérateurs de
 * SICOAC, des comptes de PARENIN, EXALTIS, BELIVE) crée une zone et modifie, supprime,
 * bascule toute zone qu'il VOIT ; une zone hors de son périmètre répond 404. L'écran
 * doit donc marcher pour lui, et chaque refus doit être DIT — il n'allait qu'en console.
 *
 * Tenu aussi ici : la bascule actif/inactif envoyait `{ isActive }` seul, et le serveur
 * recopie tous les champs du corps — chaque clic effaçait le nom et la forme de la zone.
 */
describe('corpsBasculeActif — la bascule renvoie la zone entière', () => {
  const zone = {
    id: 7, name: 'Dépôt Radès', description: 'Quai 3', type: 'circle', color: '#22c55e', iconName: null,
    coordinates: null, centerLat: 36.77, centerLng: 10.27, center: { lat: 36.77, lng: 10.27 }, radius: 400,
    alertOnEntry: true, alertOnExit: false, autoStopOnEntry: false, alertSpeedLimit: 50,
    notificationCooldownMinutes: 0, maxStayDurationMinutes: 30,
    activeStartTime: '08:00:00', activeEndTime: '18:00:00', activeDays: ['monday', 'tuesday'],
    groupId: 3, isActive: true, assignedVehicleIds: ['12'], assignedVehicleNames: ['123 TU 4567']
  };

  it('inverse isActive et garde nom, forme, alertes, plage horaire et groupe', () => {
    const corps = corpsBasculeActif(zone);

    expect(corps.isActive).toBe(false);
    expect(corps).toMatchObject({
      name: 'Dépôt Radès', description: 'Quai 3', type: 'circle', color: '#22c55e',
      centerLat: 36.77, centerLng: 10.27, radius: 400,
      alertOnEntry: true, alertOnExit: false, alertSpeedLimit: 50, maxStayDurationMinutes: 30,
      activeStartTime: '08:00:00', activeEndTime: '18:00:00', activeDays: ['monday', 'tuesday'],
      groupId: 3
    });
  });

  it('garde un délai de notification à 0 (pas remplacé par 5)', () => {
    expect(corpsBasculeActif(zone).notificationCooldownMinutes).toBe(0);
  });

  it("n'envoie pas les liaisons : elles passent par leur route dédiée", () => {
    const corps = corpsBasculeActif(zone);
    expect(corps.assignedVehicleIds).toBeUndefined();
    expect(corps.assignedVehicleNames).toBeUndefined();
  });

  it('polygone : garde les points et réactive une zone inactive', () => {
    const polygone = { ...zone, type: 'polygon', centerLat: null, centerLng: null, center: undefined, radius: null,
      coordinates: [{ lat: 1, lng: 1 }, { lat: 1, lng: 2 }, { lat: 2, lng: 2 }], isActive: false };
    const corps = corpsBasculeActif(polygone);
    expect(corps.isActive).toBe(true);
    expect(corps.coordinates).toHaveLength(3);
    expect(corps.centerLat).toBeNull();
  });
});

describe('messageErreurZone — aucun refus muet', () => {
  it('404 : la zone a quitté le périmètre, la liste est relue', () => {
    expect(messageErreurZone({ status: 404 }, 'supprimer cette zone')).toContain('ne fait plus partie de votre périmètre');
  });

  it('403 sans message serveur : explique la permission Géofences', () => {
    expect(messageErreurZone({ status: 403 }, 'créer cette zone')).toContain('« Géofences »');
  });

  it('400 : reprend le message du serveur (véhicule hors périmètre)', () => {
    const err = { status: 400, error: { message: 'Un ou plusieurs véhicules sont introuvables ou hors de votre périmètre.' } };
    expect(messageErreurZone(err, 'rattacher ces véhicules')).toBe('Un ou plusieurs véhicules sont introuvables ou hors de votre périmètre.');
  });

  it('403 du contrôle des modules : reprend le message du serveur', () => {
    const err = { status: 403, error: { message: "Vous n'avez pas accès à ce module", code: 'USER_PERMISSION_DENIED' } };
    expect(messageErreurZone(err, 'modifier cette zone')).toBe("Vous n'avez pas accès à ce module");
  });

  it('réseau coupé (statut 0) et erreur serveur : un message lisible, jamais « HTTP 0 »', () => {
    expect(messageErreurZone({ status: 0 }, 'x')).toContain('Serveur injoignable');
    expect(messageErreurZone({ status: 500 }, 'modifier cette zone')).toContain('erreur 500');
    expect(messageErreurZone(undefined, 'x')).toContain('Serveur injoignable');
  });
});

describe('GeofencesComponent — écritures et refus du serveur', () => {
  let component: GeofencesComponent;
  let api: ApiService;
  let toast: ToastService;
  let rendus: number;

  const zone: any = {
    id: 7, name: 'Dépôt Radès', type: 'circle', color: '#22c55e', centerLat: 36.77, centerLng: 10.27,
    center: { lat: 36.77, lng: 10.27 }, radius: 400, alertOnEntry: true, alertOnExit: true,
    notificationCooldownMinutes: 5, isActive: true, groupId: null, assignedVehicleIds: []
  };

  beforeEach(async () => {
    // L'en-tête de l'écran (app-layout) instancie le service d'export PDF, qui précharge
    // le logo par fetch() — absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, GeofencesComponent],
      providers: [ApiService],
    }).compileComponents();

    const fixture = TestBed.createComponent(GeofencesComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);
    toast = TestBed.inject(ToastService);

    // Le gabarit (carte Leaflet, app-layout) n'est pas rendu : on compte les demandes de
    // rendu explicites, obligatoires après un retour HTTP en Angular 21.
    rendus = 0;
    (component as any).cdr = { detectChanges: () => { rendus++; } };
    jest.spyOn(component, 'refreshData').mockImplementation(() => {});
    jest.spyOn(toast, 'error').mockImplementation(() => {});
    jest.spyOn(toast, 'warning').mockImplementation(() => {});
  });

  it('bascule : envoie la zone ENTIÈRE, isActive inversé', () => {
    const update = jest.spyOn(api, 'updateGeofence').mockReturnValue(of(void 0) as any);

    component.toggleActive(zone);

    expect(update).toHaveBeenCalledWith(7, expect.objectContaining({
      name: 'Dépôt Radès', type: 'circle', centerLat: 36.77, radius: 400, isActive: false
    }));
    expect(component.refreshData).toHaveBeenCalled();
    expect(component.estEnBascule(zone)).toBe(false);
  });

  it('bascule refusée (404) : message, liste relue, basculeur libéré et rendu demandé', () => {
    jest.spyOn(api, 'updateGeofence').mockReturnValue(throwError(() => ({ status: 404 })) as any);

    component.toggleActive(zone);

    expect(toast.error).toHaveBeenCalledWith('Zone non modifiée', expect.stringContaining('périmètre'));
    expect(component.refreshData).toHaveBeenCalled();
    expect(component.estEnBascule(zone)).toBe(false);
    expect(rendus).toBeGreaterThan(0);
  });

  it('bascule : un second clic pendant la requête ne part pas', () => {
    const update = jest.spyOn(api, 'updateGeofence').mockReturnValue({ subscribe: () => ({}) } as any);

    component.toggleActive(zone);
    expect(component.estEnBascule(zone)).toBe(true);
    component.toggleActive(zone);

    expect(update).toHaveBeenCalledTimes(1);
  });

  it('suppression refusée (403) : message, rien de muet', () => {
    jest.spyOn(window, 'confirm').mockReturnValue(true);
    jest.spyOn(api, 'deleteGeofence').mockReturnValue(throwError(() => ({ status: 403 })) as any);

    component.deleteGeofence(zone);

    expect(toast.error).toHaveBeenCalledWith('Zone non supprimée', expect.stringContaining('« Géofences »'));
    expect(component.refreshData).toHaveBeenCalled();
  });

  function formulaireCercle() {
    component.geofenceForm = {
      ...component.geofenceForm,
      name: 'Chantier Bizerte', type: 'circle', centerLat: 37.27, centerLng: 9.87, radius: 300,
      assignedVehicleIds: ['12']
    };
    component.showPopup = true;
  }

  it('création refusée (403) : message, formulaire GARDÉ ouvert, bouton réactivé', () => {
    formulaireCercle();
    jest.spyOn(api, 'createGeofence').mockReturnValue(throwError(() => ({ status: 403 })) as any);

    component.saveGeofence();

    expect(toast.error).toHaveBeenCalledWith('Zone non enregistrée', expect.any(String));
    expect(component.showPopup).toBe(true);
    expect(component.enregistrementEnCours).toBe(false);
    expect(rendus).toBeGreaterThan(0);
  });

  it('modification d’une zone sortie du périmètre (404) : message, formulaire fermé, liste relue', () => {
    formulaireCercle();
    component.editingGeofence = zone;
    jest.spyOn(api, 'updateGeofence').mockReturnValue(throwError(() => ({ status: 404 })) as any);

    component.saveGeofence();

    expect(toast.error).toHaveBeenCalled();
    expect(component.showPopup).toBe(false);
    expect(component.refreshData).toHaveBeenCalled();
  });

  it('zone créée mais véhicule hors périmètre (400) : avertissement explicite, zone conservée', () => {
    formulaireCercle();
    jest.spyOn(api, 'createGeofence').mockReturnValue(of({ id: 55 }) as any);
    const rattacher = jest.spyOn(api, 'assignGeofenceVehicles').mockReturnValue(
      throwError(() => ({ status: 400, error: { message: 'Un ou plusieurs véhicules sont introuvables ou hors de votre périmètre.' } })) as any);

    component.saveGeofence();

    expect(rattacher).toHaveBeenCalledWith(55, [12]);
    expect(toast.warning).toHaveBeenCalledWith('Zone enregistrée, véhicules non rattachés',
      'Un ou plusieurs véhicules sont introuvables ou hors de votre périmètre.');
    expect(component.showPopup).toBe(false);
    expect(component.enregistrementEnCours).toBe(false);
  });

  it('création réussie par un gestionnaire de zones : formulaire fermé, liste relue, aucun message d’erreur', () => {
    formulaireCercle();
    jest.spyOn(api, 'createGeofence').mockReturnValue(of({ id: 56 }) as any);
    jest.spyOn(api, 'assignGeofenceVehicles').mockReturnValue(of(void 0) as any);

    component.saveGeofence();

    expect(component.showPopup).toBe(false);
    expect(component.refreshData).toHaveBeenCalled();
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.warning).not.toHaveBeenCalled();
  });
});
