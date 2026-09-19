import { Component, EventEmitter, Input, OnDestroy, OnInit, Output, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { ApiService } from '../../services/api.service';
import { AuthService } from '../../services/auth.service';

/**
 * Brique « Scanner une facture » — bouton + compteur de quota + envoi du fichier
 * à l'IA, sans rien savoir de l'écran qui l'accueille.
 *
 * Née le 19/09/2026 de l'écran Dépenses, seul à porter le scan : la demande est
 * de l'ajouter à Entretien effectué, Nouvelle réparation, Échéances et Carburant.
 * Le composant fait TOUT ce qui est commun (préparation de la photo, appel,
 * état « analyse en cours », quota, messages d'erreur) et émet le résultat BRUT ;
 * chaque écran décide ensuite du remplissage de SES champs — le composant ne
 * connaît ni les dépenses, ni les catégories, ni aucun formulaire.
 */

/** Une ligne facturée, décortiquée par l'IA (montant TTC de la ligne). */
export interface LigneFactureScannee {
  label: string;
  amount: number;
  category: string;
}

/** Quota MENSUEL de scans IA de la société — partagé par tous les écrans. */
export interface QuotaScan {
  used: number;
  limit: number;
  remaining: number;
  resetsAt?: string;
}

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
  /** Quota après ce scan (null si le serveur ne l'a pas renvoyé). */
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
  imports: [CommonModule],
  template: `
    @if (autorise) {
      <button class="btn-scan" (click)="fichier.click()"
              [disabled]="occupe"
              [title]="infobulle">
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2"/>
          <line x1="3" y1="12" x2="21" y2="12"/>
        </svg>
        {{ analyse ? 'Analyse…' : libelle }}
        @if (quota) {
          <!-- Scans UTILISÉS ce mois / quota — même lecture que la fiche admin. Avant le
               11/09/2026 la bulle affichait le RESTE (« 16/20 »), qui se lisait comme
               16 scans consommés et laissait croire que le compteur ne repartait jamais à zéro. -->
          <span class="scan-quota-chip" [class.scan-quota-chip-empty]="quota.remaining === 0">
            {{ quota.used }}/{{ quota.limit }} ce mois
          </span>
        }
      </button>
      <input #fichier type="file" accept="image/*,application/pdf" hidden (change)="onFichier($event)">
    }
  `,
  styles: [`
    /* L'hôte disparaît de la mise en page : le bouton reste un enfant direct de la
       barre d'actions de l'écran, exactement comme avant l'extraction. */
    :host { display: contents; }

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

    /* Compteur de quota mensuel (scans utilisés / limite) */
    .scan-quota-chip {
      display: inline-grid; place-items: center;
      padding: 1px 7px; border-radius: 999px;
      background: #7c3aed; color: #ffffff;
      font-size: 10.5px; font-weight: 800; letter-spacing: .01em;
    }
    .scan-quota-chip-empty { background: #dc2626; }
  `]
})
export class ScanFactureComponent implements OnInit, OnDestroy {
  private destroy$ = new Subject<void>();

  /** Libellé du bouton — « Scanner une facture », « Scanner un ticket »… */
  @Input() libelle = 'Scanner une facture';

  /** Écran pas prêt (véhicule non choisi, formulaire fermé…) : bouton grisé. */
  @Input() desactive = false;

  /** Motif affiché en infobulle quand `desactive` est vrai (sinon le compteur de quota). */
  @Input() raisonDesactivation = '';

  /** Scan réussi : extraction + justificatif + quota. */
  @Output() scanne = new EventEmitter<ResultatScanFacture>();

  /** Scan échoué : le message est déjà affiché, `receiptUrl` peut être exploitable. */
  @Output() echec = new EventEmitter<EchecScanFacture>();

  /** Quota mensuel de la société (null tant qu'il n'est pas chargé). */
  quota: QuotaScan | null = null;
  /** Vrai pendant l'appel : le bouton passe à « Analyse… ». */
  analyse = false;

  constructor(private apiService: ApiService, private authService: AuthService, private cdr: ChangeDetectorRef) {}

  /**
   * Le scan est ouvert à quiconque a accès à l'écran : le contrôle réel est côté
   * serveur (quota mensuel par société, appliqué avant tout appel payant à l'IA).
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

  get occupe(): boolean {
    return this.analyse || this.desactive || this.quota?.remaining === 0;
  }

  /** « 1er octobre » : jour de la prochaine remise à zéro du compteur. */
  get quotaResetLabel(): string {
    const iso = this.quota?.resetsAt;
    if (!iso) return 'le 1er du mois prochain';
    const d = new Date(iso);
    if (isNaN(d.getTime())) return 'le 1er du mois prochain';
    const mois = d.toLocaleDateString('fr-FR', { month: 'long', timeZone: 'UTC' });
    return 'le ' + (d.getUTCDate() === 1 ? '1er' : String(d.getUTCDate())) + ' ' + mois;
  }

  /** Bulle d'aide : ce que veut dire le compteur, et quand il repart à zéro. */
  get infobulle(): string {
    if (this.desactive && this.raisonDesactivation) return this.raisonDesactivation;
    const q = this.quota;
    if (!q) return 'Scanner une facture avec l\'IA';
    if (q.remaining === 0) return `Quota mensuel atteint (${q.used}/${q.limit}) — nouveau quota ${this.quotaResetLabel}. Votre administrateur peut augmenter la limite.`;
    return `${q.used} scan${q.used > 1 ? 's' : ''} utilisé${q.used > 1 ? 's' : ''} sur ${q.limit} ce mois-ci — il en reste ${q.remaining}. Compteur remis à zéro ${this.quotaResetLabel}.`;
  }

  ngOnInit(): void {
    if (!this.autorise) return;
    this.apiService.getScanQuota().pipe(takeUntil(this.destroy$)).subscribe({
      next: (q) => { this.quota = q; this.cdr.detectChanges(); },
      error: () => { /* quota indisponible → bouton reste utilisable, le serveur tranche */ }
    });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
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
        if (res?.quota) this.quota = res.quota;   // compteur mis à jour par le serveur
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
        // Les autres messages viennent du serveur (400 format, 403 non activé,
        // 429 quota atteint, 502 IA indisponible).
        const message = err?.status === 413
          ? 'Fichier trop volumineux (maximum 12 Mo). Réduisez la taille ou envoyez une photo compressée.'
          : (err?.error?.message || "L'analyse de la facture a échoué. Vous pouvez saisir les informations manuellement.");
        alert(message);
        this.echec.emit({ message, receiptUrl: err?.error?.receiptUrl || '' });
        this.cdr.detectChanges();
      }
    });
  }
}
