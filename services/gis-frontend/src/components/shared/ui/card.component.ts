import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'ui-card',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="card" [class.no-padding]="noPadding">
      <div class="card-header" *ngIf="title || showHeader">
        <div class="card-title-wrapper">
          <span class="card-title">{{ title }}</span>
          <span class="help-icon" *ngIf="showHelp">ⓘ</span>
        </div>
        <span class="online-badge" *ngIf="showOnlineBadge">ONLINE DATA</span>
        <ng-content select="[card-actions]"></ng-content>
      </div>
      <div class="card-body">
        <ng-content></ng-content>
      </div>
    </div>
  `,
  styles: [`
    .card {
      background: white;
      border: 1px solid #e2e8f0;
      border-radius: 0;
    }

    .card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 10px 14px;
      border-bottom: 1px solid #f1f5f9;
    }

    .card-title-wrapper {
      display: flex;
      align-items: center;
      gap: 6px;
    }

    .card-title {
      color: #1e293b;
      font-size: 13px;
      font-weight: 600;
    }

    .help-icon {
      color: #94a3b8;
      font-size: 11px;
    }

    .online-badge {
      color: #3b82f6;
      font-size: 9px;
      font-weight: 600;
      letter-spacing: 0.5px;
    }

    .card-body {
      padding: 14px;
    }

    .card.no-padding .card-body {
      padding: 0;
    }
  `]
})
export class CardComponent {
  @Input() title = '';
  @Input() showHeader = false;
  @Input() showHelp = false;
  @Input() showOnlineBadge = false;
  @Input() noPadding = false;
}
