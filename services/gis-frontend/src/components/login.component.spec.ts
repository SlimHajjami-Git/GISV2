import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { LoginComponent } from './login.component';

/**
 * Relecture du 19/09/2026 — la page de connexion, première chose que voit un client,
 * affichait une pastille indigo générique avec une coche à la place du logo. Ces tests
 * verrouillent le vrai logo et le garde-fou de marque qui l'accompagne.
 *
 * Le parcours européen (app-france-auth) n'est pas concerné : RegionService se décide
 * sur le nom de domaine, et jsdom sert « localhost » — c'est donc le formulaire
 * habituel qui est rendu ici, celui qui portait la pastille.
 */
describe('LoginComponent — marque de la page de connexion', () => {
  let fixture: any;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, LoginComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
  });

  it('pose le vrai logo à la place de la pastille indigo à coche', () => {
    const logo: HTMLImageElement | null = fixture.nativeElement.querySelector('.logo img.logo-mark');
    expect(logo).not.toBeNull();
    expect(logo!.getAttribute('src')).toBe('/assets/calypso-logo.svg');
    // La pastille et le nom en texte disparaissent : le mot CALYPSO est dans le logo.
    expect(fixture.nativeElement.querySelector('.logo .logo-icon')).toBeNull();
    // La ligne de service, elle, reste — elle n'est pas dans le dessin.
    expect(fixture.nativeElement.querySelector('.logo .subtitle')!.textContent).toContain('Gestion de flotte');
  });

  it("rend l'ancien bloc, à l'identique, pour une autre marque", () => {
    const composant = fixture.componentInstance;
    expect(composant.estMarqueCalypso).toBe(true);

    composant.estMarqueCalypso = false; // readonly n'existe qu'à la compilation
    composant.brand = 'Bougeo';
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.logo img.logo-mark')).toBeNull();
    expect(fixture.nativeElement.querySelector('.logo .logo-icon')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.logo .brand')!.textContent).toContain('Bougeo');
  });
});
