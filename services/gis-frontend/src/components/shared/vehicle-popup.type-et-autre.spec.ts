import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { FormsModule } from '@angular/forms';
import { VehiclePopupComponent } from './vehicle-popup.component';
import { typeDeLaListe } from '../../services/types-vehicule';

/**
 * Fiche véhicule, corrections GPA restantes du 28/10/2026 :
 *  1. le type d'un modèle du catalogue (van, hatchback…) est ramené à la liste et n'écrase
 *     plus un Type choisi ;
 *  2. une marque ou un modèle absent du catalogue se saisit avec « Autre ».
 */
describe('Fiche véhicule — type du modèle et marque « Autre »', () => {
  const marques = [{ id: 1, name: 'RENAULT', modelCount: 2 }, { id: 2, name: 'TOYOTA', modelCount: 1 }];
  const modelesRenault = [{ id: 10, name: 'Kangoo', vehicleType: 'van' }, { id: 11, name: 'Clio', vehicleType: 'hatchback' }];

  let fixture: any;
  let fiche: VehiclePopupComponent;
  let http: HttpTestingController;

  const ouvrir = async (vehicule?: any) => {
    await TestBed.configureTestingModule({
      imports: [VehiclePopupComponent, FormsModule, HttpClientTestingModule, NoopAnimationsModule]
    }).compileComponents();
    fixture = TestBed.createComponent(VehiclePopupComponent);
    fiche = fixture.componentInstance;
    if (vehicule) fixture.componentRef.setInput('vehicle', vehicule);
    fixture.componentRef.setInput('isOpen', true);
    fixture.detectChanges();
    http = TestBed.inject(HttpTestingController);
    http.match('/api/brands').forEach(r => r.flush(marques));
    http.match(r => r.url.includes('/fuelprices/types')).forEach(r => r.flush([]));
    fixture.detectChanges();
  };
  const choisirRenault = () => {
    fiche.formData.brandId = '1';
    fiche.onBrandChange('1');
    http.expectOne('/api/brands/1/models').flush(modelesRenault);
  };

  it('les types du catalogue sont ramenés à la liste de la fiche', () => {
    expect(typeDeLaListe('van')).toBe('utilitaire');
    expect(typeDeLaListe('Pick-up')).toBe('utilitaire');
    expect(typeDeLaListe('truck')).toBe('camion');
    expect(typeDeLaListe('hatchback')).toBe('citadine');
    expect(typeDeLaListe('Berline')).toBe('citadine');
    expect(typeDeLaListe('SUV')).toBe('suv');
    expect(typeDeLaListe('quad')).toBe('other');
    expect(typeDeLaListe('')).toBe('');
  });

  it('nouveau véhicule : le modèle préremplit le Type, ramené à la liste', async () => {
    await ouvrir();
    choisirRenault();
    fiche.onModelChange('10');
    expect(fiche.formData.model).toBe('Kangoo');
    expect(fiche.formData.type).toBe('utilitaire');
  });

  it('un Type choisi par l\'utilisateur n\'est plus écrasé par le modèle', async () => {
    await ouvrir();
    fiche.formData.type = 'suv';
    fiche.typeChoisi = true;   // (change) du champ Type
    choisirRenault();
    fiche.onModelChange('11');
    expect(fiche.formData.type).toBe('suv');
  });

  it('un véhicule enregistré « van » s\'ouvre en « Utilitaire », et son Type n\'est plus écrasé', async () => {
    await ouvrir({ id: 5, name: 'Livraison', plate: '123 TU 4567', brand: 'RENAULT', model: 'Kangoo', type: 'van' });
    expect(fiche.formData.type).toBe('utilitaire');
    expect(fiche.typeChoisi).toBe(true);
  });

  it('marque « Autre » : marque et modèle se saisissent, la marque part en majuscules', async () => {
    await ouvrir();
    fiche.formData.brandId = 'autre';
    fiche.onBrandChange('autre');
    fixture.detectChanges();
    expect(fiche.marqueLibre).toBe(true);
    expect(fixture.nativeElement.querySelector('#brandLibre')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#modelLibre')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('#modelId')).toBeNull();

    Object.assign(fiche.formData, { name: 'Engin', plate: '200 TU 1', mileage: 0, fuelType: 'diesel', brand: ' foton ', model: ' Tunland ' });
    let envoye: any;
    fiche.saved.subscribe((p: any) => envoye = p);
    fiche.onSubmit();
    expect(envoye.brand).toBe('FOTON');
    expect(envoye.model).toBe('Tunland');
    expect(envoye.brandId).toBeNull();
  });

  it('modèle « Autre » d\'une marque connue : saisie libre du modèle', async () => {
    await ouvrir();
    choisirRenault();
    fixture.detectChanges();
    const liste = fixture.nativeElement.querySelector('#modelId') as HTMLSelectElement;
    expect(Array.from(liste.options).map(o => o.textContent!.trim())).toContain('Autre (modèle absent de la liste)');
    fiche.formData.modelId = 'autre';
    fiche.onModelChange('autre');
    fixture.detectChanges();
    expect(fiche.modeleLibre).toBe(true);
    expect(fixture.nativeElement.querySelector('#modelLibre')).not.toBeNull();
  });

  it('véhicule existant hors catalogue : sa marque et son modèle s\'affichent dans les saisies libres', async () => {
    await ouvrir({ id: 6, name: 'Pick-up', plate: '9 TU 9', brand: 'FOTON', model: 'Tunland', type: 'utilitaire' });
    expect(fiche.formData.brandId).toBe('autre');
    expect(fiche.formData.brand).toBe('FOTON');
    expect(fiche.formData.model).toBe('Tunland');
  });
});
