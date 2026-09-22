import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';
import { RepairsComponent } from './repairs.component';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';

// tsconfig.spec.json limite les types à « jest » : l'accès fichier de Node se déclare ici
// plutôt que d'élargir la configuration partagée (même geste que la garde des exemples français).
declare const require: (module: string) => any;
declare const __dirname: string;

/**
 * Contre-relecture du 18/09/2026 — écran Réparations.
 *
 * Le serveur refuse depuis ce jour la suppression d'une réparation née de la phase 5
 * d'un dossier de sinistre (400 avec un motif en français). L'écran avalait ce refus :
 * la fenêtre de confirmation se fermait, la ligne restait, aucun message — le client
 * y voyait un bug. Deux garde-fous : le motif du serveur reste affiché, et le bouton
 * Supprimer est verrouillé sur ces lignes, comme à l'écran Dépenses.
 */
describe('RepairsComponent — réparation née d’un sinistre', () => {
  let component: RepairsComponent;
  let fixture: any;
  let api: ApiService;

  const reparationSinistre = {
    id: 7,
    vehicleId: 49,
    vehicleName: 'Camion 12',
    vehiclePlate: '123 TU 4567',
    reference: 'REP-2026-007',
    description: 'Pare-chocs avant',
    repairDate: '2026-09-10T00:00:00',
    laborCost: 120,
    partsCost: 380,
    totalCost: 500,
    status: 'completed',
    accidentEventId: 42,
    parts: []
  };

  beforeEach(async () => {
    // L'écran injecte le service d'export PDF, qui précharge le logo par fetch() —
    // absent de jsdom. Un échec de préchargement est déjà prévu par le service.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, RepairsComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(RepairsComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getSuppliers').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 200 }) as any);
    jest.spyOn(api, 'getRepairs').mockReturnValue(
      of({ items: [reparationSinistre], totalCount: 1, page: 1, pageSize: 100 }) as any
    );
  });

  it('refus 400 du serveur : le motif s’affiche et la fenêtre reste ouverte', () => {
    const motif = 'Cette réparation appartient au dossier de sinistre SIN-2026-042 : '
      + 'videz le coût réel dans la phase 5 du dossier pour la retirer.';
    jest.spyOn(api, 'deleteRepair').mockReturnValue(
      throwError(() => ({ status: 400, error: { message: motif } })) as any
    );

    component.confirmDelete(reparationSinistre as any);
    component.deleteRepair();

    expect(component.saveError).toBe(motif);
    expect(component.showDeleteConfirm).toBe(true);
  });

  it('refus sans motif : repli en français, jamais une fenêtre muette', () => {
    jest.spyOn(api, 'deleteRepair').mockReturnValue(throwError(() => ({ status: 400, error: {} })) as any);

    component.confirmDelete(reparationSinistre as any);
    component.deleteRepair();

    expect(component.saveError).toBeTruthy();
    expect(component.showDeleteConfirm).toBe(true);
  });

  it('suppression acceptée : la fenêtre se ferme et la liste est rechargée', () => {
    jest.spyOn(api, 'deleteRepair').mockReturnValue(of(void 0) as any);

    component.confirmDelete({ ...reparationSinistre, id: 8, accidentEventId: null } as any);
    component.deleteRepair();

    expect(component.saveError).toBeNull();
    expect(component.showDeleteConfirm).toBe(false);
    expect(api.getRepairs).toHaveBeenCalled();
  });

  it('ligne issue d’un sinistre : bouton Supprimer verrouillé et motif en infobulle', () => {
    fixture.detectChanges();

    expect(component.repairs[0].accidentEventId).toBe(42);

    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('button.btn-action.delete');
    expect(bouton).toBeTruthy();
    expect(bouton.disabled).toBe(true);
    expect(bouton.getAttribute('title')).toContain('sinistre #42');

    // Repère visible sur la ligne, pour que le verrou ne soit pas une surprise.
    expect(fixture.nativeElement.querySelector('.col-ref .accident-badge')?.textContent).toContain('#42');
  });
});

/**
 * Scan de facture sur l'écran Réparations (19/09/2026).
 *
 * La brique <app-scan-facture> envoie le document et rend l'extraction ; l'écran
 * ne décide que du remplissage de SON formulaire. Ce qui est vérifié ici : les
 * champs réellement pré-remplis, la répartition des lignes entre pièces et
 * main-d'œuvre (jamais inventée), l'avertissement quand la plaque ne correspond à
 * aucun véhicule, et le fait qu'un scan refusé — quota atteint — ne bloque jamais
 * la saisie à la main.
 */
describe('RepairsComponent — scan de facture', () => {
  let component: RepairsComponent;
  let fixture: any;
  let api: ApiService;
  let auth: AuthService;

  const vehicules = [
    { id: 2, name: 'Camion 12', plateNumber: '123 TU 4567', mileage: 84000 },
    { id: 3, name: 'Clio', plateNumber: '99 TU 1000', mileage: 12000 }
  ];

  /** Facture de garage : deux pièces, une ligne de main-d'œuvre, un timbre fiscal. */
  const extraction = () => ({
    supplierName: 'GARAGE EL AMEN',
    invoiceNumber: 'FA-2026-0042',
    date: '2026-09-15',
    amountHT: 288.60,
    amountTVA: 54.83,
    amountTTC: 343.43,
    total: 343.43,
    currency: 'TND',
    category: 'repair',
    vehiclePlate: '123 TU 4567',
    description: 'Freins avant',
    descriptionComplete: 'GARAGE EL AMEN — Freins avant',
    confidence: 'high',
    liters: null,
    pricePerLiter: null,
    isCreditNote: false,
    items: [
      { label: 'Plaquettes avant', amount: 120, category: 'repair' },
      { label: 'Disques avant (x2)', amount: 180, category: 'repair' },
      { label: "Main d'oeuvre 2h", amount: 42.43, category: 'repair' },
      { label: 'Timbre fiscal', amount: 1, category: 'other' }
    ]
  });

  const resultat = (modif: any = {}) => ({
    extraction: { ...extraction(), ...modif },
    receiptUrl: '/uploads/invoices/7/facture-42.jpg',
    quota: { enabled: true, budgetTokens: 60000, usedTokens: 15000, remainingTokens: 45000, percentUsed: 25, scansThisMonth: 5, estimatedScansLeft: 15 }
  });

  beforeEach(async () => {
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    // La fenêtre de saisie porte les animations @fadeIn / @slideIn : sans ce module,
    // l'ouvrir dans un test lève NG05105.
    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, NoopAnimationsModule, RepairsComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(RepairsComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);
    auth = TestBed.inject(AuthService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of(vehicules) as any);
    jest.spyOn(api, 'getSuppliers').mockReturnValue(
      of({ items: [{ id: 9, name: 'Garage El Amen', type: 'garage' }], totalCount: 1, page: 1, pageSize: 200 }) as any
    );
    jest.spyOn(api, 'getRepairs').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 100 }) as any);
    jest.spyOn(api, 'getScanQuota').mockReturnValue(of({ enabled: true, budgetTokens: 60000, usedTokens: 15000, remainingTokens: 45000, percentUsed: 25, scansThisMonth: 5, estimatedScansLeft: 15 }) as any);
    // La brique n'affiche son bouton qu'à un utilisateur connecté.
    jest.spyOn(auth, 'getCurrentUserSync').mockReturnValue({ id: '1', name: 'Test' } as any);

    fixture.detectChanges();   // ngOnInit : véhicules et fournisseurs chargés
  });

  it('le bouton du scan et sa barre « Crédit IA » sont dans la barre d’actions', () => {
    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('app-scan-facture .btn-scan');

    expect(bouton).toBeTruthy();
    expect(bouton.textContent).toContain('Scanner une facture');
    expect(bouton.textContent).not.toContain('ce mois');
    const barre = fixture.nativeElement.querySelector('app-scan-facture app-credit-ia-bar [role="progressbar"]') as HTMLElement;
    expect(barre.getAttribute('aria-valuenow')).toBe('25');
  });

  it('scan réussi : véhicule, fournisseur, date, n° de facture, description et lignes pré-remplis', () => {
    component.onFactureScannee(resultat() as any);

    expect(component.isPanelOpen).toBe(true);
    expect(component.editingRepair).toBeNull();
    expect(component.form.vehicleId).toBe('2');
    expect(component.selectedVehicle?.id).toBe(2);
    expect(component.form.supplierId).toBe('9');
    expect(component.form.repairDate).toBe('2026-09-15');
    expect(component.form.invoiceNumber).toBe('FA-2026-0042');
    expect(component.form.description).toBe('GARAGE EL AMEN — Freins avant');

    // Pièces : une ligne par pièce, quantité 1, prix unitaire = montant de la ligne.
    expect(component.form.parts.map(p => p.partName)).toEqual(['Plaquettes avant', 'Disques avant (x2)']);
    expect(component.form.parts.map(p => p.quantity)).toEqual([1, 1]);
    expect(component.getPartsCost()).toBe(300);
    // Main d'œuvre : seulement la ligne qui le dit. Le timbre fiscal n'a pas de case ici.
    expect(component.form.laborCost).toBe(42.43);
    expect(component.scanInfo?.lignesIgnorees).toBe(1);

    // Justificatif : pas de champ dédié sur la réparation, le lien part dans les notes.
    expect(component.form.notes).toContain('/uploads/invoices/7/facture-42.jpg');
    expect(component.justificatifUrl({ notes: component.form.notes } as any))
      .toBe('/uploads/invoices/7/facture-42.jpg');

    // Le type d'intervention n'est pas inventé : la facture ne le donne pas.
    expect(component.form.repairType).toBe('');
  });

  it('écart avec le total de la facture : signalé, et comblé seulement sur clic', () => {
    component.onFactureScannee(resultat() as any);

    // 343,43 facturés contre 342,43 répartis : le timbre fiscal laissé de côté.
    expect(component.getTotalCost()).toBe(342.43);
    expect(component.ecartAvecFacture()).toBe(1);

    component.reporterEcartEnMainOeuvre();

    expect(component.form.laborCost).toBe(43.43);
    expect(component.ecartAvecFacture()).toBe(0);
  });

  it('facture sans lignes détaillées : aucune répartition inventée, l’écart vaut le total', () => {
    component.onFactureScannee(resultat({ items: [] }) as any);

    expect(component.form.parts).toEqual([]);
    expect(component.form.laborCost).toBe(0);
    expect(component.ecartAvecFacture()).toBe(343.43);
  });

  it('plaque inconnue : véhicule laissé vide et plaque détectée affichée', () => {
    component.onFactureScannee(resultat({ vehiclePlate: '456 TU 9999' }) as any);
    fixture.detectChanges();

    expect(component.form.vehicleId).toBe('');
    expect(component.scanInfo?.vehiculeTrouve).toBe(false);
    const bandeau = fixture.nativeElement.querySelector('.scan-banner');
    expect(bandeau.textContent).toContain('456 TU 9999');
    expect(bandeau.textContent).toContain('introuvable');
    // Le reste du scan reste exploitable.
    expect(component.form.invoiceNumber).toBe('FA-2026-0042');
  });

  it('confiance de la lecture montrée dans le bandeau', () => {
    component.onFactureScannee(resultat() as any);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.scan-banner .scan-conf').textContent).toContain('élevée');
  });

  it('facture d’avoir : l’écran prévient au lieu de l’enregistrer comme un coût', () => {
    component.onFactureScannee(resultat({ isCreditNote: true }) as any);
    fixture.detectChanges();

    expect(component.scanInfo?.avoir).toBe(true);
    expect(fixture.nativeElement.querySelector('.scan-banner').textContent).toContain('Avoir fournisseur');
  });

  it('crédit IA épuisé : rien ne s’ouvre, la saisie à la main reste entière', () => {
    component.onEchecScan({ message: 'Crédit IA du mois épuisé (100 %). Il se recharge le 01/10/2026 ; votre administrateur peut l’augmenter.', receiptUrl: '' });

    expect(component.isPanelOpen).toBe(false);
    expect(component.scanInfo).toBeNull();

    // Le formulaire reste utilisable : « Nouvelle reparation » n'est jamais bloquée.
    component.openAddRepair();
    component.form.vehicleId = '2';
    component.form.repairDate = '2026-09-19';

    expect(component.isPanelOpen).toBe(true);
    expect(component.isFormValid()).toBe(true);
  });

  it('IA indisponible mais document stocké : formulaire vide avec le justificatif joint', () => {
    component.onEchecScan({ message: 'Analyse indisponible.', receiptUrl: '/uploads/invoices/7/panne.pdf' });
    fixture.detectChanges();

    expect(component.isPanelOpen).toBe(true);
    expect(component.form.vehicleId).toBe('');
    expect(component.form.notes).toContain('/uploads/invoices/7/panne.pdf');
    expect(component.scanInfo?.echec).toBe(true);
    expect(fixture.nativeElement.querySelector('.scan-banner').textContent).toContain('saisie à la main');
  });

  it('fermer le formulaire efface le bandeau du scan', () => {
    component.onFactureScannee(resultat() as any);
    component.closePanel();

    expect(component.scanInfo).toBeNull();
    expect(component.form.notes).toBe('');
  });

  /**
   * Le bouton du scan vit dans la barre d'actions, HORS de la fenêtre de saisie.
   * L'ombre le cachait à l'œil mais le laissait dans l'ordre de TABULATION : à la
   * touche Tab, un utilisateur au clavier le déclenchait encore et le formulaire
   * repartait à zéro — sa saisie perdue sans un mot. D'où le verrou `desactive`.
   */
  describe('le scan n’écrase jamais une saisie en cours', () => {
    const boutonScan = (): HTMLButtonElement =>
      fixture.nativeElement.querySelector('app-scan-facture .btn-scan');

    it('aucune fenêtre ouverte : le bouton du scan est utilisable', () => {
      expect(component.fenetreOuverte).toBe(false);
      expect(boutonScan().disabled).toBe(false);
    });

    it('fenêtre de saisie ouverte : bouton verrouillé, donc hors de l’ordre de tabulation', () => {
      component.openAddRepair();
      fixture.detectChanges();

      expect(component.fenetreOuverte).toBe(true);
      // disabled : ni cliquable à la souris, ni atteignable à la touche Tab.
      expect(boutonScan().disabled).toBe(true);
      expect(boutonScan().getAttribute('title')).toContain('Fermez la fenêtre ouverte');
    });

    it('fiche de détail ou confirmation de suppression ouverte : bouton verrouillé aussi', () => {
      component.viewRepair({ id: 1, parts: [], status: 'completed' } as any);
      expect(component.fenetreOuverte).toBe(true);

      component.closeView();
      component.confirmDelete({ id: 1 } as any);
      expect(component.fenetreOuverte).toBe(true);

      component.cancelDelete();
      expect(component.fenetreOuverte).toBe(false);
    });

    it('fenêtre refermée : le verrou se relâche', () => {
      component.openAddRepair();
      component.closePanel();
      fixture.detectChanges();

      expect(boutonScan().disabled).toBe(false);
    });

    /**
     * Course inverse, que le verrou ne couvre pas : scan lancé fenêtre fermée,
     * l'analyse dure quelques secondes et l'utilisateur ouvre « Nouvelle reparation »
     * entre-temps. Le scan est déjà décompté du quota : on demande, on ne jette pas.
     */
    it('scan revenu pendant une saisie commencée : remplacement seulement sur accord', () => {
      const demande = jest.spyOn(window, 'confirm').mockReturnValue(false);

      component.openAddRepair();
      component.form.description = 'Vidange saisie à la main';
      component.form.laborCost = 80;

      component.onFactureScannee(resultat() as any);

      expect(demande).toHaveBeenCalled();
      expect(component.form.description).toBe('Vidange saisie à la main');
      expect(component.form.laborCost).toBe(80);
      expect(component.form.vehicleId).toBe('');
      expect(component.scanInfo).toBeNull();

      demande.mockReturnValue(true);
      component.onFactureScannee(resultat() as any);

      expect(component.form.description).toBe('GARAGE EL AMEN — Freins avant');
      expect(component.form.vehicleId).toBe('2');
      expect(component.scanInfo).not.toBeNull();

      demande.mockRestore();
    });

    it('scan échoué revenu pendant une saisie : le justificatif ne l’efface pas non plus', () => {
      const demande = jest.spyOn(window, 'confirm').mockReturnValue(false);

      component.openAddRepair();
      component.form.description = 'Freins arrière';

      component.onEchecScan({ message: 'Analyse indisponible.', receiptUrl: '/uploads/invoices/7/panne.pdf' });

      expect(component.form.description).toBe('Freins arrière');
      expect(component.form.notes).toBe('');
      expect(component.scanInfo).toBeNull();

      demande.mockRestore();
    });
  });

  /**
   * repairs.repair_date est un timestamp (les lignes réelles portent 12:00:00+00) et
   * <input type="date"> refuse tout ce qui n'est pas yyyy-MM-dd : le champ Date,
   * pourtant obligatoire, s'affichait VIDE à la modification.
   */
  describe('date de la réparation dans le formulaire', () => {
    it('modification : l’horodatage de la base remplit vraiment le champ', () => {
      component.editRepair({
        id: 40, vehicleId: 2, vehicleName: 'Camion 12', vehiclePlate: '123 TU 4567',
        reference: 'REP-202609-0037', description: 'Embrayage',
        repairDate: '2026-08-03T12:00:00+00:00',
        laborCost: 0, partsCost: 0, totalCost: 0, status: 'completed',
        invoiceNumber: '', notes: '', repairType: null, parts: []
      } as any);

      expect(component.form.repairDate).toBe('2026-08-03');
      expect(component.isFormValid()).toBe(true);

      // Ce qu'un <input type="date"> accepte vraiment : la valeur brute de la base
      // était refusée et le champ retombait à vide.
      const champ = document.createElement('input');
      champ.type = 'date';
      champ.value = '2026-08-03T12:00:00+00:00';
      expect(champ.value).toBe('');
      champ.value = component.form.repairDate;
      expect(champ.value).toBe('2026-08-03');
    });

    it('date illisible : champ vide et enregistrement bloqué, jamais une date inventée', () => {
      component.editRepair({
        id: 41, vehicleId: 2, vehicleName: 'Camion 12', vehiclePlate: '123 TU 4567',
        reference: 'REP-202609-0038', description: '', repairDate: '',
        laborCost: 0, partsCost: 0, totalCost: 0, status: 'completed',
        invoiceNumber: '', notes: '', repairType: null, parts: []
      } as any);

      expect(component.form.repairDate).toBe('');
      expect(component.isFormValid()).toBe(false);
    });
  });

  /** Le bloc « ajouter un fournisseur » survivait à la fermeture de la fenêtre. */
  it('rouvrir la fenêtre : le bloc « ajouter un fournisseur » est replié et vide', () => {
    component.openAddRepair();
    component.showAddSupplier = true;
    component.newSupplierName = 'Garage à moitié tapé';
    component.closePanel();

    component.openAddRepair();

    expect(component.showAddSupplier).toBe(false);
    expect(component.newSupplierName).toBe('');
  });
});

/**
 * Contre-relecture du 19/09/2026 — ce qui reste atteignable DERRIÈRE la fenêtre.
 *
 * Le verrou posé sur le bouton du scan était juste mais ne couvrait qu'un bouton :
 * « Nouvelle reparation » et, sur chaque ligne du tableau, « Détails » et « Modifier »
 * restaient dans l'ordre de TABULATION derrière la fenêtre ouverte. Une touche Tab
 * pendant une saisie rouvrait un formulaire vierge ou le remplaçait par une autre
 * réparation — le travail en cours perdu sans un mot.
 *
 * Même mécanisme pour tous (le getter `fenetreOuverte`), et l'apparence qui va avec :
 * `disabled` sort le bouton de la tabulation ET déclenche les règles `:disabled` du
 * composant (opacité + curseur barré), pour que le gris ne soit pas une énigme.
 */
describe('RepairsComponent — une fenêtre ouverte verrouille l’arrière-plan', () => {
  let component: RepairsComponent;
  let fixture: any;
  let api: ApiService;
  let auth: AuthService;

  /** Source de l'écran : seul endroit où la feuille de styles du composant est lisible en test. */
  const source = (): string =>
    String(require('fs').readFileSync(require('path').join(__dirname, 'repairs.component.ts'), 'utf8'));

  const reparationSimple = {
    id: 11, vehicleId: 2, vehicleName: 'Clio', vehiclePlate: '99 TU 1000',
    reference: 'REP-2026-011', description: 'Vidange', repairDate: '2026-09-18T12:00:00',
    laborCost: 60, partsCost: 40, totalCost: 100, status: 'completed',
    invoiceNumber: '', notes: '', repairType: null, accidentEventId: null, parts: []
  };

  const reparationSinistre = {
    ...reparationSimple,
    id: 12, reference: 'REP-2026-012', description: 'Pare-chocs avant',
    repairDate: '2026-09-10T12:00:00', accidentEventId: 42
  };

  /** La ligne du tableau qui porte cette référence (l'ordre dépend du tri). */
  const ligne = (reference: string): HTMLTableRowElement => {
    const trouvee = (Array.from(fixture.nativeElement.querySelectorAll('tr.repair-row')) as HTMLTableRowElement[])
      .find(tr => (tr.querySelector('.repair-ref')?.textContent || '').includes(reference));
    if (!trouvee) throw new Error('Ligne introuvable dans le tableau : ' + reference);
    return trouvee;
  };

  const boutonLigne = (reference: string, action: 'view' | 'edit' | 'delete'): HTMLButtonElement =>
    ligne(reference).querySelector('button.btn-action.' + action) as HTMLButtonElement;

  const boutonNouvelle = (): HTMLButtonElement => fixture.nativeElement.querySelector('button.btn-add');
  const boutonScan = (): HTMLButtonElement => fixture.nativeElement.querySelector('app-scan-facture .btn-scan');

  beforeEach(async () => {
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, NoopAnimationsModule, RepairsComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(RepairsComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);
    auth = TestBed.inject(AuthService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getSuppliers').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 200 }) as any);
    jest.spyOn(api, 'getRepairs').mockReturnValue(
      of({ items: [reparationSimple, reparationSinistre], totalCount: 2, page: 1, pageSize: 100 }) as any
    );
    jest.spyOn(api, 'getScanQuota').mockReturnValue(of({ enabled: true, budgetTokens: 60000, usedTokens: 15000, remainingTokens: 45000, percentUsed: 25, scansThisMonth: 5, estimatedScansLeft: 15 }) as any);
    jest.spyOn(auth, 'getCurrentUserSync').mockReturnValue({ id: '1', name: 'Test' } as any);

    fixture.detectChanges();
  });

  it('aucune fenêtre ouverte : tout l’arrière-plan reste utilisable', () => {
    expect(component.fenetreOuverte).toBe(false);
    expect(boutonNouvelle().disabled).toBe(false);
    expect(boutonScan().disabled).toBe(false);
    expect(boutonLigne('REP-2026-011', 'view').disabled).toBe(false);
    expect(boutonLigne('REP-2026-011', 'edit').disabled).toBe(false);
    expect(boutonLigne('REP-2026-011', 'delete').disabled).toBe(false);
    expect(boutonNouvelle().getAttribute('title')).toBe('Nouvelle réparation');
    expect(boutonLigne('REP-2026-011', 'view').getAttribute('title')).toBe('Détails');
    expect(boutonLigne('REP-2026-011', 'edit').getAttribute('title')).toBe('Modifier');
  });

  it('fenêtre de saisie ouverte : « Nouvelle reparation », Détails et Modifier verrouillés dans le DOM', () => {
    component.openAddRepair();
    fixture.detectChanges();

    // disabled : ni cliquable à la souris, ni atteignable à la touche Tab.
    expect(boutonNouvelle().disabled).toBe(true);
    expect(boutonLigne('REP-2026-011', 'view').disabled).toBe(true);
    expect(boutonLigne('REP-2026-011', 'edit').disabled).toBe(true);
    expect(boutonLigne('REP-2026-011', 'delete').disabled).toBe(true);
    // Un seul mécanisme : le verrou du scan, déjà en place, sert aux autres boutons.
    expect(boutonScan().disabled).toBe(true);

    // Le motif est lisible, sinon le gris est une énigme.
    for (const b of [boutonNouvelle(), boutonLigne('REP-2026-011', 'view'), boutonLigne('REP-2026-011', 'edit')]) {
      expect(b.getAttribute('title')).toContain('Fermez la fenêtre ouverte');
    }
  });

  /**
   * L'apparence désactivée n'est pas un détail : sans elle le bouton paraît cliquable
   * et l'utilisateur croit à une panne. Elle tient aux règles `:disabled` de l'écran,
   * que l'attribut `disabled` déclenche.
   *
   * jest-preset-angular retire les styles du composant compilé (ɵcmp.styles est vide) et
   * jsdom ne résout de toute façon pas `:disabled` : la seule garde possible est de
   * relire le source, comme le fait déjà la garde des exemples français.
   */
  it('un bouton verrouillé se VOIT : opacité réduite et curseur barré', () => {
    const styles = source().replace(/\s+/g, ' ');

    expect(styles).toContain('.btn-add:disabled { opacity:.5; cursor:not-allowed; }');
    expect(styles).toContain('.btn-action:disabled { opacity:.4; cursor:not-allowed; }');
    // Un bouton verrouillé ne doit pas non plus s'allumer au survol : le survol promettrait
    // un clic qui n'arrivera jamais.
    expect(styles).toContain('.btn-add:hover:not(:disabled)');
    expect(styles).toContain('.btn-action.view:hover:not(:disabled)');
    expect(styles).toContain('.btn-action.edit:hover:not(:disabled)');
    expect(styles).toContain('.btn-action.delete:hover:not(:disabled)');
  });

  it('fiche de détail ouverte : les boutons des lignes sont verrouillés aussi', () => {
    component.viewRepair(component.repairs[0]);
    fixture.detectChanges();

    expect(boutonNouvelle().disabled).toBe(true);
    expect(boutonLigne('REP-2026-011', 'edit').disabled).toBe(true);
  });

  it('confirmation de suppression ouverte : l’arrière-plan est verrouillé', () => {
    component.confirmDelete(component.repairs[0]);
    fixture.detectChanges();

    expect(boutonNouvelle().disabled).toBe(true);
    expect(boutonLigne('REP-2026-011', 'view').disabled).toBe(true);
  });

  it('ligne née d’un sinistre : Supprimer garde SON motif, définitif, fenêtre ouverte ou non', () => {
    expect(boutonLigne('REP-2026-012', 'delete').disabled).toBe(true);
    expect(boutonLigne('REP-2026-012', 'delete').getAttribute('title')).toContain('sinistre #42');

    component.openAddRepair();
    fixture.detectChanges();

    expect(boutonLigne('REP-2026-012', 'delete').disabled).toBe(true);
    expect(boutonLigne('REP-2026-012', 'delete').getAttribute('title')).toContain('sinistre #42');
    expect(fixture.nativeElement.querySelector('.col-ref .accident-badge')?.textContent).toContain('#42');
  });

  it('fenêtre refermée : le verrou se relâche partout', () => {
    component.openAddRepair();
    fixture.detectChanges();
    component.closePanel();
    fixture.detectChanges();

    expect(boutonNouvelle().disabled).toBe(false);
    expect(boutonScan().disabled).toBe(false);
    expect(boutonLigne('REP-2026-011', 'view').disabled).toBe(false);
    expect(boutonLigne('REP-2026-011', 'edit').disabled).toBe(false);
    // La ligne de sinistre, elle, reste verrouillée : son motif ne dépend pas de la fenêtre.
    expect(boutonLigne('REP-2026-012', 'delete').disabled).toBe(true);
  });
});

/**
 * Compteurs du haut de l'écran Réparations (recette du 19/09/2026).
 *
 * Karim filtre sur « Commercial 01 » : le tableau affiche bien une seule réparation
 * à 72 €, et les compteurs continuent d'annoncer 36 réparations et 10 570 € — tout le
 * parc. calculateStats() lisait `repairs` (tout ce qui est chargé) au lieu de
 * `filteredRepairs` (ce qui est affiché) ; le filtre de statut était ignoré pareil.
 *
 * Règle métier conservée : une réparation ANNULÉE ne coûte rien et reste hors du
 * « Coût total » (même règle que GET /repairs/stats), le compteur d'annulées est là
 * pour qu'on retrouve le compte.
 */
describe('RepairsComponent — les compteurs suivent le filtre', () => {
  let component: RepairsComponent;
  let fixture: any;
  let api: ApiService;

  const reparation = (o: Record<string, any>): any => ({
    vehiclePlate: '100 TU 1000', reference: 'REP', description: 'Intervention',
    repairDate: '2026-09-10T12:00:00', laborCost: 0, partsCost: 0,
    invoiceNumber: '', notes: '', repairType: null, accidentEventId: null, parts: [],
    ...o
  });

  // Parc de test calqué sur la base locale : Commercial 01 n'a qu'une réparation, à 72 €.
  const parc = [
    reparation({ id: 1, vehicleId: 5, vehicleName: 'Commercial 01', reference: 'REP-001', totalCost: 72, status: 'completed' }),
    reparation({ id: 2, vehicleId: 9, vehicleName: 'Logistique 01', reference: 'REP-002', totalCost: 600, status: 'completed' }),
    reparation({ id: 3, vehicleId: 9, vehicleName: 'Logistique 01', reference: 'REP-003', totalCost: 150, status: 'pending' }),
    reparation({ id: 4, vehicleId: 9, vehicleName: 'Logistique 01', reference: 'REP-004', totalCost: 90, status: 'in_progress' }),
    reparation({ id: 5, vehicleId: 12, vehicleName: 'Service 03', reference: 'REP-005', totalCost: 500, status: 'Cancelled' })
  ];

  /** Intitulés lus dans le DOM, dans l'ordre de la barre. */
  const libelles = (): string[] =>
    (Array.from(fixture.nativeElement.querySelectorAll('.stats-bar .stat-label')) as HTMLElement[])
      .map(e => (e.textContent || '').trim());

  const valeurs = (): string[] =>
    (Array.from(fixture.nativeElement.querySelectorAll('.stats-bar .stat-value')) as HTMLElement[])
      .map(e => (e.textContent || '').trim());

  const lignesAffichees = (): number =>
    fixture.nativeElement.querySelectorAll('tr.repair-row').length;

  beforeEach(async () => {
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, NoopAnimationsModule, RepairsComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(RepairsComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getSuppliers').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 200 }) as any);
    jest.spyOn(api, 'getRepairs').mockReturnValue(
      of({ items: parc, totalCount: parc.length, page: 1, pageSize: 100 }) as any
    );

    fixture.detectChanges();
  });

  it('sans filtre : les compteurs portent tout le parc chargé, annulée hors du coût', () => {
    expect(component.stats.totalRepairs).toBe(5);
    expect(component.stats.pendingRepairs).toBe(2);   // en attente + en cours
    expect(component.stats.completedRepairs).toBe(2);
    expect(component.stats.cancelledRepairs).toBe(1);
    expect(component.stats.totalCost).toBe(912);      // 72 + 600 + 150 + 90, les 500 annulés exclus
    expect(component.filtreActif).toBe(false);
    expect(libelles()[0]).toBe('Total réparations');
    expect(libelles()[libelles().length - 1]).toBe('Coût total');
  });

  it('filtre véhicule : les quatre compteurs suivent, et recoupent le tableau', () => {
    component.filterVehicle = '5';
    component.filterRepairs();
    fixture.detectChanges();

    expect(lignesAffichees()).toBe(1);
    expect(component.stats.totalRepairs).toBe(1);
    expect(component.stats.pendingRepairs).toBe(0);
    expect(component.stats.completedRepairs).toBe(1);
    expect(component.stats.cancelledRepairs).toBe(0);
    expect(component.stats.totalCost).toBe(72);
    expect(valeurs()[0]).toBe('1');

    component.filterVehicle = '9';
    component.filterRepairs();
    fixture.detectChanges();

    expect(lignesAffichees()).toBe(3);
    expect(component.stats.totalRepairs).toBe(3);
    expect(component.stats.pendingRepairs).toBe(2);
    expect(component.stats.completedRepairs).toBe(1);
    expect(component.stats.totalCost).toBe(840);
  });

  it('filtre statut : les compteurs suivent aussi', () => {
    component.filterStatus = 'completed';
    component.filterRepairs();
    fixture.detectChanges();

    expect(lignesAffichees()).toBe(2);
    expect(component.stats.totalRepairs).toBe(2);
    expect(component.stats.completedRepairs).toBe(2);
    expect(component.stats.pendingRepairs).toBe(0);
    expect(component.stats.cancelledRepairs).toBe(0);
    expect(component.stats.totalCost).toBe(672);
  });

  it('filtre sur les annulées : elles se comptent mais ne coûtent rien', () => {
    component.filterStatus = 'cancelled';
    component.filterRepairs();
    fixture.detectChanges();

    expect(lignesAffichees()).toBe(1);
    expect(component.stats.totalRepairs).toBe(1);
    expect(component.stats.cancelledRepairs).toBe(1);
    expect(component.stats.totalCost).toBe(0);
    // Le compteur d'annulées n'apparaît que s'il y en a : ici il est visible.
    expect(libelles()).toContain('Annulées');
  });

  it('recherche : les compteurs suivent le texte saisi', () => {
    component.searchQuery = 'Commercial';
    component.filterRepairs();
    fixture.detectChanges();

    expect(component.stats.totalRepairs).toBe(1);
    expect(component.stats.totalCost).toBe(72);
  });

  it('en attente + terminées + annulées = total, filtre actif ou non', () => {
    const recoupe = () => {
      const s = component.stats;
      expect(s.pendingRepairs + s.completedRepairs + s.cancelledRepairs).toBe(s.totalRepairs);
      expect(s.totalRepairs).toBe(component.filteredRepairs.length);
    };

    recoupe();
    for (const filtre of ['5', '9', '12']) {
      component.filterVehicle = filtre;
      component.filterRepairs();
      recoupe();
    }
  });

  it('filtre actif : l’intitulé le dit, et l’infobulle rappelle le parc entier', () => {
    component.filterVehicle = '5';
    component.filterRepairs();
    fixture.detectChanges();

    expect(component.filtreActif).toBe(true);
    expect(libelles()[0]).toBe('Réparations filtrées');
    expect(libelles()[libelles().length - 1]).toBe('Coût filtré');

    const premier: HTMLElement = fixture.nativeElement.querySelector('.stats-bar .stat-item');
    expect(premier.getAttribute('title')).toBe('Sur 5 réparations au total');

    // Filtre retiré : on revient aux intitulés du parc entier, sans infobulle.
    component.filterVehicle = '';
    component.filterRepairs();
    fixture.detectChanges();

    expect(component.filtreActif).toBe(false);
    expect(libelles()[0]).toBe('Total réparations');
    expect(fixture.nativeElement.querySelector('.stats-bar .stat-item').getAttribute('title')).toBe('');
  });

  it('tri : le classement ne change aucun compteur', () => {
    component.filterVehicle = '9';
    component.filterRepairs();
    const avant = { ...component.stats };

    component.toggleSort('totalCost');
    fixture.detectChanges();

    expect(component.stats).toEqual(avant);
    expect(lignesAffichees()).toBe(3);
  });

  it('suppression : les compteurs se recalculent, filtre toujours appliqué', () => {
    component.filterVehicle = '9';
    component.filterRepairs();
    expect(component.stats.totalRepairs).toBe(3);

    // Le serveur renvoie le parc sans REP-003 (150 €, en attente).
    const restant = parc.filter(r => r.id !== 3);
    (api.getRepairs as jest.Mock).mockReturnValue(
      of({ items: restant, totalCount: restant.length, page: 1, pageSize: 100 }) as any
    );
    jest.spyOn(api, 'deleteRepair').mockReturnValue(of(void 0) as any);

    component.confirmDelete(parc[2] as any);
    component.deleteRepair();
    fixture.detectChanges();

    expect(component.filterVehicle).toBe('9');
    expect(component.stats.totalRepairs).toBe(2);
    expect(component.stats.pendingRepairs).toBe(1);
    expect(component.stats.totalCost).toBe(690);
    expect(lignesAffichees()).toBe(2);
  });
});

/**
 * Grosse société — l'écran s'arrêtait à 100 lignes sans le dire (20/09/2026).
 *
 * loadRepairs() demandait UNE page de 100 et tout le reste se faisait dessus :
 * les quatre compteurs, les filtres, la recherche, le tri et l'export. Une société
 * de 250 réparations lisait donc « 100 Total réparations », un coût total amputé,
 * et 150 réparations INVISIBLES qu'aucune recherche ne retrouvait — sans un mot.
 * La société de test n'en a que 38 : le défaut ne se voyait pas en local.
 *
 * Le faux serveur ci-dessous pagine pour de vrai (page + pageSize) : il rejoue
 * exactement ce que fait l'API.
 */
describe('RepairsComponent — une société de 250 réparations', () => {
  let component: RepairsComponent;
  let fixture: any;
  let api: ApiService;

  /** 250 réparations, dont la 201e porte un libellé que l'on ira chercher. */
  const parcGeant: any[] = Array.from({ length: 250 }, (_, i) => ({
    id: i + 1,
    vehicleId: (i % 10) + 1,
    vehicleName: `Véhicule ${(i % 10) + 1}`,
    vehiclePlate: `${100 + (i % 10)} TU 1000`,
    reference: `REP-${String(i + 1).padStart(4, '0')}`,
    description: i === 200 ? 'Boîte de vitesses' : 'Intervention courante',
    repairDate: '2026-09-10T12:00:00',
    laborCost: 40,
    partsCost: 60 + i,
    totalCost: 100 + i,
    // Une annulée toutes les dix : elle compte dans le total, jamais dans le coût.
    status: i % 10 === 9 ? 'cancelled' : (i % 3 === 0 ? 'pending' : 'completed'),
    invoiceNumber: '', notes: '', repairType: null, accidentEventId: null, parts: []
  }));

  const attendu = {
    total: parcGeant.length,
    annulees: parcGeant.filter(r => r.status === 'cancelled').length,
    cout: parcGeant.filter(r => r.status !== 'cancelled').reduce((s, r) => s + r.totalCost, 0)
  };

  /** Le serveur rend la tranche demandée et le VRAI total, comme GET /api/repairs. */
  const serveurPagine = (o?: { page?: number; pageSize?: number }) => {
    const pageSize = o?.pageSize ?? 50;
    const page = o?.page ?? 1;
    const debut = (page - 1) * pageSize;
    return of({
      items: parcGeant.slice(debut, debut + pageSize),
      totalCount: parcGeant.length,
      page,
      pageSize
    }) as any;
  };

  const referencesAffichees = (): string[] =>
    (Array.from(fixture.nativeElement.querySelectorAll('tr.repair-row .repair-ref')) as HTMLElement[])
      .map(e => (e.textContent || '').trim());

  beforeEach(async () => {
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, NoopAnimationsModule, RepairsComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(RepairsComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getSuppliers').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 200 }) as any);
    jest.spyOn(api, 'getRepairs').mockImplementation(serveurPagine as any);

    // Pages de 100, la taille qui a produit le défaut : le chargement doit en
    // enchaîner trois (100 + 100 + 50) au lieu de s'arrêter à la première.
    component.taillePageChargement = 100;
    fixture.detectChanges();
  });

  it('les compteurs annoncent le parc entier, pas la première page', () => {
    expect(component.totalParc).toBe(attendu.total);
    expect(component.stats.totalRepairs).toBe(attendu.total);
    expect(component.stats.cancelledRepairs).toBe(attendu.annulees);
    expect(component.stats.totalCost).toBe(attendu.cout);
    expect(component.stats.pendingRepairs + component.stats.completedRepairs + component.stats.cancelledRepairs)
      .toBe(component.stats.totalRepairs);
  });

  it('le chargement enchaîne les pages du serveur jusqu’au dernier enregistrement', () => {
    expect(component.repairs.length).toBe(attendu.total);
    expect(component.chargementComplet).toBe(true);
    const pagesDemandees = (api.getRepairs as jest.Mock).mock.calls.map(c => c[0]?.page);
    expect(pagesDemandees).toEqual([1, 2, 3]);
  });

  it('la recherche atteint la 201e réparation, invisible dans les cent premières', () => {
    component.searchQuery = 'Boîte de vitesses';
    component.filterRepairs();
    fixture.detectChanges();

    expect(component.filteredRepairs.length).toBe(1);
    expect(component.filteredRepairs[0].reference).toBe('REP-0201');
    expect(referencesAffichees()).toContain('REP-0201');
    expect(component.stats.totalRepairs).toBe(1);
    // L'infobulle du premier compteur rappelle le parc ENTIER, pas la page chargée.
    expect(component.infobulleTotal).toBe(`Sur ${attendu.total} réparations au total`);
  });

  it('la pagination affiche une page à la fois et permet d’atteindre REP-0201', () => {
    expect(referencesAffichees().length).toBe(component.taillePageAffichage);
    expect(component.totalPagesAffichage).toBe(Math.ceil(attendu.total / component.taillePageAffichage));

    // Tri par référence croissante : REP-0201 est la 201e ligne.
    component.sortColumn = 'reference';
    component.sortDirection = 'asc';
    component.allerPage(Math.ceil(201 / component.taillePageAffichage));
    fixture.detectChanges();

    expect(referencesAffichees()).toContain('REP-0201');
  });

  it('l’export PDF porte sur tout l’ensemble filtré, pas sur la page affichée', () => {
    const exporte = jest.spyOn((component as any).pdfService, 'exportGroupedReport').mockImplementation(() => {});

    component.exportPdf();

    const arg = exporte.mock.calls[0][0] as any;
    const lignes = arg.groups.reduce((s: number, g: any) => s + g.rows.length, 0);
    expect(lignes).toBe(attendu.total);
    expect(arg.subtitle).toContain(`${attendu.total} réparation(s)`);
  });

  it('une page qui échoue en cours de route : l’écran garde ce qu’il a et le DIT', () => {
    jest.spyOn(console, 'error').mockImplementation(() => {});
    jest.spyOn(api, 'getRepairStats').mockReturnValue(of({
      totalRepairs: attendu.total, pendingRepairs: 84, completedRepairs: 141, cancelledRepairs: attendu.annulees,
      totalCost: attendu.cout, averageCost: 0, totalLaborCost: 0, totalPartsCost: 0
    }) as any);
    (api.getRepairs as jest.Mock).mockImplementation((o?: any) =>
      o?.page === 1 ? serveurPagine(o) : throwError(() => ({ status: 500 })));

    component.loadRepairs();
    fixture.detectChanges();

    // Cent lignes sur 250 : le pire serait de les présenter comme le parc entier.
    expect(component.repairs.length).toBe(100);
    expect(component.chargementComplet).toBe(false);
    expect(fixture.nativeElement.querySelector('.avertissement-volume')).toBeTruthy();
    expect(component.stats.totalRepairs).toBe(attendu.total);
  });
});

/**
 * Parc au-delà de la limite de chargement : l'écran ne doit ni tout avaler ni mentir.
 * Les compteurs viennent alors du SERVEUR (GET /api/repairs/stats, filtres véhicule et
 * statut compris) et un bandeau dit noir sur blanc que le tableau et la recherche ne
 * portent que sur les lignes chargées.
 */
describe('RepairsComponent — parc au-delà de la limite de chargement', () => {
  let component: RepairsComponent;
  let fixture: any;
  let api: ApiService;

  const parc: any[] = Array.from({ length: 250 }, (_, i) => ({
    id: i + 1, vehicleId: 1, vehicleName: 'Camion', vehiclePlate: '100 TU 1',
    reference: `REP-${String(i + 1).padStart(4, '0')}`, description: 'Intervention',
    repairDate: '2026-09-10T12:00:00', laborCost: 40, partsCost: 60,
    totalCost: 100, status: 'completed',
    invoiceNumber: '', notes: '', repairType: null, accidentEventId: null, parts: []
  }));

  beforeEach(async () => {
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, NoopAnimationsModule, RepairsComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(RepairsComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getSuppliers').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 200 }) as any);
    jest.spyOn(api, 'getRepairs').mockImplementation(((o?: any) => {
      const pageSize = o?.pageSize ?? 50;
      const page = o?.page ?? 1;
      const debut = (page - 1) * pageSize;
      return of({ items: parc.slice(debut, debut + pageSize), totalCount: parc.length, page, pageSize }) as any;
    }) as any);
    jest.spyOn(api, 'getRepairStats').mockReturnValue(of({
      totalRepairs: 250, pendingRepairs: 10, completedRepairs: 235, cancelledRepairs: 5,
      totalCost: 24500, averageCost: 100, totalLaborCost: 9800, totalPartsCost: 14700
    }) as any);

    component.taillePageChargement = 100;
    component.limiteChargement = 150;
    fixture.detectChanges();
  });

  it('le chargement s’arrête à la limite et l’écran le DIT', () => {
    expect(component.chargementComplet).toBe(false);
    expect(component.repairs.length).toBeLessThan(parc.length);

    const bandeau: HTMLElement = fixture.nativeElement.querySelector('.avertissement-volume');
    expect(bandeau).toBeTruthy();
    expect(bandeau.textContent).toContain('250');
    expect(bandeau.textContent).toContain(String(component.repairs.length));
  });

  it('les compteurs viennent du serveur et restent vrais', () => {
    expect(api.getRepairStats).toHaveBeenCalled();
    expect(component.stats.totalRepairs).toBe(250);
    expect(component.stats.totalCost).toBe(24500);
    expect(component.stats.cancelledRepairs).toBe(5);
  });

  it('un filtre statut est envoyé au serveur pour que les compteurs suivent', () => {
    (api.getRepairStats as jest.Mock).mockClear();
    component.filterStatus = 'completed';
    component.filterRepairs();

    expect(api.getRepairStats).toHaveBeenCalledWith(expect.objectContaining({ status: 'completed' }));
  });

  // Revue du 20/09/2026 : filtrer sur un véhicule rendait le sous-ensemble COMPLET
  // (chargementComplet = true, bandeau disparu) ; revenir à « Tous les véhicules » ne
  // rechargeait alors plus rien et l'écran présentait ce sous-ensemble comme tout le parc.
  it('revenir à « Tous les véhicules » recharge au lieu de garder le sous-ensemble du filtre', () => {
    component.filterVehicle = '1';
    component.onFiltreServeur();
    expect(api.getRepairs).toHaveBeenCalledWith(expect.objectContaining({ vehicleId: 1 }));

    (api.getRepairs as jest.Mock).mockClear();
    (api.getRepairs as jest.Mock).mockImplementation((o?: any) =>
      of({ items: parc.slice(0, 100), totalCount: parc.length, page: o?.page ?? 1, pageSize: 100 }) as any);

    component.filterVehicle = '';
    component.onFiltreServeur();

    expect(api.getRepairs).toHaveBeenCalled();
    expect((api.getRepairs as jest.Mock).mock.calls[0][0]).toEqual(
      expect.objectContaining({ vehicleId: undefined }));
    expect(component.chargementComplet).toBe(false);
  });
});
