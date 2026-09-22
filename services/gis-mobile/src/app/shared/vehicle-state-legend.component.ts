import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonicModule } from '@ionic/angular';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { VEHICLE_STATES, VEHICLE_STATE_ORDER, VehicleMotionState, stateMarkerHtml } from '../core/vehicle-state.util';

interface LegendRow {
  state: VehicleMotionState;
  label: string;
  badge: SafeHtml;
}

/** Mémorise le choix replié / déplié d'un lancement à l'autre (confort, pas une donnée). */
export const LEGEND_COLLAPSED_KEY = 'calypso.map-legend.collapsed';

/**
 * Légende des couleurs posée sur la carte de suivi. Le client confondait les
 * états parce que chaque écran avait son code : la légende montre les MÊMES
 * pastilles que les marqueurs (même fabrique stateMarkerHtml), avec leur libellé.
 * Repliable : repliée, elle ne garde que quatre points de couleur et le mot
 * « Légende », pour ne rien masquer de la carte.
 */
@Component({
  selector: 'app-vehicle-state-legend',
  standalone: true,
  imports: [CommonModule, IonicModule],
  template: `
    <div class="vsl" [class.collapsed]="collapsed">
      <button type="button" class="vsl-toggle" (click)="toggle($event)"
              [attr.aria-expanded]="!collapsed" aria-controls="vsl-list"
              [attr.aria-label]="collapsed ? 'Afficher la légende des couleurs' : 'Replier la légende des couleurs'">
        <span class="vsl-dots" *ngIf="collapsed" aria-hidden="true">
          <span *ngFor="let r of rows" class="vsl-dot" [style.background]="colorOf(r.state)"></span>
        </span>
        <span class="vsl-title">Légende</span>
        <ion-icon [name]="collapsed ? 'chevron-up' : 'chevron-down'" aria-hidden="true"></ion-icon>
      </button>
      <ul id="vsl-list" class="vsl-list" *ngIf="!collapsed">
        <li *ngFor="let r of rows" class="vsl-row" [attr.data-state]="r.state">
          <span class="vsl-badge" [innerHTML]="r.badge"></span>
          <span class="vsl-label">{{ r.label }}</span>
        </li>
      </ul>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .vsl {
      background: var(--ion-card-background, #fff);
      color: var(--ion-text-color, #111827);
      border-radius: 12px;
      box-shadow: 0 2px 12px rgba(0,0,0,0.15);
      padding: 4px 8px 6px;
      font-size: 12px;
    }
    .vsl.collapsed { padding: 0 4px; border-radius: 16px; }
    .vsl-toggle {
      display: flex; align-items: center; gap: 6px;
      width: 100%; min-height: 32px; padding: 0 4px;
      border: 0; background: transparent; color: inherit; font: inherit;
      font-weight: 600; cursor: pointer; -webkit-tap-highlight-color: transparent;
    }
    .vsl-toggle:focus-visible { outline: 2px solid var(--ion-color-primary, #3880ff); outline-offset: 2px; border-radius: 8px; }
    .vsl-toggle ion-icon { font-size: 14px; margin-left: auto; color: var(--ion-color-medium, #6b7280); }
    .vsl-title { white-space: nowrap; }
    .vsl-dots { display: inline-flex; gap: 3px; }
    .vsl-dot { width: 9px; height: 9px; border-radius: 50%; display: inline-block; }
    .vsl-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 4px; }
    .vsl-row { display: flex; align-items: center; gap: 8px; min-height: 22px; }
    .vsl-badge { display: inline-flex; flex: none; }
    .vsl-label { font-weight: 500; white-space: nowrap; }
  `]
})
export class VehicleStateLegendComponent {
  readonly rows: LegendRow[];
  collapsed = false;

  constructor(sanitizer: DomSanitizer) {
    // Balisage CONSTANT (vehicle-state.util), aucune donnée utilisateur : le contournement
    // du nettoyage Angular est nécessaire parce qu'il supprime le SVG des pastilles.
    this.rows = VEHICLE_STATE_ORDER.map(state => ({
      state,
      label: VEHICLE_STATES[state].label,
      badge: sanitizer.bypassSecurityTrustHtml(stateMarkerHtml(state, { size: 20, border: 2, shadow: false }))
    }));
    this.collapsed = readCollapsed();
  }

  colorOf(state: VehicleMotionState): string {
    return VEHICLE_STATES[state].color;
  }

  toggle(ev?: Event) {
    // La légende flotte sur la carte : un toucher ne doit pas la traverser.
    ev?.stopPropagation();
    this.collapsed = !this.collapsed;
    try { localStorage.setItem(LEGEND_COLLAPSED_KEY, this.collapsed ? '1' : '0'); } catch { /* stockage indisponible : sans conséquence */ }
  }
}

function readCollapsed(): boolean {
  try { return localStorage.getItem(LEGEND_COLLAPSED_KEY) === '1'; } catch { return false; }
}
