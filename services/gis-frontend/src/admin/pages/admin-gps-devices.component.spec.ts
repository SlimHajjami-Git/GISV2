import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { AdminGpsDevicesComponent } from './admin-gps-devices.component';
import { AdminGpsDevice, AdminService, GpsDeviceBlocklistEntry } from '../services/admin.service';
import { ApiService } from '../../services/api.service';

/**
 * Écran Admin › Boîtiers GPS (09/10/2026) : la liste et la liste de blocage se chargent à
 * l'ouverture, les filtres sont transmis au serveur, une suppression confirmée recharge les
 * deux listes, et « Débloquer » passe par une confirmation puis recharge la liste de blocage.
 */
describe('AdminGpsDevicesComponent', () => {
  let component: AdminGpsDevicesComponent;
  let admin: AdminService;

  const fiche = (deviceId: number, vehicleId: number | null): AdminGpsDevice => ({
    deviceId, imei: `86014107668${deviceId}`, mat: 'NR08G1116', companyId: 4, companyName: 'HERTZ',
    vehicleId, plate: vehicleId ? 'HTZ 345' : null, vehicleName: vehicleId ? 'HTZ 345' : null,
    status: vehicleId ? 'assigned' : 'unassigned', lastCommunication: null, createdAt: '2026-10-01T14:31:41Z',
  });
  const blocage: GpsDeviceBlocklistEntry = { deviceUid: '86014076687624', blockedAt: '2026-10-09T08:06:05Z', deviceIdBefore: 567111, mat: 'NR08G1115' };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, AdminGpsDevicesComponent],
      providers: [AdminService, ApiService],
    }).compileComponents();
    admin = TestBed.inject(AdminService);
    jest.spyOn(admin, 'isAuthenticated').mockReturnValue(true);
    jest.spyOn(admin, 'getClients').mockReturnValue(of([]));
    component = TestBed.createComponent(AdminGpsDevicesComponent).componentInstance;
  });

  afterEach(() => jest.restoreAllMocks());

  it('charge les boîtiers et la liste de blocage à l’ouverture, sans filtre', () => {
    const lister = jest.spyOn(admin, 'getAdminGpsDevices').mockReturnValue(of([fiche(1, 10), fiche(2, null)]));
    const bloques = jest.spyOn(admin, 'getGpsDeviceBlocklist').mockReturnValue(of([blocage]));

    component.ngOnInit();

    expect(lister).toHaveBeenCalledWith({ companyId: null, unassignedOnly: false, search: '' });
    expect(component.devices).toHaveLength(2);
    expect(bloques).toHaveBeenCalled();
    expect(component.blocklist).toEqual([blocage]);
  });

  it('transmet les filtres au serveur', () => {
    const lister = jest.spyOn(admin, 'getAdminGpsDevices').mockReturnValue(of([]));
    jest.spyOn(admin, 'getGpsDeviceBlocklist').mockReturnValue(of([]));

    component.selectedCompanyId = 4;
    component.unassignedOnly = true;
    component.search = 'NR08';
    component.load();

    expect(lister).toHaveBeenLastCalledWith({ companyId: 4, unassignedOnly: true, search: 'NR08' });
  });

  it('affiche l’erreur de chargement au lieu d’une liste vide muette', () => {
    jest.spyOn(admin, 'getAdminGpsDevices').mockReturnValue(throwError(() => ({ error: { message: 'Accès réservé aux administrateurs système' } })));
    jest.spyOn(admin, 'getGpsDeviceBlocklist').mockReturnValue(of([]));

    component.load();

    expect(component.loadError).toContain('administrateurs système');
    expect(component.loading).toBe(false);
  });

  it('ouvre la fenêtre sur la fiche choisie et recharge les deux listes après une suppression', () => {
    const lister = jest.spyOn(admin, 'getAdminGpsDevices').mockReturnValue(of([]));
    const bloques = jest.spyOn(admin, 'getGpsDeviceBlocklist').mockReturnValue(of([]));

    component.ouvrirSuppression(fiche(7, null));
    expect(component.deviceToDeleteId).toBe(7);

    component.fermerSuppression({ deleted: false });
    expect(component.deviceToDeleteId).toBeNull();
    expect(lister).not.toHaveBeenCalled();

    component.ouvrirSuppression(fiche(7, null));
    component.fermerSuppression({ deleted: true });
    expect(lister).toHaveBeenCalledTimes(1);
    expect(bloques).toHaveBeenCalledTimes(1);
  });

  it('ne débloque qu’après confirmation, puis recharge la liste de blocage', () => {
    const debloquer = jest.spyOn(admin, 'unblockGpsDevice').mockReturnValue(of({ message: 'débloqué' }));
    const bloques = jest.spyOn(admin, 'getGpsDeviceBlocklist').mockReturnValue(of([]));
    jest.spyOn(window, 'confirm').mockReturnValue(false);

    component.debloquer(blocage);
    expect(debloquer).not.toHaveBeenCalled();

    jest.spyOn(window, 'confirm').mockReturnValue(true);
    component.debloquer(blocage);
    expect(debloquer).toHaveBeenCalledWith('86014076687624');
    expect(bloques).toHaveBeenCalledTimes(1);
  });
});
