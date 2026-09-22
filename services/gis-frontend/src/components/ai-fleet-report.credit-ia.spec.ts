// marked n'est livré qu'en ESM, que jest ne transforme pas dans node_modules. Même
// contournement que reports.component.spec.ts.
jest.mock('marked', () => ({ marked: { parse: (s: string) => s } }));

import { TestBed } from '@angular/core/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { of, throwError } from 'rxjs';
import { AiFleetReportComponent } from './ai-fleet-report.component';
import { ApiService } from '../services/api.service';

/**
 * Écran autonome « Rapport IA - Analyse de Flotte » et crédit IA du mois (22/09/2026) :
 * barre à côté de « Generer le rapport », bouton et questions grisés à 100 % avec le
 * motif, barre rafraîchie par chaque réponse, message du serveur sur un refus AI_CREDIT_*.
 */
describe('AiFleetReportComponent — crédit IA', () => {
  const credit = (patch: Record<string, unknown> = {}) => ({
    enabled: true, budgetTokens: 60000, usedTokens: 25200, remainingTokens: 34800,
    percentUsed: 42, scansThisMonth: 3, estimatedScansLeft: 11, resetsAt: '2026-10-01T00:00:00Z',
    ...patch
  });
  const epuise = () => credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 });
  const MESSAGE_429 = "Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l'augmenter.";
  const rapport = (patch: Record<string, unknown> = {}) => ({
    companyInfo: { name: 'Belive', type: 'transport', vehicleCount: 1 },
    generatedAt: '2026-09-22T10:00:00Z',
    fleetSummary: { totalVehicles: 1, totalDistance: 1200, totalCosts: 900, avgHealthScore: 80, totalAlerts: 0, overdueSchedules: 0 },
    charts: {
      healthDistribution: { excellent: 1, good: 0, fair: 0, poor: 0, critical: 0 },
      costBreakdown: { fuel: 600, maintenance: 300, repairs: 0, other: 0, credits: 0 },
      topFuelConsumers: [], mileageByVehicle: [], drivingScores: []
    },
    vehicleDetails: [], aiAnalysis: 'Synthèse', tokensUsed: 4800,
    ...patch
  });

  let api: Record<string, jest.Mock>;
  let fixture: any;
  let horloge: jest.SpyInstance<number, []>;

  function creer(creditInitial: unknown = credit()) {
    api = {
      getAiCredit: jest.fn(() => of(creditInitial)),
      generateFleetReport: jest.fn(() => of(rapport({ credit: credit({ usedTokens: 30000, remainingTokens: 30000, percentUsed: 50 }) }))),
      askFleetReport: jest.fn(() => of({ answer: 'Réponse', tokensUsed: 2100, credit: credit({ usedTokens: 32100, remainingTokens: 27900, percentUsed: 53 }) }))
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [RouterTestingModule, AiFleetReportComponent],
      providers: [{ provide: ApiService, useValue: api }]
    });
    fixture = TestBed.createComponent(AiFleetReportComponent);
    fixture.detectChanges();
    return fixture.componentInstance as AiFleetReportComponent;
  }

  const el = (): HTMLElement => fixture.nativeElement;
  const generer = () => el().querySelector('.btn-generate') as HTMLButtonElement;
  const pct = () => (el().querySelector('.report-header [role="progressbar"]') as HTMLElement).getAttribute('aria-valuenow');

  beforeEach(() => { horloge = jest.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-09-22T10:00:00Z')); });
  afterEach(() => { fixture?.destroy(); horloge.mockRestore(); });

  it('barre « Crédit IA » à côté de « Generer le rapport »', () => {
    creer();
    expect(api['getAiCredit']).toHaveBeenCalledTimes(1);
    expect(pct()).toBe('42');
    expect(generer().disabled).toBe(false);
  });

  it('crédit épuisé : génération grisée, motif et date de recharge affichés, aucun appel', () => {
    const c = creer(epuise());

    expect(generer().disabled).toBe(true);
    expect(el().querySelector('.credit-blocked')!.textContent!.trim())
      .toBe("Crédit IA du mois épuisé (100 %) — il se recharge le 1er octobre. Votre administrateur peut l'augmenter.");
    c.generateReport();
    expect(api['generateFleetReport']).not.toHaveBeenCalled();
  });

  it('rapport puis question : la barre suit le crédit joint à chaque réponse', () => {
    const c = creer();
    c.generateReport();
    fixture.detectChanges();
    expect(pct()).toBe('50');

    c.askQuestion('Quel véhicule remplacer ?');
    fixture.detectChanges();
    expect(pct()).toBe('53');
  });

  it('refus 429 : message du serveur, barre à 100 %, génération grisée', () => {
    const c = creer();
    api['generateFleetReport'].mockReturnValue(throwError(() => ({
      status: 429, error: { code: 'AI_CREDIT_EXHAUSTED', message: MESSAGE_429, credit: epuise() }
    })));

    c.generateReport();
    fixture.detectChanges();

    expect(el().querySelector('.error-banner')!.textContent).toContain(MESSAGE_429);
    expect(pct()).toBe('100');
    expect(generer().disabled).toBe(true);
  });

  it('question refusée (403 IA coupée) : message du serveur dans le fil, questions grisées', () => {
    const c = creer();
    c.generateReport();
    fixture.detectChanges();
    const message = "Les fonctions d'IA ne sont pas activées pour votre société.";
    api['askFleetReport'].mockReturnValue(throwError(() => ({
      status: 403, error: { code: 'AI_CREDIT_DISABLED', message, credit: credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100 }) }
    })));

    c.askQuestion('Et le carburant ?');
    fixture.detectChanges();

    expect(c.qaMessages.map(m => m.text)).toEqual(['Et le carburant ?', message]);
    expect(Array.from(el().querySelectorAll('.suggestion-btn')).every(b => (b as HTMLButtonElement).disabled)).toBe(true);
    expect(generer().disabled).toBe(true);
  });
});
