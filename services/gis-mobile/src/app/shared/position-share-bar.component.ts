import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { IonicModule } from '@ionic/angular';
import { ShareChannel } from '../core/services/position-share.service';

interface ChannelButton {
  key: ShareChannel;
  icon: string;
  label: string;
  aria: string;
}

/**
 * Rangée « Partager la position » commune à la fiche de la carte et à la fiche véhicule :
 * un seul gabarit, pour que les deux écrans proposent les mêmes canaux dans le même ordre.
 * Le composant ne partage rien lui-même — il émet le canal choisi : la fiche véhicule doit
 * parfois aller chercher la dernière position (repli REST) avant de partager.
 */
@Component({
  selector: 'app-position-share-bar',
  standalone: true,
  imports: [CommonModule, IonicModule],
  template: `
    <div class="psb" role="group" aria-label="Partager la position">
      <div class="psb-title">Partager la position</div>
      <div class="psb-row">
        <button *ngFor="let c of channels" type="button" class="psb-btn" [ngClass]="'psb-' + c.key"
                [disabled]="disabled" [attr.aria-label]="c.aria" [attr.data-channel]="c.key"
                (click)="pick(c.key, $event)">
          <span class="psb-circle"><ion-icon [name]="c.icon" aria-hidden="true"></ion-icon></span>
          <span class="psb-label">{{ c.label }}</span>
        </button>
      </div>
      <p class="psb-hint" *ngIf="disabled">{{ disabledLabel }}</p>
    </div>
  `,
  styles: [`
    :host { display: block; }
    .psb { margin-top: 12px; }
    .psb-title {
      font-size: 12px; font-weight: 600; letter-spacing: 0.02em;
      color: var(--ion-color-medium, #6b7280); margin-bottom: 6px;
    }
    .psb-row { display: flex; justify-content: space-between; gap: 4px; }
    /* Tout le bouton (rond + libellé) est la cible tactile : >= 48 px de haut, un quart de largeur. */
    .psb-btn {
      flex: 1 1 0; min-width: 44px; min-height: 48px;
      display: flex; flex-direction: column; align-items: center; gap: 4px;
      padding: 2px 0; border: 0; background: transparent; color: inherit;
      font: inherit; cursor: pointer; -webkit-tap-highlight-color: transparent;
    }
    .psb-circle {
      box-sizing: border-box; width: 44px; height: 44px; border-radius: 50%;
      display: flex; align-items: center; justify-content: center;
      color: #fff; transition: transform 80ms;
    }
    .psb-circle ion-icon { font-size: 22px; }
    .psb-btn:active:not([disabled]) .psb-circle { transform: scale(0.94); }
    .psb-btn:focus-visible .psb-circle { outline: 2px solid var(--ion-color-primary, #3880ff); outline-offset: 2px; }
    .psb-whatsapp .psb-circle { background: #25D366; }
    .psb-messenger .psb-circle { background: #0084FF; }
    .psb-sms .psb-circle { background: #6b7280; }
    .psb-more .psb-circle {
      background: var(--ion-color-light, #f4f5f8);
      color: var(--ion-color-light-contrast, #374151);
      border: 1px solid var(--ion-color-light-shade, #d7d8da);
    }
    .psb-label { font-size: 12px; line-height: 1.2; color: var(--ion-text-color, #1f2937); }
    .psb-btn[disabled] { cursor: default; }
    .psb-btn[disabled] .psb-circle { opacity: 0.35; }
    .psb-btn[disabled] .psb-label { opacity: 0.5; }
    .psb-hint { margin: 6px 0 0; font-size: 12px; color: var(--ion-color-medium, #6b7280); text-align: center; }
  `]
})
export class PositionShareBarComponent {
  /** Vrai quand aucune position n'est connue : les quatre boutons sont grisés. */
  @Input() disabled = false;
  @Input() disabledLabel = 'Aucune position connue pour ce véhicule';
  @Output() share = new EventEmitter<ShareChannel>();

  readonly channels: ChannelButton[] = [
    { key: 'whatsapp', icon: 'logo-whatsapp', label: 'WhatsApp', aria: 'Partager la position sur WhatsApp' },
    { key: 'messenger', icon: 'chatbubble-ellipses-outline', label: 'Messenger', aria: 'Partager la position sur Messenger' },
    { key: 'sms', icon: 'chatbox-outline', label: 'SMS', aria: 'Partager la position par SMS' },
    { key: 'more', icon: 'share-social-outline', label: 'Plus', aria: 'Partager la position avec une autre application' }
  ];

  pick(channel: ShareChannel, ev: Event) {
    // Les fiches se referment au toucher de leur fond : le clic ne doit jamais y remonter.
    ev.stopPropagation();
    if (this.disabled) return;
    this.share.emit(channel);
  }
}
