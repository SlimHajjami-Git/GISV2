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

/**
 * Liste déroulante des sociétés du formulaire « Nouveau véhicule ».
 *
 * <p>Signalement de Slim le 29/09/2026 : « la liste reste figée sur la première société,
 * peu importe celle que je choisis ». Cause : `companiesForPopup` était un GETTER qui
 * refaisait `map()` à chaque lecture, donc à chaque cycle de détection de changements.
 * L'entrée `[companies]` du popup changeait d'identité en permanence, le `*ngFor` des
 * `&lt;option&gt;` les détruisait et les recréait, et le `&lt;select&gt;` perdait la
 * sélection pour retomber sur sa première entrée — celle passée en société par défaut.</p>
 *
 * <p>Un getter qui alloue est invisible à la lecture du code ; dans un gabarit il
 * s'exécute des dizaines de fois par seconde. Ce test le rend visible : il échouait
 * avant le correctif et interdit d'y revenir.</p>
 */
describe('AdminVehiclesComponent — liste des sociétés du popup', () => {
  let component: AdminVehiclesComponent;

  const societes = [
    { id: 7, name: 'salah ben ali' },
    { id: 4, name: 'HERTZ' },
    { id: 1, name: 'BELIVE' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, AdminVehiclesComponent],
      providers: [AdminService, ApiService],
    }).compileComponents();

    const admin = TestBed.inject(AdminService);
    jest.spyOn(admin, 'isAuthenticated').mockReturnValue(true);
    jest.spyOn(admin, 'getVehicles').mockReturnValue(of([]));
    jest.spyOn(admin, 'getClients').mockReturnValue(of(societes as any));

    component = TestBed.createComponent(AdminVehiclesComponent).componentInstance;
    component.ngOnInit();
  });

  it("garde la MÊME identité de tableau entre deux lectures", () => {
    const premiere = component.companiesForPopup;
    const seconde = component.companiesForPopup;

    expect(seconde).toBe(premiere);
  });

  it("garde la même identité pour chaque société, ce qui permet à trackBy de réutiliser les options", () => {
    const premiere = component.companiesForPopup[0];
    const seconde = component.companiesForPopup[0];

    expect(seconde).toBe(premiere);
  });

  it('contient bien toutes les sociétés chargées, dans leur ordre', () => {
    expect(component.companiesForPopup).toEqual([
      { id: 7, name: 'salah ben ali' },
      { id: 4, name: 'HERTZ' },
      { id: 1, name: 'BELIVE' },
    ]);
  });

  it("retombe sur une liste vide quand le chargement échoue, sans lever d'erreur", async () => {
    const admin = TestBed.inject(AdminService);
    jest.spyOn(admin, 'getClients').mockReturnValue(throwError(() => new Error('réseau')));

    component.ngOnInit();

    expect(component.companiesForPopup).toEqual([]);
  });
});
