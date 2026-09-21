import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
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
});
