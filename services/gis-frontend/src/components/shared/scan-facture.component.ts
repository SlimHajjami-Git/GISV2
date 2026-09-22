import { Component, EventEmitter, Input, OnDestroy, OnInit, Output, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';
import { CreditIaBarComponent } from './credit-ia-bar.component';
import { CreditIa, creditBloque, creditDepuisReponse, creditPerime, delaiAvantRecharge, infobulleCredit, lireCreditIa, lireRefusCreditIa } from './credit-ia.helpers';

/**
 * Brique « Scanner une facture » — bouton + barre « Crédit IA » + envoi du fichier
 * à l'IA, sans rien savoir de l'écran qui l'accueille.
 *
 * Née le 19/09/2026 de l'écran Dépenses, seul à porter le scan : la demande est
 * de l'ajouter à Entretien effectué, Nouvelle réparation, Échéances et Carburant.
 * Le composant fait TOUT ce qui est commun (préparation de la photo, appel,
 * état « analyse en cours », crédit IA, messages d'erreur) et émet le résultat BRUT ;
 * chaque écran décide ensuite du remplissage de SES champs — le composant ne
 * connaît ni les dépenses, ni les catégories, ni aucun formulaire.
 */

/** Une ligne facturée, décortiquée par l'IA (montant TTC de la ligne). */
export interface LigneFactureScannee {
  label: string;
  amount: number;
  category: string;
}

/**
 * Crédit IA MENSUEL de la société, en jetons — partagé par tous les écrans et par toute
 * l'IA (scans, assistant, rapports IA). Remplace le quota en nombre de scans le 22/09/2026
 * (voir credit-ia.helpers.ts).
 */
export type QuotaScan = CreditIa;

/**
 * Champs extraits de la facture. `null` = absent ou illisible sur le document :
 * un écran ne doit JAMAIS écraser une valeur saisie avec un null.
 */
export interface ExtractionFacture {
  supplierName: string | null;
  invoiceNumber: string | null;
  /** Date du document, ISO yyyy-MM-dd. */
  date: string | null;
  amountHT: number | null;
  amountTVA: number | null;
  amountTTC: number | null;
  /** Total à retenir : TTC, à défaut HT, toujours positif. null si illisible. */
  total: number | null;
  currency: string | null;
  /** fuel | maintenance | insurance | tax | toll | parking | fine | repair | other | credit_note */
  category: string | null;
  vehiclePlate: string | null;
  description: string | null;
  /** « Fournisseur — description », prêt à poser dans un champ libre ('' si rien). */
  descriptionComplete: string;
  /** high | medium | low */
  confidence: string | null;
  /** Carburant : volume servi, en litres (null si non imprimé). */
  liters: number | null;
  /** Carburant : prix au litre imprimé (null si non imprimé). */
  pricePerLiter: number | null;
  /** Avoir fournisseur : les montants sont positifs, c'est un remboursement. */
  isCreditNote: boolean;
  items: LigneFactureScannee[];
}

/** Ce que le composant émet quand le scan a réussi. */
export interface ResultatScanFacture {
  extraction: ExtractionFacture;
  /** Fichier stocké par le serveur (/uploads/invoices/...) — à enregistrer en justificatif. */
  receiptUrl: string;
  /** Crédit IA après ce scan (null si le serveur ne l'a pas renvoyé). */
  quota: QuotaScan | null;
}

/**
 * Ce que le composant émet quand le scan a échoué. Le message a DÉJÀ été montré
 * à l'utilisateur ; `receiptUrl` est renseigné quand le serveur a tout de même
 * stocké le fichier (panne IA, PDF illisible) : l'écran peut alors ouvrir son
 * formulaire vide avec le justificatif déjà rattaché, pour une saisie à la main.
 */
export interface EchecScanFacture {
  message: string;
  receiptUrl: string;
}

/**
 * Prépare la photo avant l'envoi : rotation EXIF appliquée, côté max 2000 px,
 * ré-encodage JPEG qualité 0,85. Une photo de téléphone (4000×3000, ~6 Mo)
 * devient ~500 Ko — envoi bien plus rapide et lecture IA plus fiable.
 * Les PDF et les petites images passent tels quels ; en cas d'échec de décodage
 * (navigateur ancien, format exotique) on renvoie l'original.
 *
 * Exportée : l'écran Dépenses s'en sert aussi pour l'envoi d'une quittance.
 */
export async function preparerImageFacture(file: File): Promise<File> {
  if (!file.type.startsWith('image/') || file.type === 'image/gif') return file;
  const MAX_SIDE = 2000;
  try {
    const bmp = await createImageBitmap(file, { imageOrientation: 'from-image' } as ImageBitmapOptions);
    const scale = Math.min(1, MAX_SIDE / Math.max(bmp.width, bmp.height));
    if (scale === 1 && file.size < 1_500_000) { bmp.close(); return file; }
    const w = Math.max(1, Math.round(bmp.width * scale));
    const h = Math.max(1, Math.round(bmp.height * scale));
    const canvas = document.createElement('canvas');
    canvas.width = w; canvas.height = h;
    const ctx = canvas.getContext('2d');
    if (!ctx) { bmp.close(); return file; }
    ctx.drawImage(bmp, 0, 0, w, h);
    bmp.close();
    const blob = await new Promise<Blob | null>(res => canvas.toBlob(res, 'image/jpeg', 0.85));
    if (!blob || blob.size >= file.size) return file;   // pas de gain → original
    return new File([blob], file.name.replace(/\.[^.]+$/, '') + '.jpg', { type: 'image/jpeg' });
  } catch {
    return file;
  }
}

/** Nombre exploitable, sinon null — le serveur renvoie des nombres, l'IA parfois des chaînes. */
function nombre(v: any): number | null {
  if (typeof v === 'number') return isFinite(v) ? v : null;
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v.replace(',', '.'));
    return isFinite(n) ? n : null;
  }
  return null;
}

/**
 * Passe de la réponse serveur à une forme stable pour les écrans.
 *
 * Seule règle métier portée ici, parce qu'elle appartient à la FACTURE et non à
 * un écran : un total négatif est un avoir fournisseur (le serveur le convertit
 * déjà — montants positifs, isCreditNote — mais une API plus ancienne, ou un
 * serveur pas encore déployé, renvoie encore du négatif ; sans cette reprise le
 * montant resterait sous zéro et aucun enregistrement ne serait accepté).
 * Exportée pour les tests.
 */
export function normaliserExtraction(brut: any): ExtractionFacture {
  const x = brut || {};
  const totalBrut = nombre(x.amountTTC) ?? nombre(x.amountHT);
  const avoir = x.isCreditNote === true || (totalBrut !== null && totalBrut < 0);

  const items: LigneFactureScannee[] = (Array.isArray(x.items) ? x.items : [])
    .slice(0, 30)
    .map((it: any) => ({
      label: it?.label || '',
      amount: typeof it?.amount === 'number' ? it.amount : 0,
      category: it?.category || x.category || 'other'
    }))
    .filter((it: LigneFactureScannee) => it.label || it.amount);

  // Lignes inversées seulement si elles font un total négatif : sur un avoir aux
  // lignes imprimées en positif, les inverser les rendrait fausses.
  if (totalBrut !== null && totalBrut < 0 && items.reduce((s, it) => s + it.amount, 0) < 0) {
    items.forEach(it => { it.amount = -it.amount || 0; });
  }

  const abs = (n: number | null) => (n === null ? null : (avoir ? Math.abs(n) : n));
  const description = x.description || null;
  const fournisseur = x.supplierName || null;

  return {
    supplierName: fournisseur,
    invoiceNumber: x.invoiceNumber || null,
    date: x.date || null,
    amountHT: abs(nombre(x.amountHT)),
    amountTVA: abs(nombre(x.amountTVA)),
    amountTTC: abs(nombre(x.amountTTC)),
    total: totalBrut === null ? null : Math.abs(totalBrut),
    currency: x.currency || null,
    category: x.category || null,
    vehiclePlate: x.vehiclePlate || null,
    description,
    descriptionComplete: [fournisseur, description].filter(s => !!s).join(' — '),
    confidence: x.confidence || null,
    liters: nombre(x.liters),
    pricePerLiter: nombre(x.pricePerLiter),
    isCreditNote: avoir,
    items
  };
}

@Component({
  selector: 'app-scan-facture',
  standalone: true,
  imports: [CommonModule, CreditIaBarComponent],
  template: `
    @if (autorise) {
      <span class="scan-groupe">
        <button class="btn-scan" (click)="fichier.click()"
                [disabled]="occupe"
                [title]="infobulle">
          <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2"/>
            <line x1="3" y1="12" x2="21" y2="12"/>
          </svg>
          {{ analyse ? 'Analyse…' : libelle }}
        </button>
        @if (quota) {
          <!-- Part du crédit IA gratuit du mois déjà consommée (22/09/2026) : remplace la
               pastille « 12/20 ce mois ». HORS du bouton : un bouton rend ses enfants
               présentationnels, le rôle progressbar y serait perdu pour les lecteurs d'écran. -->
          <app-credit-ia-bar [credit]="quota" [avecScans]="true"></app-credit-ia-bar>
        }
        <input #fichier type="file" accept="image/*,application/pdf" hidden (change)="onFichier($event)">
      </span>
    }
  `,
  styles: [`
    /* L'hôte disparaît de la mise en page : le groupe bouton + barre est un enfant
       direct de la barre d'actions de l'écran, comme le bouton seul avant. */
    :host { display: contents; }

    .scan-groupe { display: inline-flex; align-items: center; gap: 8px; flex-wrap: wrap; }

    .btn-scan {
      display: inline-flex; align-items: center; gap: 6px; cursor: pointer;
      padding: 8px 14px; border-radius: 8px; font-size: 13px; font-weight: 600;
      color: #7c3aed; background: #f5f3ff; border: 1px solid #ddd6fe;
      transition: background .15s, border-color .15s;
    }
    .btn-scan:hover:not(:disabled) { background: #ede9fe; border-color: #c4b5fd; }
    /* Un curseur « progress » pendant l'analyse était prévu à l'écran Dépenses via une
       classe .scanning qui n'a jamais été posée : le bouton grisé montrait toujours
       « interdit ». Rendu conservé tel quel — le changer serait visible pour le client. */
    .btn-scan:disabled { opacity: .6; cursor: not-allowed; }
  `]
})
export class ScanFactureComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  /** Libellé du bouton — « Scanner une facture », « Scanner un ticket »… */
  @Input() libelle = 'Scanner une facture';

  /** Écran pas prêt (véhicule non choisi, formulaire fermé…) : bouton grisé. */
  @Input() desactive = false;

  /** Motif affiché en infobulle quand `desactive` est vrai (sinon l'état du crédit IA). */
  @Input() raisonDesactivation = '';

  /** Scan réussi : extraction + justificatif + crédit IA. */
  @Output() scanne = new EventEmitter<ResultatScanFacture>();

  /** Scan échoué : le message est déjà affiché, `receiptUrl` peut être exploitable. */
  @Output() echec = new EventEmitter<EchecScanFacture>();

  /** Crédit IA du mois de la société (null tant qu'il n'est pas chargé, ou API ancienne). */
  quota: QuotaScan | null = null;
  /** Vrai pendant l'appel : le bouton passe à « Analyse… ». */
  analyse = false;

  constructor(private apiService: ApiService, private authService: AuthService, private cdr: ChangeDetectorRef) {}

  /**
   * Le scan est ouvert à quiconque a accès à l'écran : le contrôle réel est côté
   * serveur (crédit IA mensuel par société, appliqué avant tout appel payant à l'IA).
   *
   * N'ajoutez PAS de condition sur un droit de module ici. Décision de Karim du
   * 19/09/2026 : le scan ne relève pas des Dépenses — c'est l'écran hôte qui est
   * gardé par SON droit, et lui seul décide de ce qu'on fait du résultat. Le
   * serveur a été aligné le même jour (PermissionMiddleware, clés du scan), sans
   * quoi le bouton s'affichait sur Carburant, Entretien, Réparation et Échéances
   * pour se faire refuser au nom d'un AUTRE module.
   */
  get autorise(): boolean {
    return !!this.authService.getCurrentUserSync();
  }

  /** Grisé pendant l'analyse, sur demande de l'écran, ou crédit épuisé / fonction fermée
   *  (100 % : le serveur refuserait le scan, inutile de le promettre). Un crédit épuisé
   *  dont la date de recharge est passée ne grise plus : le serveur l'a remis à zéro. */
  get occupe(): boolean {
    return this.analyse || this.desactive || (!!this.quota && creditBloque(this.quota, Date.now()));
  }

  /** Bulle d'aide du bouton : motif de l'écran s'il est grisé, sinon l'état du crédit. */
  get infobulle(): string {
    if (this.desactive && this.raisonDesactivation) return this.raisonDesactivation;
    const q = this.quota;
    if (!q) return 'Scanner une facture avec l\'IA';
    // « environ N scans restants » : seulement ici, à côté du bouton de scan.
    return infobulleCredit(q, Date.now(), { scans: true });
  }

  /** Relecture du crédit prévue à la date de recharge (écran resté ouvert au changement de mois). */
  private minuterieRecharge: ReturnType<typeof setTimeout> | null = null;

  ngOnInit(): void {
    if (!this.autorise) return;
    this.chargerCredit();
  }

  ngOnDestroy(): void {
    this.annulerMinuterie();
    this.destroy$.next();
    this.destroy$.complete();
  }

  private chargerCredit(): void {
    this.apiService.getScanQuota().pipe(takeUntil(this.destroy$)).subscribe({
      // lireCreditIa rend null sur une réponse d'API ancienne : pas de barre, bouton actif.
      next: (q) => { this.definirCredit(lireCreditIa(q)); this.cdr.detectChanges(); },
      error: () => { /* crédit indisponible → bouton reste utilisable, le serveur tranche */ }
    });
  }

  /** Pose le crédit reçu du serveur et prévoit sa relecture à la prochaine recharge. */
  private definirCredit(credit: QuotaScan | null): void {
    this.quota = credit;
    this.planifierRecharge();
  }

  /**
   * Le crédit n'était lu qu'à l'ouverture : épuisé le 30, l'écran resté ouvert gardait
   * le 1er une barre rouge à 100 % et un bouton grisé — aucun clic possible, donc aucun
   * appel pour apprendre la recharge. On relit donc le crédit juste après la date de
   * recharge annoncée par le serveur (délai borné, voir delaiAvantRecharge). Le bouton est
   * dégrisé même si cette relecture échoue (creditBloque tient compte de l'heure).
   */
  private planifierRecharge(): void {
    this.annulerMinuterie();
    const delai = delaiAvantRecharge(this.quota, Date.now());
    if (delai === null) return;
    this.minuterieRecharge = setTimeout(() => {
      this.minuterieRecharge = null;
      // Délai borné (≈ 24,8 jours) atteint avant la recharge : on attend encore.
      if (this.quota && !creditPerime(this.quota, Date.now())) { this.planifierRecharge(); return; }
      this.cdr.detectChanges();
      this.chargerCredit();
    }, delai);
  }

  private annulerMinuterie(): void {
    if (this.minuterieRecharge !== null) {
      clearTimeout(this.minuterieRecharge);
      this.minuterieRecharge = null;
    }
  }

  async onFichier(event: any): Promise<void> {
    const file: File | undefined = event?.target?.files?.[0];
    if (event?.target) event.target.value = '';   // permet de rechoisir le même fichier
    if (!file) return;
    this.analyse = true;
    this.cdr.detectChanges();
    const prepared = await preparerImageFacture(file);
    this.apiService.scanInvoice(prepared).pipe(takeUntil(this.destroy$)).subscribe({
      next: (res: any) => {
        this.analyse = false;
        // Barre mise à jour par le serveur (crédit relu après le scan, commun à toute l'IA).
        this.definirCredit(creditDepuisReponse(res) ?? this.quota);
        this.scanne.emit({
          extraction: normaliserExtraction(res?.extraction),
          receiptUrl: res?.receiptUrl || '',
          quota: this.quota
        });
        this.cdr.detectChanges();
      },
      error: (err: any) => {
        this.analyse = false;
        // 413 = refusé par le proxy sur la taille avant d'atteindre l'API : le message
        // générique pousserait l'utilisateur à réessayer le même fichier.
        // Les autres messages viennent du serveur (400 format, 403 IA non activée,
        // 429 crédit IA du mois épuisé, 502 IA indisponible).
        const message = err?.status === 413
          ? 'Fichier trop volumineux (maximum 12 Mo). Réduisez la taille ou envoyez une photo compressée.'
          : (err?.error?.message || "L'analyse de la facture a échoué. Vous pouvez saisir les informations manuellement.");
        // Refus 403/429 (AI_CREDIT_DISABLED / AI_CREDIT_EXHAUSTED) : le serveur joint le
        // crédit du moment. La barre passe alors à 100 % et le bouton se grise — sans quoi il
        // restait actif sur une page ouverte avant l'épuisement (assistant utilisé dans un
        // autre onglet, autre utilisateur) et chaque clic était refusé.
        const refus = lireRefusCreditIa(err);
        const credit = refus?.credit ?? lireCreditIa(err?.error?.quota);
        if (credit) this.definirCredit(credit);
        alert(message);
        this.echec.emit({ message, receiptUrl: err?.error?.receiptUrl || '' });
        this.cdr.detectChanges();
      }
    });
  }
}
