import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'ui-legend-item',
  standalone: true,
  imports: [CommonModule],
  template: `
    <div class="legend-item">
      <span class="dot" [style.background]="color"></span>
      <span class="label">{{ label }}</span>
      <span class="value">{{ value }}</span>
    </div>
  `,
  styles: [`
    .legend-item {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 11px;
    }

    .dot {
      width: 8px;
      height: 8px;
      border-radius: 2px;
      flex-shrink: 0;
    }

    .label {
      flex: 1;
      color: #64748b;
    }

    .value {
      color: #1e293b;
      font-weight: 600;
      min-width: 20px;
      text-align: right;
    }
  `]
})
export class LegendItemComponent {
  @Input() color = '#3b82f6';
  @Input() label = '';
  @Input() value: string | number = '';
}
