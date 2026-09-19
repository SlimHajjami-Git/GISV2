import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { CarburantComponent } from './carburant.component';
import { ApiService } from '../services/api.service';
import { AuthService } from '../services/auth.service';
import { ExtractionFacture, ResultatScanFacture } from './shared/scan-facture.component';

/**
 * Écran Carburant — branchement du scan de ticket (19/09/2026).
 *
 * Le bouton « Scanner un ticket » remplit le formulaire de saisie manuelle à
 * partir du ticket de station. Trois pièges tenus par ces tests : le total
 * imprimé (timbre compris) ne doit pas être écrasé par volume × prix, un
 * matricule non reconnu ne doit pas remplir le champ au hasard, et le
 * formulaire doit rester utilisable à la main quand le scan est indisponible.
 */
describe('CarburantComponent — scan d’un ticket de station', () => {
  let component: CarburantComponent;
  let fixture: any;
  let api: ApiService;

  const vehicules = [
    { id: 5, name: 'Camion 12', plate: 'AB-123-CD', type: 'truck', status: 'active', hasGps: true, isOnline: true },
    { id: 6, name: 'Utilitaire 3', plate: 'EF-456-GH', type: 'van', status: 'active', hasGps: true, isOnline: true }
  ];

  const typesCarburant = [
    { id: 25, code: 'diesel', name: 'Diesel', isSystem: true },
    { id: 27, code: 'sans_plomb', name: 'Essence Sans Plomb', isSystem: true }
  ];

  /**
   * Les 12 matricules RÉELS du parc de la société 7, relevés en base le 19/09/2026.
   * C'est sur eux que le repli par inclusion rapprochait n'importe quoi : « G »
   * rendait GA-214-RK, « 12 » GK-128-ZF et « 694 » GL-694-PN.
   */
  const matriculesReels = [
    'GA-214-RK', 'GB-587-TM', 'GC-936-LP', 'GD-421-NV', 'GE-768-HJ', 'GF-305-WQ',
    'GG-852-BD', 'GH-619-XC', 'GJ-473-KS', 'GK-128-ZF', 'GL-694-PN', 'GM-347-TR'
  ];
  const parcReel = matriculesReels.map((plate, i) => ({
    id: 32 + i, name: 'Véhicule ' + (i + 1), plate, type: 'car',
    status: 'active', hasGps: true, isOnline: true
  }));

  /** Ticket type : 40 L à 2,500 = 100,00 + 1,00 de frais → 101,00 imprimé. */
  const ticket = (patch: Partial<ExtractionFacture> = {}, receiptUrl = '/uploads/invoices/7/ticket.jpg'): ResultatScanFacture => ({
    extraction: {
      supplierName: 'Station du Rhône', invoiceNumber: 'T-2026-88', date: '2026-09-12',
      amountHT: null, amountTVA: null, amountTTC: 101, total: 101, currency: 'EUR',
      category: 'fuel', vehiclePlate: 'AB-123-CD',
      description: 'Gasoil', descriptionComplete: 'Station du Rhône — Gasoil',
      confidence: 'high', liters: 40, pricePerLiter: 2.5, isCreditNote: false, items: [],
      ...patch
    },
    receiptUrl,
    quota: { used: 3, limit: 20, remaining: 17 }
  });

  const preparer = async (quota = { used: 3, limit: 20, remaining: 17 }) => {
    // La barre de l'écran injecte le service d'export PDF, qui précharge le logo
    // par fetch() — absent de jsdom. L'échec de préchargement est déjà prévu.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, CarburantComponent],
      providers: [ApiService]
    }).compileComponents();

    fixture = TestBed.createComponent(CarburantComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    // La brique de scan n'affiche son bouton que pour un utilisateur connecté.
    jest.spyOn(TestBed.inject(AuthService), 'getCurrentUserSync').mockReturnValue({ id: 1 } as any);

    jest.spyOn(api, 'getFuelTypes').mockReturnValue(of(typesCarburant) as any);
    jest.spyOn(api, 'getVehicles').mockReturnValue(of(vehicules) as any);
    jest.spyOn(api, 'getFuelEntries').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 100, totalPages: 0 }) as any);
    jest.spyOn(api, 'getCurrentActiveFuelPrices').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'getScanQuota').mockReturnValue(of(quota) as any);
  };

  beforeEach(async () => {
    jest.restoreAllMocks();
    await preparer();
  });

  it('le ticket remplit matricule, date, volume, prix et montant', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket());

    expect(component.manualEntry.vehiclePlate).toBe('AB-123-CD');
    expect(component.manualEntry.invoiceDate).toBe('2026-09-12');
    expect(component.manualEntry.volume).toBe(40);
    expect(component.manualEntry.pricePerLiter).toBe(2.5);
    // Le total imprimé fait foi : 101,00 et non 40 × 2,500 = 100,00.
    expect(component.manualEntry.totalAmount).toBe(101);
    expect(component.totalAmountTouched).toBe(true);
    // « Gasoil » écrit sur le ticket → Diesel ; rien d'écrit → l'utilisateur choisit.
    expect(component.manualEntry.fuelTypeId).toBe(25);
    expect(component.scanTicket.champs).toEqual(
      expect.arrayContaining(['matricule', 'date', 'volume', 'prix au litre', 'type', 'montant'])
    );
    expect(component.scanTicket.receiptUrl).toBe('/uploads/invoices/7/ticket.jpg');
  });

  it('volume et prix absents du ticket : champs laissés vides, rien n’est recalculé', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({ liters: null, pricePerLiter: null }));

    expect(component.manualEntry.volume).toBeNull();
    expect(component.manualEntry.pricePerLiter).toBeNull();
    expect(component.manualEntry.totalAmount).toBe(101);
    expect(component.scanTicket.champs).not.toContain('volume');
  });

  it('total illisible : le calcul automatique de l’écran reprend la main', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({ total: null, amountTTC: null }));

    expect(component.totalAmountTouched).toBe(false);
    expect(component.manualEntry.totalAmount).toBe(100);   // 40 × 2,500

    // Le mécanisme d'origine reste actif : corriger le volume met le total à jour.
    component.manualEntry.volume = 50;
    component.onVolumeOrPriceChange();
    expect(component.manualEntry.totalAmount).toBe(125);
  });

  it('type de carburant non imprimé : l’écran ne devine pas et le dit', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({ description: 'Plein', descriptionComplete: 'Station du Rhône — Plein' }));
    fixture.detectChanges();

    expect(component.manualEntry.fuelTypeId).toBeNull();
    expect(component.scanTicket.typeNonLu).toBe(true);
    expect(fixture.nativeElement.querySelector('.scan-banner').textContent).toContain('Type de carburant absent');
  });

  it('type lu sur une ligne du ticket : « SP95 » donne Essence Sans Plomb', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({
      description: null, descriptionComplete: 'Station du Rhône',
      items: [{ label: 'SP95 40 L', amount: 101, category: 'fuel' }]
    }));

    expect(component.manualEntry.fuelTypeId).toBe(27);
    expect(component.scanTicket.typeNonLu).toBe(false);
  });

  it('matricule inconnu : champ laissé vide et matricule lu affiché', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({ vehiclePlate: 'ZZ-999-ZZ' }));
    fixture.detectChanges();

    expect(component.manualEntry.vehiclePlate).toBe('');
    expect(component.scanTicket.plaqueNonReconnue).toBe('ZZ-999-ZZ');
    const bandeau = fixture.nativeElement.querySelector('.scan-banner').textContent;
    expect(bandeau).toContain('ZZ-999-ZZ');
    expect(bandeau).toContain('introuvable');
  });

  /**
   * Rejeu de la fonction de rapprochement sur le parc RÉEL de la société 7.
   * Deux exigences tenues ici : un matricule complet se reconnaît quelle que soit
   * l'écriture, et AUCUN fragment ne désigne plus un véhicule.
   */
  it('parc réel de la société 7 : égalité reconnue, fragment refusé', () => {
    fixture.detectChanges();
    component.vehicles = parcReel as any;
    const rapprocher = (lu: string) => (component as any).trouverVehiculeParMatricule(lu);

    for (const plaque of matriculesReels) {
      // Tel quel, en minuscules, avec des espaces, collé : c'est le même véhicule, sûrement.
      for (const variante of [plaque, plaque.toLowerCase(), plaque.replace(/-/g, ' '), plaque.replace(/-/g, '')]) {
        const r = rapprocher(variante);
        expect(r.vehicule?.plate).toBe(plaque);
        expect(r.exact).toBe(true);
      }
    }

    // Les fragments qui remplissaient le champ au hasard ne rapprochent plus rien.
    for (const fragment of ['G', '1', '12', '694', 'GA', 'RK', '214', '-', '']) {
      expect(rapprocher(fragment).vehicule).toBeNull();
    }

    // Et aucun matricule réel n'en attrape un autre au passage.
    for (const plaque of matriculesReels) {
      expect(rapprocher(plaque).vehicule?.plate).toBe(plaque);
    }
  });

  it('fragment de matricule sur le ticket : champ laissé vide, fragment affiché', () => {
    fixture.detectChanges();
    component.vehicles = parcReel as any;

    component.onTicketScanne(ticket({ vehiclePlate: '12' }));
    fixture.detectChanges();

    expect(component.manualEntry.vehiclePlate).toBe('');
    expect(component.scanTicket.plaqueNonReconnue).toBe('12');
    expect(fixture.nativeElement.querySelector('.scan-banner').textContent).toContain('trop incertain');
  });

  it('matricule incomplet mais sans ambiguïté : rempli ET annoncé', () => {
    fixture.detectChanges();
    component.vehicles = parcReel as any;

    // Dernière lettre avalée par l'OCR : un seul véhicule du parc peut répondre.
    component.onTicketScanne(ticket({ vehiclePlate: 'GA-214-R' }));
    fixture.detectChanges();

    expect(component.manualEntry.vehiclePlate).toBe('GA-214-RK');
    expect(component.scanTicket.plaqueRapprochee).toBe('GA-214-R');
    expect(component.scanTicket.plaqueRetenue).toBe('GA-214-RK');
    const bandeau = fixture.nativeElement.querySelector('.scan-banner').textContent;
    expect(bandeau).toContain('GA-214-R');
    expect(bandeau).toContain('rapproché');
  });

  it('deux véhicules possibles : aucun n’est choisi à la place de l’utilisateur', () => {
    fixture.detectChanges();
    component.vehicles = [...parcReel, { ...parcReel[0], id: 99, plate: 'GA-214-RX' }] as any;

    component.onTicketScanne(ticket({ vehiclePlate: 'GA-214-R' }));

    expect(component.manualEntry.vehiclePlate).toBe('');
    expect(component.scanTicket.plaqueNonReconnue).toBe('GA-214-R');
    expect(component.scanTicket.plaqueRapprochee).toBe('');
  });

  it('matricule exact : rempli sans avertissement inutile', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({ vehiclePlate: 'ab 123 cd' }));

    expect(component.manualEntry.vehiclePlate).toBe('AB-123-CD');
    expect(component.scanTicket.plaqueRapprochee).toBe('');
    expect(component.scanTicket.plaqueNonReconnue).toBe('');
  });

  it('type de carburant déjà choisi : pas remplacé sans accord', () => {
    fixture.detectChanges();
    component.manualEntry.fuelTypeId = 27;                 // Essence, choisi à la main
    const refus = jest.spyOn(window, 'confirm').mockReturnValue(false);

    component.onTicketScanne(ticket());

    expect(refus).toHaveBeenCalled();
    expect(component.manualEntry.fuelTypeId).toBe(27);     // le « Gasoil » du ticket n'a rien écrasé
    expect(component.scanTicket.actif).toBe(false);
  });

  it('date déjà corrigée : pas remplacée sans accord', () => {
    fixture.detectChanges();
    component.manualEntry.invoiceDate = '2026-08-01';
    const refus = jest.spyOn(window, 'confirm').mockReturnValue(false);

    component.onTicketScanne(ticket());

    expect(refus).toHaveBeenCalled();
    expect(component.manualEntry.invoiceDate).toBe('2026-08-01');
  });

  it('écran neuf : la date du jour posée d’office ne déclenche aucune question', () => {
    fixture.detectChanges();
    const demande = jest.spyOn(window, 'confirm').mockReturnValue(true);

    component.onTicketScanne(ticket());

    expect(demande).not.toHaveBeenCalled();
    expect(component.manualEntry.invoiceDate).toBe('2026-09-12');
  });

  it('date absente du ticket : la date en place est signalée comme non lue', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({ date: null }));
    fixture.detectChanges();

    expect(component.scanTicket.dateNonLue).toBe(true);
    expect(component.scanTicket.champs).not.toContain('date');
    expect(fixture.nativeElement.querySelector('.scan-banner').textContent).toContain('Date absente');
  });

  it('document qui n’est pas un plein : avertissement, sans blocage', () => {
    fixture.detectChanges();

    component.onTicketScanne(ticket({ category: 'maintenance', description: 'Vidange et filtres' }));
    fixture.detectChanges();

    expect(component.scanTicket.categorieInattendue).toBe('entretien');
    expect(fixture.nativeElement.querySelector('.scan-banner').textContent).toContain('entretien');
    // Rien n'est verrouillé : le type reste à choisir, le reste est enregistrable.
    component.manualEntry.fuelTypeId = 25;
    expect(component.isManualEntryValid()).toBe(true);
  });

  it('saisie déjà commencée : rien n’est remplacé sans accord', () => {
    fixture.detectChanges();
    component.manualEntry.volume = 30;
    const refus = jest.spyOn(window, 'confirm').mockReturnValue(false);

    component.onTicketScanne(ticket());

    expect(refus).toHaveBeenCalled();
    expect(component.manualEntry.volume).toBe(30);
    expect(component.manualEntry.vehiclePlate).toBe('');
    expect(component.scanTicket.actif).toBe(false);
  });

  it('analyse en panne : le document reste rattaché et la saisie continue', () => {
    fixture.detectChanges();
    component.manualEntry.vehiclePlate = 'EF-456-GH';

    component.onEchecScan({ message: 'IA indisponible', receiptUrl: '/uploads/invoices/7/panne.jpg' });
    fixture.detectChanges();

    expect(component.manualEntry.vehiclePlate).toBe('EF-456-GH');   // saisie intacte
    expect(component.scanTicket.echec).toBe(true);
    expect(fixture.nativeElement.querySelector('.scan-banner').textContent).toContain('Ticket non analysé');
  });

  it('le justificatif et la station du ticket partent avec l’entrée', () => {
    fixture.detectChanges();
    const creation = jest.spyOn(api, 'createFuelEntry').mockReturnValue(of({ id: 1 }) as any);
    jest.spyOn(window, 'alert').mockImplementation(() => {});

    component.onTicketScanne(ticket());
    component.saveManualEntry();

    const envoye = creation.mock.calls[0][0] as any;
    expect(envoye.totalAmount).toBe(101);
    expect(envoye.stationName).toBe('Station du Rhône');
    expect(envoye.invoiceNumber).toBe('T-2026-88');
    expect(envoye.notes).toContain('/uploads/invoices/7/ticket.jpg');
  });

  it('quota mensuel atteint : bouton verrouillé, formulaire toujours utilisable', async () => {
    TestBed.resetTestingModule();
    await preparer({ used: 20, limit: 20, remaining: 0 });
    fixture.detectChanges();

    const bouton: HTMLButtonElement = fixture.nativeElement.querySelector('button.btn-scan');
    expect(bouton).toBeTruthy();
    expect(bouton.disabled).toBe(true);
    expect(bouton.getAttribute('title')).toContain('Quota mensuel atteint');

    // La saisie à la main ne dépend pas du scan.
    component.manualEntry.vehiclePlate = 'AB-123-CD';
    component.manualEntry.fuelTypeId = 25;
    component.manualEntry.totalAmount = 101;
    expect(component.isManualEntryValid()).toBe(true);
  });
});
