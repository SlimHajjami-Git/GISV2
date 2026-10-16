import { ExpensesComponent } from './expenses.component';

/**
 * Écran Dépenses, « Total 346 773 € » (campagne GPA du 06/10/2026, erreur n°19) : toutes
 * années, achats de véhicules de 2019 compris, sans le dire. Le Total dit sa période, se
 * filtre par année et se détaille par famille.
 */
describe('Dépenses — le Total dit sa période et ce qu\'il contient', () => {
  function ecran(): ExpensesComponent {
    const e = new ExpensesComponent({} as any, { detectChanges() {} } as any, { snapshot: { queryParams: {} } } as any, {} as any, {} as any);
    const ligne = (id: string, category: string, totalAmount: number, date: string, extra: any = {}) =>
      ({ id, vehicleId: 1, vehiclePlate: 'GE-768-HJ', vehicleName: '', category, label: category, quantity: 1, unitPrice: totalAmount,
         totalAmount, date: new Date(date), createdAt: new Date(date), sourceTable: 'costs', ...extra });
    (e as any).expenses = [
      ligne('acq_1', 'achat', 26_700, '2019-03-01', { sourceTable: 'acquisition_payment', counted: true }),
      ligne('fuel_1', 'carburant', 80, '2026-10-02', { sourceTable: 'fuelentries' }),
      ligne('fuel_2', 'carburant', 70, '2025-06-12', { sourceTable: 'fuelentries' }),
      ligne('cost_1', 'maintenance', 160, '2026-10-05'),
      ligne('cost_2', 'insurance_refund', 100, '2026-09-20', { isCredit: true }),
    ];
    e.filterExpenses();
    return e;
  }

  it('sans filtre : toutes périodes, achats compris et détaillés à part', () => {
    const e = ecran();
    expect(e.libellePeriode).toBe('toutes périodes');
    expect(e.getTotalAmount()).toBe(26_700 + 80 + 70 + 160 - 100);
    expect(e.repartitionTotal).toEqual([
      { libelle: 'Achat de véhicules', montant: 26_700 },
      { libelle: 'Entretien', montant: 160 },
      { libelle: 'Carburant', montant: 150 },
      { libelle: 'Remboursements et avoirs', montant: -100 },
    ]);
  });

  it('une année : le Total et le détail ne portent que sur elle', () => {
    const e = ecran();
    e.filterYear = '2026';
    e.onYearChange();
    expect(e.libellePeriode).toBe('année 2026');
    expect(e.getTotalAmount()).toBe(80 + 160 - 100);
    expect(e.repartitionTotal.map(r => r.libelle)).not.toContain('Achat de véhicules');
  });

  it('un mois remplace l\'année, et inversement', () => {
    const e = ecran();
    e.filterYear = '2026';
    e.onYearChange();
    e.filterMonth = '2026-10';
    e.onMonthChange();
    expect(e.filterYear).toBe('');
    expect(e.libellePeriode).toBe('octobre 2026');
    expect(e.getTotalAmount()).toBe(240);
    e.filterYear = '2025';
    e.onYearChange();
    expect(e.filterMonth).toBe('');
    expect(e.getTotalAmount()).toBe(70);
  });

  it('les années proposées sont celles des dépenses, la plus récente d\'abord', () => {
    expect(ecran().anneesDisponibles).toEqual([2026, 2025, 2019]);
  });
});
