import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ScanFactureComponent, normaliserExtraction } from './scan-facture.component';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';

/**
 * Socle du scan de facture (19/09/2026) : la brique extraite de l'écran Dépenses
 * va servir Entretien, Réparations, Échéances et Carburant. Ce qu'elle promet aux
 * quatre écrans est vérifié ici — forme de l'objet émis, crédit IA, messages d'erreur,
 * et le fichier stocké malgré une panne de l'IA.
 *
 * Depuis le 22/09/2026, la pastille « 12/20 ce mois » est remplacée par une barre
 * « Crédit IA » (jetons consommés sur le crédit gratuit du mois) — crédit commun à toute
 * l'IA de la société (scans, assistant, rapports IA).
 */
describe('ScanFactureComponent — brique partagée', () => {
  /** Crédit tel que le renvoie GET /api/costs/scan-quota. */
  const credit = (patch: Record<string, unknown> = {}) => ({
    enabled: true, budgetTokens: 60000, usedTokens: 25200, remainingTokens: 34800,
    percentUsed: 42, scansThisMonth: 9, estimatedScansLeft: 11, resetsAt: '2026-10-01T00:00:00Z',
    ...patch
  });

  const reponse = {
    extraction: {
      supplierName: 'STATION AGIL',
      invoiceNumber: 'T-9912',
      date: '2026-09-12',
      amountHT: 88.06,
      amountTVA: 16.85,
      amountTTC: 104.91,
      currency: 'TND',
      category: 'fuel',
      vehiclePlate: '123 TU 4567',
      description: 'Gasoil sans soufre',
      confidence: 'high',
      liters: 41.55,
      pricePerLiter: 2.525,
      isCreditNote: false,
      items: [{ label: 'Gasoil', amount: 104.91, category: 'fuel' }]
    },
    receiptUrl: '/uploads/invoices/7/abc.jpg',
    quota: credit({ usedTokens: 28600, remainingTokens: 31400, percentUsed: 47, scansThisMonth: 10, estimatedScansLeft: 10 })
  };

  let api: { scanInvoice: jest.Mock; getScanQuota: jest.Mock };

  function creer(connecte = true, creditInitial: unknown = credit()) {
    api = {
      scanInvoice: jest.fn(() => of(reponse)),
      getScanQuota: jest.fn(() => of(creditInitial))
    };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [ScanFactureComponent],
      providers: [
        { provide: ApiService, useValue: api },
        { provide: AuthService, useValue: { getCurrentUserSync: () => (connecte ? { id: 1 } : null) } }
      ]
    });
    const fixture = TestBed.createComponent(ScanFactureComponent);
    fixture.detectChanges();
    return fixture;
  }

  const pdf = () => new File(['facture'], 'facture.pdf', { type: 'application/pdf' });
  const bouton = (f: any): HTMLButtonElement => f.nativeElement.querySelector('.btn-scan');
  const barre = (f: any): HTMLElement | null => f.nativeElement.querySelector('app-credit-ia-bar [role="progressbar"]');

  /** Horloge du poste figée au 22/09/2026 : la date de recharge des crédits d'exemple
   *  (1er octobre) est à venir, quel que soit le jour où la suite tourne. */
  let horloge: jest.SpyInstance<number, []>;

  beforeEach(() => {
    (window as any).alert = jest.fn();
    horloge = jest.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-09-22T10:00:00Z'));
  });

  afterEach(() => {
    jest.useRealTimers();
    horloge.mockRestore();
  });

  it('affiche le bouton et, à côté, la barre « Crédit IA » du mois — plus la pastille « x/20 »', () => {
    const fixture = creer();
    const b = bouton(fixture);

    expect(b).toBeTruthy();
    expect(b.textContent).toContain('Scanner une facture');
    expect(b.textContent).not.toContain('ce mois');
    expect(fixture.nativeElement.querySelector('.scan-quota-chip')).toBeNull();
    expect(b.disabled).toBe(false);

    const piste = barre(fixture)!;
    expect(piste).toBeTruthy();
    expect(b.contains(piste)).toBe(false);   // hors du bouton : le rôle progressbar y serait perdu
    expect(piste.getAttribute('aria-valuenow')).toBe('42');
    expect(fixture.nativeElement.querySelector('.credit-ia-pct').textContent.trim()).toBe('42 %');
  });

  it('infobulle du bouton : pourcentage, jetons, scans restants et date de recharge', () => {
    const fixture = creer();
    expect(bouton(fixture).title).toBe(
      '42 % du crédit IA gratuit du mois utilisé (25 200 / 60 000 jetons, environ 11 scans restants). Se recharge le 1er octobre.'
    );
  });

  it('libellé sur mesure et écran pas prêt : bouton grisé avec le motif en infobulle', () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    c.libelle = 'Scanner le ticket';
    c.desactive = true;
    c.raisonDesactivation = 'Choisissez d’abord un véhicule.';
    fixture.detectChanges();

    const b = bouton(fixture);
    expect(b.textContent).toContain('Scanner le ticket');
    expect(b.disabled).toBe(true);
    expect(b.title).toBe('Choisissez d’abord un véhicule.');
  });

  it('crédit épuisé (100 %) : bouton verrouillé, barre rouge et infobulle qui dit quand il se recharge', () => {
    const fixture = creer(true, credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, scansThisMonth: 20, estimatedScansLeft: 0 }));

    const b = bouton(fixture);
    expect(b.disabled).toBe(true);
    expect(b.title).toContain('Crédit IA du mois épuisé (100 %)');
    expect(b.title).toContain('le 1er octobre');
    expect(barre(fixture)!.getAttribute('aria-valuenow')).toBe('100');
    expect(fixture.nativeElement.querySelector('.credit-ia-remplissage.niveau-critique')).toBeTruthy();
  });

  it('crédit épuisé le 30, écran resté ouvert : passé la date de recharge, le bouton se dégrise sans recharger la page', () => {
    const epuise = credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, scansThisMonth: 20, estimatedScansLeft: 0 });
    horloge.mockReturnValue(Date.parse('2026-09-30T18:00:00Z'));
    const fixture = creer(true, epuise);
    expect(bouton(fixture).disabled).toBe(true);

    // Le 1er octobre à 8 h UTC (relecture pas encore arrivée : poste en veille, réseau coupé…).
    horloge.mockReturnValue(Date.parse('2026-10-01T08:00:00Z'));
    fixture.detectChanges();

    const b = bouton(fixture);
    expect(b.disabled).toBe(false);
    expect(b.title).toBe("Le crédit IA s'est rechargé le 1er octobre : vous pouvez de nouveau scanner.");
    expect(b.title).not.toContain('il se recharge');
  });

  it('fonction désactivée : reste grisée au changement de mois (réglage de l’admin, pas un compteur)', () => {
    const ferme = credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 });
    horloge.mockReturnValue(Date.parse('2026-10-02T08:00:00Z'));
    const fixture = creer(true, ferme);

    expect(bouton(fixture).disabled).toBe(true);
  });

  it('écran ouvert au changement de mois : le crédit est relu juste après la recharge, barre remise à 0 %', () => {
    jest.useFakeTimers({ now: Date.parse('2026-09-30T23:50:00Z') });
    const fixture = creer(true, credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, scansThisMonth: 20, estimatedScansLeft: 0 }));
    expect(bouton(fixture).disabled).toBe(true);
    expect(api.getScanQuota).toHaveBeenCalledTimes(1);

    api.getScanQuota.mockReturnValue(of(credit({
      usedTokens: 0, remainingTokens: 60000, percentUsed: 0, scansThisMonth: 0, estimatedScansLeft: 20,
      resetsAt: '2026-11-01T00:00:00Z'
    })));
    jest.advanceTimersByTime(9 * 60_000);          // 23:59 : pas encore
    expect(api.getScanQuota).toHaveBeenCalledTimes(1);

    jest.advanceTimersByTime(2 * 60_000);          // 00:01 : relu
    fixture.detectChanges();

    expect(api.getScanQuota).toHaveBeenCalledTimes(2);
    expect(bouton(fixture).disabled).toBe(false);
    expect(barre(fixture)!.getAttribute('aria-valuenow')).toBe('0');
  });

  it('crédit épuisé tôt dans le mois (recharge à plus de 24,8 jours) : aucune relecture en boucle', () => {
    // setTimeout déclenche AUSSITÔT au-delà de 2^31-1 ms : sans borne, relecture immédiate
    // et sans fin.
    jest.useFakeTimers({ now: Date.parse('2026-09-02T00:00:00Z') });
    const planifie = jest.spyOn(globalThis, 'setTimeout');
    const fixture = creer(true, credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }));

    // Vérifié AVANT d'avancer l'horloge : sans borne, la boucle ne rendrait jamais la main.
    const delais = planifie.mock.calls.map(appel => Number(appel[1] ?? 0));
    expect(Math.max(...delais)).toBeLessThanOrEqual(2 ** 31 - 1);
    planifie.mockRestore();

    jest.advanceTimersByTime(25 * 24 * 3600_000);  // 27 septembre
    expect(api.getScanQuota).toHaveBeenCalledTimes(1);
    expect(bouton(fixture).disabled).toBe(true);

    jest.advanceTimersByTime(4 * 24 * 3600_000 + 60_000);   // 1er octobre, 00:01
    expect(api.getScanQuota).toHaveBeenCalledTimes(2);
  });

  it('écran fermé avant la recharge : la relecture prévue est annulée', () => {
    jest.useFakeTimers({ now: Date.parse('2026-09-30T23:50:00Z') });
    const fixture = creer(true, credit({ usedTokens: 61200, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }));

    fixture.destroy();
    jest.advanceTimersByTime(60 * 60_000);

    expect(api.getScanQuota).toHaveBeenCalledTimes(1);
  });

  it('à 99 % il reste des jetons : le bouton reste actif (le serveur accepte encore un scan)', () => {
    const fixture = creer(true, credit({ usedTokens: 59999, remainingTokens: 1, percentUsed: 99, estimatedScansLeft: 0 }));
    expect(bouton(fixture).disabled).toBe(false);
    expect(bouton(fixture).title).toContain("moins d'un scan restant");
  });

  it('fonction désactivée pour la société : bouton verrouillé, pas de barre, mention « IA désactivée »', () => {
    const fixture = creer(true, credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }));

    expect(bouton(fixture).disabled).toBe(true);
    expect(bouton(fixture).title).toBe("Les fonctions d'IA ne sont pas activées pour votre société.");
    expect(barre(fixture)).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('IA désactivée');
  });

  it('API pas encore redéployée (ancienne réponse { used, limit }) : pas de barre, bouton utilisable', () => {
    const fixture = creer(true, { used: 3, limit: 20, remaining: 17 });

    expect(fixture.componentInstance.quota).toBeNull();
    expect(barre(fixture)).toBeNull();
    expect(bouton(fixture).disabled).toBe(false);
  });

  it('utilisateur non connecté : ni bouton ni appel au crédit', () => {
    const fixture = creer(false);
    expect(fixture.nativeElement.querySelector('.btn-scan')).toBeNull();
    expect(api.getScanQuota).not.toHaveBeenCalled();
  });

  it('scan réussi : émet extraction + justificatif + crédit, et met la barre à jour', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    const recu: any[] = [];
    c.scanne.subscribe(r => recu.push(r));

    await c.onFichier({ target: { files: [pdf()], value: 'x' } });
    fixture.detectChanges();

    expect(api.scanInvoice).toHaveBeenCalled();
    expect(c.analyse).toBe(false);
    expect(recu).toHaveLength(1);
    expect(recu[0].receiptUrl).toBe('/uploads/invoices/7/abc.jpg');
    expect(recu[0].quota).toEqual(reponse.quota);
    expect(c.quota!.usedTokens).toBe(28600);
    expect(barre(fixture)!.getAttribute('aria-valuenow')).toBe('47');
    // Carburant : volume et prix au litre traversent la brique tels quels.
    expect(recu[0].extraction.liters).toBe(41.55);
    expect(recu[0].extraction.pricePerLiter).toBe(2.525);
    expect(recu[0].extraction.total).toBe(104.91);
    expect(recu[0].extraction.descriptionComplete).toBe('STATION AGIL — Gasoil sans soufre');
  });

  it('fichier trop volumineux : message explicite, aucun scan émis', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    api.scanInvoice.mockReturnValue(throwError(() => ({ status: 413 })));
    const echecs: any[] = [];
    const succes: any[] = [];
    c.echec.subscribe(e => echecs.push(e));
    c.scanne.subscribe(r => succes.push(r));

    await c.onFichier({ target: { files: [pdf()], value: '' } });

    expect(succes).toHaveLength(0);
    expect(echecs[0].message).toContain('Fichier trop volumineux (maximum 12 Mo)');
    expect(window.alert).toHaveBeenCalledWith(echecs[0].message);
    expect(c.analyse).toBe(false);
  });

  it('panne de l’IA : le motif du serveur est affiché et le fichier stocké est transmis', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    api.scanInvoice.mockReturnValue(throwError(() => ({
      status: 502,
      error: { message: 'L’analyse de la facture a échoué (service IA momentanément indisponible).', receiptUrl: '/uploads/invoices/7/def.pdf' }
    })));
    const echecs: any[] = [];
    c.echec.subscribe(e => echecs.push(e));

    await c.onFichier({ target: { files: [pdf()], value: '' } });

    expect(echecs[0].message).toContain('service IA momentanément indisponible');
    expect(echecs[0].receiptUrl).toBe('/uploads/invoices/7/def.pdf');
  });

  it('crédit épuisé entre-temps (429) : message repris mot pour mot, barre à 100 % et bouton grisé', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    const message = 'Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l\'augmenter.';
    api.scanInvoice.mockReturnValue(throwError(() => ({
      status: 429,
      error: { message, quota: credit({ usedTokens: 60500, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }) }
    })));
    const echecs: any[] = [];
    c.echec.subscribe(e => echecs.push(e));

    await c.onFichier({ target: { files: [pdf()], value: '' } });
    fixture.detectChanges();

    expect(echecs[0].message).toBe(message);
    expect(echecs[0].receiptUrl).toBe('');
    expect(barre(fixture)!.getAttribute('aria-valuenow')).toBe('100');
    expect(bouton(fixture).disabled).toBe(true);
  });

  it('scan réussi, réponse au contrat commun à toute l’IA (« credit ») : la barre suit', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    const apres = credit({ usedTokens: 33000, remainingTokens: 27000, percentUsed: 55, estimatedScansLeft: 9, byFeature: { invoice_scan: 9000, assistant_chat: 24000 } });
    api.scanInvoice.mockReturnValue(of({ extraction: reponse.extraction, receiptUrl: '/uploads/invoices/7/x.jpg', credit: apres }));

    await c.onFichier({ target: { files: [pdf()], value: '' } });
    fixture.detectChanges();

    expect(c.quota!.usedTokens).toBe(33000);
    expect(c.quota!.byFeature).toEqual({ invoice_scan: 9000, assistant_chat: 24000 });
    expect(barre(fixture)!.getAttribute('aria-valuenow')).toBe('55');
  });

  it('refus 429 AI_CREDIT_EXHAUSTED (crédit consommé par l’assistant) : message du serveur, barre à 100 %, bouton grisé', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    const message = 'Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l\'augmenter.';
    api.scanInvoice.mockReturnValue(throwError(() => ({
      status: 429,
      error: { code: 'AI_CREDIT_EXHAUSTED', message, credit: credit({ usedTokens: 61000, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }) }
    })));
    const echecs: any[] = [];
    c.echec.subscribe(e => echecs.push(e));

    await c.onFichier({ target: { files: [pdf()], value: '' } });
    fixture.detectChanges();

    expect(window.alert).toHaveBeenCalledWith(message);
    expect(echecs[0].message).toBe(message);
    expect(barre(fixture)!.getAttribute('aria-valuenow')).toBe('100');
    expect(bouton(fixture).disabled).toBe(true);
  });

  it('refus 403 AI_CREDIT_DISABLED : message du serveur, bouton verrouillé', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    const message = "Les fonctions d'IA ne sont pas activées pour votre société.";
    api.scanInvoice.mockReturnValue(throwError(() => ({
      status: 403,
      error: { code: 'AI_CREDIT_DISABLED', message, credit: credit({ enabled: false, budgetTokens: 0, usedTokens: 0, remainingTokens: 0, percentUsed: 100, estimatedScansLeft: 0 }) }
    })));

    await c.onFichier({ target: { files: [pdf()], value: '' } });
    fixture.detectChanges();

    expect(window.alert).toHaveBeenCalledWith(message);
    expect(bouton(fixture).disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('IA désactivée');
  });

  it('aucun fichier choisi : rien ne part au serveur', async () => {
    const fixture = creer();
    await fixture.componentInstance.onFichier({ target: { files: [], value: '' } });
    expect(api.scanInvoice).not.toHaveBeenCalled();
  });
});

describe('normaliserExtraction — forme émise aux écrans', () => {
  it('champs illisibles rendus null, jamais une chaîne vide déguisée', () => {
    const x = normaliserExtraction({ amountTTC: 120, category: 'repair' });

    expect(x.supplierName).toBeNull();
    expect(x.invoiceNumber).toBeNull();
    expect(x.date).toBeNull();
    expect(x.vehiclePlate).toBeNull();
    expect(x.liters).toBeNull();
    expect(x.pricePerLiter).toBeNull();
    expect(x.descriptionComplete).toBe('');
    expect(x.items).toEqual([]);
    expect(x.isCreditNote).toBe(false);
    expect(x.total).toBe(120);
  });

  it('réponse vide : aucune exception, tout est null', () => {
    const x = normaliserExtraction(undefined);
    expect(x.total).toBeNull();
    expect(x.items).toEqual([]);
  });

  it('total négatif d’une API ancienne : avoir fournisseur, montants remis positifs', () => {
    const x = normaliserExtraction({
      amountHT: -100, amountTVA: -19, amountTTC: -119, category: 'repair',
      items: [{ label: 'Retour pièce', amount: -119, category: 'repair' }]
    });

    expect(x.isCreditNote).toBe(true);
    expect(x.total).toBe(119);
    expect(x.amountHT).toBe(100);
    expect(x.amountTTC).toBe(119);
    expect(x.items[0].amount).toBe(119);
    expect(x.category).toBe('repair');     // la catégorie d'écran reste au choix de l'écran
  });

  it('avoir aux lignes imprimées en positif : les lignes ne changent pas de signe', () => {
    const x = normaliserExtraction({
      amountTTC: 119, isCreditNote: true,
      items: [{ label: 'Retour pièce', amount: 119, category: 'repair' }]
    });

    expect(x.isCreditNote).toBe(true);
    expect(x.items[0].amount).toBe(119);
  });

  it('lignes : vides écartées, catégorie héritée de la facture, 30 au maximum', () => {
    const x = normaliserExtraction({
      category: 'maintenance',
      amountTTC: 500,
      items: [
        { label: 'Vidange', amount: 95 },
        { label: '', amount: 0 },
        { amount: 18.5, category: 'repair' },
        ...Array.from({ length: 40 }, (_, i) => ({ label: 'Ligne ' + i, amount: 1 }))
      ]
    });

    expect(x.items).toHaveLength(29);              // 30 lues, la ligne vide écartée
    expect(x.items[0]).toEqual({ label: 'Vidange', amount: 95, category: 'maintenance' });
    expect(x.items[1]).toEqual({ label: '', amount: 18.5, category: 'repair' });
  });

  it('litres et prix au litre : nombres, chaînes à virgule, ou null', () => {
    expect(normaliserExtraction({ liters: 41.55, pricePerLiter: 2.525 }).liters).toBe(41.55);
    expect(normaliserExtraction({ liters: '41,55' }).liters).toBe(41.55);
    expect(normaliserExtraction({ pricePerLiter: 'illisible' }).pricePerLiter).toBeNull();
    expect(normaliserExtraction({ liters: null }).liters).toBeNull();
  });

  it('total : TTC d’abord, HT à défaut', () => {
    expect(normaliserExtraction({ amountTTC: 119, amountHT: 100 }).total).toBe(119);
    expect(normaliserExtraction({ amountHT: 100 }).total).toBe(100);
  });
});
