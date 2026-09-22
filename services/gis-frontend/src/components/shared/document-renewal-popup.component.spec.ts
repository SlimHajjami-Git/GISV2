import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of } from 'rxjs';

import { DocumentRenewalPopupComponent, VehicleDocument } from './document-renewal-popup.component';
import { normaliserExtraction, ResultatScanFacture } from './scan-facture.component';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';

/**
 * Scan de la quittance sur l'écran Échéances (19/09/2026).
 *
 * Ce que la modale « Renouveler … » doit tenir : proposer sans écraser, dire ce
 * qu'elle a rempli, avertir quand le document ne correspond pas au véhicule ou
 * au type renouvelé, et rester saisissable à la main quoi qu'il arrive (quota
 * atteint, IA en panne). La modale est partagée avec l'écran Documents : le
 * renouvellement lui-même doit continuer à partir tel quel.
 */
describe('DocumentRenewalPopupComponent — scan de la quittance', () => {
  const PLAQUE = '123 TU 4567';

  /** Quittance d'assurance lisible, telle que le serveur la rend. */
  const quittanceAssurance = {
    supplierName: 'STAR ASSURANCES',
    invoiceNumber: 'POL-2026-77',
    date: '2026-09-10',
    amountHT: 400,
    amountTVA: 80,
    amountTTC: 480,
    currency: 'TND',
    category: 'insurance',
    vehiclePlate: PLAQUE,
    description: 'Prime annuelle tous risques',
    confidence: 'high',
    isCreditNote: false,
    items: [] as any[]
  };

  let api: {
    getSuppliers: jest.Mock; createSupplier: jest.Mock; renewDocument: jest.Mock;
    getScanQuota: jest.Mock; scanInvoice: jest.Mock;
  };
  let fixture: ComponentFixture<DocumentRenewalPopupComponent>;
  let composant: DocumentRenewalPopupComponent;

  function document(partiel: Partial<VehicleDocument> = {}): VehicleDocument {
    return {
      vehicleId: 12,
      vehicleName: 'Hilux',
      vehiclePlate: PLAQUE,
      type: 'insurance',
      expiryDate: new Date('2026-09-01T00:00:00Z'),
      reminderDays: 30,
      status: 'expired',
      daysUntilExpiry: -18,
      ...partiel
    } as VehicleDocument;
  }

  /** Ouvre la modale sur un document, comme le fait l'écran Échéances. */
  async function ouvrir(doc: VehicleDocument = document(), quota = { enabled: true, budgetTokens: 60000, usedTokens: 9000, remainingTokens: 51000, percentUsed: 15, scansThisMonth: 3, estimatedScansLeft: 17 }) {
    api = {
      getSuppliers: jest.fn(() => of({
        items: [{ id: 3, name: 'STAR ASSURANCES', type: 'insurance', city: 'Tunis' }],
        totalCount: 1
      })),
      createSupplier: jest.fn(() => of(9)),
      renewDocument: jest.fn(() => of({ costId: 55, message: 'ok' })),
      getScanQuota: jest.fn(() => of(quota)),
      scanInvoice: jest.fn(() => of({ extraction: quittanceAssurance, receiptUrl: '', quota }))
    };

    TestBed.resetTestingModule();
    await TestBed.configureTestingModule({
      imports: [DocumentRenewalPopupComponent, FormsModule, NoopAnimationsModule],
      providers: [
        { provide: ApiService, useValue: api },
        { provide: AuthService, useValue: { getCurrentUserSync: () => ({ id: 1 }) } }
      ]
    }).compileComponents();

    fixture = TestBed.createComponent(DocumentRenewalPopupComponent);
    composant = fixture.componentInstance;
    fixture.componentRef.setInput('isOpen', true);
    fixture.componentRef.setInput('document', doc);
    fixture.detectChanges();
    return fixture;
  }

  /** Résultat émis par <app-scan-facture>, passé par la normalisation de la brique. */
  function resultat(brut: any = quittanceAssurance, receiptUrl = '/uploads/invoices/7/quittance.jpg'): ResultatScanFacture {
    return { extraction: normaliserExtraction(brut), receiptUrl, quota: null };
  }

  const texte = () => fixture.nativeElement.textContent as string;

  /** yyyy-MM-dd décalé de n jours : les attentes ne vieillissent pas avec le calendrier. */
  function jourIso(decalage: number): string {
    const d = new Date();
    d.setDate(d.getDate() + decalage);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
  }

  /** La même date écrite comme sur une quittance française : jj/mm/aaaa. */
  function jourFr(decalage: number): string {
    const [a, m, j] = jourIso(decalage).split('-');
    return `${j}/${m}/${a}`;
  }

  /** Scanne une quittance dont la description porte la mention voulue. */
  function scannerDescription(description: string): void {
    composant.onFactureScannee(resultat({ ...quittanceAssurance, description }));
  }

  beforeEach(() => {
    (window as any).alert = jest.fn();
  });

  it('le bouton de scan est posé hors du formulaire de renouvellement', async () => {
    await ouvrir();
    const bouton = fixture.nativeElement.querySelector('.btn-scan') as HTMLButtonElement;
    expect(bouton).toBeTruthy();
    // Le bouton de la brique n'a pas de type="button" : à l'intérieur du <form>
    // il enregistrerait le renouvellement à chaque clic.
    expect(bouton.closest('form')).toBeNull();
  });

  it('un scan lisible pré-remplit montant, date, n° de police, fournisseur, notes et justificatif', async () => {
    await ouvrir();
    composant.onFactureScannee(resultat());
    fixture.detectChanges();

    expect(composant.formData.amount).toBe(480);
    expect(composant.formData.date).toBe('2026-09-10');
    expect(composant.formData.documentNumber).toBe('POL-2026-77');
    expect(composant.formData.provider).toBe('STAR ASSURANCES');
    expect(composant.formData.notes).toBe('Prime annuelle tous risques');
    expect(composant.formData.documentUrl).toBe('/uploads/invoices/7/quittance.jpg');

    // Ce qui a été rempli est montré, avec la confiance de l'extraction.
    expect(texte()).toContain('Pré-rempli par le scan');
    expect(texte()).toContain('Confiance élevée');
    expect(texte()).toContain('Montant payé');
    expect(composant.scanAvertissements).toEqual([]);
  });

  it('la nouvelle date d\'expiration n\'est PAS déduite de la facture', async () => {
    await ouvrir();
    const calculee = composant.formData.newExpiryDate;   // +1 an, posé à l'ouverture
    composant.onFactureScannee(resultat());
    expect(composant.formData.newExpiryDate).toBe(calculee);
    expect(composant.scanExpirationProposee).toBe('');
  });

  it('une validité écrite sur le document est proposée, jamais appliquée d\'office', async () => {
    await ouvrir();
    const calculee = composant.formData.newExpiryDate;
    const echeance = jourIso(500);
    scannerDescription(`Prime annuelle — valable jusqu'au ${jourFr(500)}`);
    fixture.detectChanges();

    expect(composant.scanExpirationProposee).toBe(echeance);
    expect(composant.formData.newExpiryDate).toBe(calculee);
    expect(texte()).toContain('Validité lue sur le document');

    composant.appliquerExpirationScannee();
    expect(composant.formData.newExpiryDate).toBe(echeance);
  });

  it('une période de validité propose sa FIN, pas son début', async () => {
    await ouvrir();
    // Formulation la plus fréquente d'une attestation d'assurance : deux dates
    // se suivent, et c'est la seconde qui est l'échéance.
    scannerDescription(`Période de validité du ${jourFr(-200)} au ${jourFr(165)}`);

    expect(composant.scanExpirationProposee).toBe(jourIso(165));
  });

  it('une date déjà passée n\'est jamais proposée comme nouvelle échéance', async () => {
    await ouvrir();
    const calculee = composant.formData.newExpiryDate;
    scannerDescription(`Police expirée le ${jourFr(-15)} — à renouveler`);
    fixture.detectChanges();

    // Rien à proposer : le calcul par défaut (+1 an) reste seul maître.
    expect(composant.scanExpirationProposee).toBe('');
    expect(composant.formData.newExpiryDate).toBe(calculee);
    expect(texte()).not.toContain('Validité lue sur le document');
  });

  it('la date du jour n\'est pas une échéance à venir', async () => {
    await ouvrir();
    scannerDescription(`Attestation valable jusqu'au ${jourFr(0)}`);
    expect(composant.scanExpirationProposee).toBe('');
  });

  it('une validité au format ISO ou impossible est traitée correctement', async () => {
    await ouvrir();
    scannerDescription(`Validité jusqu'au ${jourIso(300)}`);
    expect(composant.scanExpirationProposee).toBe(jourIso(300));

    // 31 février : on écarte plutôt que de laisser glisser au 3 mars.
    await ouvrir();
    scannerDescription('Valable jusqu\'au 31/02/2099');
    expect(composant.scanExpirationProposee).toBe('');
  });

  it('une validité portée par une ligne de la quittance est lue aussi', async () => {
    await ouvrir();
    composant.onFactureScannee(resultat({
      ...quittanceAssurance,
      description: 'Prime annuelle tous risques',
      items: [{ label: `Garantie valable jusqu'au ${jourFr(400)}`, amount: 480 }]
    }));

    expect(composant.scanExpirationProposee).toBe(jourIso(400));
  });

  it('un montant déjà saisi n\'est pas écrasé : la valeur lue passe en avertissement', async () => {
    await ouvrir();
    composant.formData.amount = 300;
    composant.onFactureScannee(resultat());
    fixture.detectChanges();

    expect(composant.formData.amount).toBe(300);
    expect(composant.scanAvertissements.join(' ')).toContain('champ déjà saisi');
    expect(texte()).toContain('champ déjà saisi');
  });

  it('une plaque qui ne correspond pas au véhicule renouvelé est signalée', async () => {
    await ouvrir(document({ vehiclePlate: '999 TU 1111' }));
    composant.onFactureScannee(resultat());
    fixture.detectChanges();

    expect(composant.scanAvertissements.join(' ')).toContain('Plaque détectée');
    expect(texte()).toContain('999 TU 1111');
    // Le renouvellement ne change pas de véhicule pour autant.
    expect(composant.formData.vehicleId).toBe(12);
  });

  it('un document d\'une autre nature est signalé sans bloquer la saisie', async () => {
    await ouvrir();
    composant.onFactureScannee(resultat({ ...quittanceAssurance, category: 'fuel' }));
    fixture.detectChanges();

    expect(composant.scanAvertissements.join(' ')).toContain('Carburant');
    expect(texte()).toContain('vérifiez le fichier');
    // Les champs restent modifiables : aucun verrou posé par l'avertissement.
    expect((fixture.nativeElement.querySelector('#amount') as HTMLInputElement).disabled).toBe(false);
  });

  it('un fournisseur inconnu est signalé et pré-remplit « + Ajouter »', async () => {
    await ouvrir();
    composant.onFactureScannee(resultat({ ...quittanceAssurance, supplierName: 'MAGHREBIA' }));
    fixture.detectChanges();

    expect(composant.formData.provider).toBe('');
    expect(composant.scanFournisseurIntrouvable).toBe('MAGHREBIA');
    expect(texte()).toContain('absent de la liste');

    composant.toggleNewSupplierForm();
    expect(composant.newSupplier.name).toBe('MAGHREBIA');
  });

  it('crédit IA épuisé : le bouton est verrouillé mais le formulaire reste utilisable', async () => {
    await ouvrir(document(), { enabled: true, budgetTokens: 60000, usedTokens: 60000, remainingTokens: 0, percentUsed: 100, scansThisMonth: 20, estimatedScansLeft: 0 });

    const bouton = fixture.nativeElement.querySelector('.btn-scan') as HTMLButtonElement;
    expect(bouton.disabled).toBe(true);
    expect(bouton.getAttribute('title')).toContain('Crédit IA du mois épuisé');

    // Saisie à la main puis enregistrement : rien n'est bloqué.
    expect((fixture.nativeElement.querySelector('#amount') as HTMLInputElement).disabled).toBe(false);
    composant.formData.amount = 480;
    composant.formData.date = '2026-09-10';
    composant.onSubmit();
    expect(api.renewDocument).toHaveBeenCalledTimes(1);
  });

  it('IA en panne mais fichier stocké : le justificatif est rattaché pour une saisie à la main', async () => {
    await ouvrir();
    composant.onEchecScan({ message: 'Analyse indisponible', receiptUrl: '/uploads/invoices/7/panne.pdf' });
    fixture.detectChanges();

    expect(composant.formData.documentUrl).toBe('/uploads/invoices/7/panne.pdf');
    expect(composant.formData.amount).toBe(0);
    expect(texte()).toContain('saisissez les informations à la main');
  });

  it('le justificatif scanné part avec le renouvellement (appelants inchangés)', async () => {
    await ouvrir();
    composant.onFactureScannee(resultat());
    composant.onSubmit();

    expect(api.renewDocument).toHaveBeenCalledWith(12, expect.objectContaining({
      vehicleId: 12,
      documentType: 'insurance',
      amount: 480,
      paymentDate: '2026-09-10',
      documentNumber: 'POL-2026-77',
      provider: 'STAR ASSURANCES',
      documentUrl: '/uploads/invoices/7/quittance.jpg'
    }));
  });

  it('rouvrir la modale sur un autre document efface le récapitulatif précédent', async () => {
    await ouvrir();
    composant.onFactureScannee(resultat());
    expect(composant.scanFait).toBe(true);

    fixture.componentRef.setInput('document', document({ vehicleId: 20, type: 'tax' }));
    fixture.detectChanges();

    expect(composant.scanFait).toBe(false);
    expect(composant.formData.documentUrl).toBe('');
    expect(texte()).not.toContain('Pré-rempli par le scan');
  });
});
