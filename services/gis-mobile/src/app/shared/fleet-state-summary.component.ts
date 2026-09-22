import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonicModule } from '@ionic/angular';
import {
  VEHICLE_STATES, VEHICLE_STATE_ORDER, VehicleMotionState, VehicleStateStyle, countByState
} from '../core/vehicle-state.util';

/**
 * « État de la flotte » du tableau de bord : les quatre états de la carte (couleur, icône,
 * libellé, compteur) et une barre proportionnelle. Composant à part pour que la page garde
 * un seul rôle : elle compte, ce bloc affiche — avec les constantes de vehicle-state.util,
 * jamais une couleur recodée.
 */
@Component({
  selector: 'app-fleet-state-summary',
  standalone: true,
  imports: [CommonModule, IonicModule],
  template: `
    <div class="fleet-title">État de la flotte</div>
    <div class="fleet-bar" role="img" [attr.aria-label]="summary()">
      <span *ngFor="let s of states" class="fleet-seg" [attr.data-state]="s.state"
            [style.background]="s.color" [style.flex-grow]="countOf(s.state)"></span>
    </div>
    <div class="fleet-states">
      <div *ngFor="let s of states" class="fleet-state" [attr.data-state]="s.state">
        <span class="fleet-icon" [style.background]="s.tint" [style.color]="s.color">
          <ion-icon [name]="s.icon" aria-hidden="true"></ion-icon>
        </span>
        <span class="fleet-count">{{ countOf(s.state) }}</span>
        <span class="fleet-label">{{ s.label }}</span>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .fleet-title { font-size: 14px; font-weight: 600; color: var(--ion-text-color); margin-bottom: 10px; }
    /* Barre proportionnelle : chaque segment prend la part de son état (flex-grow = compteur). */
    .fleet-bar {
      display: flex; height: 8px; border-radius: 4px; overflow: hidden;
      background: var(--ion-color-light); margin-bottom: 12px;
    }
    .fleet-seg { flex-basis: 0; min-width: 0; }
    .fleet-states { display: grid; grid-template-columns: repeat(4, 1fr); gap: 6px; }
    .fleet-state { display: flex; flex-direction: column; align-items: center; text-align: center; gap: 2px; }
    .fleet-icon {
      width: 32px; height: 32px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
    }
    .fleet-icon ion-icon { font-size: 18px; }
    .fleet-count { font-size: 18px; font-weight: 700; color: var(--ion-text-color); line-height: 1.2; }
    .fleet-label { font-size: 11px; color: var(--ion-color-medium-shade, #555); line-height: 1.2; }
  `]
})
export class FleetStateSummaryComponent {
  /** Compteurs par état (countByState) ; un état absent vaut 0. */
  @Input() counts: Record<VehicleMotionState, number> = countByState([]);

  readonly states: VehicleStateStyle[] = VEHICLE_STATE_ORDER.map(s => VEHICLE_STATES[s]);

  countOf(state: VehicleMotionState): number {
    return this.counts?.[state] ?? 0;
  }

  /** Résumé lu par les lecteurs d'écran à la place de la barre de couleurs. */
  summary(): string {
    return this.states.map(s => `${s.label} : ${this.countOf(s.state)}`).join(', ');
  }
}
