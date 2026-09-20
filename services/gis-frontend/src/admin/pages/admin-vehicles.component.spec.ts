import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { AdminVehiclesComponent } from './admin-vehicles.component';
import { AdminService, AdminVehicle } from '../services/admin.service';
import { ApiService } from '../../services/api.service';

/**
 * Fenêtre de confirmation de la suppression d'un véhicule — la seule partie du lot
 * « sinistre » entièrement visible à l'écran, et la plus irréversible.
 *
 * <p>Deux règles verrouillées ici : la phrase générale (« toutes les données du véhicule
 * sont supprimées avec lui ») est TOUJOURS rendue, même quand les compteurs n'arrivent
 * pas ou valent zéro — la fenêtre annonçait sinon « aucune donnée rattachée » devant une
 * suppression qui en détruisait ; et une réponse arrivée pour un autre véhicule ne
 * s'affiche jamais devant celui qu'on s'apprête à supprimer.</p>
 */
describe('AdminVehiclesComponent — fenêtre de suppression', () => {
  let component: AdminVehiclesComponent;
  let fixture: any;
  let api: ApiService;

  const vehiculeA: AdminVehicle = { id: 11, name: 'Service 01', plate: 'GA-214-RK' } as AdminVehicle;
  const vehiculeB: AdminVehicle = { id: 22, name: 'Service 02', plate: 'GB-310-TN' } as AdminVehicle;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, AdminVehiclesComponent],
      providers: [AdminService, ApiService],
    }).compileComponents();

    const admin = TestBed.inject(AdminService);
    jest.spyOn(admin, 'isAuthenticated').mockReturnValue(true);
    jest.spyOn(admin, 'getVehicles').mockReturnValue(of([]));
    jest.spyOn(admin, 'getClients').mockReturnValue(of([]));

    fixture = TestBed.createComponent(AdminVehiclesComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);
  });

  /** Texte de la fenêtre, espaces normalisés. */
  function texteFenetre(): string {
    return (fixture.nativeElement.querySelector('.delete-modal')?.textContent || '')
      .replace(/\s+/g, ' ');
  }

  it('garde la phrase générale quand le compteur échoue', () => {
    jest.spyOn(api, 'getVehicleDeletionImpact').mockReturnValue(throwError(() => new Error('500')));

    component.confirmDelete(vehiculeA);
    fixture.detectChanges();

    expect(component.deleteImpactFailed).toBe(true);
    const texte = texteFenetre();
    expect(texte).toContain('Toutes les donnees du vehicule');
    expect(texte).toContain('Impossible de verifier les donnees rattachees.');
  });

  it('rend la puce « aucune donnée rattachée » APRÈS la phrase générale', () => {
    jest.spyOn(api, 'getVehicleDeletionImpact')
      .mockReturnValue(of({ vehicleId: vehiculeA.id, accidents: 0, repairs: 0, costs: 0 }));

    component.confirmDelete(vehiculeA);
    fixture.detectChanges();

    const texte = texteFenetre();
    const phrase = texte.indexOf('Cette action est irreversible.');
    const puce = texte.indexOf('Aucune reparation, depense ni dossier de sinistre rattache.');
    expect(phrase).toBeGreaterThanOrEqual(0);
    expect(puce).toBeGreaterThan(phrase);
  });

  it('ignore la réponse d\'un véhicule qui n\'est plus celui de la fenêtre', () => {
    jest.spyOn(api, 'getVehicleDeletionImpact')
      .mockReturnValue(of({ vehicleId: vehiculeA.id, accidents: 3, repairs: 4, costs: 5 }));

    // La fenêtre porte le véhicule B, la réponse qui arrive concerne le véhicule A
    // (ouverture précédente, réponse lente) : elle ne doit pas s'afficher.
    component.confirmDelete(vehiculeB);
    fixture.detectChanges();

    expect(component.deleteImpact).toBeNull();
    expect(texteFenetre()).not.toContain('4 reparation(s)');
  });
});
