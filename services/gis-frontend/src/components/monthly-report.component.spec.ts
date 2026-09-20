import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { MonthlyReportComponent } from './monthly-report.component';
import { ApiService } from '../services/api.service';

/**
 * Ancien écran « Rapport mensuel » (/reports/monthly), règle des avoirs et
 * remboursements du 18/09/2026.
 *
 * Le serveur calcule « percentage » sur le total NET des crédits. Sur la société de
 * test, septembre 2026 — carburant 1 071,13, entretien 1 830, réparations 1 300,
 * assurance 625, remboursement d'assurance −1 200, total net 3 626,13 — il rendait
 * 29,5 / 50,5 / 35,9 / 17,2 % et −33,1 % pour la ligne de crédit. Une largeur de
 * barre négative est refusée par le navigateur : la barre de l'avoir restait pleine,
 * lue comme la plus grosse dépense du mois.
 *
 * L'écran recalcule maintenant les parts sur les dépenses BRUTES (4 826,13) et la
 * ligne de crédit n'en a aucune.
 */
describe('MonthlyReportComponent — répartition des coûts avec un avoir', () => {
  let component: MonthlyReportComponent;
  let fixture: any;
  let api: ApiService;

  // Pourcentages tels que le serveur les rend : base = total NET.
  const net = 1071.13 + 1830 + 1300 + 625 - 1200;
  const pctServeur = (montant: number) => Math.round((montant / net) * 1000) / 10;

  const rapport = {
    monthName: 'Septembre 2026',
    reportPeriod: '01/09/2026 - 30/09/2026',
    fleetHasGps: false,
    fleetOverview: { totalVehicles: 12, byType: [] },
    executiveSummary: {},
    utilization: { byVehicle: [] },
    maintenance: { byType: [] },
    driverPerformance: {},
    keyPerformanceIndicators: [],
    alerts: [],
    monthOverMonth: null,
    costAnalysis: {
      totalOperationalCost: net,
      fuelCost: 1071.13,
      maintenanceCost: 3130,
      insuranceCost: 625,
      otherCosts: 0,
      creditAmount: -1200,
      costPerKm: 0.31,
      costPerVehicle: 302.18,
      byCategory: [
        { category: 'Entretien', amount: 1830, percentage: pctServeur(1830) },
        { category: 'Réparations', amount: 1300, percentage: pctServeur(1300) },
        { category: 'Carburant', amount: 1071.13, percentage: pctServeur(1071.13) },
        { category: 'Assurance', amount: 625, percentage: pctServeur(625) },
        { category: 'Remboursement assurance', amount: -1200, percentage: pctServeur(-1200) }
      ],
      dailyTrend: [],
      byVehicle: []
    },
    charts: {
      fleetComposition: { labels: [], values: [], colors: [] },
      vehicleStatusDistribution: { labels: [], values: [], colors: [] },
      costDistribution: { labels: [], values: [], colors: [] },
      maintenanceCostByType: { labels: [], values: [], colors: [] },
      driverRanking: { labels: [], values: [], colors: [] },
      efficiencyTrend: { labels: [], series: [] },
      dailyDistanceTrend: { labels: [], series: [] }
    }
  };

  beforeEach(async () => {
    // La barre latérale injecte le service d'export PDF, qui précharge le logo par
    // fetch() — absent de jsdom. Un échec de préchargement est déjà prévu par le service.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, MonthlyReportComponent],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(MonthlyReportComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getMonthlyFleetReport').mockReturnValue(of(rapport) as any);
    jest.spyOn(api, 'getMonthlyCostReport').mockReturnValue(of(null) as any);
  });

  afterEach(() => {
    fixture?.destroy();
  });

  it('le constat : le pourcentage rendu par l’API est faux dès qu’un avoir existe', () => {
    fixture.detectChanges();

    const duServeur = (libelle: string) =>
      rapport.costAnalysis.byCategory.find(c => c.category === libelle)!.percentage;

    // Base NET : la colonne fait bien 100, mais l'assurance pèse 17,2 % de dépenses
    // qu'elle ne représente pas, et le crédit sort en part négative — largeur refusée.
    expect(duServeur('Assurance')).toBeCloseTo(17.2, 1);
    expect(duServeur('Remboursement assurance')).toBeCloseTo(-33.1, 1);
    expect(duServeur('Remboursement assurance')).toBeLessThan(0);

    // L'écran ne s'en sert plus : aucune part affichée n'est celle du serveur.
    const affichee = component.categoriesCouts.find(c => c.category === 'Assurance')!.percent!;
    expect(affichee).not.toBeCloseTo(duServeur('Assurance'), 1);
  });

  it('les parts portent sur les dépenses brutes et font 100', () => {
    fixture.detectChanges();

    const parts = component.categoriesCouts.filter(c => c.percent !== null);
    expect(parts).toHaveLength(4);
    expect(parts.reduce((s, c) => s + c.percent!, 0)).toBeCloseTo(100, 6);

    const brut = 1071.13 + 1830 + 1300 + 625;
    const part = (libelle: string) => component.categoriesCouts.find(c => c.category === libelle)!.percent!;
    expect(part('Carburant')).toBeCloseTo((1071.13 / brut) * 100, 6);
    expect(part('Assurance')).toBeCloseTo((625 / brut) * 100, 6);
    // 13,0 % des dépenses réelles, et non les 17,2 % du serveur.
    expect(part('Assurance')).toBeCloseTo(12.95, 2);
  });

  it('la ligne d’avoir n’a pas de part : « — », jamais un pourcentage négatif', () => {
    fixture.detectChanges();

    const credit = component.categoriesCouts.find(c => c.category === 'Remboursement assurance')!;
    expect(credit.amount).toBe(-1200);
    expect(credit.percent).toBeNull();
    expect(component.aDesAvoirs).toBe(true);
  });

  it('aucune barre de progression négative ni pleine sur un crédit', () => {
    fixture.detectChanges();
    component.setActiveSection('costs');
    fixture.detectChanges();

    const lignes: HTMLTableRowElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('.data-table-container tbody tr')
    );
    const ligneCredit = lignes.find(tr => tr.textContent?.includes('Remboursement assurance'));
    expect(ligneCredit).toBeTruthy();
    // Pas de barre du tout : une largeur négative est refusée par le navigateur et
    // laisserait la barre pleine, c'est-à-dire la plus grosse dépense du mois.
    expect(ligneCredit!.querySelector('.progress-bar')).toBeNull();
    expect(ligneCredit!.querySelector('.part-credit')?.textContent?.trim()).toBe('—');

    const largeurs = Array.from(
      fixture.nativeElement.querySelectorAll('.data-table-container .progress')
    ).map((el: any) => parseFloat(el.style.width));
    expect(largeurs).toHaveLength(4);
    largeurs.forEach(l => {
      expect(l).toBeGreaterThan(0);
      expect(l).toBeLessThanOrEqual(100);
    });
  });

  it('la jauge d’un KPI reste dans [0, 100], objectif nul ou valeur négative compris', () => {
    expect(component.largeurKpi({ value: -0.4, target: 0.25 } as any)).toBe(0);
    expect(component.largeurKpi({ value: 0.5, target: 0.25 } as any)).toBe(100);
    expect(component.largeurKpi({ value: 0.125, target: 0.25 } as any)).toBeCloseTo(50, 6);
    expect(component.largeurKpi({ value: 12, target: 0 } as any)).toBe(0);
  });
});
