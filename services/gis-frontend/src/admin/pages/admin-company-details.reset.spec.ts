import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { AdminCompanyDetailsComponent } from './admin-company-details.component';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService, CompanyResetResult } from '../services/admin.service';

/** La vraie mise en page admin relance une horloge chaque seconde : inutile ici. */
@Component({ selector: 'admin-layout', standalone: true, template: '<ng-content></ng-content>' })
class MiseEnPageFactice {}

/**
 * Fiche société admin — remise à zéro (relecture du 21/09/2026, R7c). L'écran promettait
 * « Conservé : … ses utilisateurs » alors que les comptes chauffeurs perdent leur fiche
 * et sont désactivés. Tenu ici : l'aperçu et le résultat disent combien de comptes
 * chauffeurs sont désactivés (driverAccountsClosed), et la phrase « Conservé » suit.
 */
describe('AdminCompanyDetailsComponent — remise à zéro et comptes chauffeurs', () => {
  let component: AdminCompanyDetailsComponent;
  let fixture: any;
  let admin: any;

  const resultat = (patch: Partial<CompanyResetResult> = {}): CompanyResetResult => ({
    companyId: 7, companyName: 'TransportTest',
    deleted: [{ table: 'drivers', rows: 4 }, { table: 'vehicles', rows: 116 }],
    totalRows: 120, filesDeleted: 0, kept: [], durationMs: 12, dryRun: true,
    driverAccountsClosed: 3,
    ...patch
  });

  const preparer = async (apercu: CompanyResetResult, execution?: CompanyResetResult) => {
    // Le service d'export PDF éventuel précharge un logo par fetch() — absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));
    admin = {
      getClient: jest.fn(() => of({ id: 7, name: 'TransportTest', email: 'contact@transporttest.tn', status: 'active' })),
      getVehicles: jest.fn(() => of([])),
      getCompanyRoles: jest.fn(() => of([])),
      getCompanyUsers: jest.fn(() => of([])),
      getSociete: jest.fn(() => of({})),
      getAdminUser: jest.fn(() => null),
      getBillingOverview: jest.fn(() => of({ count: 0, items: [] })),
      resetCompanyData: jest.fn((_id: number, _name: string, dryRun: boolean) =>
        of(dryRun ? apercu : (execution ?? resultat({ dryRun: false }))))
    };

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, AdminCompanyDetailsComponent],
      providers: [
        { provide: AdminService, useValue: admin },
        { provide: ActivatedRoute, useValue: { params: of({ id: '7' }), snapshot: { paramMap: new Map(), queryParamMap: new Map() } } }
      ]
    })
      .overrideComponent(AdminCompanyDetailsComponent, {
        remove: { imports: [AdminLayoutComponent] },
        add: { imports: [MiseEnPageFactice] }
      })
      .compileComponents();

    fixture = TestBed.createComponent(AdminCompanyDetailsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  afterEach(() => fixture?.destroy());

  const texte = (selecteur: string): string =>
    (fixture.nativeElement.querySelector(selecteur)?.textContent ?? '').replace(/\s+/g, ' ').trim();

  it('aperçu : annonce les comptes chauffeurs désactivés et ne promet plus de garder « ses utilisateurs »', async () => {
    await preparer(resultat());

    component.openResetModal();
    fixture.detectChanges();

    expect(admin.resetCompanyData).toHaveBeenCalledWith(7, '', true);
    expect(texte('.reset-driver-accounts'))
      .toContain('3 comptes chauffeurs seront désactivés : leurs fiches partent avec les données');
    expect(texte('.reset-kept-users'))
      .toBe('ses utilisateurs et leurs rôles, sauf 3 comptes chauffeurs qui seront désactivés');
  });

  it('résultat : dit combien de comptes chauffeurs ont été désactivés', async () => {
    await preparer(resultat(), resultat({ dryRun: false, totalRows: 118, driverAccountsClosed: 3 }));

    component.openResetModal();
    fixture.detectChanges();
    component.resetConfirmName = 'TransportTest';
    component.confirmReset();
    fixture.detectChanges();

    expect(admin.resetCompanyData).toHaveBeenLastCalledWith(7, 'TransportTest', false);
    expect(component.resetDone).toBe(true);
    expect(texte('.reset-success')).toContain('118 lignes supprimées');
    expect(texte('.reset-driver-accounts'))
      .toBe('3 comptes chauffeurs désactivés : leurs fiches sont parties avec les données.');
  });

  it('aucun compte chauffeur : pas d’avertissement, « ses utilisateurs et leurs rôles » reste vrai', async () => {
    await preparer(resultat({ driverAccountsClosed: 0 }));

    component.openResetModal();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.reset-driver-accounts')).toBeNull();
    expect(texte('.reset-kept-users')).toBe('ses utilisateurs et leurs rôles');
  });
});
