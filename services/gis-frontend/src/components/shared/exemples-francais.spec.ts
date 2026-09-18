import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { FormsModule } from '@angular/forms';
import { EmployeePopupComponent } from './employee-popup.component';
import { GaragePopupComponent } from './garage-popup.component';
import { VehiclePopupComponent } from './vehicle-popup.component';
import { DocumentRenewalPopupComponent } from './document-renewal-popup.component';

/**
 * EXPORT-EXEMPLES-3 : Belive GPA s'adresse à l'export, les exemples proposés dans les champs
 * des fenêtres partagées ne doivent plus être tunisiens (indicatif +216, villes, patronymes,
 * assureurs, plaque locale). Les exemples sont en dur dans les gabarits : on lit donc le rendu.
 */
describe('fenêtres partagées : exemples destinés au marché français', () => {
  /** Motifs relevés en recette dans les attributs placeholder. */
  const marqueursTunisiens = /\+216|Tunis|Bourguiba|Ben Ali|Ben Salah|Mohamed|Sonia|@[\w.-]+\.tn\b/;

  const exemples = (hote: HTMLElement): string[] =>
    Array.from(hote.querySelectorAll('input, textarea'))
      .map(champ => champ.getAttribute('placeholder') ?? '')
      .filter(valeur => valeur.length > 0);

  describe('EmployeePopupComponent (chauffeur)', () => {
    let fixture: ComponentFixture<EmployeePopupComponent>;

    beforeEach(async () => {
      await TestBed.configureTestingModule({
        imports: [EmployeePopupComponent, FormsModule, NoopAnimationsModule]
      }).compileComponents();
      fixture = TestBed.createComponent(EmployeePopupComponent);
      fixture.componentInstance.isOpen = true;
      fixture.detectChanges();
    });

    it('aucun exemple tunisien dans les champs', () => {
      expect(exemples(fixture.nativeElement).filter(v => marqueursTunisiens.test(v))).toEqual([]);
    });

    it('identité et téléphone proposent des exemples français', () => {
      const proposes = exemples(fixture.nativeElement);
      expect(proposes).toContain('Ex: Jean');
      expect(proposes).toContain('Ex: Dupont');
      expect(proposes).toContain('+33 6 12 34 56 78');
    });
  });

  describe('GaragePopupComponent (fournisseur)', () => {
    let fixture: ComponentFixture<GaragePopupComponent>;

    beforeEach(async () => {
      await TestBed.configureTestingModule({
        imports: [GaragePopupComponent, FormsModule, NoopAnimationsModule]
      }).compileComponents();
      fixture = TestBed.createComponent(GaragePopupComponent);
      fixture.componentInstance.isOpen = true;
      fixture.detectChanges();
    });

    it('aucun exemple tunisien dans les champs', () => {
      expect(exemples(fixture.nativeElement).filter(v => marqueursTunisiens.test(v))).toEqual([]);
    });

    it('adresse, ville, code postal, téléphone et courriel sont français', () => {
      const proposes = exemples(fixture.nativeElement);
      expect(proposes).toContain('Ex: Garage Central Lyon');
      expect(proposes).toContain('Ex: 45 avenue de la République');
      expect(proposes).toContain('Ex: Lyon');
      expect(proposes).toContain('Ex: 69003');
      expect(proposes).toContain('+33 X XX XX XX XX');
      expect(proposes).toContain('contact@garage.fr');
      expect(proposes).toContain('Ex: Jean Dupont');
    });
  });

  describe('VehiclePopupComponent (plaque et numéro SIM du boîtier)', () => {
    /** La section GPS n'apparaît qu'à l'administrateur système, boîtier coché. */
    const rendre = async (gpsMode: 'existing' | 'new'): Promise<ComponentFixture<VehiclePopupComponent>> => {
      await TestBed.configureTestingModule({
        imports: [VehiclePopupComponent, FormsModule, HttpClientTestingModule, NoopAnimationsModule]
      }).compileComponents();
      const fixture = TestBed.createComponent(VehiclePopupComponent);
      fixture.componentInstance.isOpen = true;
      fixture.componentInstance.isSystemAdminContext = true;
      fixture.componentInstance.formData.hasGPS = true;
      fixture.componentInstance.gpsMode = gpsMode;
      // Le champ SIM du boîtier déjà déclaré n'est rendu qu'une fois l'appareil choisi.
      if (gpsMode === 'existing') fixture.componentInstance.formData.gpsDeviceId = 1;
      fixture.detectChanges();
      return fixture;
    };

    it('aucun exemple tunisien, boîtier nouveau ou déjà déclaré', async () => {
      for (const mode of ['new', 'existing'] as const) {
        const fixture = await rendre(mode);
        expect(exemples(fixture.nativeElement).filter(v => marqueursTunisiens.test(v))).toEqual([]);
        TestBed.resetTestingModule();
      }
    });

    it('la plaque propose le format français', async () => {
      const fixture = await rendre('new');
      expect(exemples(fixture.nativeElement)).toContain('Ex: AB-123-CD');
    });

    it('le numéro SIM propose un exemple français dans les deux modes', async () => {
      for (const mode of ['new', 'existing'] as const) {
        const fixture = await rendre(mode);
        expect(exemples(fixture.nativeElement)).toContain('Ex: +33 6 12 34 56 78');
        TestBed.resetTestingModule();
      }
    });
  });

  describe('DocumentRenewalPopupComponent (fournisseur du document)', () => {
    let fixture: ComponentFixture<DocumentRenewalPopupComponent>;

    beforeEach(async () => {
      await TestBed.configureTestingModule({
        imports: [DocumentRenewalPopupComponent, FormsModule, HttpClientTestingModule, NoopAnimationsModule]
      }).compileComponents();
      fixture = TestBed.createComponent(DocumentRenewalPopupComponent);
      fixture.componentInstance.isOpen = true;
      fixture.componentInstance.document = { type: 'insurance' } as any;
      // Les exemples d'adresse visés sont ceux du formulaire « nouveau fournisseur ».
      fixture.componentInstance.showNewSupplierForm = true;
      fixture.detectChanges();
    });

    it('aucun exemple tunisien dans le formulaire « nouveau fournisseur »', () => {
      expect(exemples(fixture.nativeElement).filter(v => marqueursTunisiens.test(v))).toEqual([]);
    });

    it('adresse, ville, code postal, téléphone et courriel du fournisseur sont français', () => {
      const proposes = exemples(fixture.nativeElement);
      expect(proposes).toContain('Ex: 45 avenue de la République');
      expect(proposes).toContain('Ex: Lyon');
      expect(proposes).toContain('Ex: 69003');
      expect(proposes).toContain('+33 X XX XX XX XX');
      expect(proposes).toContain('contact@exemple.fr');
    });

    it('assurance : assureurs français, plus CNIA Saada ni Wafa Assurance', () => {
      expect(fixture.componentInstance.getProviderPlaceholder()).toBe('Ex: AXA, Macif, Groupama...');
    });

    it('vignette : administration française, plus la Recette des finances', () => {
      fixture.componentInstance.document = { type: 'tax' } as any;
      expect(fixture.componentInstance.getProviderPlaceholder()).toBe('Ex: Trésor public');
    });
  });
});
