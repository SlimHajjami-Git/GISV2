import { TestBed } from '@angular/core/testing';
import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpClientTestingModule, provideHttpClientTesting } from '@angular/common/http/testing';
import { RouterTestingHarness, RouterTestingModule } from '@angular/router/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { of, Subject, throwError } from 'rxjs';
import { ToursComponent } from './tours.component';
import { ApiService } from '../services/api.service';
import { ToastService } from '../services/toast.service';
import { SignalRService } from '../services/signalr.service';
import { NO_APP_ACCOUNT_TOOLTIP } from './tours-tracking.helpers';

/**
 * Écran Tournées — envoi au chauffeur (21/09/2026).
 *
 * Le gestionnaire envoie la tournée sur le téléphone de son chauffeur (compte
 * application) puis la suit. Tenu ici : le bouton « Envoyer au chauffeur » est
 * verrouillé sans compte application actif, l'appel POST /tours/{id}/send part
 * bien, et l'issue du push (téléphone joint ou non) est dite au gestionnaire.
 */
describe('ToursComponent — envoi de la tournée au chauffeur', () => {
  let component: ToursComponent;
  let fixture: any;
  let api: ApiService;
  let toast: ToastService;

  const drivers = [
    { id: 1, firstName: 'Ali', lastName: 'Ben Salah', assignedVehicleId: 10, status: 'active', userId: 11, accountStatus: 'active' },
    { id: 2, firstName: 'Sami', lastName: 'Sans App', assignedVehicleId: null, status: 'active', userId: null, accountStatus: null }
  ];

  const tourDetail = (patch: any = {}) => ({
    id: 5, name: 'Livraison Sfax', status: 'planned', vehicleId: 10, vehicleName: 'Camion 12',
    driverId: 1, driverName: 'Ali Ben Salah', scheduledStartTime: '2026-09-22T07:00:00Z',
    sentAt: null, openedAt: null, actualStartTime: null,
    estimatedDistanceKm: 120, estimatedDurationMinutes: 95, estimatedFuelLiters: 10.8, totalPauseMinutes: 0,
    waypoints: [
      { id: 50, sequenceOrder: 0, type: 'origin', name: 'Dépôt', latitude: 36.8, longitude: 10.1, isCompleted: false },
      { id: 51, sequenceOrder: 1, type: 'destination', name: 'Client', latitude: 34.7, longitude: 10.7, isCompleted: false }
    ],
    pauses: [],
    ...patch
  });

  const preparer = async (routeId: string | null = null) => {
    // L'écran injecte le service d'export PDF, qui précharge le logo par fetch() — absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, ToursComponent],
      providers: [
        ApiService,
        { provide: ActivatedRoute, useValue: { paramMap: of(convertToParamMap(routeId ? { id: routeId } : {})), snapshot: { paramMap: convertToParamMap({}), queryParamMap: convertToParamMap({}) } } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(ToursComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);
    toast = TestBed.inject(ToastService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of([{ id: 10, name: 'Camion 12', plate: '123 TU 4567' }]) as any);
    jest.spyOn(api, 'getDrivers').mockReturnValue(of(drivers) as any);
    jest.spyOn(api, 'getGeofences').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getTours').mockReturnValue(of({ items: [{ ...tourDetail(), waypointCount: 2 }], totalCount: 1 }) as any);
    jest.spyOn(api, 'getTourStats').mockReturnValue(of({ total: 1, planned: 1, inProgress: 0, completed: 0, cancelled: 0 }) as any);
    jest.spyOn(api, 'getTourTracking').mockReturnValue(of({ source: 'none', vehicle: null, phone: null, progress: {}, waypoints: [] }) as any);
    jest.spyOn(toast, 'success').mockImplementation(() => {});
    jest.spyOn(toast, 'warning').mockImplementation(() => {});
    jest.spyOn(toast, 'info').mockImplementation(() => {});
    jest.spyOn(toast, 'error').mockImplementation(() => {});

    // Leaflet n'existe pas sous jsdom : les cartes ne sont pas construites.
    jest.spyOn(component, 'initDetailMap').mockImplementation(() => {});
    jest.spyOn(component, 'initTourMap').mockImplementation(() => {});
  };

  afterEach(() => {
    fixture?.destroy();
  });

  const boutonEnvoyer = (): HTMLButtonElement | null => fixture.nativeElement.querySelector('button.btn-send');

  it('chauffeur sans compte application : bouton verrouillé, motif « créez-le dans Utilisateurs »', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({ driverId: 2, driverName: 'Sami Sans App' })) as any);
    fixture.detectChanges();

    component.openDetail({ id: 5 });
    fixture.detectChanges();

    const bouton = boutonEnvoyer();
    expect(bouton).toBeTruthy();
    expect(bouton!.disabled).toBe(true);
    expect(bouton!.getAttribute('title')).toBe(NO_APP_ACCOUNT_TOOLTIP);
    expect(bouton!.textContent).toContain('Envoyer au chauffeur');
  });

  it('sans chauffeur : bouton verrouillé aussi', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({ driverId: null, driverName: null })) as any);
    fixture.detectChanges();

    component.openDetail({ id: 5 });
    fixture.detectChanges();

    expect(boutonEnvoyer()!.disabled).toBe(true);
    expect(boutonEnvoyer()!.getAttribute('title')).toContain('chauffeur');
  });

  it('chauffeur avec compte : bouton actif, POST /tours/5/send, toast « Envoyée au téléphone du chauffeur »', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail()) as any);
    const send = jest.spyOn(api, 'sendTourToDriver').mockReturnValue(
      of({ sentAt: '2026-09-21T09:30:00Z', push: 'delivered_to_fcm', resent: false }) as any
    );
    fixture.detectChanges();

    component.openDetail({ id: 5 });
    fixture.detectChanges();

    const bouton = boutonEnvoyer()!;
    expect(bouton.disabled).toBe(false);
    expect(bouton.getAttribute('title')).not.toBe(NO_APP_ACCOUNT_TOOLTIP);

    bouton.click();
    fixture.detectChanges();

    expect(send).toHaveBeenCalledWith(5);
    expect(toast.success).toHaveBeenCalledWith('Tournée envoyée', 'Envoyée au téléphone du chauffeur', expect.any(Number));
    // La tournée porte désormais sentAt : le bouton devient « Renvoyer », la liste aussi.
    expect(component.selectedTour.sentAt).toBe('2026-09-21T09:30:00Z');
    expect(component.tours[0].sentAt).toBe('2026-09-21T09:30:00Z');
    expect(boutonEnvoyer()!.textContent).toContain('Renvoyer au chauffeur');
  });

  it.each([
    ['no_device', 'warning', "Envoyée, mais le chauffeur n'a pas encore ouvert l'application sur son téléphone"],
    ['firebase_off', 'warning', 'Enregistrée, notification non délivrée'],
    ['failed', 'warning', 'Enregistrée, notification non délivrée'],
    ['quiet_hours', 'info', 'Enregistrée, le chauffeur est en heures silencieuses']
  ])('push %s → toast %s', async (push, type, message) => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({ sentAt: '2026-09-20T10:00:00Z' })) as any);
    jest.spyOn(api, 'sendTourToDriver').mockReturnValue(of({ sentAt: '2026-09-21T09:30:00Z', push, resent: true }) as any);
    fixture.detectChanges();

    component.openDetail({ id: 5 });
    component.sendSelectedTour();

    expect((toast as any)[type]).toHaveBeenCalledWith('Tournée renvoyée', message, expect.any(Number));
  });

  it('refus DRIVER_NO_APP_ACCOUNT du serveur : le message du serveur est affiché', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail()) as any);
    const motif = "Ce chauffeur n'a pas de compte application actif. Créez-le dans Utilisateurs (case « Chauffeur »).";
    jest.spyOn(api, 'sendTourToDriver').mockReturnValue(
      throwError(() => ({ status: 400, error: { code: 'DRIVER_NO_APP_ACCOUNT', message: motif } })) as any
    );
    fixture.detectChanges();

    component.openDetail({ id: 5 });
    component.sendSelectedTour();

    expect(toast.error).toHaveBeenCalledWith('Tournée non envoyée', motif, expect.any(Number));
    expect(component.selectedTour.sentAt).toBeNull();
    expect(component.sending).toBe(false);
  });

  it('formulaire : « Enregistrer et envoyer » n’apparaît qu’avec un chauffeur équipé, et crée PUIS envoie', async () => {
    await preparer();
    const create = jest.spyOn(api, 'createTour').mockReturnValue(of(tourDetail({ id: 9 })) as any);
    const send = jest.spyOn(api, 'sendTourToDriver').mockReturnValue(
      of({ sentAt: '2026-09-21T09:30:00Z', push: 'no_device', resent: false }) as any
    );
    fixture.detectChanges();

    component.openCreate();
    component.tourForm.name = 'Test';
    component.tourForm.vehicleId = 10;
    component.tourForm.scheduledStartTime = '2026-09-22T07:00';
    component.tourForm.waypoints[0].latitude = 36.8; component.tourForm.waypoints[0].longitude = 10.1;
    component.tourForm.waypoints[1].latitude = 34.7; component.tourForm.waypoints[1].longitude = 10.7;

    component.tourForm.driverId = 2; // sans compte
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('button.btn-save-send')).toBeNull();
    expect(fixture.nativeElement.querySelector('.drv-app-no')).toBeTruthy();

    component.tourForm.driverId = 1; // avec compte
    fixture.detectChanges();
    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('button.btn-save-send');
    expect(bouton).toBeTruthy();
    expect(fixture.nativeElement.querySelector('.drv-app-yes')).toBeTruthy();

    bouton.click();
    fixture.detectChanges();

    expect(create).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith(9);
    expect(toast.warning).toHaveBeenCalled();
    expect(component.currentView).toBe('list');
    // Chargement initial + après la création + après l'envoi : sans ce dernier, la
    // ligne neuve restait « Non envoyée » (liste rechargée avant la fin de l'envoi).
    expect((api.getTours as jest.Mock).mock.calls.length).toBe(3);
  });

  it('badge « application » dans la liste des chauffeurs du formulaire', async () => {
    await preparer();
    fixture.detectChanges();
    component.openCreate();

    const libelles = component.driverOptions().map(d => component.driverLabel(d));
    expect(libelles).toEqual(['Ali Ben Salah · 📱 application', 'Sami Sans App']);
  });

  it('événement SignalR TourOpened : la tournée ouverte et la ligne de liste se mettent à jour sans rechargement', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({ sentAt: '2026-09-21T09:30:00Z' })) as any);
    fixture.detectChanges();
    component.openDetail({ id: 5 });

    TestBed.inject(SignalRService).tourEvent$.next({ name: 'TourOpened', tourId: 5, payload: { tourId: 5, openedAt: '2026-09-21T09:35:00Z' } });

    expect(component.selectedTour.openedAt).toBe('2026-09-21T09:35:00Z');
    expect(component.tours[0].openedAt).toBe('2026-09-21T09:35:00Z');
    expect(component.sendStatusLabel(component.selectedTour)).toBe('Ouverte');
  });

  it('/tournees/:id ouvre directement le détail', async () => {
    await preparer('5');
    const getTour = jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail()) as any);
    fixture.detectChanges();

    expect(getTour).toHaveBeenCalledWith(5);
    expect(component.currentView).toBe('detail');
    expect(component.selectedTour?.id).toBe(5);
  });

  // Recette 21/09 : le détail de T5 ouvert, le chauffeur touche « Je pars » ; au
  // « Retour », la liste montrait encore T5 « Planifiée / Ouverte ».
  it('TourStatusChanged pendant un détail ouvert : la liste est rechargée, la ligne est à jour au Retour', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({ sentAt: '2026-09-21T07:30:00Z', openedAt: '2026-09-21T07:35:00Z' })) as any);
    fixture.detectChanges();
    component.openDetail({ id: 5 });
    const getTours = api.getTours as jest.Mock;
    const appelsAvant = getTours.mock.calls.length;
    getTours.mockReturnValue(of({
      items: [{ ...tourDetail({ status: 'in_progress', sentAt: '2026-09-21T07:30:00Z', openedAt: '2026-09-21T07:35:00Z', actualStartTime: '2026-09-21T08:02:00Z' }), waypointCount: 2 }],
      totalCount: 1
    }));

    TestBed.inject(SignalRService).tourEvent$.next({ name: 'TourStatusChanged', tourId: 5, payload: { tourId: 5, status: 'in_progress' } });

    expect(getTours.mock.calls.length).toBe(appelsAvant + 1);
    component.closeDetail();
    expect(component.tours[0].status).toBe('in_progress');
    expect(component.sendStatusLabel(component.tours[0])).toBe('Partie');
  });

  it('renvoi d’une tournée déjà ouverte : de nouveau « Envoyée », l’ancienne ouverture n’est plus montrée', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({ sentAt: '2026-09-21T08:00:00Z', openedAt: '2026-09-21T08:05:00Z' })) as any);
    jest.spyOn(api, 'sendTourToDriver').mockReturnValue(of({ sentAt: '2026-09-21T10:45:00Z', push: 'delivered_to_fcm', resent: true }) as any);
    fixture.detectChanges();
    component.openDetail({ id: 5 });
    fixture.detectChanges();
    const libelles = () => Array.from(fixture.nativeElement.querySelectorAll('.info-lbl')).map((e: any) => e.textContent.trim());
    expect(component.sendStatusLabel(component.selectedTour)).toBe('Ouverte');
    expect(libelles()).toContain('Ouverte sur le téléphone');

    component.sendSelectedTour();
    fixture.detectChanges();

    expect(component.sendStatusLabel(component.selectedTour)).toBe('Envoyée');
    expect(component.selectedTour.openedAt).toBeNull();
    expect(libelles()).not.toContain('Ouverte sur le téléphone');
  });

  it('détail rechargé avec une ouverture ANTÉRIEURE au dernier envoi (serveur) : « Envoyée », ouverture masquée', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({ sentAt: '2026-09-21T10:45:00Z', openedAt: '2026-09-21T08:05:00Z' })) as any);
    fixture.detectChanges();
    component.openDetail({ id: 5 });
    fixture.detectChanges();

    expect(component.sendStatusLabel(component.selectedTour)).toBe('Envoyée');
    const libelles = Array.from(fixture.nativeElement.querySelectorAll('.info-lbl')).map((e: any) => e.textContent.trim());
    expect(libelles).not.toContain('Ouverte sur le téléphone');
  });

  it('« Départ signalé par le chauffeur » reste affiché après la confirmation de l’origine par le boîtier', async () => {
    await preparer();
    jest.spyOn(api, 'getTour').mockReturnValue(of(tourDetail({
      status: 'in_progress', sentAt: '2026-09-21T07:30:00Z', actualStartTime: '2026-09-21T08:02:00Z',
      waypoints: [
        { id: 50, sequenceOrder: 0, type: 'origin', name: 'Dépôt', latitude: 36.8, longitude: 10.1, isCompleted: true, arrivalSource: 'device', driverDepartedAt: '2026-09-21T08:02:00Z' },
        { id: 51, sequenceOrder: 1, type: 'destination', name: 'Client', latitude: 34.7, longitude: 10.7, isCompleted: false }
      ]
    })) as any);
    fixture.detectChanges();
    component.openDetail({ id: 5 });
    fixture.detectChanges();

    const libelles = Array.from(fixture.nativeElement.querySelectorAll('.info-lbl')).map((e: any) => e.textContent.trim());
    expect(libelles).toContain('Départ signalé par le chauffeur');
  });

  // Réponse lente de GET /tours/5, clic sur « Surveillance » avant elle : l'adresse
  // de la page suivante était réécrite en /tournees/5 et /tracking sondé à vie.
  it('réponse du détail arrivée après avoir quitté la page : adresse intacte, aucun sondage /tracking', async () => {
    await preparer();
    const reponse = new Subject<any>();
    jest.spyOn(api, 'getTour').mockReturnValue(reponse as any);
    fixture.detectChanges();
    const replaceState = jest.spyOn(TestBed.inject(Location), 'replaceState');

    component.openDetail({ id: 5 });
    fixture.destroy();
    reponse.next(tourDetail({ status: 'in_progress' }));

    try {
      expect(replaceState).not.toHaveBeenCalled();
      expect(api.getTourTracking).not.toHaveBeenCalled();
      expect((component as any).trackingInterval).toBeNull();
    } finally {
      clearInterval((component as any).trackingInterval);
    }
  });

  it('une notification ouvre la 5 pendant que la ligne 9 charge encore : la réponse tardive de la 9 est ignorée', async () => {
    await preparer();
    const reponses: Record<number, Subject<any>> = { 5: new Subject<any>(), 9: new Subject<any>() };
    jest.spyOn(api, 'getTour').mockImplementation((id: any) => reponses[id] as any);
    fixture.detectChanges();

    component.openDetail({ id: 9 });
    component.openDetailById(5);
    reponses[5].next(tourDetail({ id: 5 }));
    reponses[9].next(tourDetail({ id: 9, name: 'Tournée 9' }));

    expect(component.selectedTour.id).toBe(5);
  });

  describe('marqueur du téléphone sur la carte du détail', () => {
    let marker: any;
    let icones: any[];
    let carte: any;
    const phone = (ageSeconds: number) => ({
      latitude: 36.8, longitude: 10.1, speedKph: 72, accuracyM: 8, batteryLevel: 54,
      recordedAt: new Date(Date.now() - ageSeconds * 1000).toISOString()
    });

    beforeEach(async () => {
      await preparer();
      fixture.detectChanges();
      icones = [];
      marker = { setLatLng: jest.fn(), setIcon: jest.fn(), setPopupContent: jest.fn() };
      marker.addTo = jest.fn(() => marker);
      marker.bindPopup = jest.fn(() => marker);
      // Leaflet n'existe pas sous jsdom : L est remplacé le temps du test.
      (globalThis as any).L = { divIcon: jest.fn((o: any) => { icones.push(o); return o; }), marker: jest.fn(() => marker) };
      carte = { removeLayer: jest.fn(), remove: jest.fn() };
      (component as any).detailMap = carte;
    });

    afterEach(() => {
      delete (globalThis as any).L;
    });

    it('boîtier aux commandes, point « eco » d’une minute : marqueur grisé et daté, pas présenté comme du direct', () => {
      component.updatePhoneMarker({ source: 'device', deviceAvailable: true, phoneAvailable: true, phone: phone(60) });

      expect(icones[0].html).toContain('plive-stale');
      const appels = marker.setPopupContent.mock.calls;
      const popup = appels[appels.length - 1][0];
      expect(popup).toContain('Position il y a 1 min');
      expect(popup).toContain('Le boîtier suit la tournée');
    });

    it('le téléphone prend le relais : marqueur plein ; le boîtier revient et le point vieillit : marqueur retiré', () => {
      component.updatePhoneMarker({ source: 'device', phoneAvailable: true, phone: phone(30) });
      component.updatePhoneMarker({ source: 'phone', phoneAvailable: true, phone: phone(5) });
      expect(marker.setIcon).toHaveBeenCalledTimes(1);
      expect(marker.setIcon.mock.calls[0][0].html).not.toContain('plive-stale');

      component.updatePhoneMarker({ source: 'device', phoneAvailable: false, phone: phone(600) });
      expect(carte.removeLayer).toHaveBeenCalledWith(marker);
    });

    it('batterie du bandeau masquée avec le marqueur (point périmé pendant que le boîtier suit)', () => {
      component.trackingData = { source: 'device', phoneAvailable: false, phone: phone(3 * 3600) };
      expect(component.phoneBattery()).toBeNull();
      component.trackingData = { source: 'phone', phoneAvailable: true, phone: phone(10) };
      expect(component.phoneBattery()).toBe('54 %');
    });
  });
});

/**
 * Adresse /tournees/:id et notifications (21/09/2026), avec le VRAI routeur :
 * ouvrir ou fermer un détail change l'adresse par replaceState, à l'insu du
 * routeur ; renaviguer vers /tournees/5 réutilise le composant sans nouvelle
 * émission de paramMap. Un clic sur une notification de la tournée ne faisait
 * alors rien, ou laissait une autre tournée affichée.
 */
describe('ToursComponent — adresse /tournees/:id et clic sur une notification', () => {
  let api: ApiService;
  let location: Location;

  const tour = (id: number) => ({
    id, name: `Tournée ${id}`, status: 'planned', vehicleId: 10, vehicleName: 'Camion 12',
    driverId: 1, driverName: 'Ali Ben Salah', scheduledStartTime: '2026-09-22T07:00:00Z',
    sentAt: null, openedAt: null, actualStartTime: null, estimatedDistanceKm: 120, estimatedDurationMinutes: 95,
    waypoints: [
      { id: id * 10, sequenceOrder: 0, type: 'origin', name: 'Dépôt', latitude: 36.8, longitude: 10.1, isCompleted: false },
      { id: id * 10 + 1, sequenceOrder: 1, type: 'destination', name: 'Client', latitude: 34.7, longitude: 10.7, isCompleted: false }
    ],
    pauses: []
  });

  const demarrer = async (url: string) => {
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'tournees', component: ToursComponent },
          { path: 'tournees/:id', component: ToursComponent }
        ]),
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    });
    api = TestBed.inject(ApiService);
    location = TestBed.inject(Location);
    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getDrivers').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getGeofences').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getTours').mockReturnValue(of({ items: [], totalCount: 0 }) as any);
    jest.spyOn(api, 'getTourStats').mockReturnValue(of({ total: 0, planned: 0, inProgress: 0, completed: 0, cancelled: 0 }) as any);
    jest.spyOn(api, 'getTour').mockImplementation((id: any) => of(tour(Number(id))) as any);
    // Leaflet n'existe pas sous jsdom : les cartes ne sont pas construites.
    jest.spyOn(ToursComponent.prototype, 'initDetailMap').mockImplementation(() => {});
    jest.spyOn(ToursComponent.prototype, 'initTourMap').mockImplementation(() => {});

    const harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, ToursComponent);
    return { harness, component };
  };

  afterEach(() => jest.restoreAllMocks());

  it('« Retour » puis notification de la MÊME tournée : le détail se rouvre', async () => {
    const { harness, component } = await demarrer('/tournees/5');
    expect(component.currentView).toBe('detail');

    component.closeDetail();
    expect(location.path()).toBe('/tournees');
    expect(component.currentView).toBe('list');

    await harness.navigateByUrl('/tournees/5'); // clic sur « Départ : T5 » dans la cloche

    expect(harness.routeDebugElement?.componentInstance).toBe(component); // composant réutilisé
    expect(component.currentView).toBe('detail');
    expect(component.selectedTour.id).toBe(5);
  });

  it('tournée 9 ouverte depuis la liste, notification de la 5 : c’est la 5 qui s’affiche (Annuler agirait sur elle)', async () => {
    const { harness, component } = await demarrer('/tournees/5');
    component.closeDetail();
    component.openDetail({ id: 9 });
    expect(location.path()).toBe('/tournees/9');

    await harness.navigateByUrl('/tournees/5');

    expect(component.selectedTour.id).toBe(5);
    expect(location.path()).toBe('/tournees/5');
  });

  it('« Modifier » puis « Annuler » : l’adresse revient à la liste et la notification rouvre le détail', async () => {
    const { harness, component } = await demarrer('/tournees/5');
    component.editTour();
    component.closeCreate();
    expect(location.path()).toBe('/tournees');

    await harness.navigateByUrl('/tournees/5');

    expect(component.currentView).toBe('detail');
    expect(component.selectedTour.id).toBe(5);
  });

  it('détail ouvert depuis la liste, lien « Tournées » du menu : retour à la liste', async () => {
    const { harness, component } = await demarrer('/tournees');
    component.openDetail({ id: 9 });
    expect(component.currentView).toBe('detail');

    await harness.navigateByUrl('/tournees');

    expect(component.currentView).toBe('list');
    expect(location.path()).toBe('/tournees');
  });

  it('ouverture sur /tournees/5 : un seul chargement du détail', async () => {
    await demarrer('/tournees/5');
    expect((api.getTour as jest.Mock).mock.calls.filter(c => Number(c[0]) === 5).length).toBe(1);
  });
});
