import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { FormsModule } from '@angular/forms';
import { VehiclePopupComponent } from './vehicle-popup.component';

/**
 * Point 4 des corrections restantes (06/11/2026) : dans la console admin, la fiche véhicule ne
 * proposait aucun boîtier disponible — elle appelait la route de l'espace client, refusée faute
 * de clé client. Elle appelle désormais la route admin, pour la société DU VÉHICULE.
 */
describe('Fiche véhicule (console admin) — boîtiers disponibles', () => {
  let fixture: any;
  let fiche: VehiclePopupComponent;
  let http: HttpTestingController;

  const routeAdmin = (r: any) => r.url.endsWith('/admin/gps-devices/available');
  const routeClient = (r: any) => r.url.endsWith('/gps/devices/available') && !r.url.includes('/admin/');

  const ouvrir = async (vehicule: any) => {
    await TestBed.configureTestingModule({
      imports: [VehiclePopupComponent, FormsModule, HttpClientTestingModule, NoopAnimationsModule]
    }).compileComponents();
    fixture = TestBed.createComponent(VehiclePopupComponent);
    fiche = fixture.componentInstance;
    fixture.componentRef.setInput('isSystemAdminContext', true);
    fixture.componentRef.setInput('companies', [{ id: 4, name: 'HERTZ' }, { id: 7, name: 'Belive GPA' }]);
    fixture.componentRef.setInput('vehicle', vehicule);
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    http = TestBed.inject(HttpTestingController);
    http.match(r => r.url.includes('/brands')).forEach(r => r.flush([]));
    http.match(r => r.url.includes('/fuelprices/types')).forEach(r => r.flush([]));
  };

  it('demande les boîtiers libres de la société DU VÉHICULE par la route admin, jamais par la route client', async () => {
    await ouvrir({ id: 20, name: 'HTZ 1', plate: 'HTZ 1', companyId: 4, type: 'citadine' });
    expect(http.match(routeClient).length).toBe(0);
    const req = http.expectOne(routeAdmin);
    expect(req.request.params.get('companyId')).toBe('4');
    req.flush([{ id: 10, deviceUid: '860000000000101', mat: 'NR1', lastCommunication: null }]);
    expect(fiche.availableGpsDevices.map((d: any) => d.deviceUid)).toEqual(['860000000000101']);
  });

  it('la société change : la liste suit, et l\'appareil choisi dans l\'ancienne liste est retiré', async () => {
    await ouvrir({ id: 20, name: 'HTZ 1', plate: 'HTZ 1', companyId: 4, type: 'citadine' });
    http.expectOne(routeAdmin).flush([{ id: 10, deviceUid: '860000000000101' }]);
    fiche.formData.gpsDeviceId = 10 as any;
    fiche.onExistingDeviceSelected();
    expect(fiche.formData.gpsImei).toBe('860000000000101');

    fiche.formData.companyId = 7 as any;
    fiche.onCompanyChange();
    const req = http.expectOne(routeAdmin);
    expect(req.request.params.get('companyId')).toBe('7');
    req.flush([]);
    expect(fiche.formData.gpsDeviceId).toBeUndefined();
    expect(fiche.formData.gpsImei).toBe('');
    expect(fiche.availableGpsDevices).toEqual([]);
  });

  it('un véhicule déjà équipé garde son appareil affiché, même sans boîtier libre dans sa société', async () => {
    await ouvrir({ id: 21, name: 'HTZ 2', plate: 'HTZ 2', companyId: 4, type: 'citadine', hasGPS: true, gpsDeviceId: 11, gpsImei: '860000000000102' });
    http.expectOne(routeAdmin).flush([]);
    expect(fiche.gpsMode).toBe('existing');
    expect(fiche.formData.gpsDeviceId).toBe(11);
    expect(fiche.availableGpsDevices.map((d: any) => d.id)).toEqual([11]);
  });

  it('l\'appareil posé sur le véhicule est en tête de liste, déjà choisi et marqué « posé sur ce véhicule »', async () => {
    await ouvrir({ id: 21, name: 'HTZ 2', plate: 'HTZ 2', companyId: 4, type: 'citadine', hasGPS: true,
                   gpsDeviceId: 11, gpsImei: '860000000000102', gpsMat: 'NR2' });
    http.expectOne(routeAdmin).flush([{ id: 10, deviceUid: '860000000000101', lastCommunication: null }]);
    expect(fiche.availableGpsDevices.map((d: any) => d.id)).toEqual([11, 10]);

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const choix = fixture.nativeElement.querySelector('#gpsDeviceId') as HTMLSelectElement;
    expect(choix.value).toBe('11');
    const affiche = choix.options[choix.selectedIndex].textContent || '';
    expect(affiche).toContain('860000000000102');
    expect(affiche).toContain('posé sur ce véhicule');
  });
});
