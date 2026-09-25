import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { FormsModule } from '@angular/forms';
import { VehiclePopupComponent } from './vehicle-popup.component';
import { rangerMarques } from '../../services/marques-courantes';

/**
 * Liste « Marque » de la fiche véhicule (Karim, 25/09/2026) : « les marques les plus
 * connues au début (avec un ordre alphabétique) et le reste après (avec un ordre
 * alphabétique aussi) ».
 */
describe('Fiche véhicule — ordre de la liste des marques', () => {
  const catalogue = ['YUTONG', 'RENAULT', 'ABARTH', 'Citroën', 'KING LONG', 'AUDI', 'TOYOTA', 'BOVA']
    .map((name, i) => ({ id: i + 1, name, modelCount: 3 }));

  it('les courantes d\'abord, puis les autres, chaque groupe par ordre alphabétique', () => {
    const { courantes, autres } = rangerMarques(catalogue);
    expect(courantes.map(m => m.name)).toEqual(['AUDI', 'Citroën', 'RENAULT', 'TOYOTA']);
    expect(autres.map(m => m.name)).toEqual(['ABARTH', 'BOVA', 'KING LONG', 'YUTONG']);
  });

  it('la vraie fiche affiche « -- Sélectionner -- », puis les deux groupes dans cet ordre', async () => {
    await TestBed.configureTestingModule({
      imports: [VehiclePopupComponent, FormsModule, HttpClientTestingModule, NoopAnimationsModule]
    }).compileComponents();
    const fixture = TestBed.createComponent(VehiclePopupComponent);
    fixture.componentRef.setInput('isOpen', true);      // ngOnChanges : chargement des marques
    fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    // ngOnInit et ngOnChanges chargent chacun la liste : les deux reçoivent le catalogue.
    http.match('/api/brands').forEach(r => r.flush(catalogue));
    fixture.detectChanges();

    const liste = fixture.nativeElement.querySelector('[data-guide="vehicule-marque"]') as HTMLSelectElement;
    const groupes = Array.from(liste.querySelectorAll('optgroup')).map(g => ({
      titre: g.label,
      marques: Array.from(g.querySelectorAll('option')).map(o => o.textContent!.trim()),
    }));
    expect(liste.options[0].textContent!.trim()).toBe('-- Sélectionner --');
    expect(groupes).toEqual([
      { titre: 'Marques les plus courantes', marques: ['AUDI', 'Citroën', 'RENAULT', 'TOYOTA'] },
      { titre: 'Autres marques', marques: ['ABARTH', 'BOVA', 'KING LONG', 'YUTONG'] },
    ]);
  });
});
