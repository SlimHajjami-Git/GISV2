import {
  RevueFacture, revueDepuisScan, scanDepuisRevue, VerifierFactureComponent
} from './verifier-facture.component';
import { ResultatScanFacture } from './scan-facture.component';

/**
 * Le panneau « Vérifier la facture », partagé par Dépenses, Entretien et Réparations
 * depuis le 29/09/2026. Il n'appelle aucune API : ce qui compte ici est qu'il ne
 * PERDE rien en route et qu'il n'autorise pas une validation impossible.
 */

const scan = (over: Partial<ResultatScanFacture['extraction']> = {}): ResultatScanFacture => ({
  receiptUrl: '/uploads/invoices/f1.jpg',
  quota: null,
  extraction: {
    supplierName: 'STAFIM SA',
    invoiceNumber: 'FVAV454639',
    date: '2026-09-18',
    amountHT: 600.5,
    amountTVA: 114.297,
    amountTTC: 714.797,
    total: 714.797,
    currency: 'TND',
    category: 'maintenance',
    vehiclePlate: '541 TUN 77U240',
    description: 'Révision',
    descriptionComplete: 'STAFIM SA — Révision, filtres, plaquettes',
    confidence: 'high',
    liters: null,
    pricePerLiter: null,
    isCreditNote: false,
    items: [
      { label: 'Revision - Operations System', amount: 46.2, category: 'maintenance' },
      { label: 'Filtre a huile', amount: 23.5, category: 'maintenance' }
    ],
    ...over
  }
});

describe('VerifierFactureComponent — aller-retour scan ⇄ revue', () => {
  it('ne perd AUCUN champ de l’extraction quand rien n’est corrigé', () => {
    const origine = scan();
    const relu = scanDepuisRevue(revueDepuisScan(origine, '7'), origine);

    // Les champs que le panneau ne montre pas doivent traverser INTACTS : ils
    // alimentent l'écran Carburant (litres, prix au litre) et les totaux HT/TVA.
    expect(relu.extraction.amountHT).toBe(600.5);
    expect(relu.extraction.amountTVA).toBe(114.297);
    expect(relu.extraction.amountTTC).toBe(714.797);
    expect(relu.extraction.currency).toBe('TND');
    expect(relu.extraction.liters).toBeNull();
    expect(relu.extraction.pricePerLiter).toBeNull();
    expect(relu.extraction.description).toBe('Révision');
    expect(relu.extraction.vehiclePlate).toBe('541 TUN 77U240');
    // Et ceux qu'il montre reviennent à l'identique.
    expect(relu.extraction.total).toBe(714.797);
    expect(relu.extraction.supplierName).toBe('STAFIM SA');
    expect(relu.extraction.invoiceNumber).toBe('FVAV454639');
    expect(relu.extraction.items).toEqual(origine.extraction.items);
    expect(relu.receiptUrl).toBe('/uploads/invoices/f1.jpg');
  });

  it('rend les corrections de l’utilisateur, pas les valeurs lues', () => {
    const origine = scan();
    const modele = revueDepuisScan(origine, '7');
    modele.supplierName = 'GARAGE DU LAC';
    modele.amount = 800;
    modele.items[0].amount = 50;
    modele.items.push({ label: 'Main d’œuvre', amount: 120, category: 'repair' });

    const relu = scanDepuisRevue(modele, origine);
    expect(relu.extraction.supplierName).toBe('GARAGE DU LAC');
    expect(relu.extraction.total).toBe(800);
    expect(relu.extraction.items.map(l => l.amount)).toEqual([50, 23.5, 120]);
  });

  it('jette les lignes vides et normalise, sans toucher à l’extraction d’origine', () => {
    const origine = scan();
    const modele = revueDepuisScan(origine, '7');
    modele.items.push({ label: '   ', amount: 0, category: 'other' });   // ligne ajoutée puis laissée vide
    modele.items[1].label = '  Filtre a huile  ';

    const relu = scanDepuisRevue(modele, origine);
    expect(relu.extraction.items.length).toBe(2);
    expect(relu.extraction.items[1].label).toBe('Filtre a huile');
    // Le modèle est une COPIE : l'extraction d'origine n'a pas bougé.
    expect(origine.extraction.items.length).toBe(2);
    expect(origine.extraction.items[1].label).toBe('Filtre a huile');
  });

  it('un avoir lu par l’IA ouvre la revue sur la catégorie « Avoir fournisseur »', () => {
    const modele = revueDepuisScan(scan({ isCreditNote: true, category: 'insurance' }), '7');
    expect(modele.category).toBe('credit_note');
    expect(modele.creditNote).toBe(true);
  });

  it('les null de l’IA deviennent des chaînes vides, jamais « null » à l’écran', () => {
    const modele = revueDepuisScan(scan({
      supplierName: null, invoiceNumber: null, confidence: null, vehiclePlate: null, descriptionComplete: ''
    }), '');
    expect(modele.supplierName).toBe('');
    expect(modele.invoiceNumber).toBe('');
    expect(modele.confidence).toBe('');
    expect(modele.vehiclePlate).toBe('');
    expect(modele.description).toBe('');
  });
});

describe('VerifierFactureComponent — garde de validation', () => {
  const panneau = (modele: RevueFacture, over: Partial<VerifierFactureComponent> = {}) => {
    const c = new VerifierFactureComponent();
    c.modele = modele;
    Object.assign(c, over);
    return c;
  };

  it('sans véhicule choisi, « Enregistrer » reste grisé', () => {
    const c = panneau(revueDepuisScan(scan(), ''));
    expect(c.peutValider()).toBe(false);
  });

  it('Dépenses exige un montant positif ; Entretien et Réparations non', () => {
    const sansTotal = () => revueDepuisScan(scan({ total: null }), '7');
    // Dépenses : le serveur refuse zéro, le bouton doit rester grisé.
    expect(panneau(sansTotal()).peutValider()).toBe(false);
    // Entretien / Réparations : le panneau ne fait que préparer un formulaire,
    // une facture au total illisible doit pouvoir passer.
    expect(panneau(sansTotal(), { montantObligatoire: false }).peutValider()).toBe(true);
  });

  it('pendant l’enregistrement, le bouton se verrouille', () => {
    const c = panneau(revueDepuisScan(scan(), '7'), { enregistrement: true });
    expect(c.peutValider()).toBe(false);
  });

  it('véhicule imposé : la plaque lue sert d’alerte, elle ne bloque pas', () => {
    const modele = revueDepuisScan(scan({ vehiclePlate: '999 TU 1111' }), '7');
    const c = panneau(modele, {
      vehiculeVerrouille: true,
      vehicules: [{ id: 7, plate: '123 TU 4567', name: 'Camion 12' }]
    });
    expect(c.memePlaqueQueLeVehicule()).toBe(false);   // l'avertissement s'affiche
    expect(c.peutValider()).toBe(true);                // mais on peut continuer
  });

  it('plaque identique à la ponctuation près : aucune alerte', () => {
    const modele = revueDepuisScan(scan({ vehiclePlate: '123-tu-4567' }), '7');
    const c = panneau(modele, {
      vehiculeVerrouille: true,
      vehicules: [{ id: 7, plate: '123 TU 4567', name: 'Camion 12' }]
    });
    expect(c.memePlaqueQueLeVehicule()).toBe(true);
  });

  it('somme des lignes ≠ total : l’écart est signalé', () => {
    const modele = revueDepuisScan(scan(), '7');   // lignes 46,2 + 23,5 = 69,7 contre 714,797
    const c = panneau(modele);
    expect(c.sommeLignes()).toBeCloseTo(69.7, 3);
    expect(c.sommeDiffere()).toBe(true);
    modele.amount = 69.7;
    expect(c.sommeDiffere()).toBe(false);
  });

  it('les avertissements d’avoir ne s’affichent que si l’écran connaît cette notion', () => {
    const modele = revueDepuisScan(scan({ isCreditNote: true }), '7');
    // Réparations / Entretien : pas de catégorie « avoir » → aucun message.
    expect(panneau(modele).messageAvoirDetecte).toBe(false);
    expect(panneau(modele).messageAvoirQuitte).toBe(false);
    // Dépenses : catégorie conservée → message « c'est bien un avoir ».
    const depenses = panneau(modele, { categorieAvoir: 'credit_note' });
    expect(depenses.messageAvoirDetecte).toBe(true);
    // Catégorie changée à la main → message inverse, le montant partirait en dépense.
    modele.category = 'repair';
    expect(depenses.messageAvoirDetecte).toBe(false);
    expect(depenses.messageAvoirQuitte).toBe(true);
  });
});
