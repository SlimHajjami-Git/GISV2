import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of } from 'rxjs';
import { AccidentReportComponent } from './accident-report.component';
import { ApiService, AccidentReportDocumentDto } from '../services/api.service';

/**
 * Contre-relecture du 18/09/2026 — le « PDF expert » de la déclaration est désormais
 * rangé en PIÈCE JOINTE du dossier pour ne plus écraser le rapport produit par Calypso.
 * Mais la fiche ne rendait que les documents de type « photo » : le document venait
 * d'être envoyé et n'était plus atteignable nulle part dans l'application.
 */
describe('AccidentReportComponent — pièces jointes du dossier', () => {
  let component: AccidentReportComponent;
  let fixture: any;
  let api: ApiService;

  const piece: AccidentReportDocumentDto = {
    id: 7,
    documentType: 'expert_report',
    fileName: 'expertise-assurance.pdf',
    fileUrl: '/uploads/accident-reports/42/expertise-assurance.pdf',
    fileSize: 240000,
    mimeType: 'application/pdf',
    uploadedAt: '2026-09-18T10:00:00Z',
  };

  beforeEach(async () => {
    // Le service PDF précharge le logo par fetch(), absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, AccidentReportComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(AccidentReportComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    // Dossier confirmé ouvert par un administrateur : le bloc est actif.
    component.isAdmin = true;
    component.status = 'confirmed';
    component.accidentEventId = 42;
    // regeneratePdf est neutralisé : joindre une pièce REGÉNÈRE bien le rapport (le PDF
    // du dossier liste ses pièces jointes), et le laisser courir enverrait un jsPDF dans
    // jsdom. Ce que ces tests prouvent est la ROUTE choisie par le bouton, pas l'absence
    // de régénération.
    jest.spyOn(component, 'regeneratePdf').mockResolvedValue(undefined);
  });

  it('affiche les pièces jointes qui ne sont pas des photos', () => {
    component.attachments = [piece];
    fixture.detectChanges();

    const lien: HTMLAnchorElement | null = fixture.nativeElement.querySelector('.doc-list a');
    expect(lien).not.toBeNull();
    expect(lien!.getAttribute('href')).toBe(piece.fileUrl);
    expect(lien!.textContent).toContain('expertise-assurance.pdf');
    expect(lien!.textContent).toContain("Rapport d'expertise");
  });

  it('envoie le document sur /documents, jamais sur /upload-pdf', () => {
    const document = jest.spyOn(api, 'uploadAccidentDocument')
      .mockReturnValue(of({ documentId: 9, fileUrl: '/uploads/accident-reports/42/constat.pdf' }));
    const rapport = jest.spyOn(api, 'uploadAccidentReportPdf');
    const fichier = new File(['%PDF-1.4'], 'constat.pdf', { type: 'application/pdf' });

    component.attachmentType = 'police_report';
    component.onAttachmentFile({ target: { files: [fichier], value: '' } } as any);

    expect(document).toHaveBeenCalledWith(42, fichier, 'police_report');
    expect(rapport).not.toHaveBeenCalled();
    expect(component.attachments.map((d) => d.fileName)).toEqual(['constat.pdf']);
  });

  it('ne propose que les formats que le serveur accepte', () => {
    // « image/* » laissait passer un .heic (format par défaut d'un iPhone), refusé
    // ensuite par le serveur : l'utilisateur choisissait un fichier pour rien.
    // ngOnInit relit les droits depuis AuthService : les remettre APRÈS le premier
    // rendu, sinon le bloc d'envoi n'est pas affiché.
    fixture.detectChanges();
    component.isAdmin = true;
    component.status = 'confirmed';
    component.accidentEventId = 42;
    fixture.detectChanges();

    const champ: HTMLInputElement | null = fixture.nativeElement.querySelector('.doc-block input[type="file"]');
    expect(champ).not.toBeNull();
    const accepte = champ!.getAttribute('accept') || '';
    expect(accepte).not.toContain('image/*');
    expect(accepte).toContain('.webp');
  });

  it('retire une pièce jointe du dossier', () => {
    const suppression = jest.spyOn(api, 'deleteAccidentDocument').mockReturnValue(of(undefined as any));
    jest.spyOn(window, 'confirm').mockReturnValue(true);
    component.attachments = [piece];

    component.removeAttachment(piece);

    expect(suppression).toHaveBeenCalledWith(42, 7);
    expect(component.attachments).toEqual([]);
  });
});

/**
 * Relecture du 19/09/2026 — le rapport de sinistre affiché à l'écran dessinait encore
 * le pin fait main à DEUX endroits (en-tête et bloc de signature) et portait la
 * baseline anglaise « Fleet Analytics », alors que c'est précisément le document que
 * Karim désignait en demandant « le logo de Calypso dans le rapport ». Ces tests
 * verrouillent les deux emplacements et l'absence de la baseline.
 */
describe('AccidentReportComponent — marque du document', () => {
  let fixture: any;

  beforeEach(async () => {
    // Le service PDF précharge le logo par fetch(), absent de jsdom.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, AccidentReportComponent],
      providers: [ApiService],
    }).compileComponents();

    fixture = TestBed.createComponent(AccidentReportComponent);
    fixture.detectChanges();
  });

  it("pose le vrai logo en en-tête ET dans la signature, et plus aucun pin dessiné à la main", () => {
    const entete: HTMLImageElement | null = fixture.nativeElement.querySelector('.doc-brand img.brand-logo-img');
    expect(entete).not.toBeNull();
    expect(entete!.getAttribute('src')).toBe('/assets/calypso-logo.svg');

    const signature: HTMLImageElement | null = fixture.nativeElement.querySelector('.sign-brand img.sign-logo-img');
    expect(signature).not.toBeNull();
    expect(signature!.getAttribute('src')).toBe('/assets/calypso-logo.svg');

    // Le pin était un SVG en ligne avec ses propres dégradés, un par emplacement.
    const html: string = fixture.nativeElement.innerHTML;
    expect(html).not.toContain('pinGradHdr');
    expect(html).not.toContain('pinGradFtr');
  });

  it("n'écrit plus la baseline anglaise « Fleet Analytics »", () => {
    expect(fixture.nativeElement.textContent).not.toContain('Fleet Analytics');
  });

  it("n'affiche le logo Calypso que pour la marque Calypso", () => {
    // Garde-fou : brandName vient de environment.ts, en copie locale par serveur.
    // Un déploiement « Bougeo » ne doit pas imprimer le logo d'un concurrent sur un
    // document que SON client transmet à son assureur.
    const composant = fixture.componentInstance;
    expect(composant.estMarqueCalypso).toBe(true);

    composant.estMarqueCalypso = false; // readonly n'existe qu'à la compilation
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.doc-brand img.brand-logo-img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.sign-brand img.sign-logo-img')).toBeNull();
    expect(fixture.nativeElement.querySelector('.doc-brand .brand-name')!.textContent).toContain(composant.brandName);
  });
});
