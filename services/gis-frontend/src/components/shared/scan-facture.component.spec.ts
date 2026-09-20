import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { ScanFactureComponent, normaliserExtraction } from './scan-facture.component';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';

/**
 * Socle du scan de facture (19/09/2026) : la brique extraite de l'écran Dépenses
 * va servir Entretien, Réparations, Échéances et Carburant. Ce qu'elle promet aux
 * quatre écrans est vérifié ici — forme de l'objet émis, quota, messages d'erreur,
 * et le fichier stocké malgré une panne de l'IA.
 */
describe('ScanFactureComponent — brique partagée', () => {
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
    quota: { used: 4, limit: 20, remaining: 16, resetsAt: '2026-10-01T00:00:00Z' }
  };

  let api: { scanInvoice: jest.Mock; getScanQuota: jest.Mock };

  function creer(connecte = true) {
    api = {
      scanInvoice: jest.fn(() => of(reponse)),
      getScanQuota: jest.fn(() => of({ used: 3, limit: 20, remaining: 17, resetsAt: '2026-10-01T00:00:00Z' }))
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

  beforeEach(() => {
    (window as any).alert = jest.fn();
  });

  it('affiche le bouton, son libellé et le compteur de quota du mois', () => {
    const fixture = creer();
    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('.btn-scan');

    expect(bouton).toBeTruthy();
    expect(bouton.textContent).toContain('Scanner une facture');
    expect(bouton.textContent).toContain('3/20 ce mois');   // scans UTILISÉS, pas restants
    expect(bouton.disabled).toBe(false);
  });

  it('libellé sur mesure et écran pas prêt : bouton grisé avec le motif en infobulle', () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    c.libelle = 'Scanner le ticket';
    c.desactive = true;
    c.raisonDesactivation = 'Choisissez d’abord un véhicule.';
    fixture.detectChanges();

    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('.btn-scan');
    expect(bouton.textContent).toContain('Scanner le ticket');
    expect(bouton.disabled).toBe(true);
    expect(bouton.title).toBe('Choisissez d’abord un véhicule.');
  });

  it('quota épuisé : bouton verrouillé et infobulle qui dit quand il repart', () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    c.quota = { used: 20, limit: 20, remaining: 0, resetsAt: '2026-10-01T00:00:00Z' };
    fixture.detectChanges();

    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('.btn-scan');
    expect(bouton.disabled).toBe(true);
    expect(bouton.title).toContain('Quota mensuel atteint (20/20)');
    expect(bouton.title).toContain('le 1er octobre');
    expect(fixture.nativeElement.querySelector('.scan-quota-chip-empty')).toBeTruthy();
  });

  it('utilisateur non connecté : ni bouton ni appel au quota', () => {
    const fixture = creer(false);
    expect(fixture.nativeElement.querySelector('.btn-scan')).toBeNull();
    expect(api.getScanQuota).not.toHaveBeenCalled();
  });

  it('scan réussi : émet extraction + justificatif + quota, et met à jour le compteur', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    const recu: any[] = [];
    c.scanne.subscribe(r => recu.push(r));

    await c.onFichier({ target: { files: [pdf()], value: 'x' } });

    expect(api.scanInvoice).toHaveBeenCalled();
    expect(c.analyse).toBe(false);
    expect(recu).toHaveLength(1);
    expect(recu[0].receiptUrl).toBe('/uploads/invoices/7/abc.jpg');
    expect(recu[0].quota).toEqual(reponse.quota);
    expect(c.quota!.used).toBe(4);
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

  it('quota atteint : le refus du serveur (429) est repris mot pour mot', async () => {
    const fixture = creer();
    const c = fixture.componentInstance;
    api.scanInvoice.mockReturnValue(throwError(() => ({
      status: 429,
      error: { message: 'Quota mensuel de scans atteint (20/20). Il repart à zéro le 01/10/2026 ; d’ici là, votre administrateur peut augmenter la limite.' }
    })));
    const echecs: any[] = [];
    c.echec.subscribe(e => echecs.push(e));

    await c.onFichier({ target: { files: [pdf()], value: '' } });

    expect(echecs[0].message).toContain('Quota mensuel de scans atteint (20/20)');
    expect(echecs[0].receiptUrl).toBe('');
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
