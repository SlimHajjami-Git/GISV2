import { TestBed } from '@angular/core/testing';
import { CreditIaBarComponent } from './credit-ia-bar.component';
import { CreditIa } from './credit-ia.helpers';

/**
 * Barre « Crédit IA » (22/09/2026) : rendu, couleurs par seuil, accessibilité et
 * infobulle — la même barre sert à côté du bouton de scan, dans l'assistant IA, dans le
 * rapport IA flotte et sur la fiche admin (crédit commun à toute l'IA de la société).
 */
describe('CreditIaBarComponent', () => {
  const credit = (patch: Partial<CreditIa> = {}): CreditIa => ({
    enabled: true, budgetTokens: 60000, usedTokens: 25200, remainingTokens: 34800,
    percentUsed: 42, scansThisMonth: 9, estimatedScansLeft: 11, resetsAt: '2026-10-01T00:00:00Z',
    ...patch
  });

  function rendre(c: CreditIa, entrees: Partial<Pick<CreditIaBarComponent, 'largeur' | 'libelle' | 'infobulle' | 'avecScans'>> = {}) {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ imports: [CreditIaBarComponent] });
    const fixture = TestBed.createComponent(CreditIaBarComponent);
    fixture.componentRef.setInput('credit', c);
    for (const [cle, valeur] of Object.entries(entrees)) fixture.componentRef.setInput(cle, valeur);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  /** Horloge figée au 22/09/2026 : la recharge d'exemple (1er octobre) reste à venir. */
  let horloge: jest.SpyInstance<number, []>;
  beforeEach(() => { horloge = jest.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-09-22T10:00:00Z')); });
  afterEach(() => horloge.mockRestore());

  const piste = (el: HTMLElement) => el.querySelector('[role="progressbar"]') as HTMLElement;
  const remplissage = (el: HTMLElement) => el.querySelector('.credit-ia-remplissage') as HTMLElement;

  it('progressbar accessible : valeur, bornes et libellé', () => {
    const el = rendre(credit());
    const p = piste(el);

    expect(p).toBeTruthy();
    expect(p.getAttribute('aria-valuenow')).toBe('42');
    expect(p.getAttribute('aria-valuemin')).toBe('0');
    expect(p.getAttribute('aria-valuemax')).toBe('100');
    expect(p.getAttribute('aria-label')).toBe('Crédit IA du mois : 42 % utilisé');
  });

  it('piste compacte de 110 px par défaut, remplie au pourcentage, pourcentage affiché à côté', () => {
    const el = rendre(credit());

    expect(piste(el).style.width).toBe('110px');
    expect(remplissage(el).style.width).toBe('42%');
    expect(el.querySelector('.credit-ia-pct')!.textContent!.trim()).toBe('42 %');
    expect(el.querySelector('.credit-ia-libelle')!.textContent).toBe('Crédit IA');
  });

  it('largeur et libellé réglables (fiche admin)', () => {
    const el = rendre(credit(), { largeur: 160, libelle: '' });

    expect(piste(el).style.width).toBe('160px');
    expect(el.querySelector('.credit-ia-libelle')).toBeNull();
  });

  it.each([
    [42, 'niveau-ok'],
    [69, 'niveau-ok'],
    [70, 'niveau-alerte'],
    [89, 'niveau-alerte'],
    [90, 'niveau-critique'],
    [100, 'niveau-critique']
  ])('%i %% : couleur %s', (pourcentage, classe) => {
    const el = rendre(credit({ percentUsed: pourcentage }));
    const r = remplissage(el);

    expect(r.classList.contains(classe)).toBe(true);
    expect(['niveau-ok', 'niveau-alerte', 'niveau-critique'].filter(c => r.classList.contains(c))).toEqual([classe]);
  });

  it('pourcentage hors bornes ramené entre 0 et 100', () => {
    expect(piste(rendre(credit({ percentUsed: 180 }))).getAttribute('aria-valuenow')).toBe('100');
    expect(piste(rendre(credit({ percentUsed: -5 }))).getAttribute('aria-valuenow')).toBe('0');
  });

  it('infobulle standard GÉNÉRIQUE (le crédit couvre toute l’IA), ou celle fournie par l’écran', () => {
    const standard = rendre(credit());
    expect((standard.querySelector('.credit-ia') as HTMLElement).title).toBe(
      '42 % du crédit IA gratuit du mois utilisé (25 200 / 60 000 jetons). Se recharge le 1er octobre.'
    );

    // À côté du bouton de scan seulement : « environ N scans restants ».
    const scan = rendre(credit(), { avecScans: true });
    expect((scan.querySelector('.credit-ia') as HTMLElement).title).toBe(
      '42 % du crédit IA gratuit du mois utilisé (25 200 / 60 000 jetons, environ 11 scans restants). Se recharge le 1er octobre.'
    );

    const fournie = rendre(credit(), { infobulle: 'Consommation de la société' });
    expect((fournie.querySelector('.credit-ia') as HTMLElement).title).toBe('Consommation de la société');
  });

  it('date de recharge passée (écran resté ouvert) : l’infobulle ne l’annonce plus au futur', () => {
    horloge.mockReturnValue(Date.parse('2026-10-01T08:00:00Z'));
    const el = rendre(credit({ usedTokens: 60000, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }));

    expect((el.querySelector('.credit-ia') as HTMLElement).title)
      .toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau utiliser l'IA.");
    const scan = rendre(credit({ usedTokens: 60000, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }), { avecScans: true });
    expect((scan.querySelector('.credit-ia') as HTMLElement).title)
      .toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau scanner.");
  });

  it('fonction désactivée : pas de barre (elle se lirait « épuisé »), mention « IA désactivée »', () => {
    const el = rendre(credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100 }));

    expect(piste(el)).toBeNull();
    expect(el.querySelector('.credit-ia-off')!.textContent!.trim()).toBe('IA désactivée');
  });
});
