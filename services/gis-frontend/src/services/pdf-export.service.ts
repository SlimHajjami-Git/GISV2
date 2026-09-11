import { Injectable } from '@angular/core';
import jsPDF from 'jspdf';
import autoTable from 'jspdf-autotable';

/** Une colonne d\'un tableau PDF.
 *  `weight` est une largeur RELATIVE facultative : la largeur reelle vaut
 *  weight / somme des weights x largeur du tableau. Sans elle, la colonne
 *  reprend le poids par defaut de son `dataKey` (voir getColumnStyles).
 *  Elle existe parce que ces poids par defaut sont GLOBAUX : « description »
 *  ou « status » servent a plusieurs rapports, et elargir une colonne pour
 *  l\'un retrecissait celle d\'un autre. Un rapport qui a besoin d\'un
 *  reglage propre le pose ici, sans effet de bord ailleurs. */
export interface PdfColumn {
  header: string;
  dataKey: string;
  weight?: number;
}

export interface PdfReportConfig {
  title: string;
  subtitle?: string;
  vehicleName?: string;
  dateRange?: string;
  statistics?: Record<string, string>;
  columns: PdfColumn[];
  /** Orientation de la page. Par defaut portrait. Un tableau large — le
   *  rapport « Couts mensuel par vehicule » en a onze colonnes — n’y tient
   *  pas : ses en-tetes se coupaient en trois lignes (« Ent+Re / p/100K / M »). */
  orientation?: 'portrait' | 'landscape';
  /** Note affichee sous le tableau, en petit. Sert a expliquer les intitules
   *  abreges des colonnes : « E+R €/100km » ne se devine pas. */
  footnote?: string;
  /** Met en evidence la DERNIERE ligne du tableau : fond plus soutenu,
   *  texte en gras. Pour les rapports dont la derniere ligne est un total,
   *  qui se confondait avec les lignes de donnees. */
  highlightLastRow?: boolean;
  data: any[];
  formatters?: Record<string, (value: any, row: any) => string>;
}

export interface PdfGroup {
  /** Main label shown as section header (e.g. vehicle name) */
  groupLabel: string;
  /** Optional secondary info (e.g. plate, km) shown right of the label */
  groupSubtitle?: string;
  /** Rows for this group */
  rows: any[];
  /** Optional right-aligned subtotal line (e.g. "3 réparations · 1 250,00 DT") */
  subtotal?: string;
}

export interface GroupedPdfReportConfig {
  title: string;
  subtitle?: string;
  dateRange?: string;
  /** Top statistic cards (global totals) */
  statistics?: Record<string, string>;
  columns: PdfColumn[];
  groups: PdfGroup[];
  formatters?: Record<string, (value: any, row: any) => string>;
  /** Optional grand-total line shown after the last group */
  grandTotal?: string;
}

@Injectable({ providedIn: 'root' })
export class PdfExportService {

  // Couleurs relevées sur la charte Calypso (« logo Calypso.pdf », planche des
  // déclinaisons) plutôt que sur une palette générique : le bleu profond du
  // dégradé, le bleu vif de la signature, et l'orange des facettes du « C ».
  private readonly primaryColor: [number, number, number] = [8, 64, 160];    // #0840A0
  private readonly accentColor: [number, number, number] = [0, 112, 192];    // #0070C0
  private readonly brandOrange: [number, number, number] = [255, 88, 40];    // #FF5828
  private readonly lightBg: [number, number, number] = [241, 245, 249];

  /**
   * Calypso 7 — branding PDF : on charge le logo Calypso une seule fois au
   * boot du service (data URL base64) puis on l'embarque dans chaque PDF
   * via doc.addImage(). Le footer est rebrandé « Calypso · Belive » au
   * lieu de l'ancien « GIS Fleet Management ».
   */
  private logoDataUrl: string | null = null;
  private logoLoading: Promise<string | null> | null = null;

  constructor() {
    // Préchargement non bloquant : le premier export attendra cette promise,
    // les suivants utilisent la valeur cachée.
    this.preloadLogo();
  }

  /** Précharge le logo Calypso. Public : tout rapport composant son propre
   *  corps (ex. rapport de tournée) doit pouvoir l'attendre avant de dessiner. */
  preloadBrandAssets(): Promise<string | null> {
    return this.preloadLogo();
  }

  /**
   * Dessine l'en-tête de marque commun à TOUS les PDF de l'application :
   * bandeau bleu Calypso, logo dans son cartouche blanc, titre, et ligne de
   * métadonnées. Retourne l'ordonnée à laquelle le corps peut commencer.
   *
   * Extrait de exportReportSync pour être réutilisable par les rapports qui
   * ne sont pas de simples « stats + tableau » — sans cela chaque écran
   * réinventait son propre en-tête et le résultat n'avait plus rien de commun.
   */
  /** Dégradé horizontal très doux du fond d'en-tête, du blanc cassé vers un
   *  bleu pâle. jsPDF n'a pas de dégradé natif : on le compose par bandes
   *  verticales fines (160 suffisent pour n'en voir aucune marche). Factorisé
   *  pour que l'en-tête de la page 1 et le bandeau des pages suivantes ne
   *  puissent plus diverger. */
  private drawHeaderGradient(doc: jsPDF, bandH: number): void {
    const pageWidth = doc.internal.pageSize.getWidth();
    const from: [number, number, number] = [248, 251, 255];
    const to: [number, number, number] = [219, 233, 250];
    const steps = 160;
    const stepW = pageWidth / steps;
    for (let i = 0; i < steps; i++) {
      const t = i / (steps - 1);
      doc.setFillColor(
        Math.round(from[0] + (to[0] - from[0]) * t),
        Math.round(from[1] + (to[1] - from[1]) * t),
        Math.round(from[2] + (to[2] - from[2]) * t)
      );
      doc.rect(i * stepW, 0, stepW + 0.3, bandH, 'F');
    }
  }

  /** Bandeau réduit rappelé en haut des pages 2 et suivantes. Il était bleu
   *  plein avec un titre blanc : depuis que la page 1 porte un en-tête clair,
   *  cela donnait deux identités visuelles dans un même document. Même fond,
   *  même titre bleu profond, même liséré orange que la page 1, en plus court.
   *  `left` reprend la marge du tableau de l'appelant (10 ou 14 mm) pour que le
   *  titre reste aligné sur la première colonne. */
  private drawContinuationBand(doc: jsPDF, title: string, left: number): void {
    const pageWidth = doc.internal.pageSize.getWidth();
    const bandH = 12;
    this.drawHeaderGradient(doc, bandH);
    doc.setTextColor(...this.primaryColor);
    doc.setFontSize(9);
    doc.setFont(this.brandFont, 'bold');
    doc.text(this.sanitizeText(title), left, 8);
    doc.setFillColor(...this.brandOrange);
    doc.rect(0, bandH, pageWidth, 0.8, 'F');
  }

  drawBrandHeader(doc: jsPDF, opts: { title: string; meta?: string[]; rightNote?: string }): number {
    const pageWidth = doc.internal.pageSize.getWidth();
    const bandH = 30;

    // Fond CLAIR : le logo Calypso est utilisé dans sa version couleur, et son
    // « CALYPSO » bleu (#0070C0) n'a que 1,81:1 de contraste sur un bandeau
    // bleu — illisible. Sur ce fond clair il monte à ~5:1, et le logo se pose
    // sans plaque ni cartouche. Le fond est un dégradé horizontal très doux
    // (voir drawHeaderGradient), partagé avec le bandeau des pages suivantes.
    this.drawHeaderGradient(doc, bandH);

    // Logo en couleurs, posé directement sur le fond clair, au rapport réel du
    // fichier (3:1) et non plus écrasé dans un carré de 22 mm.
    const logo = this.getLogo();
    let textLeft = 14;
    if (logo) {
      const logoH = 13;
      const logoW = logoH * this.logoAspect;
      const logoY = (bandH - logoH) / 2;
      try {
        // 'MEDIUM' : compression zlib SANS perte ni ré-échantillonnage — la
        // définition du fichier est conservée telle quelle (900 px pour ~39 mm,
        // soit ~590 dpi). 'NONE' embarquerait les pixels bruts et faisait à lui
        // seul 3,4 Mo de PDF pour un rapport de deux pages.
        doc.addImage(logo, 'PNG', 14, logoY, logoW, logoH, undefined, 'MEDIUM');
      } catch { /* logo optionnel */ }
      textLeft = 14 + logoW + 11;
      // Filet vertical discret entre le logo et le titre, dans le bleu pâle
      // de la charte plutôt qu'un gris neutre.
      doc.setDrawColor(168, 197, 232);
      doc.setLineWidth(0.5);
      doc.line(textLeft - 6, 7.5, textLeft - 6, bandH - 7.5);
    }

    // Titre dans le bleu profond de la charte : c'est lui qui porte le contraste
    // sur fond clair (12,4:1), là où le bandeau bleu le portait par le blanc.
    // Largeur utile à droite du logo. Un titre long (« Évolution des coûts —
    // Commercial 01 (GD-421-NV) ») ou une ligne de métadonnées chargée
    // sortaient de la page : on réduit la taille jusqu'à ce que ça tienne,
    // sans descendre sous un plancher lisible.
    const largeurUtile = pageWidth - 14 - textLeft;
    const ajuster = (texte: string, taille: number, plancher: number) => {
      doc.setFontSize(taille);
      while (taille > plancher && doc.getTextWidth(texte) > largeurUtile) {
        taille -= 0.5;
        doc.setFontSize(taille);
      }
    };

    doc.setTextColor(...this.primaryColor);
    doc.setFont(this.brandFont, 'bold');
    const titre = this.sanitizeText(opts.title);
    ajuster(titre, 17, 13);
    doc.text(titre, textLeft, opts.meta?.length ? 14 : 18);

    if (opts.meta?.length) {
      // Métadonnées en bleu-gris : lisibles, sans concurrencer le titre.
      doc.setFont(this.brandFont, 'normal');
      doc.setTextColor(82, 103, 133);
      const ligne = opts.meta.map(m => this.sanitizeText(m)).join('   •   ');
      ajuster(ligne, 8.5, 7);
      doc.text(ligne, textLeft, 21.5);
    }
    if (opts.rightNote) {
      doc.setFontSize(9);
      doc.setFont(this.brandFont, 'normal');
      doc.setTextColor(82, 103, 133);
      doc.text(this.sanitizeText(opts.rightNote), pageWidth - 14, 14, { align: 'right' });
    }

    // Liseré orange en pied de bandeau : la seule touche de la couleur
    // d'accent de la charte, qui rappelle les facettes du « C ».
    doc.setFillColor(...this.brandOrange);
    doc.rect(0, bandH, pageWidth, 1.4, 'F');

    doc.setTextColor(0, 0, 0);
    return 38;
  }

  /** Pied de page de marque commun (« Calypso · Belive » + date + pagination).
   *  `bannerTitle` rappelle en plus un bandeau réduit en haut des pages 2+,
   *  sans quoi les pages suivantes n'avaient aucune identité visuelle. */
  drawBrandFooter(doc: jsPDF, bannerTitle?: string): void {
    const pageWidth = doc.internal.pageSize.getWidth();
    const pageHeight = doc.internal.pageSize.getHeight();
    const total = (doc as any).internal.getNumberOfPages();
    for (let i = 1; i <= total; i++) {
      doc.setPage(i);
      if (bannerTitle && i > 1) {
        this.drawContinuationBand(doc, bannerTitle, 14);
      }
      doc.setDrawColor(226, 232, 240);
      doc.line(14, pageHeight - 12, pageWidth - 14, pageHeight - 12);
      doc.setFontSize(8);
      doc.setFont(this.brandFont, 'normal');
      doc.setTextColor(130, 130, 130);
      doc.text(
        `${this.footerBrand}  |  ${this.sanitizeText('Généré le')} ${new Date().toLocaleDateString('fr-FR')} à ${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`,
        14, pageHeight - 7
      );
      doc.text(`${i} / ${total}`, pageWidth - 14, pageHeight - 7, { align: 'right' });
    }
    doc.setTextColor(0, 0, 0);
  }

  /** Assainit une chaîne pour le jeu de caractères des polices standard jsPDF. Public : voir clean(). */
  clean(text: string): string {
    return this.sanitizeText(text);
  }

  /** Couleurs de la charte, pour les rapports qui composent leur propre corps. */
  get brandColors() {
    return { primary: this.primaryColor, accent: this.accentColor, light: this.lightBg };
  }

  /** Rapport largeur/hauteur du fichier de logo, mesuré au chargement.
   *  Repli sur 3:1, le rapport du logo Calypso actuel, si la mesure échoue. */
  private logoAspect = 3;

  /** Police de marque du PDF.
   *
   *  Les exports sortaient en Helvetica, la police par defaut des PDF, alors
   *  que l’application est en Manrope : un rapport remis au client n’avait pas
   *  l’air de venir de Calypso. Manrope est embarquee dans le document, un PDF
   *  ne pouvant pas pointer vers une police distante.
   *
   *  Cout mesure le 10/09/2026 : deux fontes de 95 Ko chacune, chargees UNE
   *  fois par session comme le logo, et environ 40 Ko ajoutes au PDF une fois
   *  sous-ensemble et compresse par jsPDF. Manrope n’est que 2 a 7 % plus large
   *  qu’Helvetica : les largeurs de colonnes mesurees gardent leur marge.
   *
   *  En cas d’echec de chargement on retombe sur Helvetica : un rapport moins
   *  joli vaut mieux qu’un rapport qui ne sort pas. */
  private static readonly BrandFont = 'Manrope';
  private static readonly FallbackFont = 'helvetica';

  private fontRegular: string | null = null;
  private fontBold: string | null = null;
  private fontsLoading?: Promise<void>;

  /** Nom de police a utiliser : la marque si elle a pu etre chargee. */
  private get brandFont(): string {
    return this.fontRegular && this.fontBold
      ? PdfExportService.BrandFont
      : PdfExportService.FallbackFont;
  }

  private preloadFonts(): Promise<void> {
    if (this.fontsLoading) return this.fontsLoading;
    const enBase64 = (chemin: string) => fetch(chemin)
      .then(r => r.ok ? r.arrayBuffer() : null)
      .then(buf => {
        if (!buf) return null;
        let binaire = '';
        const octets = new Uint8Array(buf);
        // Par tranches : String.fromCharCode(...tableau) depasse la pile
        // d’appels au-dela de quelques dizaines de milliers d’octets.
        for (let i = 0; i < octets.length; i += 8192) {
          binaire += String.fromCharCode(...octets.subarray(i, i + 8192));
        }
        return btoa(binaire);
      })
      .catch(() => null);

    this.fontsLoading = Promise.all([
      enBase64('assets/fonts/manrope-regular.ttf'),
      enBase64('assets/fonts/manrope-bold.ttf')
    ]).then(([reg, gras]) => {
      this.fontRegular = reg;
      this.fontBold = gras;
    });
    return this.fontsLoading;
  }

  /** Declare la police de marque dans un document et la selectionne.
   *  A faire par document : le magasin de fichiers de jsPDF est porte par
   *  l’instance. Sans police chargee, le document reste en Helvetica. */
  private applyBrandFont(doc: jsPDF): void {
    if (!this.fontRegular || !this.fontBold) return;
    try {
      doc.addFileToVFS('Manrope-Regular.ttf', this.fontRegular);
      doc.addFont('Manrope-Regular.ttf', PdfExportService.BrandFont, 'normal');
      doc.addFileToVFS('Manrope-Bold.ttf', this.fontBold);
      doc.addFont('Manrope-Bold.ttf', PdfExportService.BrandFont, 'bold');
      doc.setFont(PdfExportService.BrandFont, 'normal');
    } catch {
      // Police refusee : on laisse Helvetica plutot que de casser l’export.
      this.fontRegular = null;
      this.fontBold = null;
    }
  }

  private preloadLogo(): Promise<string | null> {
    if (this.logoDataUrl) return Promise.resolve(this.logoDataUrl);
    if (this.logoLoading) return this.logoLoading;
    // Version COULEUR, a FOND TRANSPARENT : c'est celle que le client veut voir
    // sur ses rapports, et elle se pose directement sur le fond clair de
    // l'en-tête (voir drawBrandHeader). Le fichier rasterisé depuis le PDF de
    // charte était opaque : jsPDF embarquait alors un rectangle blanc autour du
    // logo, bien visible sur le dégradé. Il a été détouré (canal alpha déduit du
    // canal minimum), ce qui redonne exactement les couleurs de la charte sur un
    // fond blanc ou quasi blanc, le seul sur lequel ce logo est utilisé.
    this.logoLoading = fetch('assets/logo/calypso-logo.png')
      .then(r => r.ok ? r.blob() : null)
      .then(blob => {
        if (!blob) return null;
        return new Promise<string>((resolve) => {
          const reader = new FileReader();
          reader.onloadend = () => {
            const url = (reader.result as string) || '';
            this.logoDataUrl = url;
            // Mesure du rapport réel : l'en-tête dimensionne le cartouche
            // dessus, pour ne jamais déformer le logo si le fichier change.
            const img = new Image();
            img.onload = () => {
              if (img.naturalWidth > 0 && img.naturalHeight > 0) {
                this.logoAspect = img.naturalWidth / img.naturalHeight;
              }
              resolve(url);
            };
            img.onerror = () => resolve(url);
            img.src = url;
          };
          reader.readAsDataURL(blob);
        });
      })
      .catch(() => null);
    return this.logoLoading;
  }

  /** Logo Calypso à insérer dans le header du PDF. Retourne le data URL ou null si l'asset n'est pas dispo. */
  private getLogo(): string | null {
    return this.logoDataUrl;
  }

  /** Texte du pied de page de tous les exports PDF.
   *  « Belive » retiré le 10/09/2026 : c'est la société éditrice, pas la marque
   *  vue par le client. Un rapport remis à un client français portait donc le nom
   *  d'une société tunisienne qui ne lui dit rien. Calypso est la marque du
   *  produit et se suffit. S'applique à TOUS les rapports, pas au seul rapport
   *  des réparations. */
  private readonly footerBrand = 'Calypso';

  private sanitizeText(text: string): string {
    if (!text) return '';
    // Normalize to NFC (composed form) — keeps é, è, ê, à, ç etc. as single characters
    // Helvetica in jsPDF supports Latin-1 (U+00A0–U+00FF) which includes all French accents
    return text
      .normalize('NFC')
      // € est HORS Latin-1 mais present dans WinAnsi, l’encodage que jsPDF
      // applique aux polices standard : verifie, Helvetica le dessine
      // correctement en normal comme en gras. Sans cette exception il tombait
      // dans la table ci-dessous, n’y trouvait rien et disparaissait du PDF.
      .replace(/[^\x20-\x7E\xA0-\xFF\u20AC]/g, (ch) => {
        const map: Record<string, string> = {
          '\u2019': "'", '\u2018': "'", '\u201C': '"', '\u201D': '"',
          '\u2013': '-', '\u2014': '-', '\u2026': '...',
          '\u0152': 'OE', '\u0153': 'oe',
          // Fl\u00E8ches et symboles : Helvetica/WinAnsi ne les conna\u00EEt pas et
          // jsPDF corrompt alors TOUTE la ligne (glyphes \u00AB (cid:0) \u00BB illisibles
          // dans le PDF final). Les traduire au lieu de les supprimer garde
          // le sens : \u00AB 06:10 -> 06:13 \u00BB.
          '\u2192': '->', '\u2190': '<-', '\u2194': '<->', '\u21D2': '=>',
          '\u2022': '-',
          '\u2264': '<=', '\u2265': '>=', '\u2260': '!=', '\u2248': '~',
          '\u2212': '-', '\u2044': '/',
          // Espaces typographiques : toLocaleString('fr-FR') ins\u00E8re une espace
          // ins\u00E9cable \u00E9troite (U+202F) dans les dates et les nombres.
          '\u202F': ' ', '\u2009': ' ', '\u200B': '',
          // Sans ces trois-l\u00E0, un champ multiligne (note, description) voyait
          // ses mots COLL\u00C9S : \u00AB ligne1\nligne2 \u00BB devenait \u00AB ligne1ligne2 \u00BB.
          '\n': ' ', '\r': ' ', '\t': ' ',
        };
        return map[ch] || '';
      })
      .replace(/^\s+/, '')   // trim leading spaces left by stripped emojis
      .replace(/\s{2,}/g, ' ') // collapse multiple spaces into one
      // Recoud la ponctuation laissée orpheline par les caractères retirés.
      // Cas réel : une adresse Nominatim « RL 941 <arabe>, Tezdaine » donnait
      // « RL 941 , Tezdaine » une fois l'arabe supprimé.
      .replace(/\s+,/g, ',')
      .replace(/,(\s*,)+/g, ',')
      .replace(/[\s,]+$/, '');
  }

  async exportReport(config: PdfReportConfig): Promise<void> {
    // Calypso 7 — on attend le logo (preloaded au boot du service).
    await Promise.all([this.preloadLogo(), this.preloadFonts()]);
    return this.exportReportSync(config);
  }

  private exportReportSync(config: PdfReportConfig): void {
    const doc = new jsPDF({ orientation: config.orientation ?? 'portrait', unit: 'mm', format: 'a4' });
    this.applyBrandFont(doc);
    const pageWidth = doc.internal.pageSize.getWidth();
    let y = 15;

    // Appelle drawBrandHeader au lieu de redessiner son propre en-t\u00eate : cette
    // copie \u00e9tait rest\u00e9e alors que drawBrandHeader avait justement \u00e9t\u00e9 extrait
    // pour l'\u00e9viter, si bien qu'un changement de charte devait \u00eatre fait \u00e0 trois
    // endroits \u2014 et ne l'\u00e9tait pas.
    const meta: string[] = [];
    if (config.vehicleName) meta.push(`V\u00e9hicule: ${this.sanitizeText(config.vehicleName)}`);
    if (config.dateRange) meta.push(`P\u00e9riode: ${this.sanitizeText(config.dateRange)}`);
    meta.push(`G\u00e9n\u00e9r\u00e9 le: ${new Date().toLocaleDateString('fr-FR')} \u00e0 ${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`);

    y = this.drawBrandHeader(doc, {
      title: config.title,
      meta,
      rightNote: config.subtitle
    });

    // ── Statistics block ──
    if (config.statistics && Object.keys(config.statistics).length > 0) {
      const entries = Object.entries(config.statistics);
      // Jusqu’a CINQ cartes sur une ligne. Le plafond etait a quatre : un
      // rapport a cinq chiffres de synthese en renvoyait un seul, tout seul,
      // sur une deuxieme ligne. A cinq colonnes chaque carte fait encore
      // 34 mm utiles en portrait, de quoi loger « 10 410,00 € » a 12 points.
      const colCount = entries.length <= 5 ? entries.length : 4;
      const cardW = (pageWidth - 28) / colCount;
      const cardH = 20;

      entries.slice(0, 8).forEach(([label, value], i) => {
        const row = Math.floor(i / colCount);
        const col = i % colCount;
        const x = 14 + col * cardW;
        const cy = y + row * (cardH + 3);

        doc.setFillColor(...this.lightBg);
        doc.roundedRect(x, cy, cardW - 3, cardH, 2, 2, 'F');

        doc.setTextColor(100, 116, 139);
        doc.setFontSize(8);
        doc.setFont(this.brandFont, 'normal');
        doc.text(this.sanitizeText(label), x + 4, cy + 7);

        doc.setTextColor(15, 23, 42);
        doc.setFontSize(12);
        doc.setFont(this.brandFont, 'bold');
        doc.text(this.sanitizeText(String(value)), x + 4, cy + 15);
      });

      const totalRows = Math.ceil(entries.length / colCount);
      y += totalRows * (cardH + 3) + 4;
    }

    // ── Data table ──
    const headers = config.columns.map(c => this.sanitizeText(c.header));
    const body = config.data.map(row => {
      return config.columns.map(col => {
        const val = row[col.dataKey];
        if (config.formatters && config.formatters[col.dataKey]) {
          return this.sanitizeText(config.formatters[col.dataKey](val, row));
        }
        if (val === null || val === undefined) return '-';
        return this.sanitizeText(String(val));
      });
    });

    autoTable(doc, {
      head: [headers],
      body: body,
      startY: y,
      theme: 'grid',
      styles: {
        fontSize: 7,
        cellPadding: 2.5,
        lineColor: [226, 232, 240],
        lineWidth: 0.2,
        textColor: [30, 41, 59],
        font: this.brandFont,
        overflow: 'linebreak'
      },
      headStyles: {
        fillColor: this.primaryColor,
        textColor: [255, 255, 255],
        font: this.brandFont,
        fontStyle: 'bold',
        fontSize: 7.5,
        cellPadding: 2.5
      },
      alternateRowStyles: {
        fillColor: [248, 250, 252]
      },
      // Derniere ligne mise en evidence quand c’est un total : sans cela elle
      // se confondait avec les lignes de donnees, y compris avec le gris des
      // lignes alternees. didParseCell passe APRES le theme, donc gagne.
      didParseCell: (data: any) => {
        if (!config.highlightLastRow) return;
        if (data.section !== 'body') return;
        if (data.row.index !== body.length - 1) return;
        data.cell.styles.fillColor = [226, 232, 240];
        data.cell.styles.textColor = [15, 23, 42];
        data.cell.styles.fontStyle = 'bold';
      },
      columnStyles: this.getColumnStyles(config.columns, pageWidth - 20),
      margin: { left: 10, right: 10 },
      tableWidth: pageWidth - 20,
      didDrawPage: (data: any) => {
        // Re-draw header on subsequent pages
        const pageNum = (doc as any).internal.getCurrentPageInfo().pageNumber;
        if (pageNum > 1) {
          this.drawContinuationBand(doc, config.title, 10);
        }
        // Footer on every page
        const pageCount = (doc as any).internal.getNumberOfPages();
        doc.setFontSize(7);
        doc.setTextColor(148, 163, 184);
        doc.setFont(this.brandFont, 'normal');
        const footerY = doc.internal.pageSize.getHeight() - 7;
        doc.text(`${this.footerBrand} · ${this.sanitizeText(config.title)}`, 10, footerY);
      }
    });

    // ── Note de bas de tableau ──
    // Les intitules de colonnes sont abreges pour tenir sur une ligne ; la
    // note dit ce qu’ils recouvrent. Posee sous le tableau, sur une page
    // neuve si le tableau finit trop bas.
    if (config.footnote) {
      const finTableau = (doc as any).lastAutoTable?.finalY ?? 0;
      let ny = finTableau + 6;
      if (ny > doc.internal.pageSize.getHeight() - 18) {
        doc.addPage();
        ny = 20;
      }
      doc.setFontSize(7.5);
      doc.setFont(this.brandFont, 'normal');
      doc.setTextColor(100, 116, 139);
      const largeur = doc.internal.pageSize.getWidth() - 28;
      const texte = doc.splitTextToSize(this.sanitizeText(config.footnote), largeur);
      doc.text(texte, 14, ny);
    }

    // Pagination ecrite APRES le tableau. Dans didDrawPage,
    // getNumberOfPages() ne connait que les pages deja creees : un rapport de
    // deux pages affichait donc « Page 1 / 1 » sur la premiere. On repasse une
    // fois le document termine, quand le total est enfin connu.
    const totalPages = (doc as any).internal.getNumberOfPages();
    for (let i = 1; i <= totalPages; i++) {
      doc.setPage(i);
      doc.setFontSize(7);
      doc.setTextColor(148, 163, 184);
      doc.setFont(this.brandFont, 'normal');
      doc.text(
        `Page ${i} / ${totalPages}`,
        doc.internal.pageSize.getWidth() - 10,
        doc.internal.pageSize.getHeight() - 7,
        { align: 'right' }
      );
    }

    // ── Save ──
    const filename = this.sanitizeFilename(config.title);
    const dateStr = new Date().toISOString().split('T')[0];
    doc.save(`${filename}_${dateStr}.pdf`);
  }

  /**
   * Export a PDF where rows are organized in sections (one table per group),
   * each with its own subtotal line, followed by a grand total.
   * Reuses the same header bar, footer, column sizing and sanitizer as exportReport.
   */
  async exportGroupedReport(config: GroupedPdfReportConfig): Promise<void> {
    await Promise.all([this.preloadLogo(), this.preloadFonts()]);
    return this.exportGroupedReportSync(config);
  }

  private exportGroupedReportSync(config: GroupedPdfReportConfig): void {
    const doc = new jsPDF({ orientation: 'portrait', unit: 'mm', format: 'a4' });
    this.applyBrandFont(doc);
    const pageWidth = doc.internal.pageSize.getWidth();
    const pageHeight = doc.internal.pageSize.getHeight();
    let y = 15;

    // Même en-tête de marque que partout ailleurs (troisième et dernière copie
    // de l'ancien bandeau supprimée : la charte se change désormais en un seul
    // endroit, drawBrandHeader).
    const meta: string[] = [];
    if (config.dateRange) meta.push(`P\u00e9riode: ${this.sanitizeText(config.dateRange)}`);
    meta.push(`G\u00e9n\u00e9r\u00e9 le: ${new Date().toLocaleDateString('fr-FR')} \u00e0 ${new Date().toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`);

    y = this.drawBrandHeader(doc, {
      title: config.title,
      meta,
      rightNote: config.subtitle
    });

    // ── Statistics block (global totals) ──
    if (config.statistics && Object.keys(config.statistics).length > 0) {
      const entries = Object.entries(config.statistics);
      const colCount = Math.min(entries.length, 4);
      const cardW = (pageWidth - 28) / colCount;
      const cardH = 20;

      entries.slice(0, 8).forEach(([label, value], i) => {
        const row = Math.floor(i / colCount);
        const col = i % colCount;
        const x = 14 + col * cardW;
        const cy = y + row * (cardH + 3);

        doc.setFillColor(...this.lightBg);
        doc.roundedRect(x, cy, cardW - 3, cardH, 2, 2, 'F');

        doc.setTextColor(100, 116, 139);
        doc.setFontSize(8);
        doc.setFont(this.brandFont, 'normal');
        doc.text(this.sanitizeText(label), x + 4, cy + 7);

        doc.setTextColor(15, 23, 42);
        doc.setFontSize(12);
        doc.setFont(this.brandFont, 'bold');
        doc.text(this.sanitizeText(String(value)), x + 4, cy + 15);
      });

      const totalRows = Math.ceil(entries.length / colCount);
      y += totalRows * (cardH + 3) + 6;
    }

    const headers = config.columns.map(c => this.sanitizeText(c.header));

    // Draws header bar + footer on each new page (used by didDrawPage)
    const drawPageChrome = () => {
      const pageNum = (doc as any).internal.getCurrentPageInfo().pageNumber;
      if (pageNum > 1) {
        this.drawContinuationBand(doc, config.title, 10);
      }
      const pageCount = (doc as any).internal.getNumberOfPages();
      doc.setFontSize(7);
      doc.setTextColor(148, 163, 184);
      doc.setFont(this.brandFont, 'normal');
      const footerY = pageHeight - 7;
      // Oubli du rebrand Calypso : ce pied de page (export groupé) affichait
      // encore « GIS Fleet Management » alors que l'export simple était déjà
      // passé à footerBrand — deux marques différentes selon le rapport.
      doc.text(`${this.footerBrand} - ${this.sanitizeText(config.title)}`, 10, footerY);
    };

    // ── Render each group ──
    for (let g = 0; g < config.groups.length; g++) {
      const group = config.groups[g];

      // If the section header + a couple of rows wouldn't fit on current page, start a new page
      if (y > pageHeight - 50) {
        doc.addPage();
        y = 18;
      }

      // Section header bar
      doc.setFillColor(...this.lightBg);
      doc.roundedRect(10, y, pageWidth - 20, 10, 1.5, 1.5, 'F');
      doc.setFont(this.brandFont, 'bold');
      doc.setFontSize(11);
      doc.setTextColor(...this.primaryColor);
      doc.text(this.sanitizeText(group.groupLabel), 14, y + 7);
      if (group.groupSubtitle) {
        doc.setFont(this.brandFont, 'normal');
        doc.setFontSize(9);
        doc.setTextColor(100, 116, 139);
        doc.text(this.sanitizeText(group.groupSubtitle), pageWidth - 14, y + 7, { align: 'right' });
      }
      y += 12;

      // Section table
      const body = group.rows.map(row => {
        return config.columns.map(col => {
          const val = row[col.dataKey];
          if (config.formatters && config.formatters[col.dataKey]) {
            return this.sanitizeText(config.formatters[col.dataKey](val, row));
          }
          if (val === null || val === undefined) return '-';
          return this.sanitizeText(String(val));
        });
      });

      autoTable(doc, {
        head: [headers],
        body: body,
        startY: y,
        theme: 'grid',
        styles: {
          fontSize: 7,
          cellPadding: 2.5,
          lineColor: [226, 232, 240],
          lineWidth: 0.2,
          textColor: [30, 41, 59],
          font: this.brandFont,
          overflow: 'linebreak'
        },
        headStyles: {
          fillColor: this.primaryColor,
          textColor: [255, 255, 255],
          font: this.brandFont,
          fontStyle: 'bold',
          fontSize: 7.5,
          cellPadding: 2.5
        },
        alternateRowStyles: {
          fillColor: [248, 250, 252]
        },
        columnStyles: this.getColumnStyles(config.columns, pageWidth - 20),
        margin: { left: 10, right: 10 },
        tableWidth: pageWidth - 20,
        didDrawPage: drawPageChrome
      });

      y = (doc as any).lastAutoTable.finalY + 2;

      // Subtotal line
      if (group.subtotal) {
        if (y > pageHeight - 18) {
          doc.addPage();
          y = 18;
        }
        doc.setFillColor(241, 245, 249);
        doc.rect(10, y, pageWidth - 20, 7, 'F');
        doc.setFont(this.brandFont, 'bold');
        doc.setFontSize(8.5);
        doc.setTextColor(30, 41, 59);
        doc.text(this.sanitizeText(group.subtotal), pageWidth - 14, y + 5, { align: 'right' });
        y += 10;
      } else {
        y += 4;
      }
    }

    // ── Grand total ──
    if (config.grandTotal) {
      if (y > pageHeight - 20) {
        doc.addPage();
        y = 18;
      }
      doc.setFillColor(...this.primaryColor);
      doc.rect(10, y, pageWidth - 20, 10, 'F');
      doc.setFont(this.brandFont, 'bold');
      doc.setFontSize(11);
      doc.setTextColor(255, 255, 255);
      doc.text(this.sanitizeText(config.grandTotal), pageWidth - 14, y + 7, { align: 'right' });
      y += 12;
    }

    // Ensure footer is drawn on the last page (autoTable only triggers didDrawPage
    // when it actually renders a table on that page — the grand total alone wouldn't).
    drawPageChrome();

    // Pagination ecrite APRES le tableau. Dans didDrawPage,
    // getNumberOfPages() ne connait que les pages deja creees : un rapport de
    // deux pages affichait donc « Page 1 / 1 » sur la premiere. On repasse une
    // fois le document termine, quand le total est enfin connu.
    const totalPages = (doc as any).internal.getNumberOfPages();
    for (let i = 1; i <= totalPages; i++) {
      doc.setPage(i);
      doc.setFontSize(7);
      doc.setTextColor(148, 163, 184);
      doc.setFont(this.brandFont, 'normal');
      doc.text(
        `Page ${i} / ${totalPages}`,
        doc.internal.pageSize.getWidth() - 10,
        doc.internal.pageSize.getHeight() - 7,
        { align: 'right' }
      );
    }

    // ── Save ──
    const filename = this.sanitizeFilename(config.title);
    const dateStr = new Date().toISOString().split('T')[0];
    doc.save(`${filename}_${dateStr}.pdf`);
  }

  private getColumnStyles(columns: PdfColumn[], tableWidth = 190): Record<number, any> {
    const styles: Record<number, any> = {};

    // Assign proportional weight to each column based on content type
    const widthWeights: Record<string, number> = {
      // Wide columns — addresses, descriptions, multi-value text
      'address': 3, '_address': 3.5, 'startAddress': 3, 'location': 2.5,
      'description': 3, 'incidentType': 2.5,
      // Medium columns — names, dates, formatted values
      'vehicleName': 2, 'vehicle': 2, 'supplierName': 2,
      'startTime': 1.8, 'endTime': 1.8, 'time': 1.8, 'date': 1.5,
      'period': 1.8, 'dayOfWeek': 1.2,
      'duration': 1.2, 'drivingTime': 1.5,
      'reference': 1.5, 'typeLabel': 1.5, '_typeLabel': 1.5,
      'severityLabel': 1.3, 'status': 1.2, 'eventType': 1.3,
      // Narrow columns — numbers, short values
      'distance': 1.2, 'speed': 1, 'maxSpeed': 1, 'limit': 1,
      // Un kilométrage à six chiffres vaut « 154 546 km » : au poids 1,2
      // l'unité passait à la ligne dès que le compteur dépassait 100 000.
      'fuelPercent': 1, 'fuelChange': 1, 'odometer': 1.5, 'mileage': 1.5,
      'tripCount': 0.8, 'eventNumber': 0.6, 'score': 0.8,
      'avgDaily': 1.2, 'activeDays': 1,
      'laborCostFormatted': 1.2, 'partsCostFormatted': 1.2,
      // Un montant formaté vaut « 1 480,00 EUR » : au poids 1,2 la devise
      // passait à la ligne sous le nombre.
      'totalCostFormatted': 1.6, 'costFormatted': 1.6,
      'fuelEstimated': 1.2, 'costEstimated': 1.2, 'avgConsumption': 1.2,
      'consumption': 1, 'plate': 1.2, 'fuel': 1, 'cost': 1,
      // 'type' n'est utilisé que par le rapport Coûts maintenance, où il porte
      // le NOM du modèle d'entretien (« Révision périodique », « Vidange +
      // filtre à huile »). Au poids 1 la colonne coupait le dernier caractère
      // sur sa propre ligne (« Révision périodiqu / e ») : il lui faut la
      // largeur d'une colonne de texte, pas celle d'un code court.
      '_type': 1, 'type': 2, 'value': 1, 'trips': 0.8, 'stops': 0.8,
    };

    const defaultWeight = 1.2;
    // Priorite au poids porte par la colonne elle-meme, puis au poids par
    // defaut de son dataKey, puis au repli.
    const weights = columns.map(col => col.weight ?? widthWeights[col.dataKey] ?? defaultWeight);
    const totalWeight = weights.reduce((sum, w) => sum + w, 0);

    columns.forEach((_col, i) => {
      styles[i] = { cellWidth: (weights[i] / totalWeight) * tableWidth };
    });

    return styles;
  }

  private sanitizeFilename(name: string): string {
    return name
      .normalize('NFD').replace(/[\u0300-\u036f]/g, '')
      .replace(/[^a-zA-Z0-9_\- ]/g, '')
      .replace(/\s+/g, '_')
      .toLowerCase();
  }

  // ── Column definitions per report type ──

  getColumnsForReport(type: string, options?: any): PdfColumn[] {
    switch (type) {
      case 'trips':
        // Calypso 7 (correction client) : la colonne \u00ab Type \u00bb n a aucune
        // valeur informative quand les lignes Arret sont deja filtrees a
        // la source (PDF n affiche que les vrais trajets). On la retire.
        return [
          ...(options?.allVehicles ? [{ header: 'V\u00e9hicule', dataKey: 'vehicleName' }] : []),
          { header: '#', dataKey: '_tripNumber' },
          { header: 'D\u00e9but', dataKey: 'startTime' },
          { header: 'Fin', dataKey: 'endTime' },
          { header: 'Dur\u00e9e', dataKey: 'duration' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Vit. max', dataKey: 'maxSpeed' },
          { header: 'Lieu', dataKey: '_address' }
        ];
      case 'stops':
        return [
          ...(options?.allVehicles ? [{ header: 'Véhicule', dataKey: 'vehicleName' }] : []),
          { header: 'Type', dataKey: '_typeLabel' },
          { header: 'Début', dataKey: 'time' },
          { header: 'Fin', dataKey: 'endTime' },
          { header: 'Durée', dataKey: 'duration' },
          { header: 'Adresse', dataKey: 'address' }
        ];
      case 'mileage':
        return [
          { header: 'Date', dataKey: 'date' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Trajets', dataKey: 'tripCount' },
          { header: 'Temps conduite', dataKey: 'drivingTime' },
          { header: 'Vit. max', dataKey: 'maxSpeed' },
          { header: 'Odom\u00e8tre', dataKey: 'odometer' }
        ];
      case 'mileage-period':
        const cols = [{ header: 'Période', dataKey: 'period' }];
        if (options?.periodType === 'day') cols.push({ header: 'Jour', dataKey: 'dayOfWeek' });
        cols.push({ header: 'Distance', dataKey: 'distance' });
        if (options?.periodType === 'month') {
          cols.push({ header: 'Moy. jour', dataKey: 'avgDaily' });
          cols.push({ header: 'Jours actifs', dataKey: 'activeDays' });
        }
        cols.push({ header: 'Trajets', dataKey: 'tripCount' });
        cols.push({ header: 'Temps conduite', dataKey: 'drivingTime' });
        if (options?.periodType !== 'month') {
          cols.push({ header: 'Vit. max', dataKey: 'maxSpeed' });
        }
        return cols;
      case 'daily':
        return [
          { header: '#', dataKey: 'eventNumber' },
          { header: 'Horaire', dataKey: 'time' },
          { header: 'Événement', dataKey: 'typeLabel' },
          { header: 'Durée', dataKey: 'duration' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Vitesse', dataKey: 'speed' },
          { header: 'Lieu', dataKey: 'address' }
        ];
      case 'speed':
        return [
          { header: 'Véhicule', dataKey: 'vehicleName' },
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Vitesse', dataKey: 'speed' },
          { header: 'Adresse', dataKey: 'address' }
        ];
      case 'speed-infraction':
        return [
          { header: 'Véhicule', dataKey: 'vehicle' },
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Adresse', dataKey: 'address' },
          { header: 'Vitesse', dataKey: 'speed' },
          { header: 'Limite', dataKey: 'limit' },
          { header: 'Sévérité', dataKey: 'severityLabel' }
        ];
      case 'driving-behavior':
        return [
          { header: 'Véhicule', dataKey: 'vehicle' },
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Type d\'incident', dataKey: 'incidentType' },
          { header: 'Adresse', dataKey: 'address' },
          { header: 'Valeur', dataKey: 'value' },
          { header: 'Sévérité', dataKey: 'severityLabel' }
        ];
      case 'fuel':
        return [
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Niveau', dataKey: 'fuelPercent' },
          { header: 'Variation', dataKey: 'fuelChange' },
          { header: 'Type', dataKey: 'eventType' },
          { header: 'Position', dataKey: 'location' },
          { header: 'Odomètre', dataKey: 'odometer' }
        ];
      case 'fuel-estimation':
        return [
          { header: 'Véhicule', dataKey: 'vehicleName' },
          { header: 'Distance', dataKey: 'distance' },
          { header: 'Consommation est.', dataKey: 'fuelEstimated' },
          { header: 'Coût est.', dataKey: 'costEstimated' },
          { header: 'Conso. moy.', dataKey: 'avgConsumption' }
        ];
      // Recette du 10/09/2026 : la colonne « Référence » est retirée. C'est un
      // identifiant interne de la réparation, sans valeur pour le lecteur du
      // rapport, et il mangeait la largeur dont Description et Fournisseur ont
      // besoin. Le champ reste en base et sur la fiche détail.
      case 'costs':
        // Largeurs mesurees a la police REELLE du corps (Manrope 7, 5 mm de
        // et avec 5 mm de marges internes, « 1 385,28 EUR » occupe 20,4 mm et
        // « Complétée » 16,9 mm. Aux poids globaux ces colonnes tombaient a
        // 16,6 mm et coupaient le montant en deux (« 327,60 / EUR »).
        // Largeurs mesurees a la police du corps (Helvetica 7) avec 5 mm de
        // marges internes. Le passage de « EUR » au symbole € rend 3,9 mm par
        // montant : ils repartent a Description (49 mm pour la plus longue) et
        // a Fournisseur (32,6 mm pour « Garage Renault Lyon Est »), qui
        // passaient jusqu’ici sur deux lignes.
        // Statut retire : le handler de creation force Status a completed, si
        // bien que la colonne repetait « Complétée » sur chaque ligne.
        return [
          { header: 'Immatriculation', dataKey: 'vehicleName', weight: 1.8 },
          { header: 'Date', dataKey: 'date', weight: 1.2 },
          { header: 'Description', dataKey: 'description', weight: 3.2 },
          { header: 'Fournisseur', dataKey: 'supplierName', weight: 2.35 },
          { header: 'Main d\'oeuvre', dataKey: 'laborCostFormatted', weight: 1.68 },
          { header: 'Pièces', dataKey: 'partsCostFormatted', weight: 1.2 },
          { header: 'Total', dataKey: 'totalCostFormatted', weight: 1.2 }
        ];
      // Recette du 10/09/2026 : le statut ne sert plus à rien dans l'export —
      // le rapport ne contient que des entretiens réalisés depuis que les
      // lignes planifiées en ont été retirées, donc la colonne valait
      // « Terminée » sur toutes les lignes. Le fournisseur la remplace :
      // c'est l'information qu'on cherche sur un historique d'entretien.
      case 'maintenance':
        // Largeurs mesurees (Helvetica 7 pour le corps, 7,5 gras pour les
        // en-tetes, 5 mm de marges internes) : « Immatriculation » 24,7 mm,
        // « Révision périodique » 26,5 mm, « Garage Renault Lyon Est » 32,6 mm,
        // « 154 546 km » 17,8 mm, « 265,00 € » 14,5 mm. Le reste va a
        // Description, seule colonne dont le retour a la ligne est normal.
        return [
          { header: 'Immatriculation', dataKey: 'vehicleName', weight: 2 },
          { header: 'Date', dataKey: 'date', weight: 1.25 },
          { header: 'Type', dataKey: 'type', weight: 2.15 },
          { header: 'Description', dataKey: 'description', weight: 3.35 },
          { header: 'Fournisseur', dataKey: 'supplierName', weight: 2.6 },
          { header: 'Coût', dataKey: 'costFormatted', weight: 1.25 },
          { header: 'Km', dataKey: 'mileage', weight: 1.5 }
        ];
      default:
        return [
          { header: 'Date/Heure', dataKey: 'time' },
          { header: 'Valeur', dataKey: 'value' },
          { header: 'Détails', dataKey: 'details' },
          { header: 'Kilométrage', dataKey: 'kilometrage' }
        ];
    }
  }

  getFormattersForReport(type: string): Record<string, (value: any, row: any) => string> {
    switch (type) {
      case 'trips':
        return {
          // Numéro de trajet seul (T1, T2…). Les pseudos-lignes « Arret »
          // qui polluaient le PDF sont filtrees a la source dans
          // reports.component.ts.
          '_tripNumber': (_v: any, row: any) => `T${row.tripNumber}`,
          '_address': (_v: any, row: any) => `${row.startAddress || ''} → ${row.endAddress || ''}`
        };
      case 'stops':
        return {
          '_typeLabel': (_v: any, row: any) => `${row.typeCode} - ${row.typeLabel}`
        };
      default:
        return {};
    }
  }
}
