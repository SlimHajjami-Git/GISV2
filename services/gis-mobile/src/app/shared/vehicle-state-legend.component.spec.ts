import { CUSTOM_ELEMENTS_SCHEMA } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { LEGEND_COLLAPSED_KEY, VehicleStateLegendComponent } from './vehicle-state-legend.component';

describe('VehicleStateLegendComponent (légende des couleurs de la carte)', () => {
  let fixture: ComponentFixture<VehicleStateLegendComponent>;

  const el = (): HTMLElement => fixture.nativeElement;
  const toggle = () => el().querySelector('.vsl-toggle') as HTMLButtonElement;

  async function create() {
    await TestBed.configureTestingModule({
      imports: [VehicleStateLegendComponent],
      schemas: [CUSTOM_ELEMENTS_SCHEMA]
    }).compileComponents();
    fixture = TestBed.createComponent(VehicleStateLegendComponent);
    fixture.detectChanges();
  }

  beforeEach(() => localStorage.removeItem(LEGEND_COLLAPSED_KEY));
  afterEach(() => {
    fixture?.destroy();
    localStorage.removeItem(LEGEND_COLLAPSED_KEY);
  });

  it('dépliée par défaut : quatre lignes, pastille + libellé, dans l\'ordre de la demande', async () => {
    await create();
    const rows = Array.from<HTMLElement>(el().querySelectorAll('.vsl-row'));
    expect(rows.map(r => r.getAttribute('data-state'))).toEqual(['moving', 'idling', 'parked', 'offline']);
    expect(rows.map(r => r.querySelector('.vsl-label')!.textContent!.trim()))
      .toEqual(['En route', 'Au ralenti', 'À l\'arrêt', 'Déconnecté']);
    expect(rows.map(r => /background:(#[0-9a-f]{6})/.exec(r.querySelector('.vs-badge')!.getAttribute('style')!)![1]))
      .toEqual(['#10b981', '#f59e0b', '#ef4444', '#9ca3af']);
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
  });

  it('repliée, elle ne garde que quatre points de couleur ; le choix est mémorisé', async () => {
    await create();
    toggle().click();
    fixture.detectChanges();
    expect(el().querySelector('.vsl-list')).toBeNull();
    const dots = Array.from<HTMLElement>(el().querySelectorAll('.vsl-dot'));
    expect(dots.length).toBe(4);
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(toggle().getAttribute('aria-label')).toBe('Afficher la légende des couleurs');
    expect(localStorage.getItem(LEGEND_COLLAPSED_KEY)).toBe('1');

    // Relancée, la carte retrouve la légende repliée.
    fixture.destroy();
    fixture = TestBed.createComponent(VehicleStateLegendComponent);
    fixture.detectChanges();
    expect(el().querySelector('.vsl-list')).toBeNull();

    toggle().click();
    fixture.detectChanges();
    expect(el().querySelectorAll('.vsl-row').length).toBe(4);
    expect(localStorage.getItem(LEGEND_COLLAPSED_KEY)).toBe('0');
  });
});
