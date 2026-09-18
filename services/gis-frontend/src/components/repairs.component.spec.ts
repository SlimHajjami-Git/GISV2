import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { RepairsComponent } from './repairs.component';
import { ApiService } from '../services/api.service';

/**
 * Contre-relecture du 18/09/2026 — écran Réparations.
 *
 * Le serveur refuse depuis ce jour la suppression d'une réparation née de la phase 5
 * d'un dossier de sinistre (400 avec un motif en français). L'écran avalait ce refus :
 * la fenêtre de confirmation se fermait, la ligne restait, aucun message — le client
 * y voyait un bug. Deux garde-fous : le motif du serveur reste affiché, et le bouton
 * Supprimer est verrouillé sur ces lignes, comme à l'écran Dépenses.
 */
describe('RepairsComponent — réparation née d’un sinistre', () => {
  let component: RepairsComponent;
  let fixture: any;
  let api: ApiService;

  const reparationSinistre = {
    id: 7,
    vehicleId: 49,
    vehicleName: 'Camion 12',
    vehiclePlate: '123 TU 4567',
    reference: 'REP-2026-007',
    description: 'Pare-chocs avant',
    repairDate: '2026-09-10T00:00:00',
    laborCost: 120,
    partsCost: 380,
    totalCost: 500,
    status: 'completed',
    accidentEventId: 42,
    parts: []
  };

  beforeEach(async () => {
    // L'écran injecte le service d'export PDF, qui précharge le logo par fetch() —
    // absent de jsdom. Un échec de préchargement est déjà prévu par le service.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, RepairsComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(RepairsComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getSuppliers').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 200 }) as any);
    jest.spyOn(api, 'getRepairs').mockReturnValue(
      of({ items: [reparationSinistre], totalCount: 1, page: 1, pageSize: 100 }) as any
    );
  });

  it('refus 400 du serveur : le motif s’affiche et la fenêtre reste ouverte', () => {
    const motif = 'Cette réparation appartient au dossier de sinistre SIN-2026-042 : '
      + 'videz le coût réel dans la phase 5 du dossier pour la retirer.';
    jest.spyOn(api, 'deleteRepair').mockReturnValue(
      throwError(() => ({ status: 400, error: { message: motif } })) as any
    );

    component.confirmDelete(reparationSinistre as any);
    component.deleteRepair();

    expect(component.saveError).toBe(motif);
    expect(component.showDeleteConfirm).toBe(true);
  });

  it('refus sans motif : repli en français, jamais une fenêtre muette', () => {
    jest.spyOn(api, 'deleteRepair').mockReturnValue(throwError(() => ({ status: 400, error: {} })) as any);

    component.confirmDelete(reparationSinistre as any);
    component.deleteRepair();

    expect(component.saveError).toBeTruthy();
    expect(component.showDeleteConfirm).toBe(true);
  });

  it('suppression acceptée : la fenêtre se ferme et la liste est rechargée', () => {
    jest.spyOn(api, 'deleteRepair').mockReturnValue(of(void 0) as any);

    component.confirmDelete({ ...reparationSinistre, id: 8, accidentEventId: null } as any);
    component.deleteRepair();

    expect(component.saveError).toBeNull();
    expect(component.showDeleteConfirm).toBe(false);
    expect(api.getRepairs).toHaveBeenCalled();
  });

  it('ligne issue d’un sinistre : bouton Supprimer verrouillé et motif en infobulle', () => {
    fixture.detectChanges();

    expect(component.repairs[0].accidentEventId).toBe(42);

    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('button.btn-action.delete');
    expect(bouton).toBeTruthy();
    expect(bouton.disabled).toBe(true);
    expect(bouton.getAttribute('title')).toContain('sinistre #42');

    // Repère visible sur la ligne, pour que le verrou ne soit pas une surprise.
    expect(fixture.nativeElement.querySelector('.col-ref .accident-badge')?.textContent).toContain('#42');
  });
});
