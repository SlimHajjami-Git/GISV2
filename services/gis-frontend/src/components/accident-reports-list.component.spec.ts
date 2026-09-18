import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';
import { RouterTestingModule } from '@angular/router/testing';
import { FormsModule } from '@angular/forms';
import { of, throwError } from 'rxjs';
import { AccidentReportsListComponent } from './accident-reports-list.component';
import { AccidentDecisionModalComponent } from './shared/accident-decision-modal.component';
import { ApiService } from '../services/api.service';

/**
 * Contre-relecture du 18/09/2026 — défaut BLOQUANT : le « PDF expert (facultatif) » de
 * la déclaration manuelle partait sur POST /:id/upload-pdf, donc dans
 * accident_events.pdf_report_url, le MÊME champ que le rapport produit par Calypso.
 * Comme ce rapport est désormais régénéré après chaque phase enregistrée (et l'ancien
 * fichier effacé du disque), le document de l'expert disparaissait sans un mot.
 *
 * Le fichier du client est une PIÈCE JOINTE du dossier (documentType « expert_report »).
 */
describe('AccidentReportsListComponent — PDF fourni par le client', () => {
  let component: AccidentReportsListComponent;
  let api: ApiService;

  beforeEach(async () => {
    // L'écran embarque la modale de décision, donc le service PDF, qui précharge le
    // logo par fetch() — absent de jsdom. Un échec de préchargement est déjà prévu.
    (globalThis as any).fetch = jest.fn(() => Promise.resolve({ ok: false }));

    await TestBed.configureTestingModule({
      imports: [HttpClientTestingModule, RouterTestingModule, FormsModule, AccidentReportsListComponent],
      providers: [ApiService],
    }).compileComponents();

    const fixture = TestBed.createComponent(AccidentReportsListComponent);
    component = fixture.componentInstance;
    api = TestBed.inject(ApiService);

    jest.spyOn(api, 'listAccidentEvents').mockReturnValue(of({ items: [], totalCount: 0, page: 1, pageSize: 25 } as any));
    jest.spyOn(api, 'getVehicles').mockReturnValue(of([]) as any);
    jest.spyOn(api, 'createManualAccident').mockReturnValue(of({ accidentEventId: 42 }));
  });

  function remplirFormulaire(): void {
    component.manualForm.vehicleId = 49 as any;
    component.manualForm.incidentAt = '2026-09-10T08:00';
  }

  it('range le fichier joint en pièce jointe du dossier, jamais dans le rapport PDF', () => {
    const document = jest.spyOn(api, 'uploadAccidentDocument').mockReturnValue(of({ documentId: 7, fileUrl: '/uploads/accident-reports/42/x.pdf' }));
    const rapport = jest.spyOn(api, 'uploadAccidentReportPdf');
    const fichier = new File(['%PDF-1.4'], 'rapport-expert.pdf', { type: 'application/pdf' });

    remplirFormulaire();
    component.manualPdf = fichier;
    component.submitManual();

    expect(document).toHaveBeenCalledWith(42, fichier, 'expert_report');
    expect(rapport).not.toHaveBeenCalled();
  });

  it('sans fichier joint : aucun envoi de document', () => {
    const document = jest.spyOn(api, 'uploadAccidentDocument');

    remplirFormulaire();
    component.manualPdf = null;
    component.submitManual();

    expect(api.createManualAccident).toHaveBeenCalled();
    expect(document).not.toHaveBeenCalled();
  });

  it("envoi échoué : un non-administrateur est renvoyé vers un administrateur", () => {
    // Déclarer un sinistre n'est pas réservé aux administrateurs, JOINDRE une pièce l'est :
    // le message promettait à tous un bouton que seuls les administrateurs voient.
    jest.spyOn(api, 'uploadAccidentDocument').mockReturnValue(throwError(() => new Error('502')));
    const message = jest.spyOn(window, 'alert').mockImplementation(() => {});

    component.isAdmin = false;
    remplirFormulaire();
    component.manualPdf = new File(['%PDF-1.4'], 'rapport-expert.pdf', { type: 'application/pdf' });
    component.submitManual();

    expect(message).toHaveBeenCalledWith(expect.stringContaining('Demandez à un administrateur'));

    message.mockClear();
    component.isAdmin = true;
    remplirFormulaire();
    component.manualPdf = new File(['%PDF-1.4'], 'rapport-expert.pdf', { type: 'application/pdf' });
    component.submitManual();

    expect(message).toHaveBeenCalledWith(expect.stringContaining('Pièces jointes'));
  });
});

/**
 * La modale de décision est montée par la mise en page de cet écran (app-layout) : elle
 * attache un PDF au dossier à la confirmation d'un accident détecté. Sans abonnement
 * boîtier, ce PDF imprimait latitude, longitude, IMEI et score de confiance — des champs
 * sans aucune donnée derrière eux.
 */
describe('AccidentDecisionModalComponent — PDF attaché à la confirmation', () => {
  function modale(moduleMonitoring: boolean) {
    const rapport = { accidentEventId: 42 } as any;
    const pdf = { generate: jest.fn(() => new Blob()) };
    const api = {
      getAccidentReport: jest.fn(() => of(rapport)),
      uploadAccidentReportPdf: jest.fn(() => of({})),
    };
    const permissions = { getSubscriptionFeatures: () => ({ moduleMonitoring }) };
    const composant = new AccidentDecisionModalComponent(
      {} as any, {} as any, api as any, pdf as any, permissions as any, {} as any, {} as any);
    return { composant, pdf, rapport };
  }

  it('sans boîtier : le PDF ne porte pas la localisation', () => {
    const { composant, pdf, rapport } = modale(false);

    (composant as any).attachAutoPdf(42);

    expect(pdf.generate).toHaveBeenCalledWith(rapport, { withLocation: false });
  });

  it('avec boîtier : la localisation reste imprimée', () => {
    const { composant, pdf, rapport } = modale(true);

    (composant as any).attachAutoPdf(42);

    expect(pdf.generate).toHaveBeenCalledWith(rapport, { withLocation: true });
  });
});
