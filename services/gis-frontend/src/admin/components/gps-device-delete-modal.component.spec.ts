import { SimpleChange } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { Subject, of, throwError } from 'rxjs';
import { GpsDeviceDeleteModalComponent } from './gps-device-delete-modal.component';
import { AdminService, GpsDeviceDeletionImpact, GpsDeviceDeletionResult } from '../services/admin.service';

/**
 * Fenêtre « Supprimer le boîtier » (09/10/2026). Ce qui est verrouillé : rien ne se
 * supprime sans inventaire ni sans l'IMEI retapé ; un refus du serveur désactive le
 * bouton ; une réponse arrivée pour une autre fiche ne s'affiche jamais ; le résultat
 * remplace le formulaire et la fermeture dit si quelque chose a été supprimé.
 */
describe('GpsDeviceDeleteModalComponent', () => {
  let component: GpsDeviceDeleteModalComponent;
  let admin: AdminService;

  const inventaire = (deviceId: number, extra: Partial<GpsDeviceDeletionImpact> = {}): GpsDeviceDeletionImpact => ({
    deviceId, imei: '860141076687632', mat: 'NR08G1116', companyId: 4, companyName: 'HERTZ',
    lastCommunication: '2026-10-08T10:26:17Z', activeRecently: false, vehicle: null,
    rows: [
      { table: 'gps_positions', label: 'position(s) GPS', rows: 7, capped: false },
      { table: 'gps_alerts', label: 'alerte(s) GPS', rows: 0, capped: false },
    ],
    totalRows: 7, capped: false, accidentFilesKept: 0, warnings: [], refusal: null,
    confirmationToken: '860141076687632', ...extra,
  });

  const ouvrir = (id: number) => {
    component.deviceId = id;
    component.ngOnChanges({ deviceId: new SimpleChange(null, id, true) });
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, GpsDeviceDeleteModalComponent],
      providers: [AdminService],
    }).compileComponents();
    admin = TestBed.inject(AdminService);
    component = TestBed.createComponent(GpsDeviceDeleteModalComponent).componentInstance;
  });

  afterEach(() => jest.restoreAllMocks());

  it('charge l’inventaire à l’ouverture et ne garde que les lignes non nulles', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockReturnValue(of(inventaire(5)));

    ouvrir(5);

    expect(component.impact?.deviceId).toBe(5);
    expect(component.rows.map(r => r.table)).toEqual(['gps_positions']);
    expect(component.canDelete).toBe(false);
  });

  it('n’active la suppression qu’avec l’IMEI retapé, espaces et casse ignorés', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockReturnValue(of(inventaire(5)));
    ouvrir(5);

    component.confirmation = '860141076687633';
    expect(component.canDelete).toBe(false);
    component.confirmation = ' 8601 4107 6687 632 ';
    expect(component.canDelete).toBe(true);
  });

  it('désactive la suppression quand le serveur refuse (historique d’un autre véhicule)', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact')
      .mockReturnValue(of(inventaire(5, { refusal: 'Ce boîtier porte l’historique d’un autre véhicule encore présent : 242 TU 7511.' })));
    const supprimer = jest.spyOn(admin, 'deleteGpsDeviceWithData');
    ouvrir(5);

    component.confirmation = '860141076687632';
    expect(component.canDelete).toBe(false);
    component.confirmer();
    expect(supprimer).not.toHaveBeenCalled();
  });

  it('ignore une réponse d’inventaire arrivée pour une autre fiche', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockReturnValue(of(inventaire(99)));

    ouvrir(5);

    expect(component.impact).toBeNull();
    expect(component.canDelete).toBe(false);
  });

  it('désactive la suppression si l’inventaire échoue', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockReturnValue(throwError(() => new Error('500')));

    ouvrir(5);

    expect(component.impactFailed).toBe(true);
    expect(component.canDelete).toBe(false);
  });

  it('supprime une seule fois, affiche le résultat et signale la suppression à la fermeture', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockReturnValue(of(inventaire(5)));
    const resultat: GpsDeviceDeletionResult = {
      success: true, message: 'Boîtier IMEI 860141076687632 supprimé : 7 lignes dans 1 tables.',
      deleted: [{ table: 'gps_positions', label: 'position(s) GPS', rows: 7, capped: false }],
      totalRows: 7, vehicleDeleted: false, durationMs: 42,
    };
    const supprimer = jest.spyOn(admin, 'deleteGpsDeviceWithData').mockReturnValue(of(resultat));
    const ferme = jest.fn();
    component.closed.subscribe(ferme);
    ouvrir(5);
    component.confirmation = '860141076687632';

    component.confirmer();
    component.confirmer();

    expect(supprimer).toHaveBeenCalledTimes(1);
    expect(supprimer).toHaveBeenCalledWith(5, '860141076687632');
    expect(component.result?.totalRows).toBe(7);
    expect(component.canDelete).toBe(false);

    component.fermer();
    expect(ferme).toHaveBeenCalledWith({ deleted: true });
  });

  it('ne se ferme pas tant que la suppression est en cours, et prévient avant de quitter la page', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockReturnValue(of(inventaire(5)));
    const enCours = new Subject<GpsDeviceDeletionResult>();
    jest.spyOn(admin, 'deleteGpsDeviceWithData').mockReturnValue(enCours.asObservable());
    const ferme = jest.fn();
    component.closed.subscribe(ferme);
    ouvrir(5);
    component.confirmation = '860141076687632';

    component.confirmer();
    expect(component.deleting).toBe(true);

    component.fermer();
    expect(ferme).not.toHaveBeenCalled();

    const evenement = { preventDefault: jest.fn(), returnValue: undefined as unknown } as unknown as BeforeUnloadEvent;
    component.avantDechargement(evenement);
    expect(evenement.preventDefault).toHaveBeenCalled();

    enCours.next({ success: true, message: 'ok', deleted: [], totalRows: 0, vehicleDeleted: false, durationMs: 1 });
    enCours.complete();
    expect(component.deleting).toBe(false);
    component.fermer();
    expect(ferme).toHaveBeenCalledWith({ deleted: true });
  });

  it('repart de zéro à la réouverture sur une autre fiche', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockImplementation((id: number) => of(inventaire(id)));
    ouvrir(5);
    component.confirmation = '860141076687632';
    component.error = 'ancien refus';

    component.deviceId = null;
    component.ngOnChanges({ deviceId: new SimpleChange(5, null, false) });
    expect(component.impact).toBeNull();

    ouvrir(6);
    expect(component.impact?.deviceId).toBe(6);
    expect(component.confirmation).toBe('');
    expect(component.error).toBe('');
    expect(component.canDelete).toBe(false);
  });

  it('montre le refus du serveur et laisse la fenêtre ouverte', () => {
    jest.spyOn(admin, 'getGpsDeviceDeletionImpact').mockReturnValue(of(inventaire(5)));
    jest.spyOn(admin, 'deleteGpsDeviceWithData')
      .mockReturnValue(throwError(() => ({ error: { message: 'Confirmation incorrecte : tapez « 860141076687632 ».' } })));
    const ferme = jest.fn();
    component.closed.subscribe(ferme);
    ouvrir(5);
    component.confirmation = '860141076687632';

    component.confirmer();

    expect(component.error).toContain('Confirmation incorrecte');
    expect(component.result).toBeNull();
    expect(ferme).not.toHaveBeenCalled();

    component.fermer();
    expect(ferme).toHaveBeenCalledWith({ deleted: false });
  });
});
