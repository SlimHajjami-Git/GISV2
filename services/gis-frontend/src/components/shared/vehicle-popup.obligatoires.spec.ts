import { TestBed, ComponentFixture } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { FormsModule } from '@angular/forms';
import { VehiclePopupComponent } from './vehicle-popup.component';

/**
 * Fiche véhicule, champs obligatoires (Karim, 25/09/2026) : « "type de carburant" doit
 * être un champ obligatoire », « Sélectionner » en première ligne comme « Marque », et
 * l'étoile sur les champs obligatoires seulement. La vraie fiche, pas une copie : le
 * tutoriel a ses propres tests sur une fiche de test.
 */
describe('Fiche véhicule — champs obligatoires', () => {
  let fixture: ComponentFixture<VehiclePopupComponent>;
  let alerte: jest.SpyInstance;

  const ouvrir = async () => {
    await TestBed.configureTestingModule({
      imports: [VehiclePopupComponent, FormsModule, HttpClientTestingModule, NoopAnimationsModule]
    }).compileComponents();
    fixture = TestBed.createComponent(VehiclePopupComponent);
    fixture.componentInstance.isOpen = true;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  };
  const libelles = () => Array.from(fixture.nativeElement.querySelectorAll('label[for]') as NodeListOf<HTMLLabelElement>)
    .map(l => l.textContent!.replace(/\s+/g, ' ').trim());
  /** Fiche remplie de tout ce qui est obligatoire en « Achat ». */
  const remplirObligatoires = () => Object.assign(fixture.componentInstance.formData, {
    name: 'Camion principal', plate: 'AB-123-CD', year: 2024, type: 'camion',
    status: 'available', mileage: 85000, fuelType: 'diesel'
  });

  beforeEach(() => { alerte = jest.spyOn(window, 'alert').mockImplementation(() => {}); });
  afterEach(() => alerte.mockRestore());

  it('nouveau véhicule : aucun carburant pré-choisi, « -- Sélectionner -- » en première ligne', async () => {
    await ouvrir();
    expect(fixture.componentInstance.formData.fuelType).toBe('');
    const liste = fixture.nativeElement.querySelector('[data-guide="vehicule-carburant"]') as HTMLSelectElement;
    expect(liste.options[0].textContent!.trim()).toBe('-- Sélectionner --');
    expect(liste.selectedOptions[0]?.textContent?.trim()).toBe('-- Sélectionner --');
  });

  it('l\'étoile après le libellé, sur les champs obligatoires seulement : ni Marque ni Modèle', async () => {
    await ouvrir();
    const l = libelles();
    for (const oblig of ['Nom du véhicule *', 'Plaque *', 'Année *', 'Type *', 'Statut *', 'Compteur *', 'Type de carburant *']) {
      expect(l).toContain(oblig);
    }
    for (const facultatif of ['Marque', 'Modèle', 'Couleur', 'Capacité réservoir (L)', 'Date de mise en circulation', "Type d'acquisition", "Date d'achat", "Prix d'achat"]) {
      expect(l).toContain(facultatif);
    }
  });

  it('« Ajouter » sans carburant : alerte qui le nomme, rien n\'est enregistré', async () => {
    await ouvrir();
    remplirObligatoires();
    fixture.componentInstance.formData.fuelType = '';
    const emis = jest.fn();
    fixture.componentInstance.saved.subscribe(emis);
    fixture.componentInstance.onSubmit();
    expect(alerte).toHaveBeenCalledWith('Renseignez les champs obligatoires (*) : Type de carburant.');
    expect(emis).not.toHaveBeenCalled();
  });

  it('« Crédit » : traite, durée, date de début et jour de paiement exigés, avec leur étoile', async () => {
    await ouvrir();
    remplirObligatoires();
    fixture.componentInstance.formData.acquisitionType = 'leasing';
    fixture.detectChanges();
    const l = libelles();
    for (const oblig of ['Traite mensuelle *', 'Durée du crédit *', 'Date de début du crédit *', 'Jour de paiement *', 'Apport']) {
      expect(l).toContain(oblig);
    }
    const emis = jest.fn();
    fixture.componentInstance.saved.subscribe(emis);
    fixture.componentInstance.onSubmit();
    expect(alerte).toHaveBeenCalledWith('Renseignez les champs obligatoires (*) : Traite mensuelle, Durée du crédit, Date de début du crédit, Jour de paiement.');
    expect(emis).not.toHaveBeenCalled();

    Object.assign(fixture.componentInstance.formData, {
      leasingMonthlyPayment: 1250, leasingDurationMonths: 36, leasingStartDate: '2026-10-01', leasingPaymentDay: 5
    });
    fixture.componentInstance.onSubmit();
    expect(emis).toHaveBeenCalledTimes(1);
  });

  it('tout ce qui est obligatoire est rempli : la fiche est enregistrée, sans alerte', async () => {
    await ouvrir();
    remplirObligatoires();
    const emis = jest.fn();
    fixture.componentInstance.saved.subscribe(emis);
    fixture.componentInstance.onSubmit();
    expect(alerte).not.toHaveBeenCalled();
    expect(emis).toHaveBeenCalledTimes(1);
  });
});
