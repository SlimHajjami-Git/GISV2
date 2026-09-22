import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { MaintenanceTemplatesComponent } from './maintenance-templates.component';
import { ApiService } from '../services/api.service';
import { PermissionService } from '../services/permission.service';
import { ResultatScanFacture } from './shared/scan-facture.component';

/**
 * Scan de facture branché sur « Entretien effectué » (19/09/2026).
 *
 * La brique partagée <app-scan-facture> envoie le document, décompte le quota et
 * affiche les erreurs ; cet écran ne décide que du remplissage de SES champs.
 * Ce qui est vérifié ici : les bons champs pré-remplis, rien d'écrasé de ce que
 * l'utilisateur a tapé, la plaque qui ne correspond pas signalée sans bloquer,
 * et un scan refusé (quota atteint) qui laisse le formulaire utilisable.
 */
describe('MaintenanceTemplatesComponent — scan de facture', () => {
  let component: MaintenanceTemplatesComponent;
  let api: ApiService;

  const modeles = [
    { id: '1', name: 'Vidange moteur', description: '', intervalKm: 10000, intervalMonths: 12, estimatedCost: 150, priority: 'medium' as const, category: 'Moteur', isActive: true },
    { id: '2', name: 'Plaquettes de frein', description: '', intervalKm: 30000, intervalMonths: null, estimatedCost: 200, priority: 'high' as const, category: 'Freinage', isActive: true },
  ];

  const vehicule = {
    vehicleId: '49', vehicleName: 'Camion 12', vehiclePlate: '123 TU 4567', currentMileage: 80000,
    maintenanceItems: [
      { scheduleId: 11, templateId: '1', templateName: 'Vidange moteur', lastDoneDate: null, lastDoneKm: null, nextDueKm: 90000, status: 'due' as const, kmUntilDue: 200, isPaused: false },
      { scheduleId: 12, templateId: '2', templateName: 'Plaquettes de frein', lastDoneDate: null, lastDoneKm: null, nextDueKm: 110000, status: 'ok' as const, kmUntilDue: 30000, isPaused: false },
    ]
  };

  /** Facture de garage type : deux prestations reconnues, plus la main d'œuvre. */
  function facture(surcharge: any = {}): ResultatScanFacture {
    return {
      extraction: {
        supplierName: 'GARAGE EL MOTORS SARL',
        invoiceNumber: 'FA-2026-0042',
        date: '2026-09-15',
        amountHT: 288.240, amountTVA: 54.760, amountTTC: 343.000,
        total: 343, currency: 'TND', category: 'maintenance',
        vehiclePlate: '123 TU 4567',
        description: 'Vidange et freins',
        descriptionComplete: 'GARAGE EL MOTORS SARL — Vidange et freins',
        confidence: 'high', liters: null, pricePerLiter: null, isCreditNote: false,
        items: [
          { label: 'Vidange moteur 10W40', amount: 120, category: 'maintenance' },
          { label: 'Plaquettes de frein avant', amount: 180, category: 'maintenance' },
          { label: 'Main d\'oeuvre', amount: 43, category: 'maintenance' },
        ],
        ...surcharge
      },
      receiptUrl: '/uploads/invoices/7/facture.jpg',
      quota: { enabled: true, budgetTokens: 60000, usedTokens: 15000, remainingTokens: 45000, percentUsed: 25, scansThisMonth: 5, estimatedScansLeft: 15 }
    };
  }

  beforeEach(async () => {
    // L'en-tête de l'écran (app-layout) instancie le service d'export PDF, qui
    // précharge le logo par fetch() — absent de jsdom. L'échec est déjà prévu côté
    // service, il suffit de lui répondre.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, FormsModule, MaintenanceTemplatesComponent],
      providers: [ApiService],
    }).compileComponents();

    const fixture = TestBed.createComponent(MaintenanceTemplatesComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);
    TestBed.inject(PermissionService);

    // La modale n'est jamais rendue ici : son gabarit embarque <app-layout>, qui
    // appartient à un autre lot et réclame toute l'application. On teste la logique
    // de remplissage, pas le rendu — le cycle de détection est donc neutralisé.
    (component as any).cdr = { detectChanges: () => {} };

    jest.spyOn(api, 'getSuppliers').mockReturnValue(of({
      items: [{ id: 5, name: 'Garage El Motors', city: 'Tunis' }], totalCount: 1, page: 1, pageSize: 500
    }) as any);

    component.templates = modeles as any;
    component.vehicleSchedules = [vehicule] as any;
    component.canUseSuppliers = true;
  });

  function ouvrirEntretien() {
    component.openMarkDone(vehicule as any, vehicule.maintenanceItems[0] as any);
  }

  it('scan réussi : date, fournisseur, lignes et notes de la facture sont proposés', () => {
    ouvrirEntretien();
    component.onFactureScannee(facture());

    expect(component.markData.date).toBe('2026-09-15');
    // Fournisseur rapproché par le nom, jamais créé.
    expect(component.markData.supplierId).toBe(5);

    const lignes = component.markData.invoiceLines;
    expect(lignes.length).toBe(2);
    // La ligne de l'entretien coché absorbe la main d'œuvre : 120 + 43.
    expect(lignes[0].templateId).toBe('1');
    expect(lignes[0].price).toBe(163);
    expect(lignes[1].templateId).toBe('2');
    expect(lignes[1].price).toBe(180);
    // Le Total saisi retombe sur le total lu : aucun dinar perdu.
    expect(component.getInvoiceTotal()).toBe(343);
    expect(component.scanTotalDiffere()).toBe(false);

    // Ni champ « n° de facture » ni justificatif dans ce formulaire : reporté en Notes.
    expect(component.markData.notes).toContain('FA-2026-0042');
    expect(component.markData.notes).toContain('Main d\'oeuvre');

    expect(component.scanLu!.confiance).toBe('high');
    expect(component.scanConfianceLabel()).toBe('élevée');
    expect(component.scanLu!.receiptUrl).toBe('/uploads/invoices/7/facture.jpg');
    expect(component.scanLu!.plaqueDifferente).toBe(false);
    expect(component.isMarkValid()).toBe(true);
  });

  it('sans détail de lignes : le total de la facture va sur l’entretien coché', () => {
    ouvrirEntretien();
    component.onFactureScannee(facture({ items: [] }));

    expect(component.markData.invoiceLines.length).toBe(1);
    expect(component.markData.invoiceLines[0].price).toBe(343);
    expect(component.scanLu!.lignesNonRapprochees.length).toBe(0);
  });

  /**
   * Le scan propose, il n'invente pas. Quand les lignes reconnues couvrent déjà le
   * total lu, le reste tombe à 0 : écrire ce 0 dans le champ prix enregistrait un
   * entretien à 0 sans que rien ne le signale. Le champ doit rester VIDE — une
   * ligne sans prix n'est même pas envoyée au serveur.
   */
  it('reste nul : le prix de l’entretien coché reste vide, jamais 0', () => {
    ouvrirEntretien();
    component.onFactureScannee(facture({
      total: 343, amountTTC: 343,
      items: [{ label: 'Plaquettes de frein avant', amount: 343, category: 'maintenance' }]
    }));

    const lignes = component.markData.invoiceLines;
    // Rien sur la facture ne chiffre la vidange : le montant reste à saisir.
    expect(lignes[0].templateId).toBe('1');
    expect(lignes[0].price).toBeNull();
    expect(component.scanLu!.entretienASaisir).toBe(true);
    // La ligne reconnue, elle, est bien chiffrée.
    expect(lignes[1].templateId).toBe('2');
    expect(lignes[1].price).toBe(343);
  });

  it('total inférieur aux lignes lues : aucun montant ne redescend, aucun 0 inventé', () => {
    // a) Remise sur la facture : le montant déjà rapproché (120) est conservé.
    ouvrirEntretien();
    component.onFactureScannee(facture({
      total: 250, amountTTC: 250,
      items: [
        { label: 'Vidange moteur 10W40', amount: 120, category: 'maintenance' },
        { label: 'Plaquettes de frein avant', amount: 180, category: 'maintenance' },
      ]
    }));
    expect(component.markData.invoiceLines[0].price).toBe(120);
    expect(component.scanLu!.entretienASaisir).toBe(false);

    // b) Reste négatif et champ vide : il reste vide, à l'utilisateur de trancher.
    ouvrirEntretien();
    component.onFactureScannee(facture({
      total: 350, amountTTC: 350,
      items: [{ label: 'Plaquettes de frein avant', amount: 400, category: 'maintenance' }]
    }));
    expect(component.markData.invoiceLines[0].price).toBeNull();
    expect(component.scanLu!.entretienASaisir).toBe(true);
  });

  /**
   * Deux causes très différentes étaient annoncées sous le même libellé « sans
   * modèle d'entretien » : la main d'œuvre, que rien ne peut enregistrer, et une
   * prestation dont le modèle est bien reconnu mais déjà chiffrée à l'écran.
   */
  it('lignes non placées : « aucun modèle » et « déjà chiffré » sont annoncés séparément', () => {
    ouvrirEntretien();
    // L'utilisateur a chiffré les plaquettes lui-même avant de scanner.
    component.addInvoiceLine();
    const ligneFreins = component.markData.invoiceLines[1];
    component.onLineTemplateChange(ligneFreins, '2');
    ligneFreins.price = 200;

    component.onFactureScannee(facture());

    // Main d'œuvre : aucun modèle ne la reconnaît, le montant n'a nulle part où aller.
    expect(component.scanLu!.lignesNonRapprochees.map(l => l.label)).toEqual(['Main d\'oeuvre']);
    // Plaquettes : le modèle existe, la ligne est déjà chiffrée — rien ne manque.
    expect(component.scanLu!.lignesDejaChiffrees.map(l => l.label)).toEqual(['Plaquettes de frein avant']);

    // La saisie de l'utilisateur n'est pas touchée, le montant lu est rappelé en Notes.
    expect(ligneFreins.price).toBe(200);
    expect(component.markData.notes).toContain('montant du détail conservé');
    // Le reste (343 - 200) revient à l'entretien coché : le total saisi retombe juste.
    expect(component.markData.invoiceLines[0].price).toBe(143);
    expect(component.getInvoiceTotal()).toBe(343);
    expect(component.scanTotalDiffere()).toBe(false);
    expect(component.scanLu!.resteAffecte).toBe(true);
  });

  it('saisie de l’utilisateur : ni la date ni le montant tapés ne sont écrasés', () => {
    ouvrirEntretien();
    component.markData.date = '2026-09-10';
    component.markData.invoiceLines[0].price = 200;
    component.markData.notes = 'Facture remise en main propre';

    component.onFactureScannee(facture());

    expect(component.markData.date).toBe('2026-09-10');
    expect(component.markData.invoiceLines[0].price).toBe(200);
    expect(component.markData.notes).toBe('Facture remise en main propre');
    expect(component.scanLu!.notesRemplies).toBe(false);
    // La valeur lue reste visible pour que l'utilisateur tranche lui-même.
    expect(component.scanLu!.dateNonAppliquee).toBe('2026-09-15');
    expect(component.scanLu!.totalFacture).toBe(343);
    expect(component.scanTotalDiffere()).toBe(true);
  });

  /**
   * Le scénario qui retournait la protection contre elle-même : l'écran prenait le
   * CONTENU du champ prix comme nouvelle référence « posé par l'écran » à la fin de
   * chaque scan. Un prix tapé à la main — que le scan venait justement de respecter —
   * devenait donc un prix d'écran, et le scan suivant l'écrasait. On scanne souvent
   * deux fois (photo floue, deuxième page, facture corrigée) : le montant du garage
   * remplaçait alors celui que l'utilisateur avait négocié, sans un mot.
   */
  it('deux scans de suite : le prix tapé à la main reste intact au second scan', () => {
    ouvrirEntretien();
    component.markData.invoiceLines[0].price = 200;   // prix tapé par l'utilisateur

    component.onFactureScannee(facture());            // 1er scan : il le respecte
    expect(component.markData.invoiceLines[0].price).toBe(200);

    component.onFactureScannee(facture());            // 2e scan : il le respecte ENCORE
    expect(component.markData.invoiceLines[0].price).toBe(200);
    // Le montant lu reste rappelé, et la ligne reconnue par le 1er scan n'est pas dupliquée.
    expect(component.markData.invoiceLines.length).toBe(2);
    expect(component.scanLu!.lignesDejaChiffrees.map(l => l.label)).toEqual(['Vidange moteur 10W40']);
    expect(component.scanTotalDiffere()).toBe(true);
  });

  /**
   * L'envers du décor : ce que le scan a posé lui-même, un scan suivant a le droit
   * de le remplacer (photo floue reprise, facture corrigée par le garage). Sans quoi
   * le correctif ci-dessus figerait le formulaire au premier scan.
   */
  it('second scan d’une facture corrigée : le scan remplace bien SA propre proposition', () => {
    ouvrirEntretien();
    component.onFactureScannee(facture());
    expect(component.markData.invoiceLines[0].price).toBe(163);

    // Le garage renvoie la facture corrigée : vidange 150, freins 180, main d'œuvre 70.
    component.onFactureScannee(facture({
      total: 400, amountTTC: 400,
      items: [
        { label: 'Vidange moteur 10W40', amount: 150, category: 'maintenance' },
        { label: 'Plaquettes de frein avant', amount: 180, category: 'maintenance' },
        { label: 'Main d\'oeuvre', amount: 70, category: 'maintenance' },
      ]
    }));

    // 150 + les 70 de main d'œuvre que rien d'autre ne peut porter.
    expect(component.markData.invoiceLines[0].price).toBe(220);
    expect(component.markData.invoiceLines[1].price).toBe(180);
    expect(component.getInvoiceTotal()).toBe(400);
    expect(component.scanTotalDiffere()).toBe(false);
    // Ses propres lignes sont mises à jour, plus annoncées comme « déjà chiffrées ».
    expect(component.scanLu!.lignesDejaChiffrees).toEqual([]);
    expect(component.scanLu!.lignesNonRapprochees.map(l => l.label)).toEqual(['Main d\'oeuvre']);
  });

  /**
   * Le « Dernier prix » rappelé quand on choisit un modèle est de même nature que
   * celui posé à l'ouverture : une proposition de l'écran, pas une saisie. Un scan
   * doit donc pouvoir le remplacer par le montant du document — sinon le Total saisi
   * reste sur un ancien prix et ne retombe plus sur le total de la facture.
   */
  it('rappel du dernier prix payé sur une ligne ajoutée : le scan peut le remplacer', () => {
    component.lastPaidPrices.set('2', 200);
    ouvrirEntretien();
    component.addInvoiceLine();
    component.onLineTemplateChange(component.markData.invoiceLines[1], '2');
    expect(component.markData.invoiceLines[1].price).toBe(200);   // rappel de l'écran

    component.onFactureScannee(facture());

    expect(component.markData.invoiceLines[1].price).toBe(180);   // montant de la facture
    expect(component.markData.invoiceLines[0].price).toBe(163);
    expect(component.getInvoiceTotal()).toBe(343);
    expect(component.scanTotalDiffere()).toBe(false);
  });

  it('fournisseur déjà choisi ou introuvable : aucun garage inventé', () => {
    ouvrirEntretien();
    component.markData.supplierId = 9;
    component.onFactureScannee(facture());
    expect(component.markData.supplierId).toBe(9);

    ouvrirEntretien();
    component.onFactureScannee(facture({ supplierName: 'STATION SHELL LAC' }));
    expect(component.markData.supplierId).toBeNull();
    expect(component.scanLu!.fournisseurRapproche).toBe(false);
    expect(component.scanLu!.fournisseurLu).toBe('STATION SHELL LAC');
  });

  it('plaque de la facture différente du véhicule : avertissement, sans blocage', () => {
    ouvrirEntretien();
    component.onFactureScannee(facture({ vehiclePlate: '999 TU 1111' }));

    expect(component.scanLu!.plaqueDifferente).toBe(true);
    expect(component.scanLu!.plaqueLue).toBe('999 TU 1111');
    // Le véhicule de la fiche n'est pas touché et l'enregistrement reste possible.
    expect(component.markData.vehicleId).toBe('49');
    expect(component.isMarkValid()).toBe(true);
  });

  it('facture d’avoir : signalée, jamais enregistrée comme un coût en douce', () => {
    ouvrirEntretien();
    component.onFactureScannee(facture({ isCreditNote: true, category: 'credit_note' }));
    expect(component.scanLu!.avoir).toBe(true);
  });

  it('crédit IA épuisé : message déjà montré, formulaire toujours utilisable', () => {
    ouvrirEntretien();
    component.markData.invoiceLines[0].price = 150;

    component.onEchecScan({
      message: 'Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l’augmenter.',
      receiptUrl: ''
    });

    // Rien n'est ajouté à l'écran : le message vient de la brique, la saisie continue.
    expect(component.scanLu).toBeNull();
    expect(component.markData.invoiceLines.length).toBe(1);
    expect(component.markData.invoiceLines[0].price).toBe(150);
    expect(component.isMarkValid()).toBe(true);
  });

  it('panne de l’IA : le document stocké reste consultable, la saisie continue', () => {
    ouvrirEntretien();
    component.onEchecScan({
      message: "L'analyse de la facture a échoué.",
      receiptUrl: '/uploads/invoices/7/panne.pdf'
    });

    expect(component.scanLu!.receiptUrl).toBe('/uploads/invoices/7/panne.pdf');
    expect(component.scanLu!.echec).toContain('saisissez les montants');
    // Aucune ligne n'est ajoutée ni vidée : la saisie à la main enchaîne directement.
    expect(component.markData.invoiceLines.length).toBe(1);
    component.markData.invoiceLines[0].price = 150;
    expect(component.isMarkValid()).toBe(true);
  });

  it('fermeture de la modale : le bandeau du scan ne survit pas au dossier suivant', () => {
    ouvrirEntretien();
    component.onFactureScannee(facture());
    expect(component.scanLu).not.toBeNull();

    component.closeMarkDone();
    expect(component.scanLu).toBeNull();
  });

  /**
   * Le journal d'entretien n'a pas de colonne justificatif : sans ce lien dans
   * les notes, la facture scannée n'est référencée nulle part et le balayage
   * des factures orphelines l'efface le lendemain, alors que la ligne reste.
   * Choix de Karim du 19/09/2026 : les notes, comme Carburant et Réparations,
   * plutôt qu'une migration.
   */
  it('le lien de la facture scannée part dans les notes, une seule fois', () => {
    const envoyes: any[] = [];
    jest.spyOn(api, 'markMaintenanceDone').mockImplementation((p: any) => { envoyes.push(p); return of(1 as any); });
    jest.spyOn(component, 'loadTemplates').mockImplementation(() => {});
    jest.spyOn(component, 'loadVehicles').mockImplementation(() => {});

    ouvrirEntretien();
    component.onFactureScannee(facture());
    component.confirmMarkDone();

    expect(envoyes.length).toBe(2);
    for (const p of envoyes) {
      expect(p.notes).toContain('/uploads/invoices/7/facture.jpg');
      // Une seule mention, même si l'utilisateur renvoie après un échec partiel.
      expect(p.notes.split('/uploads/invoices/7/facture.jpg').length - 1).toBe(1);
      // Ce que l'utilisateur avait déjà dans les notes reste devant.
      expect(p.notes).toContain('FA-2026-0042');
    }
  });

  it('sans scan, les notes restent celles de l’utilisateur', () => {
    const envoyes: any[] = [];
    jest.spyOn(api, 'markMaintenanceDone').mockImplementation((p: any) => { envoyes.push(p); return of(1 as any); });
    jest.spyOn(component, 'loadTemplates').mockImplementation(() => {});
    jest.spyOn(component, 'loadVehicles').mockImplementation(() => {});

    ouvrirEntretien();
    component.markData.notes = 'Vidange faite au garage du coin';
    component.markData.invoiceLines[0].price = 120;
    component.confirmMarkDone();

    expect(envoyes.length).toBe(1);
    expect(envoyes[0].notes).toBe('Vidange faite au garage du coin');
  });
});
