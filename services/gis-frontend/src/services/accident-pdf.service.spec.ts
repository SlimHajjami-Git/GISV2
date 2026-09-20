import { TestBed } from '@angular/core/testing';
import { AccidentPdfService } from './accident-pdf.service';
import { PdfExportService } from './pdf-export.service';
import { UserPreferencesService } from './user-preferences.service';
import { AccidentReportDto } from './api.service';

/**
 * Recette Karim du 18/09/2026 : « après avoir terminé les informations dans le
 * rapport, le fichier PDF n'a pas été généré avec les informations renseignées ».
 * Le document n'écrivait aucun champ des phases et posait un formulaire vierge.
 *
 * jsPDF est remplacé par un double qui note ce qui est DESSINÉ (textes et lignes
 * de tableaux) : on vérifie le contenu sans avoir à relire un PDF binaire.
 */
jest.mock('jspdf', () => {
  const captures = { textes: [] as string[] };
  class FauxDocument {
    internal = {
      pageSize: { getWidth: () => 210, getHeight: () => 297 },
      getNumberOfPages: () => 1,
    };
    setFont(): void { }
    setFontSize(): void { }
    setTextColor(): void { }
    setFillColor(): void { }
    setDrawColor(): void { }
    setLineWidth(): void { }
    setPage(): void { }
    rect(): void { }
    roundedRect(): void { }
    line(): void { }
    addImage(): void { }
    addPage(): void { }
    getTextWidth(): number { return 10; }
    splitTextToSize(texte: string): string[] { return String(texte).split('\n'); }
    text(contenu: string | string[]): void {
      captures.textes.push(...(Array.isArray(contenu) ? contenu : [contenu]));
    }
    output(): Blob { return new Blob(['%PDF-1.4'], { type: 'application/pdf' }); }
  }
  return { __esModule: true, default: FauxDocument, __captures: captures };
});

jest.mock('jspdf-autotable', () => {
  const tables: any[] = [];
  const autoTable = (doc: any, options: any) => {
    tables.push(options);
    doc.lastAutoTable = { finalY: (options.startY ?? 0) + 12 };
  };
  (autoTable as any).__tables = tables;
  return { __esModule: true, default: autoTable };
});

describe('AccidentPdfService — le PDF porte ce qui est saisi', () => {
  const captures = (jest.requireMock('jspdf') as any).__captures as { textes: string[] };
  const tables = ((jest.requireMock('jspdf-autotable') as any).default as any).__tables as any[];

  let service: AccidentPdfService;

  const pdfExportStub: Partial<PdfExportService> = {
    drawBrandHeader: jest.fn().mockReturnValue(38),
    drawBrandFooter: jest.fn(),
    clean: (texte: string) => texte,
    applyBrandToDocument: jest.fn().mockReturnValue('helvetica'),
    prepareBrandDocument: jest.fn().mockResolvedValue('helvetica'),
    get brandColors() { return { primary: [8, 64, 160], accent: [0, 112, 192], light: [241, 245, 249] } as any; },
  };

  const prefsStub = {
    formatCurrency: (montant: number | null | undefined) => `${(montant ?? 0).toFixed(2)} TND`,
    current: { currency: 'TND' },
  };

  const dossier = (surcharge: Partial<AccidentReportDto> = {}): AccidentReportDto => ({
    id: 42,
    companyId: 7,
    origin: 'manual',
    vehicleId: 49,
    gpsDeviceId: null,
    driverId: null,
    deviceUid: '',
    incidentAt: '2026-09-12T13:30:00Z',
    latitude: 0,
    longitude: 0,
    referenceCode: 'ACC-2026-014',
    vehicleLabel: 'Service 01 (GA-214-RK)',
    locationCommune: 'La Marsa',
    locationGovernorate: 'Tunis',
    locationRoadType: null,
    synthesisText: null,
    confidence: 100,
    story: null,
    reasons: null,
    indicators: null,
    weatherConditions: 'Pluie',
    roadConditions: 'Chaussée mouillée',
    policeReportNumber: 'PV-8891',
    mileageAtAccident: 84500,
    status: 'confirmed',
    decidedByUserId: null,
    decidedByName: null,
    decidedAt: null,
    initialDescription: 'Aile avant droite enfoncée',
    initialSeverity: 'moderate',
    damagedZones: ['avant', 'aile droite'],
    expertVisitedAt: '2026-09-15T00:00:00Z',
    expertName: 'M. Bouzid',
    expertCompany: 'STAR Expertise',
    expertAssessment: 'Réparation possible',
    expertEstimatedAmount: 1100,
    mechanicQuoteAt: '2026-09-16T00:00:00Z',
    mechanicName: 'Garage Central',
    mechanicQuotedAmount: 1250,
    repairStartedAt: '2026-09-17T00:00:00Z',
    repairCompletedAt: '2026-09-20T00:00:00Z',
    actualRepairCost: 1200,
    towDetectedAt: null,
    claimNumber: 'SIN-2026-778',
    claimSubmittedAt: '2026-09-21T00:00:00Z',
    claimApprovedAmount: 900,
    claimStatus: 'approved',
    thirdPartyInvolved: true,
    witnesses: null,
    additionalNotes: null,
    pdfReportUrl: null,
    documents: [
      { id: 1, documentType: 'repair_invoice', fileName: 'facture.pdf', fileUrl: '/uploads/x.pdf', fileSize: 10, mimeType: 'application/pdf', uploadedAt: '2026-09-20T10:00:00Z' },
    ],
    thirdParties: [
      { id: 1, name: 'Ali Ben Salah', phone: '20 111 222', vehiclePlate: '123 TU 4567', vehicleModel: 'Clio', insuranceCompany: 'COMAR', insuranceNumber: 'P-9981', insuranceExpiry: '2027-01-31T00:00:00Z' },
    ],
    updatedAt: '2026-09-21T09:00:00Z',
    vehicleExists: true,
    repairReference: 'REP-202609-0003',
    ...surcharge,
  } as AccidentReportDto);

  /** Tout ce que le document écrit, textes libres et cellules de tableaux confondus. */
  const contenu = (): string => {
    const cellules = tables.flatMap(t => [
      ...(t.head ?? []).flat(),
      ...(t.body ?? []).flat(),
    ]);
    return [...captures.textes, ...cellules].join(' | ');
  };

  beforeEach(() => {
    captures.textes.length = 0;
    tables.length = 0;
    TestBed.configureTestingModule({
      providers: [
        { provide: PdfExportService, useValue: pdfExportStub },
        { provide: UserPreferencesService, useValue: prefsStub },
      ],
    });
    service = TestBed.inject(AccidentPdfService);
  });

  it('écrit chaque phase renseignée : expertise, devis, réparation, assurance', () => {
    service.generate(dossier(), { withLocation: false });
    const texte = contenu();

    expect(texte).toContain('M. Bouzid');
    expect(texte).toContain('STAR Expertise');
    expect(texte).toContain('1100.00 TND');
    expect(texte).toContain('Garage Central');
    expect(texte).toContain('1250.00 TND');
    expect(texte).toContain('1200.00 TND');
    expect(texte).toContain('REP-202609-0003');
    expect(texte).toContain('SIN-2026-778');
    expect(texte).toContain('Approuvé');
    expect(texte).toContain('900.00 TND');
    expect(texte).toContain('Aile avant droite enfoncée');
    expect(texte).toContain('PV-8891');
  });

  it('liste les tiers impliqués et les pièces jointes', () => {
    service.generate(dossier(), { withLocation: false });
    const texte = contenu();

    expect(texte).toContain('Ali Ben Salah');
    expect(texte).toContain('123 TU 4567');
    expect(texte).toContain('COMAR');
    expect(texte).toContain('Facture de réparation');
    expect(texte).toContain('facture.pdf');
  });

  it("n'imprime plus de formulaire vierge quand tout est renseigné", () => {
    service.generate(dossier(), { withLocation: false });

    expect(contenu()).not.toContain('à compléter');
  });

  it('un bloc vide, et lui seul, porte une ligne à compléter', () => {
    service.generate(dossier({
      expertVisitedAt: null, expertName: null, expertCompany: null,
      expertAssessment: null, expertEstimatedAmount: null,
    }), { withLocation: false });
    const texte = contenu();

    expect(texte).toContain('à compléter');
    // Les autres blocs restent remplis : la ligne ne vaut que pour l'expertise.
    expect(texte).toContain('Garage Central');
    expect(texte).toContain('1200.00 TND');
  });

  it('bloc assurance vidé : il porte lui aussi la ligne à compléter', () => {
    // « Tiers impliqué : Non » était poussé quoi qu'il arrive : le bloc assurance
    // n'était JAMAIS vide et avait l'air renseigné alors que rien ne l'était.
    service.generate(dossier({
      claimNumber: null, claimSubmittedAt: null, claimStatus: null,
      claimApprovedAmount: null, thirdPartyInvolved: false,
    }), { withLocation: false });

    expect(contenu()).toContain('à compléter');
  });

  it('nomme le véhicule dans le corps, même sans libellé enregistré', () => {
    // vehicle_label est un instantané nullable : sur un dossier ancien, le rapport
    // remis à l'assureur ne nommait le véhicule nulle part.
    service.generate(dossier({ vehicleLabel: null }), { withLocation: false });
    const texte = contenu();

    expect(texte).toContain('Véhicule');
    expect(texte).toContain('#49');
  });

  it('société sans boîtier : ni coordonnées ni IMEI, mais le lieu déclaré reste', () => {
    service.generate(dossier(), { withLocation: false });
    const texte = contenu();

    expect(texte).not.toContain('Latitude');
    expect(texte).not.toContain('IMEI');
    expect(texte).toContain('La Marsa');
  });

  it('société équipée : le rapport garde les coordonnées et le boîtier', () => {
    service.generate(dossier({ latitude: 36.8188, longitude: 10.1657, deviceUid: '868020030' }), { withLocation: true });
    const texte = contenu();

    expect(texte).toContain('Latitude');
    expect(texte).toContain('36.818800');
    expect(texte).toContain('868020030');
  });

  it("reprend l'en-tête et le pied de page de marque communs aux autres rapports", () => {
    service.generate(dossier(), { withLocation: false });

    expect(pdfExportStub.drawBrandHeader).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({ title: 'Rapport de sinistre' }),
    );
    expect(pdfExportStub.drawBrandFooter).toHaveBeenCalled();
  });

  it('version attendue : les ressources de marque sont chargées avant de dessiner', async () => {
    await service.generateAsync(dossier(), { withLocation: false });

    expect(pdfExportStub.prepareBrandDocument).toHaveBeenCalled();
  });
});
