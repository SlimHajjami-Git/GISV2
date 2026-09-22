import { Component, Input } from '@angular/core';
import { CreditIa, infobulleCredit, niveauCredit, NiveauCredit } from './credit-ia.helpers';

/**
 * Barre compacte « Crédit IA » : part du crédit IA gratuit du mois déjà consommée par
 * les scans de factures (22/09/2026, remplace la pastille « 12/20 ce mois »).
 *
 * Posée À CÔTÉ du bouton « Scanner une facture », jamais dedans : un bouton rend ses
 * enfants purement présentationnels pour les lecteurs d'écran, le rôle progressbar y
 * aurait été perdu. Réutilisée plus large sur la fiche société de l'admin.
 *
 * Fonction désactivée (budget 0) : pas de barre — une barre rouge à 100 % se lirait
 * « crédit épuisé », alors que la fonction est fermée.
 */
@Component({
  selector: 'app-credit-ia-bar',
  standalone: true,
  template: `
    @if (credit.enabled) {
      <span class="credit-ia" [title]="titre">
        @if (libelle) { <span class="credit-ia-libelle">{{ libelle }}</span> }
        <span class="credit-ia-piste" role="progressbar"
              aria-valuemin="0" aria-valuemax="100"
              [attr.aria-valuenow]="pourcentage"
              [attr.aria-valuetext]="pourcentage + ' % utilisé'"
              [attr.aria-label]="'Crédit IA du mois : ' + pourcentage + ' % utilisé'"
              [style.width.px]="largeur">
          <span class="credit-ia-remplissage" [class.niveau-ok]="niveau === 'ok'"
                [class.niveau-alerte]="niveau === 'alerte'" [class.niveau-critique]="niveau === 'critique'"
                [style.width.%]="pourcentage"></span>
        </span>
        <span class="credit-ia-pct" [class.niveau-ok]="niveau === 'ok'"
              [class.niveau-alerte]="niveau === 'alerte'" [class.niveau-critique]="niveau === 'critique'">{{ pourcentage }} %</span>
      </span>
    } @else {
      <span class="credit-ia credit-ia-off" [title]="titre">IA désactivée</span>
    }
  `,
  styles: [`
    :host { display: inline-flex; align-items: center; }
    .credit-ia {
      display: inline-flex; align-items: center; gap: 6px; white-space: nowrap;
      font-size: 11px; font-weight: 600; color: #6b7280; cursor: default;
    }
    .credit-ia-libelle { color: #7c3aed; }
    .credit-ia-piste {
      position: relative; display: inline-block; flex: 0 0 auto;
      height: 6px; border-radius: 999px; overflow: hidden;
      background: #e5e7eb;
    }
    .credit-ia-remplissage {
      position: absolute; left: 0; top: 0; bottom: 0;
      border-radius: 999px; transition: width .3s ease;
    }
    .credit-ia-remplissage.niveau-ok { background: #16a34a; }
    .credit-ia-remplissage.niveau-alerte { background: #f59e0b; }
    .credit-ia-remplissage.niveau-critique { background: #dc2626; }
    .credit-ia-pct { font-variant-numeric: tabular-nums; min-width: 34px; }
    .credit-ia-pct.niveau-ok { color: #15803d; }
    .credit-ia-pct.niveau-alerte { color: #b45309; }
    .credit-ia-pct.niveau-critique { color: #b91c1c; }
    .credit-ia-off {
      padding: 1px 7px; border-radius: 999px;
      background: #fee2e2; color: #b91c1c; font-weight: 700;
    }
  `]
})
export class CreditIaBarComponent {
  /** Crédit du mois (voir lireCreditIa). */
  @Input({ required: true }) credit!: CreditIa;

  /** Largeur de la piste, en pixels : compacte à côté du bouton, plus large sur la fiche admin. */
  @Input() largeur = 110;

  /** Libellé avant la piste ('' pour aucun). */
  @Input() libelle = 'Crédit IA';

  /** Infobulle ; par défaut le texte standard (pourcentage, jetons, scans restants, recharge). */
  @Input() infobulle: string | null = null;

  get pourcentage(): number {
    const p = Math.floor(Number(this.credit?.percentUsed) || 0);
    return Math.min(100, Math.max(0, p));
  }

  get niveau(): NiveauCredit {
    return niveauCredit(this.pourcentage);
  }

  get titre(): string {
    // L'heure du poste : passé la date de recharge, l'infobulle ne l'annonce plus au futur.
    return this.infobulle || infobulleCredit(this.credit, Date.now());
  }
}
