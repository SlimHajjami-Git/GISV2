import { Injectable, inject } from '@angular/core';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';
import { AccidentReportDto } from './api.service';
import { PdfExportService } from './pdf-export.service';
import { UserPreferencesService } from './user-preferences.service';

/** Une ligne « libellé / valeur » d'un bloc du rapport. */
type Ligne = [string, string];

/**
 * Rapport de sinistre au format PDF, tel qu'il est remis à l'assureur.
 *
 * Recette Karim du 18/09/2026 : « après avoir terminé les informations dans le
 * rapport, le fichier PDF n'a pas été généré avec les informations renseignées »
 * et « mets le PDF avec l'entête comme les autres rapports ». Le document
 * n'écrivait AUCUN champ des phases (expertise, devis, réparation, assurance,
 * tiers, pièces jointes) et posait à la place un formulaire vierge en dur ; son
 * en-tête rouge n'était celui d'aucun autre rapport de l'application.
 *
 * Il reprend donc l'en-tête, la police et le pied de page de
 * <see cref="PdfExportService"/>, et n'imprime une ligne « à compléter » que pour
 * un bloc réellement vide.
 */
@Injectable({ providedIn: 'root' })
export class AccidentPdfService {
  private readonly pdfExport = inject(PdfExportService);
  private readonly prefs = inject(UserPreferencesService);

  private readonly margin = 14;

  /**
   * Version attendue : logo et police de marque chargés avant de dessiner.
   * À préférer partout où l'appelant peut attendre (bouton « Régénérer le PDF »,
   * régénération après l'enregistrement d'une phase).
   */
  async generateAsync(report: AccidentReportDto, opts?: { withLocation?: boolean }): Promise<Blob> {
    const doc = this.newDocument();
    const police = await this.pdfExport.prepareBrandDocument(doc);
    return this.build(doc, police, report, opts);
  }

  /**
   * Version synchrone, pour la génération automatique à la confirmation d'un
   * accident : elle n'attend pas les ressources de marque et retombe sur
   * Helvetica si elles ne sont pas encore chargées. Ne lève jamais sur une
   * donnée manquante — un PDF incomplet vaut mieux qu'une confirmation bloquée.
   */
  generate(report: AccidentReportDto, opts?: { withLocation?: boolean }): Blob {
    const doc = this.newDocument();
    const police = this.pdfExport.applyBrandToDocument(doc);
    return this.build(doc, police, report, opts);
  }

  private newDocument(): jsPDF {
    return new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
  }

  /**
   * `opts.withLocation` (vrai par défaut) : faux pour une société sans boîtier
   * (offre GPA). Recette du 11/09/2026 : sans GPS, aucune coordonnée n'est ni
   * mesurée ni saisie — le PDF affichait 0,000000 / 0,000000 et un IMEI vide.
   * On retire alors Latitude, Longitude et « Boîtier GPS (IMEI) » ; le lieu
   * DÉCLARÉ (commune, gouvernorat) reste, c'est souvent le seul du rapport.
   */
  private build(doc: jsPDF, police: string, report: AccidentReportDto, opts?: { withLocation?: boolean }): Blob {
    const withLocation = opts?.withLocation !== false;
    const titre = 'Rapport de sinistre';

    const meta: string[] = [`Réf. ${report.referenceCode ?? `ACC-${report.id}`}`];
    if (report.vehicleLabel) meta.push(`Véhicule : ${report.vehicleLabel}`);
    if (report.incidentAt) meta.push(`Sinistre du ${this.dateHeure(report.incidentAt)}`);

    let y = this.pdfExport.drawBrandHeader(doc, {
      title: titre,
      meta,
      rightNote: this.statutLabel(report.status),
    });

    // ── Contexte ──────────────────────────────────────────────────────────
    const lieu = [report.locationCommune, report.locationGovernorate, report.locationRoadType]
      .filter(Boolean).join(', ');
    const contexte: Ligne[] = [['Date et heure', this.dateHeure(report.incidentAt)]];
    // Véhicule nommé DANS le corps, pas seulement dans l'en-tête : vehicle_label est
    // nullable (instantané dénormalisé), et un dossier ancien ne le portait nulle part
    // — le rapport remis à l'assureur ne disait alors pas de quel véhicule il parlait.
    contexte.push(['Véhicule', report.vehicleLabel || (report.vehicleId != null ? `#${report.vehicleId}` : '—')]);
    if (lieu) contexte.push(['Lieu', lieu]);
    if (withLocation) {
      contexte.push(
        ['Latitude', report.latitude != null ? report.latitude.toFixed(6) : '—'],
        ['Longitude', report.longitude != null ? report.longitude.toFixed(6) : '—'],
        ['Boîtier GPS (IMEI)', report.deviceUid || '—'],
        ['Score de confiance', `${report.confidence ?? 0}/100`],
      );
    }
    if (report.weatherConditions) contexte.push(['Conditions météo', report.weatherConditions]);
    if (report.roadConditions) contexte.push(['État de la chaussée', report.roadConditions]);
    if (report.policeReportNumber) contexte.push(['N° de constat / PV', report.policeReportNumber]);
    if (report.mileageAtAccident != null) contexte.push(['Kilométrage au sinistre', `${report.mileageAtAccident} km`]);
    if (report.decidedByName) {
      contexte.push(['Confirmé par', report.decidedAt
        ? `${report.decidedByName} — ${this.dateHeure(report.decidedAt)}`
        : report.decidedByName]);
    }
    if (report.towDetectedAt) contexte.push(['Remorquage détecté', this.dateHeure(report.towDetectedAt)]);
    y = this.bloc(doc, police, y, 'Contexte du sinistre', contexte);

    // ── Synthèse et chronologie (détection automatique) ───────────────────
    if (report.synthesisText) y = this.paragraphe(doc, police, y, 'Synthèse', report.synthesisText);

    if (report.story?.length) {
      y = this.tableau(doc, police, y, 'Chronologie', [['Heure', 'Événement']],
        report.story.map(s => [s.time, this.joindre(s.title, s.body)]),
        { 0: { cellWidth: 26, fontStyle: 'bold' } });
    }

    if (report.indicators?.length) {
      y = this.tableau(doc, police, y, 'Indicateurs techniques', [['Indicateur', 'Valeur']],
        report.indicators.map(i => [i.label, this.joindre(i.value, i.hint)]),
        { 0: { cellWidth: 60 } });
    }

    if (report.reasons?.length) {
      y = this.tableau(doc, police, y, 'Motifs du diagnostic', [['#', 'Observation']],
        report.reasons.map((r, i) => [`${i + 1}`, this.joindre(r.title, r.text)]),
        { 0: { cellWidth: 10, fontStyle: 'bold' } });
    }

    // ── Les phases du dossier, dans l'ordre de la chronologie de l'écran ──
    const degats: Ligne[] = [];
    if (report.initialDescription) degats.push(['Description', report.initialDescription]);
    if (report.initialSeverity) degats.push(['Gravité', this.graviteLabel(report.initialSeverity)]);
    if (report.damagedZones?.length) degats.push(['Zones touchées', report.damagedZones.join(', ')]);
    y = this.bloc(doc, police, y, 'Dégâts constatés', degats);

    const expertise: Ligne[] = [];
    if (report.expertVisitedAt) expertise.push(['Date de visite', this.date(report.expertVisitedAt)]);
    if (report.expertName) expertise.push(['Expert', report.expertName]);
    if (report.expertCompany) expertise.push(['Cabinet / compagnie', report.expertCompany]);
    if (report.expertEstimatedAmount != null) expertise.push(['Montant estimé', this.montant(report.expertEstimatedAmount)]);
    if (report.expertAssessment) expertise.push(['Appréciation', report.expertAssessment]);
    y = this.bloc(doc, police, y, "Expertise d'assurance", expertise);

    const devis: Ligne[] = [];
    if (report.mechanicName) devis.push(['Garage', report.mechanicName]);
    if (report.mechanicQuoteAt) devis.push(['Date du devis', this.date(report.mechanicQuoteAt)]);
    if (report.mechanicQuotedAmount != null) devis.push(['Montant du devis', this.montant(report.mechanicQuotedAmount)]);
    y = this.bloc(doc, police, y, 'Devis du garage', devis);

    const reparation: Ligne[] = [];
    if (report.repairStartedAt) reparation.push(['Début', this.date(report.repairStartedAt)]);
    if (report.repairCompletedAt) reparation.push(['Fin', this.date(report.repairCompletedAt)]);
    if (report.actualRepairCost != null) reparation.push(['Coût réel facturé', this.montant(report.actualRepairCost)]);
    if (report.repairReference) reparation.push(['Fiche de réparation', report.repairReference]);
    y = this.bloc(doc, police, y, 'Réparation', reparation);

    const assurance: Ligne[] = [];
    if (report.claimNumber) assurance.push(['N° de sinistre', report.claimNumber]);
    if (report.claimSubmittedAt) assurance.push(['Déclaré le', this.date(report.claimSubmittedAt)]);
    if (report.claimStatus) assurance.push(['Statut', this.claimStatusLabel(report.claimStatus)]);
    if (report.claimApprovedAmount != null) assurance.push(['Montant approuvé', this.montant(report.claimApprovedAmount)]);
    // « Tiers impliqué : Non » ne vaut que dans un bloc déjà renseigné : poussée
    // inconditionnellement, cette ligne donnait à un bloc assurance VIDE l'air d'être
    // rempli et lui volait sa mention « à compléter ».
    if (report.thirdPartyInvolved || assurance.length > 0) {
      assurance.push(['Tiers impliqué', report.thirdPartyInvolved ? 'Oui' : 'Non']);
    }
    y = this.bloc(doc, police, y, 'Sinistre assurance', assurance);

    // ── Tiers et pièces jointes ──────────────────────────────────────────
    if (report.thirdParties?.length) {
      y = this.tableau(doc, police, y, 'Tiers impliqués',
        [['Nom', 'Téléphone', 'Véhicule', 'Assurance']],
        report.thirdParties.map(t => [
          t.name || '—',
          t.phone || '—',
          [t.vehiclePlate, t.vehicleModel].filter(Boolean).join(' — ') || '—',
          this.joindre(
            [t.insuranceCompany, t.insuranceNumber].filter(Boolean).join(' · '),
            t.insuranceExpiry ? `Échéance : ${this.date(t.insuranceExpiry)}` : null) || '—',
        ]),
        { 0: { cellWidth: 40 }, 1: { cellWidth: 28 } });
    }

    if (report.documents?.length) {
      y = this.tableau(doc, police, y, 'Pièces jointes',
        [['Pièce', 'Fichier', 'Ajoutée le']],
        report.documents.map(d => [
          this.documentLabel(d.documentType),
          d.fileName || '—',
          this.date(d.uploadedAt),
        ]),
        { 0: { cellWidth: 45 }, 2: { cellWidth: 28 } });
    }

    if (report.additionalNotes) y = this.paragraphe(doc, police, y, 'Notes internes', report.additionalNotes);
    if (report.witnesses) y = this.paragraphe(doc, police, y, 'Témoins', report.witnesses);

    this.pdfExport.drawBrandFooter(doc, titre);
    return doc.output('blob');
  }

  // ── Briques de mise en page ────────────────────────────────────────────

  /** Bloc « libellé / valeur ». Vide, il n'imprime qu'une ligne à compléter. */
  private bloc(doc: jsPDF, police: string, y: number, titre: string, lignes: Ligne[]): number {
    y = this.titre(doc, police, y, titre, lignes.length === 0 ? 14 : 24);
    if (lignes.length === 0) {
      doc.setFont(police, 'normal');
      doc.setFontSize(9.5);
      doc.setTextColor(130, 130, 130);
      doc.text('Non renseigné — à compléter.', this.margin, y);
      doc.setTextColor(0, 0, 0);
      return y + 8;
    }

    autoTable(doc, {
      startY: y,
      body: lignes.map(([label, valeur]) => [this.net(label), this.net(valeur)]),
      theme: 'plain',
      styles: { font: police, fontSize: 9.5, cellPadding: 1.4, overflow: 'linebreak' },
      columnStyles: {
        0: { fontStyle: 'bold', cellWidth: 52, textColor: [71, 85, 105] },
        1: { textColor: [15, 23, 42] },
      },
      margin: { left: this.margin, right: this.margin },
    });
    return this.finDuTableau(doc) + 6;
  }

  /** Tableau à en-têtes, aux couleurs de la charte. */
  private tableau(
    doc: jsPDF, police: string, y: number, titre: string,
    tete: string[][], corps: string[][], colonnes?: Record<number, any>,
  ): number {
    y = this.titre(doc, police, y, titre, 26);
    autoTable(doc, {
      startY: y,
      head: tete,
      body: corps.map(ligne => ligne.map(cellule => this.net(cellule))),
      theme: 'striped',
      styles: { font: police, fontSize: 8.5, cellPadding: 1.8, overflow: 'linebreak' },
      headStyles: { font: police, fillColor: this.pdfExport.brandColors.primary, textColor: [255, 255, 255] },
      columnStyles: colonnes,
      margin: { left: this.margin, right: this.margin },
    });
    return this.finDuTableau(doc) + 6;
  }

  /** Paragraphe libre (synthèse, appréciation, notes). */
  private paragraphe(doc: jsPDF, police: string, y: number, titre: string, texte: string): number {
    y = this.titre(doc, police, y, titre, 22);
    doc.setFont(police, 'normal');
    doc.setFontSize(9.5);
    doc.setTextColor(15, 23, 42);
    const pageWidth = doc.internal.pageSize.getWidth();
    const lignes: string[] = doc.splitTextToSize(this.net(texte), pageWidth - 2 * this.margin);
    for (const ligne of lignes) {
      y = this.saut(doc, y, 8);
      doc.text(ligne, this.margin, y);
      y += 5;
    }
    doc.setTextColor(0, 0, 0);
    return y + 4;
  }

  /** Titre de section, précédé si besoin d'un saut de page. */
  private titre(doc: jsPDF, police: string, y: number, texte: string, besoin: number): number {
    y = this.saut(doc, y, besoin);
    doc.setFont(police, 'bold');
    doc.setFontSize(11.5);
    doc.setTextColor(...this.pdfExport.brandColors.primary);
    doc.text(this.net(texte), this.margin, y);
    doc.setTextColor(0, 0, 0);
    return y + 5;
  }

  /** Passe à la page suivante si la place restante ne suffit pas au bloc. */
  private saut(doc: jsPDF, y: number, besoin: number): number {
    const hauteurUtile = doc.internal.pageSize.getHeight() - 18;
    if (y + besoin <= hauteurUtile) return y;
    doc.addPage();
    // Sous le bandeau réduit que le pied de page rappelle en haut des pages 2+.
    return 22;
  }

  private finDuTableau(doc: jsPDF): number {
    return (doc as any).lastAutoTable?.finalY ?? 22;
  }

  // ── Formats ────────────────────────────────────────────────────────────

  /** Nettoyage commun aux PDF de marque (accents conservés, caractères hors police
   *  retirés). Ligne à ligne : le nettoyage remplace les retours à la ligne par des
   *  espaces, ce qui collerait le titre et le corps d'une cellule sur deux lignes. */
  private net(valeur: unknown): string {
    return String(valeur ?? '')
      .split('\n')
      .map(ligne => this.pdfExport.clean(ligne))
      .filter(ligne => ligne.length > 0)
      .join('\n');
  }

  private joindre(...parts: (string | null | undefined)[]): string {
    return parts.filter(p => !!p && String(p).trim().length > 0).join('\n');
  }

  private date(iso: string | null | undefined): string {
    if (!iso) return '—';
    const d = new Date(iso);
    return isNaN(d.getTime()) ? '—' : d.toLocaleDateString('fr-FR');
  }

  private dateHeure(iso: string | null | undefined): string {
    if (!iso) return '—';
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '—';
    return `${d.toLocaleDateString('fr-FR')} à ${d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`;
  }

  /** Montant dans la devise de la société (jamais un symbole en dur). */
  private montant(valeur: number): string {
    return this.prefs.formatCurrency(valeur);
  }

  private statutLabel(statut: string | null | undefined): string {
    switch (statut) {
      case 'confirmed': return 'Sinistre confirmé';
      case 'dismissed': return 'Fausse alerte';
      case 'pending': return 'En attente de décision';
      default: return '';
    }
  }

  private graviteLabel(gravite: string): string {
    switch (gravite) {
      case 'minor': return 'Légers';
      case 'moderate': return 'Modérés';
      case 'severe': return 'Graves';
      case 'total': return 'Véhicule irréparable';
      default: return gravite;
    }
  }

  private claimStatusLabel(statut: string): string {
    switch (statut) {
      case 'pending': return 'En cours';
      case 'approved': return 'Approuvé';
      case 'partial': return 'Partiellement approuvé';
      case 'rejected': return 'Rejeté';
      case 'closed': return 'Clos';
      default: return statut;
    }
  }

  private documentLabel(type: string): string {
    switch (type) {
      case 'photo': return 'Photo des dégâts';
      case 'expert_report': return "Rapport d'expertise";
      case 'mechanic_quote': return 'Devis du garage';
      case 'repair_invoice': return 'Facture de réparation';
      case 'insurance_response': return "Réponse de l'assurance";
      case 'police_report': return 'Constat / PV';
      case 'detection_pdf': return 'Rapport de détection';
      default: return 'Autre pièce';
    }
  }
}
