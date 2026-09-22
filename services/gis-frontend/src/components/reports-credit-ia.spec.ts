// marked n'est livré qu'en ESM, que jest ne transforme pas dans node_modules : le charger
// avec le composant fait échouer la suite avant le premier test. Même contournement que
// reports.component.spec.ts.
jest.mock('marked', () => ({ marked: { parse: (s: string) => s } }));

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { ReportsComponent } from './reports.component';
import { ApiService } from '../services/api.service';

/**
 * Rapport IA flotte (écran Rapports) et crédit IA du mois (22/09/2026, « le quota inclut
 * l'utilisation de l'IA ») : barre « Crédit IA » dès que le rapport est choisi, « Exécuter »
 * et questions de suivi grisés à 100 % avec un message clair, barre rafraîchie par le crédit
 * joint à chaque réponse, message du serveur sur un refus 403/429 AI_CREDIT_*.
 */
describe('Rapport IA flotte — crédit IA', () => {
  let composant: ReportsComponent;
  let fixture: ComponentFixture<ReportsComponent>;
  let api: ApiService;
  let horloge: jest.SpyInstance<number, []>;

  const credit = (patch: Record<string, unknown> = {}) => ({
    enabled: true, budgetTokens: 60000, usedTokens: 25200, remainingTokens: 34800,
    percentUsed: 42, scansThisMonth: 3, estimatedScansLeft: 11, resetsAt: '2026-10-01T00:00:00Z',
    ...patch
  });
  const epuise = () => credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 });
  const MESSAGE_429 = "Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l'augmenter.";

  const rapportFlotte = (patch: Record<string, unknown> = {}) => ({
    companyInfo: { name: 'Belive', type: 'transport', vehicleCount: 1 },
    generatedAt: '2026-09-22T10:00:00Z', period: 'month', periodDays: 30,
    fleetSummary: { totalVehicles: 1, activeVehicles: 1, totalDistance: 1200, totalTrips: 12, avgHealthScore: 80, totalCosts: 900, totalAlerts: 0, overdueSchedules: 0 },
    charts: {
      healthDistribution: { excellent: 1, good: 0, fair: 0, poor: 0, critical: 0 },
      costBreakdown: { fuel: 600, maintenance: 300, repairs: 0, other: 0, credits: 0 },
      topFuelConsumers: [], mileageByVehicle: [], drivingScores: []
    },
    vehicleDetails: [],
    aiAnalysis: '## Synthèse',
    tokensUsed: 4800,
    ...patch
  });

  beforeEach(async () => {
    // Le service PDF précharge le logo par fetch(), absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));
    // L'écran Rapports restaure le rapport du test précédent depuis sessionStorage (et le
    // relance) : chaque test part d'un écran vierge.
    sessionStorage.clear();
    horloge = jest.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-09-22T10:00:00Z'));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, ReportsComponent],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(ReportsComponent);
    composant = fixture.componentInstance;
    api = TestBed.inject(ApiService);
    jest.spyOn(api, 'isAuthenticated').mockReturnValue(true);
    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]));
  });

  afterEach(() => { fixture.destroy(); sessionStorage.clear(); horloge.mockRestore(); });

  const choisirRapportIa = (c: unknown = credit()) => {
    jest.spyOn(api, 'getAiCredit').mockReturnValue(of(c as any));
    composant.selectTemplate(composant.templates.find(t => t.type === 'ai-fleet')!);
    fixture.detectChanges();
  };

  const el = (): HTMLElement => fixture.nativeElement;
  const executer = () => el().querySelector('.action-row .btn-execute') as HTMLButtonElement;
  const zoneRapportIa = () => el().querySelector('.ai-fleet-workspace') as HTMLElement;

  it('choisir le rapport IA flotte lit le crédit et affiche la barre avant « Exécuter »', () => {
    choisirRapportIa();

    expect(api.getAiCredit).toHaveBeenCalledTimes(1);
    const barre = el().querySelector('.ai-credit-sidebar [role="progressbar"]') as HTMLElement;
    expect(barre.getAttribute('aria-valuenow')).toBe('42');
    expect(executer().disabled).toBe(false);
  });

  it('un autre rapport ne lit pas le crédit IA', () => {
    const lecture = jest.spyOn(api, 'getAiCredit');
    composant.selectTemplate(composant.templates.find(t => t.type === 'trips')!);

    expect(lecture).not.toHaveBeenCalled();
    expect(composant.executionBloqueeParCreditIa).toBe(false);
  });

  it('crédit épuisé : « Exécuter » grisé avec le motif et la date de recharge, aucun appel parti', () => {
    choisirRapportIa(epuise());
    const generer = jest.spyOn(api, 'generateFleetReport');

    expect(executer().disabled).toBe(true);
    expect(el().querySelector('.validation-hint.ai-credit-blocked')!.textContent!.trim())
      .toBe("Crédit IA du mois épuisé (100 %) — il se recharge le 1er octobre. Votre administrateur peut l'augmenter.");

    composant.executeAiFleetReport();
    expect(generer).not.toHaveBeenCalled();
  });

  it('IA coupée pour la société : « Exécuter » grisé, motif affiché', () => {
    choisirRapportIa(credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100 }));

    expect(executer().disabled).toBe(true);
    expect(el().querySelector('.validation-hint.ai-credit-blocked')!.textContent!.trim())
      .toBe("Les fonctions d'IA ne sont pas activées pour votre société.");
  });

  it('rapport généré : la barre suit le crédit joint à la réponse, questions de suivi actives', () => {
    choisirRapportIa();
    jest.spyOn(api, 'generateFleetReport').mockReturnValue(of(rapportFlotte({ credit: credit({ usedTokens: 30000, remainingTokens: 30000, percentUsed: 50 }) })));

    composant.executeAiFleetReport();
    fixture.detectChanges();

    expect(composant.aiFleetCredit!.usedTokens).toBe(30000);
    const barreQa = el().querySelector('.ai-credit-qa [role="progressbar"]') as HTMLElement;
    expect(barreQa.getAttribute('aria-valuenow')).toBe('50');
    expect(Array.from(el().querySelectorAll('.ai-suggestion-btn')).some(b => (b as HTMLButtonElement).disabled)).toBe(false);
  });

  it('refus 429 AI_CREDIT_EXHAUSTED à la génération : message du serveur, barre à 100 %, « Exécuter » grisé', () => {
    choisirRapportIa();
    jest.spyOn(api, 'generateFleetReport').mockReturnValue(throwError(() => ({
      status: 429, error: { code: 'AI_CREDIT_EXHAUSTED', message: MESSAGE_429, credit: epuise() }
    })));

    composant.executeAiFleetReport();
    fixture.detectChanges();

    expect(composant.statisticsData['Erreur']).toBe(MESSAGE_429);
    // Le message du serveur est À L'ÉCRAN, dans la zone du rapport (elle restait vide).
    expect(zoneRapportIa().querySelector('.ai-fleet-erreur')!.textContent).toContain(MESSAGE_429);
    expect(composant.aiFleetCredit!.percentUsed).toBe(100);
    expect(executer().disabled).toBe(true);
  });

  it('relance refusée (429) d’un rapport déjà affiché : message du serveur à l’écran, rapport déjà payé conservé', () => {
    // Barre lue à 95 % : « Exécuter » reste actif ; entre-temps un autre utilisateur épuise le crédit.
    choisirRapportIa(credit({ usedTokens: 57000, remainingTokens: 3000, percentUsed: 95 }));
    const generer = jest.spyOn(api, 'generateFleetReport')
      .mockReturnValueOnce(of(rapportFlotte({ credit: credit({ usedTokens: 57000, remainingTokens: 3000, percentUsed: 95 }) })) as any)
      .mockReturnValueOnce(throwError(() => ({
        status: 429, error: { code: 'AI_CREDIT_EXHAUSTED', message: MESSAGE_429, credit: epuise() }
      })));
    composant.executeAiFleetReport();
    fixture.detectChanges();
    expect(zoneRapportIa().querySelector('.ai-kpi-row')).toBeTruthy();
    expect(zoneRapportIa().querySelector('.ai-fleet-erreur')).toBeNull();

    composant.executeAiFleetReport();
    fixture.detectChanges();

    expect(generer).toHaveBeenCalledTimes(2);
    expect(zoneRapportIa().querySelector('.ai-fleet-erreur')!.textContent).toContain(MESSAGE_429);
    expect(zoneRapportIa().querySelector('.ai-kpi-row')).toBeTruthy();
    expect(composant.aiFleetReport).not.toBeNull();
  });

  it('une génération réussie efface le message d’un refus précédent', () => {
    choisirRapportIa();
    jest.spyOn(api, 'generateFleetReport')
      .mockReturnValueOnce(throwError(() => ({ status: 503, error: { message: 'Service IA indisponible' } })))
      .mockReturnValueOnce(of(rapportFlotte({ credit: credit() })) as any);
    composant.executeAiFleetReport();
    fixture.detectChanges();
    expect(zoneRapportIa().querySelector('.ai-fleet-erreur')!.textContent).toContain('Service IA indisponible');

    composant.executeAiFleetReport();
    fixture.detectChanges();

    expect(zoneRapportIa().querySelector('.ai-fleet-erreur')).toBeNull();
    expect(zoneRapportIa().querySelector('.ai-kpi-row')).toBeTruthy();
  });

  it('crédit épuisé : un clic sur une pastille de période ne fige pas l’écran en chargement et ne relance rien', () => {
    choisirRapportIa(epuise());
    const generer = jest.spyOn(api, 'generateFleetReport');

    // Chemin des relances automatiques (pastille, véhicule) : il passe par executeReport().
    composant.onStandardPeriodChange('week');
    composant.executeReport();
    fixture.detectChanges();

    expect(generer).not.toHaveBeenCalled();
    expect(composant.loading).toBe(false);
    expect(composant.aiFleetLoading).toBe(false);
    // Un autre rapport reste exécutable : l'écran n'est pas bloqué jusqu'au rechargement.
    composant.selectTemplate(composant.templates.find(t => t.type === 'trips')!);
    fixture.detectChanges();
    expect(executer().disabled).toBe(false);
  });

  it('rapport IA flotte : ni pastilles de période standard ni dates personnalisées (sa période est « Période d’analyse »)', () => {
    choisirRapportIa();
    // « Personnalisé » laissé par un rapport GPS précédent : ses dates ne concernent pas ce rapport.
    composant.selectedStandardPeriod = 'custom';
    fixture.detectChanges();

    expect(el().querySelectorAll('.period-row button').length).toBe(0);
    expect(el().querySelector('input[type="date"]')).toBeNull();

    composant.selectTemplate(composant.templates.find(t => t.type === 'trips')!);
    fixture.detectChanges();
    expect(el().querySelectorAll('.period-row button').length).toBeGreaterThan(0);
    expect(el().querySelector('input[type="date"]')).toBeTruthy();
  });

  it('question de suivi refusée (429) : message du serveur dans le fil, envoi grisé', () => {
    choisirRapportIa();
    jest.spyOn(api, 'generateFleetReport').mockReturnValue(of(rapportFlotte({ credit: credit() })));
    composant.executeAiFleetReport();
    jest.spyOn(api, 'askFleetReport').mockReturnValue(throwError(() => ({
      status: 429, error: { code: 'AI_CREDIT_EXHAUSTED', message: MESSAGE_429, credit: epuise() }
    })));

    composant.askAiFleetQuestion('Quel véhicule remplacer ?');
    fixture.detectChanges();

    expect(composant.aiFleetQaMessages.map(m => m.text)).toEqual(['Quel véhicule remplacer ?', MESSAGE_429]);
    expect(Array.from(el().querySelectorAll('.ai-suggestion-btn')).every(b => (b as HTMLButtonElement).disabled)).toBe(true);
    expect(el().querySelector('.ai-qa-card .ai-credit-blocked')).toBeTruthy();

    const demande = jest.spyOn(api, 'askFleetReport');
    demande.mockClear();
    composant.askAiFleetQuestion('Et le carburant ?');
    expect(demande).not.toHaveBeenCalled();
  });

  it('retour sur l’écran : le rapport IA flotte n’est PAS relancé tout seul (il consommerait le crédit sans clic)', () => {
    jest.useFakeTimers();
    try {
      sessionStorage.setItem('reports_state', 'x');   // valeur factice écrasée ci-dessous par le service
      const etat = { selectedTemplateId: composant.templates.find(t => t.type === 'ai-fleet')!.id, reportGenerated: true, selectedStandardPeriod: 'today', aiFleetPeriod: 'month' };
      jest.spyOn((composant as any).reportStateService, 'restore').mockReturnValue(etat);
      jest.spyOn(api, 'getAiCredit').mockReturnValue(of(credit() as any));
      const generer = jest.spyOn(api, 'generateFleetReport').mockReturnValue(of(rapportFlotte() as any));

      (composant as any).restoreState();
      jest.advanceTimersByTime(1000);

      expect(composant.selectedTemplate!.type).toBe('ai-fleet');
      expect(generer).not.toHaveBeenCalled();
      expect(api.getAiCredit).toHaveBeenCalled();   // la barre, elle, est bien affichée
    } finally {
      jest.useRealTimers();
    }
  });

  it('explication d’une tranche refusée : le message du serveur remplace « indisponible »', () => {
    composant.consumptionReport = { segmentKm: 100, segments: [], summary: {} } as any;
    composant.selectedVehicleId = '1';
    jest.spyOn(api, 'explainConsumptionSegment').mockReturnValue(throwError(() => ({
      status: 429, error: { code: 'AI_CREDIT_EXHAUSTED', message: MESSAGE_429, credit: epuise() }
    })));

    composant.explainSegment({ startTime: '2026-09-01T08:00:00Z', endTime: '2026-09-01T10:00:00Z' } as any);

    expect(composant.aiExplanation).toBe(MESSAGE_429);
  });
});
