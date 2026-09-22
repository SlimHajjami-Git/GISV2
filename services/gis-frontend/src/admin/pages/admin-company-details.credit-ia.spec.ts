import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { AdminCompanyDetailsComponent } from './admin-company-details.component';
import { AdminLayoutComponent } from '../components/admin-layout.component';
import { AdminService } from '../services/admin.service';
import { SCAN_CREDIT_INVALID_MESSAGE, SCAN_CREDIT_SEPARATOR_MESSAGE } from './scan-quota.helpers';

/** La vraie mise en page admin relance une horloge chaque seconde : inutile ici. */
@Component({ selector: 'admin-layout', standalone: true, template: '<ng-content></ng-content>' })
class MiseEnPageFactice {}

/**
 * Fiche société admin — crédit IA mensuel (22/09/2026) : le champ « nombre de scans »
 * devient « Crédit IA mensuel (jetons) — scans, assistant, rapports IA », avec la barre de
 * consommation du mois, l'équivalence en scans et la ventilation du mois par fonction
 * (le crédit couvre toute l'IA de la société).
 */
describe('AdminCompanyDetailsComponent — crédit IA mensuel', () => {
  let component: AdminCompanyDetailsComponent;
  let fixture: any;
  let admin: any;

  /** Société encore réglée sur l'ANCIEN quota (50 scans), 15 scans ce mois. */
  const fiche = {
    invoiceScanMonthlyTokens: null,
    invoiceScanMonthlyLimit: 50,
    invoiceScanBudgetTokens: 150000,
    invoiceScanUsedTokens: 45000,
    invoiceScanPercentUsed: 30,
    invoiceScanUsedThisMonth: 15,
    invoiceScanResetsAt: '2026-10-01T00:00:00Z',
    aiCreditByFeature: {
      invoice_scan: 30000, assistant_chat: 12000, vehicle_compare: 0, vehicle_report: 0,
      fleet_report: 3000, fleet_report_ask: 0, consumption_explain: 0, accident_narrative: 0
    }
  };

  const preparer = async () => {
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));
    admin = {
      getClient: jest.fn(() => of({ id: 7, name: 'TransportTest', email: 'contact@transporttest.tn', status: 'active' })),
      getVehicles: jest.fn(() => of([])),
      getCompanyRoles: jest.fn(() => of([])),
      getCompanyUsers: jest.fn(() => of([])),
      getSociete: jest.fn(() => of(fiche)),
      getAdminUser: jest.fn(() => null),
      getBillingOverview: jest.fn(() => of({ count: 0, items: [] })),
      setScanQuota: jest.fn((_id: number, tokens: number | null) => of({
        invoiceScanMonthlyTokens: tokens, budgetTokens: tokens ?? 60000, usedTokens: 45000,
        remainingTokens: Math.max(0, (tokens ?? 60000) - 45000), percentUsed: 50, scansThisMonth: 15,
        resetsAt: '2026-10-01T00:00:00Z',
        byFeature: { invoice_scan: 30000, assistant_chat: 15000 }
      }))
    };

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, AdminCompanyDetailsComponent],
      providers: [
        { provide: AdminService, useValue: admin },
        { provide: ActivatedRoute, useValue: { params: of({ id: '7' }), snapshot: { paramMap: new Map(), queryParamMap: new Map() } } }
      ]
    })
      .overrideComponent(AdminCompanyDetailsComponent, {
        remove: { imports: [AdminLayoutComponent] },
        add: { imports: [MiseEnPageFactice] }
      })
      .compileComponents();

    fixture = TestBed.createComponent(AdminCompanyDetailsComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  };

  afterEach(() => fixture?.destroy());

  const texte = (selecteur: string): string =>
    (fixture.nativeElement.querySelector(selecteur)?.textContent ?? '').replace(/\s+/g, ' ').trim();
  const barre = (): HTMLElement | null =>
    fixture.nativeElement.querySelector('.scan-credit-etat app-credit-ia-bar [role="progressbar"]');

  it('barre de consommation du mois, jetons, scans et date de recharge', async () => {
    await preparer();

    expect(barre()!.getAttribute('aria-valuenow')).toBe('30');
    expect(barre()!.style.width).toBe('160px');
    expect(texte('.scan-credit-etat')).toContain('45 000 / 150 000 jetons ce mois · 15 scans · se recharge le 1er octobre');
    expect(texte('.scan-credit-etat')).toContain('ancien quota : 50 scans');
  });

  it('champ « Crédit IA mensuel (jetons) — scans, assistant, rapports IA » prérempli du budget converti, équivalence en scans dessous', async () => {
    await preparer();

    expect(texte('label.scan-credit-label')).toBe('Crédit IA mensuel (jetons) — scans, assistant, rapports IA');
    expect(component.scanQuotaInput).toBe('150000');
    expect(texte('.scan-credit-equivalence')).toBe('≈ 50 scans par mois');
  });

  it('enregistrer : jetons envoyés, ancien quota oublié, barre redessinée depuis la réponse', async () => {
    await preparer();

    component.scanQuotaInput = 90000;     // un NOMBRE affecté par le code reste accepté
    component.saveScanQuota();
    fixture.detectChanges();

    expect(admin.setScanQuota).toHaveBeenCalledWith(7, 90000);
    expect(component.scanCreditTokens).toBe(90000);
    expect(component.scanCreditLegacy).toBeNull();
    expect(barre()!.getAttribute('aria-valuenow')).toBe('50');
    expect(texte('.scan-credit-etat')).not.toContain('ancien quota');
    expect(texte('.scan-quota-msg')).toBe('Crédit IA enregistré.');
  });

  it('ventilation du mois par fonction : libellés, jetons et part, fonctions à 0 écartées', async () => {
    await preparer();

    const lignes = Array.from(fixture.nativeElement.querySelectorAll('.credit-ventilation li') as NodeListOf<HTMLElement>)
      .map(li => Array.from(li.children).map(e => e.textContent!.trim()).join(' | '));
    expect(lignes).toEqual([
      'Scans de factures | 30 000 | 67 %',
      'Assistant IA | 12 000 | 27 %',
      'Rapports IA flotte | 3 000 | 7 %'
    ]);
    expect(texte('.scan-quota-card h3')).toBe('Crédit IA — scans, assistant, rapports IA');
  });

  it('enregistrer : la ventilation suit la réponse du serveur', async () => {
    await preparer();

    component.scanQuotaInput = '90000';
    component.saveScanQuota();
    fixture.detectChanges();

    const lignes = Array.from(fixture.nativeElement.querySelectorAll('.credit-ventilation li') as NodeListOf<HTMLElement>)
      .map(li => Array.from(li.children).map(e => e.textContent!.trim()).join(' | '));
    expect(lignes).toEqual(['Scans de factures | 30 000 | 67 %', 'Assistant IA | 15 000 | 33 %']);
  });

  it('0 = désactiver (jamais « vide = défaut »), vide = défaut', async () => {
    await preparer();

    component.scanQuotaInput = 0;
    component.saveScanQuota();
    expect(admin.setScanQuota).toHaveBeenLastCalledWith(7, 0);

    component.scanQuotaInput = null;      // champ effacé
    component.saveScanQuota();
    expect(admin.setScanQuota).toHaveBeenLastCalledWith(7, null);
  });

  /** Tape dans le VRAI champ, comme l'administrateur (événement input → ngModel). */
  const saisir = async (texteSaisi: string) => {
    // Écriture initiale du ngModel (promesse résolue) passée avant de taper. Pas de
    // whenStable : la fiche garde des minuteries actives et ne serait jamais « stable ».
    await new Promise(r => setTimeout(r, 0));
    fixture.detectChanges();
    const champ: HTMLInputElement = fixture.nativeElement.querySelector('#scan-credit-input');
    champ.value = texteSaisi;
    champ.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    return champ;
  };

  it('« 150 000 » tapé dans le champ : 150 000 jetons envoyés (un champ number l’aurait réduit à null = défaut)', async () => {
    await preparer();

    const champ = await saisir('150 000');
    expect(champ.type).toBe('text');
    expect(champ.getAttribute('inputmode')).toBe('numeric');
    expect(texte('.scan-credit-equivalence')).toBe('≈ 50 scans par mois');

    component.saveScanQuota();
    expect(admin.setScanQuota).toHaveBeenCalledWith(7, 150000);
  });

  it('« 150.000 » tapé dans le champ : refusé avec un message, jamais 150 jetons ni le défaut', async () => {
    await preparer();

    await saisir('150.000');
    component.saveScanQuota();
    fixture.detectChanges();

    expect(admin.setScanQuota).not.toHaveBeenCalled();
    expect(texte('.scan-quota-msg')).toBe(SCAN_CREDIT_SEPARATOR_MESSAGE);
    expect(texte('.scan-credit-equivalence')).toBe(SCAN_CREDIT_SEPARATOR_MESSAGE);
  });

  it('valeur hors bornes : message, rien n’est envoyé', async () => {
    await preparer();

    component.scanQuotaInput = 10_000_001;
    component.saveScanQuota();
    fixture.detectChanges();

    expect(admin.setScanQuota).not.toHaveBeenCalled();
    expect(texte('.scan-quota-msg')).toBe(SCAN_CREDIT_INVALID_MESSAGE);
  });
});
