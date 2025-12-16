import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'ui-stat-card',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="stat-card" [style.border-left-color]="accentColor">
      <div class="stat-icon" [style.background]="iconBg" [style.color]="accentColor">
        <ng-content select="[icon]"></ng-content>
      </div>
      <div class="stat-content">
        <span class="stat-label">{{ label }}</span>
        <span class="stat-value">{{ value }}</span>
      </div>
    </div>
  `,
  styles: [`
    .stat-card {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 12px 16px;
      background: white;
      border-radius: 4px;
      border: 1px solid #e2e8f0;
      border-left: 3px solid #3b82f6;
    }

    .stat-icon {
      width: 36px;
      height: 36px;
      border-radius: 6px;
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .stat-content {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .stat-label {
      font-size: 11px;
      color: #64748b;
      font-weight: 500;
      text-transform: uppercase;
      letter-spacing: 0.3px;
    }

    .stat-value {
      font-size: 20px;
      font-weight: 700;
      color: #1e293b;
      line-height: 1;
    }
  `]
})
export class StatCardComponent {
  @Input() label = '';
  @Input() value: string | number = '';
  @Input() accentColor = '#3b82f6';
  @Input() iconBg = 'rgba(59, 130, 246, 0.1)';
}
